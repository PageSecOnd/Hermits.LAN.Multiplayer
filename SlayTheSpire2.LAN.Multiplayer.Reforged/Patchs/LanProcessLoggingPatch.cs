using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Services;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Patchs
{
    [HarmonyPatch(typeof(NetHostGameService), "StartENetHost")]
    internal static class LanProcessLogHostPatch
    {
        private static void Prefix(ushort __0, int __1)
        {
            var log = LanProcessLogService.Instance;
            log.SetRole("host");
            log.Info($"Starting ENet host. port={__0} peerCapacity={__1}");
        }
    }

    [HarmonyPatch(typeof(JoinFlow), "AttemptJoin")]
    internal static class LanProcessLogClientJoinPatch
    {
        private static void Prefix()
        {
            var log = LanProcessLogService.Instance;
            log.SetRole("client");
            log.Info("AttemptJoin started.");
        }
    }

    [HarmonyPatch(typeof(JoinFlow), "AttemptLoadJoin")]
    internal static class LanProcessLogClientLoadJoinPatch
    {
        private static void Prefix()
        {
            var log = LanProcessLogService.Instance;
            log.SetRole("client");
            log.Info("AttemptLoadJoin started.");
        }
    }

    [HarmonyPatch(typeof(JoinFlow), "AttemptRejoin")]
    internal static class LanProcessLogClientRejoinPatch
    {
        private static void Prefix()
        {
            var log = LanProcessLogService.Instance;
            log.SetRole("client");
            log.Info("AttemptRejoin started.");
        }
    }

    [HarmonyPatch(typeof(StartRunLobby), "BeginRunForAllPlayers")]
    internal static class LanProcessLogBeginRunPatch
    {
        private static void Prefix(StartRunLobby __instance)
        {
            LanProcessLogService.Instance.Info(
                $"Beginning multiplayer run. players={__instance.Players.Count} netType={__instance.NetService.Type}");
        }
    }

    [HarmonyPatch(typeof(CombatManager), "SetUpCombat")]
    internal static class LanProcessLogCombatStartPatch
    {
        private static void Prefix()
        {
            LanProcessLogService.Instance.Info("Combat setup started.");
        }
    }

    [HarmonyPatch(typeof(ChecksumTracker), "GenerateChecksum", new[] { typeof(string), typeof(GameAction) })]
    internal static class LanProcessLogChecksumPatch
    {
        private static void Postfix(string __0, NetChecksumData __result)
        {
            LanProcessLogService.Instance.Info($"Checksum generated. id={__result.id} context={__0}");
        }
    }

    [HarmonyPatch(typeof(RunManager), "StateDiverged")]
    internal static class LanProcessLogDivergencePatch
    {
        private static void Prefix(ulong __0)
        {
            LanProcessLogService.Instance.Error($"State divergence reported by/for peer={__0}.");
        }
    }
}
