using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MegaCrit.Sts2.Core.Logging;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Discovery;

internal sealed class LanDiscoveryClient : IDisposable
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RoomTimeout = TimeSpan.FromSeconds(4);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, RoomState> _rooms = [];
    private readonly Dictionary<int, long> _sentAt = [];
    private CancellationTokenSource? _cancellation;
    private UdpClient? _udp;
    private int _sequence;

    public bool Start()
    {
        Stop();
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            var cancellation = new CancellationTokenSource();
            _udp = udp;
            _cancellation = cancellation;
            _ = Task.Run(() => SendLoopAsync(udp, cancellation.Token));
            _ = Task.Run(() => ReceiveLoopAsync(udp, cancellation.Token));
            return true;
        }
        catch (Exception exception)
        {
            Log.Warn($"LAN discovery scan could not start: {exception.Message}");
            Stop();
            return false;
        }
    }

    public IReadOnlyList<DiscoveredRoom> GetRooms()
    {
        lock (_gate)
        {
            var cutoff = DateTime.UtcNow - RoomTimeout;
            foreach (var hostId in _rooms.Where(pair => pair.Value.LastSeenUtc < cutoff)
                         .Select(pair => pair.Key).ToArray())
                _rooms.Remove(hostId);

            return _rooms.Values.Select(state => state.ToRoom()).OrderBy(room => room.Quality)
                .ThenBy(room => room.LatencyMs).ThenBy(room => room.HostName).ToArray();
        }
    }

    public void Stop()
    {
        var cancellation = Interlocked.Exchange(ref _cancellation, null);
        var udp = Interlocked.Exchange(ref _udp, null);
        cancellation?.Cancel();
        udp?.Dispose();
        cancellation?.Dispose();
        lock (_gate)
        {
            _rooms.Clear();
            _sentAt.Clear();
        }
    }

    public void Dispose() => Stop();

    private async Task SendLoopAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var sequence = Interlocked.Increment(ref _sequence);
                var sentAt = Stopwatch.GetTimestamp();
                lock (_gate)
                {
                    _sentAt[sequence] = sentAt;
                    foreach (var room in _rooms.Values)
                        room.Quality.OnProbeSent(sequence);
                    foreach (var old in _sentAt.Keys.Where(key => key < sequence - 32).ToArray())
                        _sentAt.Remove(old);
                }

                var query = DiscoveryProtocol.EncodeQuery(sequence);
                foreach (var address in GetBroadcastAddresses())
                {
                    try
                    {
                        await udp.SendAsync(query, new IPEndPoint(address, DiscoveryProtocol.Port), cancellationToken);
                    }
                    catch (SocketException)
                    {
                    }
                }

                await Task.Delay(ProbeInterval, cancellationToken);
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
                Log.Warn($"LAN discovery probe loop stopped unexpectedly: {exception.Message}");
        }
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var packet = await udp.ReceiveAsync(cancellationToken);
                if (!DiscoveryProtocol.TryDecodeAdvertisement(packet.Buffer, out var advertisement))
                    continue;

                var now = Stopwatch.GetTimestamp();
                lock (_gate)
                {
                    if (!_sentAt.TryGetValue(advertisement.Sequence, out var sentAt))
                        continue;

                    var latency = Stopwatch.GetElapsedTime(sentAt, now).TotalMilliseconds;
                    if (!_rooms.TryGetValue(advertisement.HostId, out var state))
                    {
                        state = new RoomState(advertisement.HostId);
                        _rooms.Add(advertisement.HostId, state);
                    }

                    state.Quality.OnResponse(advertisement.Sequence, latency);
                    state.Address = packet.RemoteEndPoint.Address.ToString();
                    state.Advertisement = advertisement;
                    state.LastSeenUtc = DateTime.UtcNow;
                }
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
                Log.Warn($"LAN discovery receive loop stopped unexpectedly: {exception.Message}");
        }
    }

    private static IReadOnlyList<IPAddress> GetBroadcastAddresses()
    {
        var addresses = new HashSet<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
        try
        {
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up ||
                    network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask == null)
                        continue;

                    var address = unicast.Address.GetAddressBytes();
                    var mask = unicast.IPv4Mask.GetAddressBytes();
                    var broadcast = new byte[4];
                    for (var index = 0; index < broadcast.Length; index++)
                        broadcast[index] = (byte)(address[index] | ~mask[index]);
                    addresses.Add(new IPAddress(broadcast));
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        return addresses.ToArray();
    }

    private sealed class RoomState(Guid hostId)
    {
        public Guid HostId { get; } = hostId;
        public string Address { get; set; } = string.Empty;
        public DiscoveryAdvertisement Advertisement { get; set; }
        public DiscoveryQualityTracker Quality { get; } = new();
        public DateTime LastSeenUtc { get; set; }

        public DiscoveredRoom ToRoom() => new(
            HostId,
            Address,
            Advertisement.GamePort,
            Advertisement.HostName,
            Advertisement.GameMode,
            Advertisement.GameChannel,
            Advertisement.ModVersion,
            Advertisement.CurrentPlayers,
            Advertisement.MaxPlayers,
            Advertisement.AcceptingPlayers,
            Quality.AverageLatencyMs,
            Quality.PacketLossPercent,
            Quality.Quality,
            LastSeenUtc);
    }
}
