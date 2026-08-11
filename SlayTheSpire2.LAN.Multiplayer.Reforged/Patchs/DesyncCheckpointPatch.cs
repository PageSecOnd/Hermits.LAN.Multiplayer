using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Checksums;
using MegaCrit.Sts2.Core.Runs;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Services;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Patchs
{
    /// <summary>
    /// The game already emits a checksum at the authoritative "After player turn start" boundary.
    /// Reuse that boundary for checkpoints instead of adding another patch to the turn/action executor.
    /// </summary>
    [HarmonyPatch(typeof(ChecksumTracker), "GenerateChecksum", new[] { typeof(string), typeof(GameAction) })]
    internal static class ChecksumTrackerTurnCheckpointPatch
    {
        private static void Postfix(string __0, GameAction? __1, NetChecksumData __result)
        {
            var service = CombatStateSnapshotService.Instance;
            service.ObserveChecksum(__result);

            if (!IsPlayerTurnStartContext(__0))
                return;

            service.CaptureTurnCheckpoint(__0, __1, __result);
        }

        private static bool IsPlayerTurnStartContext(string? context)
        {
            return !string.IsNullOrWhiteSpace(context) &&
                   context.Contains("player turn start", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Preserve the checksum ID carried by a peer's divergence message before the game escalates it to
    /// RunManager.StateDiverged, whose event signature no longer contains the checksum ID.
    /// </summary>
    [HarmonyPatch(typeof(ChecksumTracker), "OnReceivedStateDivergenceMessage")]
    internal static class ChecksumTrackerRemoteDivergencePatch
    {
        private static void Prefix(StateDivergenceMessage __0, ulong __1)
        {
            CombatStateSnapshotService.Instance.RememberRemoteDivergenceChecksum(__1, __0.senderChecksum.id);
        }
    }

    /// <summary>
    /// A new combat invalidates checkpoints from the previous combat.
    /// </summary>
    [HarmonyPatch(typeof(CombatManager), "SetUpCombat")]
    internal static class CombatManagerCheckpointResetPatch
    {
        private static void Prefix()
        {
            CombatStateSnapshotService.Instance.ResetForNewCombat();
        }
    }

    /// <summary>
    /// RunManager.StateDiverged is the game's existing escalation point after checksum disagreement.
    /// This prefix records a field-level diff and persists it before the vanilla disconnect/teardown path runs.
    /// </summary>
    [HarmonyPatch(typeof(RunManager), "StateDiverged")]
    internal static class RunManagerStateDivergedDiagnosticsPatch
    {
        private static void Prefix(ulong __0, NetFullCombatState __1)
        {
            var report = CombatStateSnapshotService.Instance.BuildDesyncReport(__0, __1);
            var logText = report.ToLogText();
            var reportPath = DesyncDiagnosticPersistence.Save(report);

            GD.PushError(logText);
            ShowDiagnosticDialog(report, reportPath);
        }

        private static void ShowDiagnosticDialog(DesyncDiagnosticReport report, string? reportPath)
        {
            try
            {
                if (Engine.GetMainLoop() is not SceneTree tree)
                    return;

                var root = tree.Root;
                var previous = root.GetNodeOrNull<AcceptDialog>("LanMultiplayerDesyncDiagnostic");
                previous?.QueueFree();

                var dialogText = report.ToPlayerText();
                if (!string.IsNullOrWhiteSpace(reportPath))
                    dialogText += $"{Environment.NewLine}{Environment.NewLine}Report saved to:{Environment.NewLine}{reportPath}";

                var dialog = new AcceptDialog
                {
                    Name = "LanMultiplayerDesyncDiagnostic",
                    Title = "LAN Multiplayer - Desync detected",
                    DialogText = dialogText,
                    MinSize = new Vector2I(720, 420),
                    Unresizable = false,
                    Exclusive = false
                };

                root.AddChild(dialog);
                dialog.PopupCentered(new Vector2I(760, 460));
            }
            catch (Exception exception)
            {
                GD.PushWarning($"[LAN Multiplayer] Could not show desync diagnostic dialog: {exception}");
            }
        }
    }
}
