using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Services;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Patchs
{
    /// <summary>
    /// Developer diagnostic injector. Press F10 during a combat to compare the live local state against a
    /// shallow-cloned fake remote state whose lastExecutedActionId differs by one.
    ///
    /// This intentionally DOES NOT invoke RunManager.StateDiverged, so vanilla disconnect/teardown is not
    /// triggered. It exercises only the LAN mod's state diff + report persistence path.
    /// </summary>
    [HarmonyPatch(typeof(NGame), "_Input")]
    internal static class InjectTestDesyncPatch
    {
        private const ulong FakeRemotePlayerId = ulong.MaxValue - 10;
        private static bool _keyWasDown;

        private static void Postfix(InputEvent __0)
        {
            if (__0 is not InputEventKey keyEvent || keyEvent.Keycode != Key.F10)
                return;

            if (!keyEvent.Pressed)
            {
                _keyWasDown = false;
                return;
            }

            if (keyEvent.Echo || _keyWasDown)
                return;

            _keyWasDown = true;
            Inject();
        }

        private static void Inject()
        {
            try
            {
                var runManager = RunManager.Instance;
                if (!runManager.IsInProgress)
                {
                    GD.PushWarning("[LAN Multiplayer][DesyncTest] F10 ignored because no run is in progress.");
                    return;
                }

                var runState = Traverse.Create(runManager).Property("State").GetValue<RunState?>();
                if (runState == null)
                {
                    GD.PushWarning("[LAN Multiplayer][DesyncTest] F10 ignored because RunManager.State is unavailable.");
                    return;
                }

                var localState = NetFullCombatState.FromRun(runState, null!);
                var fakeRemoteState = Clone(localState);
                if (!BumpLastExecutedActionId(fakeRemoteState))
                {
                    GD.PushWarning("[LAN Multiplayer][DesyncTest] Could not mutate lastExecutedActionId; injection aborted.");
                    return;
                }

                var report = CombatStateSnapshotService.Instance.BuildDesyncReport(FakeRemotePlayerId, fakeRemoteState);
                var reportPath = DesyncDiagnosticPersistence.Save(report);

                GD.PushError("[LAN Multiplayer][DesyncTest] Injected synthetic state mismatch.\n" + report.ToLogText());
                if (!string.IsNullOrWhiteSpace(reportPath))
                    GD.Print($"[LAN Multiplayer][DesyncTest] Synthetic desync report: {reportPath}");
            }
            catch (Exception exception)
            {
                // A developer hotkey must never be able to interrupt the live combat.
                GD.PushWarning($"[LAN Multiplayer][DesyncTest] Synthetic desync injection failed: {exception}");
            }
        }

        private static NetFullCombatState Clone(NetFullCombatState source)
        {
            var memberwiseClone = typeof(object).GetMethod(
                "MemberwiseClone",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            return (NetFullCombatState)memberwiseClone.Invoke(source, null)!;
        }

        private static bool BumpLastExecutedActionId(NetFullCombatState state)
        {
            var field = typeof(NetFullCombatState).GetField(
                "lastExecutedActionId",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                return false;

            var value = field.GetValue(state);
            object? nextValue = value switch
            {
                byte v => unchecked((byte)(v + 1)),
                sbyte v => unchecked((sbyte)(v + 1)),
                short v => unchecked((short)(v + 1)),
                ushort v => unchecked((ushort)(v + 1)),
                int v => unchecked(v + 1),
                uint v => unchecked(v + 1),
                long v => unchecked(v + 1),
                ulong v => unchecked(v + 1),
                _ => null
            };

            if (nextValue == null)
                return false;

            field.SetValue(state, nextValue);
            return true;
        }
    }
}
