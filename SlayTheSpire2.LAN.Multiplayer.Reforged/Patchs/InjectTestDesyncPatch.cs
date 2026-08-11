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
    /// shallow-cloned fake remote state whose lastExecutedActionId differs from the local snapshot.
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
                if (!BumpLastExecutedActionId(fakeRemoteState, out var before, out var after))
                {
                    GD.PushWarning("[LAN Multiplayer][DesyncTest] Could not mutate lastExecutedActionId; injection aborted.");
                    return;
                }

                GD.Print($"[LAN Multiplayer][DesyncTest] Injecting synthetic mismatch: lastExecutedActionId {before} -> {after}.");

                var report = CombatStateSnapshotService.Instance.BuildDesyncReport(FakeRemotePlayerId, fakeRemoteState);
                var reportPath = DesyncDiagnosticPersistence.Save(report);

                GD.PushError("[LAN Multiplayer][DesyncTest] Injected synthetic state mismatch.\n" + report.ToLogText());
                if (!string.IsNullOrWhiteSpace(reportPath))
                    GD.Print($"[LAN Multiplayer][DesyncTest] Synthetic desync report: {reportPath}");

                ShowSyntheticDialog(report, reportPath, before, after);
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

        private static bool BumpLastExecutedActionId(NetFullCombatState state, out string before, out string after)
        {
            before = "<unknown>";
            after = "<unknown>";

            var field = typeof(NetFullCombatState).GetField(
                "lastExecutedActionId",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                return false;

            var value = field.GetValue(state);
            before = value?.ToString() ?? "null";

            var valueType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
            object? nextValue = value == null
                ? OneForIntegralType(valueType)
                : IncrementIntegralValue(value, valueType);

            if (nextValue == null)
                return false;

            field.SetValue(state, nextValue);
            after = field.GetValue(state)?.ToString() ?? "null";
            return after != before;
        }

        private static object? OneForIntegralType(Type type)
        {
            if (type == typeof(byte)) return (byte)1;
            if (type == typeof(sbyte)) return (sbyte)1;
            if (type == typeof(short)) return (short)1;
            if (type == typeof(ushort)) return (ushort)1;
            if (type == typeof(int)) return 1;
            if (type == typeof(uint)) return 1u;
            if (type == typeof(long)) return 1L;
            if (type == typeof(ulong)) return 1UL;
            return null;
        }

        private static object? IncrementIntegralValue(object value, Type type)
        {
            if (type == typeof(byte)) return unchecked((byte)((byte)value + 1));
            if (type == typeof(sbyte)) return unchecked((sbyte)((sbyte)value + 1));
            if (type == typeof(short)) return unchecked((short)((short)value + 1));
            if (type == typeof(ushort)) return unchecked((ushort)((ushort)value + 1));
            if (type == typeof(int)) return unchecked((int)value + 1);
            if (type == typeof(uint)) return unchecked((uint)value + 1u);
            if (type == typeof(long)) return unchecked((long)value + 1L);
            if (type == typeof(ulong)) return unchecked((ulong)value + 1UL);
            return null;
        }

        private static void ShowSyntheticDialog(
            DesyncDiagnosticReport report,
            string? reportPath,
            string before,
            string after)
        {
            try
            {
                if (Engine.GetMainLoop() is not SceneTree tree)
                    return;

                var root = tree.Root;
                var previous = root.GetNodeOrNull<AcceptDialog>("LanMultiplayerSyntheticDesyncTest");
                previous?.QueueFree();

                var dialogText =
                    $"Synthetic desync injected successfully.{System.Environment.NewLine}" +
                    $"lastExecutedActionId: {before} -> {after}{System.Environment.NewLine}{System.Environment.NewLine}" +
                    report.ToPlayerText();

                if (!string.IsNullOrWhiteSpace(reportPath))
                    dialogText += $"{System.Environment.NewLine}{System.Environment.NewLine}Report saved to:{System.Environment.NewLine}{reportPath}";

                var dialog = new AcceptDialog
                {
                    Name = "LanMultiplayerSyntheticDesyncTest",
                    Title = "LAN Multiplayer - Synthetic desync test",
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
                GD.PushWarning($"[LAN Multiplayer][DesyncTest] Could not show synthetic desync dialog: {exception}");
            }
        }
    }
}
