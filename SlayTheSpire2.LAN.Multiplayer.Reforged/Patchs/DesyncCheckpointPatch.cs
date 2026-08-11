using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer.Game;
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
        private static void Postfix(string __0, GameAction? __1)
        {
            if (!IsPlayerTurnStartContext(__0))
                return;

            CombatStateSnapshotService.Instance.CaptureTurnCheckpoint(__0, __1);
        }

        private static bool IsPlayerTurnStartContext(string? context)
        {
            return !string.IsNullOrWhiteSpace(context) &&
                   context.Contains("player turn start", StringComparison.OrdinalIgnoreCase);
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
    /// This prefix is deliberately observational: it records a field-level diff and shows it to the
    /// player, then allows the original game behavior to continue unchanged.
    /// </summary>
    [HarmonyPatch(typeof(RunManager), "StateDiverged")]
    internal static class RunManagerStateDivergedDiagnosticsPatch
    {
        private static void Prefix(ulong __0, NetFullCombatState __1)
        {
            var report = CombatStateSnapshotService.Instance.BuildDesyncReport(__0, __1);
            var logText = report.ToLogText();

            GD.PushError(logText);
            ShowDiagnosticDialog(report);
        }

        private static void ShowDiagnosticDialog(DesyncDiagnosticReport report)
        {
            try
            {
                if (Engine.GetMainLoop() is not SceneTree tree)
                    return;

                var root = tree.Root;
                var previous = root.GetNodeOrNull<AcceptDialog>("LanMultiplayerDesyncDiagnostic");
                previous?.QueueFree();

                var dialog = new AcceptDialog
                {
                    Name = "LanMultiplayerDesyncDiagnostic",
                    Title = "LAN Multiplayer - Desync detected",
                    DialogText = report.ToPlayerText(),
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
