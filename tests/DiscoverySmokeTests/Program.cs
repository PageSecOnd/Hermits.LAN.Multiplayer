using SlayTheSpire2.LAN.Multiplayer.Reforged.Discovery;
using System.Net;
using System.Net.Sockets;

var failures = new List<string>();

Check("query round-trip", () =>
{
    var bytes = DiscoveryProtocol.EncodeQuery(42);
    return DiscoveryProtocol.TryDecodeQuery(bytes, out var sequence) && sequence == 42;
});

Check("advertisement round-trip", () =>
{
    var expected = new DiscoveryAdvertisement(7, Guid.NewGuid(), 33771, 2, 4, true,
        "云林散人", "Standard", "stable", "2.1.0");
    var bytes = DiscoveryProtocol.EncodeAdvertisement(expected);
    return DiscoveryProtocol.TryDecodeAdvertisement(bytes, out var actual) && actual == expected;
});

Check("invalid packet rejected", () =>
{
    var bytes = DiscoveryProtocol.EncodeQuery(1);
    bytes[0] ^= 0xFF;
    return !DiscoveryProtocol.TryDecodeQuery(bytes, out _);
});

Check("oversize packet rejected", () =>
    !DiscoveryProtocol.TryDecodeAdvertisement(new byte[DiscoveryProtocol.MaxDatagramSize + 1], out _));

Check("rolling loss and latency", () =>
{
    var tracker = new DiscoveryQualityTracker();
    for (var sequence = 1; sequence <= 5; sequence++)
        tracker.OnProbeSent(sequence);
    tracker.OnResponse(1, 20);
    tracker.OnResponse(2, 30);
    tracker.OnResponse(4, 40);
    tracker.OnResponse(4, 2); // duplicate must not skew latency
    return Math.Abs(tracker.PacketLossPercent - 25) < 0.01 &&
           Math.Abs(tracker.AverageLatencyMs - 30) < 0.01 &&
           tracker.Quality == ConnectionQuality.Poor;
});

Check("quality thresholds", () =>
    DiscoveryQualityTracker.CalculateQuality(35, 0.5) == ConnectionQuality.Excellent &&
    DiscoveryQualityTracker.CalculateQuality(80, 2) == ConnectionQuality.Good &&
    DiscoveryQualityTracker.CalculateQuality(170, 6) == ConnectionQuality.Fair &&
    DiscoveryQualityTracker.CalculateQuality(220, 1) == ConnectionQuality.Poor);

Check("UDP loopback exchange", () => UdpLoopbackExchange().GetAwaiter().GetResult());

if (failures.Count > 0)
{
    Console.Error.WriteLine($"Discovery smoke tests failed: {string.Join(", ", failures)}");
    return 1;
}

Console.WriteLine("Discovery protocol smoke tests passed (7/7).");
return 0;

void Check(string name, Func<bool> test)
{
    try
    {
        if (!test())
            failures.Add(name);
    }
    catch (Exception exception)
    {
        failures.Add($"{name}: {exception.GetType().Name}");
    }
}

async Task<bool> UdpLoopbackExchange()
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    using var host = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    var hostEndpoint = (IPEndPoint)host.Client.LocalEndPoint!;

    await client.SendAsync(DiscoveryProtocol.EncodeQuery(99), hostEndpoint, cancellation.Token);
    var query = await host.ReceiveAsync(cancellation.Token);
    if (!DiscoveryProtocol.TryDecodeQuery(query.Buffer, out var sequence) || sequence != 99)
        return false;

    var expected = new DiscoveryAdvertisement(sequence, Guid.NewGuid(), 33771, 1, 4, true,
        "Loopback Host", "Standard", "stable", "2.1.0");
    await host.SendAsync(DiscoveryProtocol.EncodeAdvertisement(expected), query.RemoteEndPoint, cancellation.Token);
    var response = await client.ReceiveAsync(cancellation.Token);
    return DiscoveryProtocol.TryDecodeAdvertisement(response.Buffer, out var actual) && actual == expected;
}
