namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Discovery;

internal sealed class DiscoveryQualityTracker
{
    private const int ProbeWindow = 20;
    private const int LatencyWindow = 8;

    private readonly SortedDictionary<int, bool> _probes = [];
    private readonly Queue<double> _latencies = [];

    public double AverageLatencyMs => _latencies.Count == 0 ? 0 : _latencies.Average();

    public double PacketLossPercent
    {
        get
        {
            // The newest probe may still be in flight. Score it only after the next
            // probe is sent so a healthy low-latency room does not flicker to 50% loss.
            var completed = _probes.Take(Math.Max(0, _probes.Count - 1)).Select(pair => pair.Value).ToArray();
            return completed.Length == 0
                ? 0
                : completed.Count(received => !received) * 100d / completed.Length;
        }
    }

    public ConnectionQuality Quality => CalculateQuality(AverageLatencyMs, PacketLossPercent);

    public void OnProbeSent(int sequence)
    {
        _probes.TryAdd(sequence, false);
        TrimProbes();
    }

    public bool OnResponse(int sequence, double latencyMs)
    {
        var duplicate = _probes.TryGetValue(sequence, out var received) && received;
        _probes[sequence] = true;
        TrimProbes();

        if (!duplicate && latencyMs >= 0 && latencyMs < 60_000)
        {
            _latencies.Enqueue(latencyMs);
            while (_latencies.Count > LatencyWindow)
                _latencies.Dequeue();
        }

        return !duplicate;
    }

    public static ConnectionQuality CalculateQuality(double latencyMs, double lossPercent)
    {
        if (latencyMs <= 40 && lossPercent <= 1)
            return ConnectionQuality.Excellent;
        if (latencyMs <= 100 && lossPercent <= 3)
            return ConnectionQuality.Good;
        if (latencyMs <= 200 && lossPercent <= 8)
            return ConnectionQuality.Fair;
        return ConnectionQuality.Poor;
    }

    private void TrimProbes()
    {
        while (_probes.Count > ProbeWindow)
            _probes.Remove(_probes.Keys.First());
    }
}

internal enum ConnectionQuality
{
    Excellent,
    Good,
    Fair,
    Poor
}
