using System.Collections;
using System.Globalization;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Services
{
    /// <summary>
    /// Keeps a small ring buffer of detached combat-state snapshots. The snapshots are intentionally
    /// read-only for now: they provide a safe foundation for a later rollback implementation without
    /// changing the game's action execution or network protocol in this version.
    /// </summary>
    internal sealed class CombatStateSnapshotService
    {
        private const int MaxCheckpoints = 5;
        private const int MaxReportedDifferences = 48;

        private static readonly Lazy<CombatStateSnapshotService> Lazy = new(() => new CombatStateSnapshotService());
        public static CombatStateSnapshotService Instance => Lazy.Value;

        private readonly LinkedList<CombatCheckpoint> _checkpoints = new();
        private readonly object _gate = new();
        private long _nextSequence;

        private CombatStateSnapshotService()
        {
        }

        public void ResetForNewCombat()
        {
            lock (_gate)
            {
                _checkpoints.Clear();
                _nextSequence = 0;
            }

            GD.Print("[LAN Multiplayer] Cleared combat checkpoint history for a new combat.");
        }

        public void CaptureTurnCheckpoint(string context, GameAction? action)
        {
            try
            {
                var runManager = RunManager.Instance;
                if (!runManager.IsInProgress || runManager.NetService.Type == NetGameType.Singleplayer)
                    return;

                var state = NetFullCombatState.FromRun(runManager.State, action!);
                var flattened = CombatStateFlattener.Flatten(state);
                CombatCheckpoint checkpoint;

                lock (_gate)
                {
                    checkpoint = new CombatCheckpoint(
                        ++_nextSequence,
                        DateTimeOffset.UtcNow,
                        context,
                        state,
                        flattened);

                    _checkpoints.AddLast(checkpoint);
                    while (_checkpoints.Count > MaxCheckpoints)
                        _checkpoints.RemoveFirst();
                }

                GD.Print(
                    $"[LAN Multiplayer] Captured combat checkpoint #{checkpoint.Sequence} ({context}); " +
                    $"retaining the latest {GetCheckpointCount()}/{MaxCheckpoints} checkpoint(s).");
            }
            catch (Exception exception)
            {
                // Checkpointing must never be able to interrupt normal combat.
                GD.PushWarning($"[LAN Multiplayer] Failed to capture combat checkpoint: {exception}");
            }
        }

        public DesyncDiagnosticReport BuildDesyncReport(ulong remotePlayerId, NetFullCombatState remoteState)
        {
            try
            {
                var localState = NetFullCombatState.FromRun(RunManager.Instance.State, null!);
                var localSnapshot = CombatStateFlattener.Flatten(localState);
                var remoteSnapshot = CombatStateFlattener.Flatten(remoteState);
                var differences = CombatStateFlattener.Diff(
                    localSnapshot,
                    remoteSnapshot,
                    MaxReportedDifferences,
                    out var totalDifferenceCount);

                CombatCheckpoint? latestCheckpoint;
                int checkpointCount;
                lock (_gate)
                {
                    latestCheckpoint = _checkpoints.Last?.Value;
                    checkpointCount = _checkpoints.Count;
                }

                return new DesyncDiagnosticReport(
                    remotePlayerId,
                    DateTimeOffset.UtcNow,
                    totalDifferenceCount,
                    differences,
                    checkpointCount,
                    latestCheckpoint?.Sequence,
                    latestCheckpoint?.CapturedAtUtc,
                    latestCheckpoint?.Context);
            }
            catch (Exception exception)
            {
                return new DesyncDiagnosticReport(
                    remotePlayerId,
                    DateTimeOffset.UtcNow,
                    -1,
                    Array.Empty<StateDifference>(),
                    GetCheckpointCount(),
                    null,
                    null,
                    null,
                    exception.ToString());
            }
        }

        public IReadOnlyList<CombatCheckpoint> GetRecentCheckpoints()
        {
            lock (_gate)
                return _checkpoints.Reverse().ToArray();
        }

        private int GetCheckpointCount()
        {
            lock (_gate)
                return _checkpoints.Count;
        }
    }

    internal sealed record CombatCheckpoint(
        long Sequence,
        DateTimeOffset CapturedAtUtc,
        string Context,
        NetFullCombatState State,
        IReadOnlyDictionary<string, string> Snapshot);

    internal sealed record StateDifference(string Path, string LocalValue, string RemoteValue);

    internal sealed record DesyncDiagnosticReport(
        ulong RemotePlayerId,
        DateTimeOffset DetectedAtUtc,
        int TotalDifferenceCount,
        IReadOnlyList<StateDifference> Differences,
        int CheckpointCount,
        long? LatestCheckpointSequence,
        DateTimeOffset? LatestCheckpointAtUtc,
        string? LatestCheckpointContext,
        string? DiagnosticFailure = null)
    {
        public string ToLogText()
        {
            if (DiagnosticFailure != null)
            {
                return $"[LAN Multiplayer] Desync detected with player {RemotePlayerId}, but detailed comparison failed. " +
                       $"Stored checkpoints: {CheckpointCount}. Error: {DiagnosticFailure}";
            }

            var lines = new List<string>
            {
                $"[LAN Multiplayer] Multiplayer state divergence detected with player {RemotePlayerId}.",
                $"Detected at UTC: {DetectedAtUtc:O}",
                $"Different state values: {TotalDifferenceCount}",
                $"Stored turn checkpoints: {CheckpointCount}"
            };

            if (LatestCheckpointSequence.HasValue)
            {
                lines.Add(
                    $"Latest checkpoint: #{LatestCheckpointSequence} at {LatestCheckpointAtUtc:O} " +
                    $"({LatestCheckpointContext})");
            }

            if (Differences.Count == 0)
            {
                lines.Add("No field-level difference could be extracted from the supplied combat states.");
            }
            else
            {
                lines.Add("State differences (local | remote):");
                foreach (var difference in Differences)
                    lines.Add($"  {difference.Path}: {difference.LocalValue} | {difference.RemoteValue}");

                if (TotalDifferenceCount > Differences.Count)
                    lines.Add($"  ... {TotalDifferenceCount - Differences.Count} additional difference(s) omitted.");
            }

            return string.Join(Environment.NewLine, lines);
        }

        public string ToPlayerText()
        {
            if (DiagnosticFailure != null)
            {
                return "Multiplayer data is out of sync. Detailed comparison could not be generated, but turn " +
                       $"checkpoints are available: {CheckpointCount}. See godot.log for details.";
            }

            var lines = new List<string>
            {
                "Multiplayer data is out of sync.",
                $"Peer: {RemotePlayerId}",
                $"Detected differences: {TotalDifferenceCount}",
                $"Saved turn checkpoints: {CheckpointCount}"
            };

            if (LatestCheckpointSequence.HasValue)
                lines.Add($"Latest checkpoint: #{LatestCheckpointSequence} ({LatestCheckpointContext})");

            if (Differences.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("First differences (local / remote):");
                foreach (var difference in Differences.Take(8))
                    lines.Add($"• {ShortenPath(difference.Path)}: {difference.LocalValue} / {difference.RemoteValue}");
            }

            lines.Add(string.Empty);
            lines.Add("The current version records diagnostics and checkpoints only; automatic rollback is not enabled yet.");
            return string.Join(Environment.NewLine, lines);
        }

        private static string ShortenPath(string path)
        {
            const int maxLength = 72;
            return path.Length <= maxLength ? path : "…" + path[^ (maxLength - 1)..];
        }
    }

    /// <summary>
    /// Reflection-based flattener for NetFullCombatState. It deliberately depends on field shape rather than
    /// concrete nested state types, making diagnostics tolerant of game updates that add state fields.
    /// </summary>
    internal static class CombatStateFlattener
    {
        private const int MaxDepth = 12;
        private const int MaxValues = 12000;

        public static IReadOnlyDictionary<string, string> Flatten(object? root)
        {
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            Walk(root, "state", 0, values, visited);
            return values;
        }

        public static IReadOnlyList<StateDifference> Diff(
            IReadOnlyDictionary<string, string> local,
            IReadOnlyDictionary<string, string> remote,
            int maxReturned,
            out int totalDifferenceCount)
        {
            var allKeys = local.Keys
                .Concat(remote.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(DifferencePriority)
                .ThenBy(key => key, StringComparer.Ordinal);

            var result = new List<StateDifference>();
            totalDifferenceCount = 0;

            foreach (var key in allKeys)
            {
                var localValue = local.TryGetValue(key, out var localText) ? localText : "<missing>";
                var remoteValue = remote.TryGetValue(key, out var remoteText) ? remoteText : "<missing>";
                if (string.Equals(localValue, remoteValue, StringComparison.Ordinal))
                    continue;

                totalDifferenceCount++;
                if (result.Count < maxReturned)
                    result.Add(new StateDifference(key, localValue, remoteValue));
            }

            return result;
        }

        private static void Walk(
            object? value,
            string path,
            int depth,
            IDictionary<string, string> output,
            ISet<object> visited)
        {
            if (output.Count >= MaxValues)
                return;

            if (value == null)
            {
                output[path] = "<null>";
                return;
            }

            var type = value.GetType();
            if (IsScalar(type))
            {
                output[path] = FormatScalar(value);
                return;
            }

            if (depth >= MaxDepth)
            {
                output[path] = $"<{type.Name}:depth-limit>";
                return;
            }

            if (!type.IsValueType && !visited.Add(value))
            {
                output[path] = $"<{type.Name}:cycle>";
                return;
            }

            if (value is IDictionary dictionary)
            {
                var entries = new List<(string Key, object? Value)>();
                foreach (DictionaryEntry entry in dictionary)
                    entries.Add((FormatScalar(entry.Key ?? "<null>"), entry.Value));

                foreach (var entry in entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                    Walk(entry.Value, $"{path}[{entry.Key}]", depth + 1, output, visited);

                if (entries.Count == 0)
                    output[path] = "<empty>";
                return;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                var index = 0;
                foreach (var item in enumerable)
                {
                    Walk(item, $"{path}[{index}]", depth + 1, output, visited);
                    if (++index >= MaxValues || output.Count >= MaxValues)
                        break;
                }

                if (index == 0)
                    output[path] = "<empty>";
                return;
            }

            var fields = type
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => !field.IsStatic)
                .OrderBy(field => NormalizeFieldName(field.Name), StringComparer.Ordinal)
                .ToArray();

            if (fields.Length == 0)
            {
                output[path] = FormatScalar(value);
                return;
            }

            foreach (var field in fields)
            {
                object? child;
                try
                {
                    child = field.GetValue(value);
                }
                catch (Exception exception)
                {
                    output[$"{path}.{NormalizeFieldName(field.Name)}"] = $"<unreadable:{exception.GetType().Name}>";
                    continue;
                }

                Walk(
                    child,
                    $"{path}.{NormalizeFieldName(field.Name)}",
                    depth + 1,
                    output,
                    visited);
            }
        }

        private static bool IsScalar(Type type)
        {
            return type.IsPrimitive ||
                   type.IsEnum ||
                   type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(DateTime) ||
                   type == typeof(DateTimeOffset) ||
                   type == typeof(TimeSpan) ||
                   type == typeof(Guid);
        }

        private static string FormatScalar(object value)
        {
            return value switch
            {
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "<null>",
                _ => value.ToString() ?? "<null>"
            };
        }

        private static string NormalizeFieldName(string name)
        {
            const string backingFieldSuffix = ">k__BackingField";
            if (name.StartsWith('<') && name.EndsWith(backingFieldSuffix, StringComparison.Ordinal))
                return name[1..^backingFieldSuffix.Length];
            return name;
        }

        private static int DifferencePriority(string path)
        {
            var lower = path.ToLowerInvariant();
            if (lower.Contains("currenthp") || lower.Contains("maxhp") || lower.Contains("block")) return 0;
            if (lower.Contains("turnnumber") || lower.Contains("phase") || lower.Contains("energy")) return 1;
            if (lower.Contains("piles") || lower.Contains("cards") || lower.Contains("powers")) return 2;
            if (lower.Contains("rng")) return 3;
            if (lower.Contains("relic") || lower.Contains("potion") || lower.Contains("orb")) return 4;
            return 10;
        }
    }
}
