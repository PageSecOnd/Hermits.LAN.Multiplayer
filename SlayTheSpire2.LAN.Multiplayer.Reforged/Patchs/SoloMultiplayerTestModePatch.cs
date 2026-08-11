using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Patchs
{
    /// <summary>
    /// Developer-only test mode for exercising the real multiplayer run/combat path with only the local host.
    ///
    /// Vanilla StartRunLobby.IsAboutToBeginGame() deliberately returns false whenever a multiplayer lobby has
    /// exactly one player. This postfix relaxes only that one restriction. It does not add a fake player and it
    /// does not touch any network serializer, so the run still uses the normal Host/StartRunLobby/RunLobby path.
    /// </summary>
    [HarmonyPatch(typeof(StartRunLobby), "IsAboutToBeginGame")]
    internal static class SoloMultiplayerTestModePatch
    {
        private static void Postfix(StartRunLobby __instance, ref bool __result)
        {
            if (__result)
                return;

            // This test mode only applies to a real LAN host. Never make a client or single-player lobby start.
            if (__instance.NetService.Type != NetGameType.Host)
                return;

            if (__instance.Players.Count != 1)
                return;

            var localPlayer = __instance.LocalPlayer;
            if (!localPlayer.isReady)
                return;

            // Preserve vanilla's handshake safety gate. If a peer is in the middle of connecting, do not start.
            if (GetConnectingPlayerCount(__instance) != 0)
                return;

            __result = true;
            GD.Print("[LAN Multiplayer][SoloTest] Allowing one-player Host lobby to begin through the multiplayer run path.");
        }

        private static int GetConnectingPlayerCount(StartRunLobby lobby)
        {
            try
            {
                var connectingPlayers = Traverse.Create(lobby).Field("_connectingPlayers").GetValue();
                if (connectingPlayers == null)
                    return 0;

                var countProperty = connectingPlayers.GetType().GetProperty(
                    "Count",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                return countProperty?.GetValue(connectingPlayers) is int count ? count : int.MaxValue;
            }
            catch (Exception exception)
            {
                // Fail closed: if the vanilla handshake gate cannot be inspected, do not bypass it.
                GD.PushWarning($"[LAN Multiplayer][SoloTest] Could not inspect connecting players: {exception.Message}");
                return int.MaxValue;
            }
        }
    }
}
