namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Discovery;

internal sealed record DiscoveredRoom(
    Guid HostId,
    string Address,
    ushort GamePort,
    string HostName,
    string GameMode,
    string GameChannel,
    string ModVersion,
    int CurrentPlayers,
    int MaxPlayers,
    bool AcceptingPlayers,
    double LatencyMs,
    double PacketLossPercent,
    ConnectionQuality Quality,
    DateTime LastSeenUtc);
