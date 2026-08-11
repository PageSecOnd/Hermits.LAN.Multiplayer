namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Services
{
    /// <summary>
    /// Keeps the checkpoint service independent from the exact namespace import used by the game enum.
    /// This is intentionally tiny and only exposes the value needed by the diagnostics path.
    /// </summary>
    internal static class NetGameType
    {
        public const MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType Singleplayer =
            MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Singleplayer;
    }
}
