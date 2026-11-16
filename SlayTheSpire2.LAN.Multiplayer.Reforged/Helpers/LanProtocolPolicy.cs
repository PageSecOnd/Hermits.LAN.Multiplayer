namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Helpers
{
    /// <summary>
    /// Keeps LAN transport changes separate from the game's multiplayer wire format.
    ///
    /// STS2 currently uses a two-bit lobby slot id, so the vanilla protocol safely
    /// represents four lobby slots. Staying within that limit lets the game own all
    /// lobby message serialization/deserialization, which greatly reduces conflicts
    /// with other mods and future game updates.
    /// </summary>
    internal static class LanProtocolPolicy
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 4;

        public static int ClampPlayerCount(int playerCount)
        {
            return Math.Clamp(playerCount, MinPlayers, MaxPlayers);
        }
    }
}
