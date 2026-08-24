namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Compatibility;

/// <summary>
/// LAN changes transport only. Keeping the vanilla four-slot wire format avoids
/// patching lobby serialization, which is the most frequently changed game API.
/// </summary>
internal static class LanProtocolPolicy
{
    public const int MinPlayers = 2;
    public const int MaxPlayers = 4;

    public static int ClampPlayerCount(int playerCount) =>
        Math.Clamp(playerCount, MinPlayers, MaxPlayers);
}
