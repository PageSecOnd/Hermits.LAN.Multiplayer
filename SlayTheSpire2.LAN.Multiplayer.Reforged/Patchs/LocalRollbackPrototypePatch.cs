using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Services;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Patchs
{
    /// <summary>
    /// Developer-only local rollback trigger.
    ///
    /// F11 restores the newest captured player-turn checkpoint on the current process only. It never sends a
    /// network message and never suppresses the game's real desync handling. This keeps rollback experimentation
    /// isolated until the mutation path is proven safe enough to coordinate across peers.
    /// </summary>
    [HarmonyPatch(typeof(NGame), "_Input")]
    internal static class LocalRollbackPrototypePatch
    {
        private static bool _keyWasDown;

        private static void Postfix(InputEvent __0)
        {
            if (__0 is not InputEventKey keyEvent || keyEvent.Keycode != Key.F11)
                return;

            if (!keyEvent.Pressed)
            {
                _keyWasDown = false;
                return;
            }

            if (keyEvent.Echo || _keyWasDown)
                return;

            _keyWasDown = true;
            var report = CombatRollbackPrototypeService.Instance.RestoreLatestCheckpoint();
            ShowReport(report);
        }

        private static void ShowReport(RollbackPrototypeReport report)
        {
            try
            {
                if (Engine.GetMainLoop() is not SceneTree tree)
                    return;

                var root = tree.Root;
                var previous = root.GetNodeOrNull<AcceptDialog>("LanMultiplayerRollbackPrototype");
                previous?.QueueFree();

                var dialog = new AcceptDialog
                {
                    Name = "LanMultiplayerRollbackPrototype",
                    Title = report.Status switch
                    {
                        RollbackPrototypeStatus.Restored => "LAN Multiplayer - Rollback prototype: restored",
                        RollbackPrototypeStatus.Partial => "LAN Multiplayer - Rollback prototype: partial restore",
                        _ => "LAN Multiplayer - Rollback prototype: not restored"
                    },
                    DialogText = report.ToPlayerText(),
                    MinSize = new Vector2I(760, 500),
                    Unresizable = false,
                    Exclusive = false
                };

                root.AddChild(dialog);
                dialog.PopupCentered(new Vector2I(800, 560));
            }
            catch (Exception exception)
            {
                GD.PushWarning($"[LAN Multiplayer][RollbackPrototype] Could not show rollback dialog: {exception}");
            }
        }
    }
}
