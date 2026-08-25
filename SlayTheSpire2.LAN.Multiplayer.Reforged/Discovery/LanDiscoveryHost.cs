using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Compatibility;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Services;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Discovery;

internal sealed class LanDiscoveryHost
{
    private static readonly Lazy<LanDiscoveryHost> Lazy = new(() => new LanDiscoveryHost());

    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private UdpClient? _udp;
    private NetHostGameService? _netService;
    private Action<ulong>? _clientConnected;
    private Action<ulong, MegaCrit.Sts2.Core.Entities.Multiplayer.NetErrorInfo>? _clientDisconnected;
    private Action<MegaCrit.Sts2.Core.Entities.Multiplayer.NetErrorInfo>? _hostDisconnected;
    private DiscoveryAdvertisement _advertisement;
    private IReadOnlyList<(uint Network, uint Mask)> _localSubnets = [];
    private int _currentPlayers;

    public static LanDiscoveryHost Instance => Lazy.Value;

    public bool Start(NetHostGameService netService, ushort gamePort, int maxPlayers, string gameMode)
    {
        Stop();

        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryProtocol.Port));

            var cancellation = new CancellationTokenSource();
            _advertisement = new DiscoveryAdvertisement(
                0,
                Guid.NewGuid(),
                gamePort,
                1,
                maxPlayers,
                true,
                SettingsService.Instance.SettingsModel.PlayerName,
                gameMode,
                GameCompatibility.RuntimeFlavor,
                typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "2.1.0");
            Interlocked.Exchange(ref _currentPlayers, 1);
            _localSubnets = GetLocalSubnets();

            _clientConnected = _ => Interlocked.Increment(ref _currentPlayers);
            _clientDisconnected = (_, _) => DecrementPlayerCount();
            _hostDisconnected = _ => Stop();
            netService.ClientConnected += _clientConnected;
            netService.ClientDisconnected += _clientDisconnected;
            netService.Disconnected += _hostDisconnected;

            lock (_gate)
            {
                _udp = udp;
                _cancellation = cancellation;
                _netService = netService;
            }

            _ = Task.Run(() => ListenAsync(udp, cancellation.Token));
            Log.Info($"LAN discovery listening on UDP {DiscoveryProtocol.Port} for game port {gamePort}");
            return true;
        }
        catch (Exception exception)
        {
            Log.Warn($"LAN discovery could not start: {exception.Message}");
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        UdpClient? udp;
        NetHostGameService? netService;
        Action<ulong>? clientConnected;
        Action<ulong, MegaCrit.Sts2.Core.Entities.Multiplayer.NetErrorInfo>? clientDisconnected;
        Action<MegaCrit.Sts2.Core.Entities.Multiplayer.NetErrorInfo>? hostDisconnected;

        lock (_gate)
        {
            cancellation = _cancellation;
            udp = _udp;
            netService = _netService;
            clientConnected = _clientConnected;
            clientDisconnected = _clientDisconnected;
            hostDisconnected = _hostDisconnected;
            _cancellation = null;
            _udp = null;
            _netService = null;
            _clientConnected = null;
            _clientDisconnected = null;
            _hostDisconnected = null;
            _localSubnets = [];
        }

        if (netService != null)
        {
            if (clientConnected != null)
                netService.ClientConnected -= clientConnected;
            if (clientDisconnected != null)
                netService.ClientDisconnected -= clientDisconnected;
            if (hostDisconnected != null)
                netService.Disconnected -= hostDisconnected;
        }

        cancellation?.Cancel();
        udp?.Dispose();
        cancellation?.Dispose();
    }

    private async Task ListenAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var packet = await udp.ReceiveAsync(cancellationToken);
                if (!IsLocalAddress(packet.RemoteEndPoint.Address) ||
                    !DiscoveryProtocol.TryDecodeQuery(packet.Buffer, out var sequence))
                    continue;

                var playerCount = Math.Clamp(Volatile.Read(ref _currentPlayers), 1, _advertisement.MaxPlayers);
                var response = _advertisement with
                {
                    Sequence = sequence,
                    CurrentPlayers = playerCount,
                    AcceptingPlayers = playerCount < _advertisement.MaxPlayers
                };
                var bytes = DiscoveryProtocol.EncodeAdvertisement(response);
                await udp.SendAsync(bytes, packet.RemoteEndPoint, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception exception)
        {
            if (!cancellationToken.IsCancellationRequested)
                Log.Warn($"LAN discovery host stopped unexpectedly: {exception.Message}");
        }
    }

    private void DecrementPlayerCount()
    {
        while (true)
        {
            var current = Volatile.Read(ref _currentPlayers);
            if (current <= 1 || Interlocked.CompareExchange(ref _currentPlayers, current - 1, current) == current)
                return;
        }
    }

    private bool IsLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        var value = ToUInt32(bytes);
        return _localSubnets.Any(subnet => (value & subnet.Mask) == subnet.Network);
    }

    private static IReadOnlyList<(uint Network, uint Mask)> GetLocalSubnets()
    {
        var subnets = new HashSet<(uint Network, uint Mask)>();
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up)
                    continue;
                foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null)
                        continue;
                    var address = ToUInt32(unicast.Address.GetAddressBytes());
                    var mask = ToUInt32(unicast.IPv4Mask.GetAddressBytes());
                    if (mask != 0)
                        subnets.Add((address & mask, mask));
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        return subnets.ToArray();
    }

    private static uint ToUInt32(IReadOnlyList<byte> bytes)
    {
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }
}
