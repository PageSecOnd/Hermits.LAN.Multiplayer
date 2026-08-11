using System.Collections;
using System.Globalization;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Services
{
    /// <summary>
    /// Developer-only, local combat rollback prototype.
    /// F11 restores the newest player-turn checkpoint on the current process and then verifies the
    /// reconstructed NetFullCombatState against the stored checkpoint. It does not participate in
    /// real desync recovery yet.
    /// </summary>
    internal sealed class CombatRollbackPrototypeService
    {
        private const int MaxReportedDifferences = 32;

        private static readonly Lazy<CombatRollbackPrototypeService> Lazy =
            new(() => new CombatRollbackPrototypeService());

        public static CombatRollbackPrototypeService Instance => Lazy.Value;

        private CombatRollbackPrototypeService()
        {
        }

        public RollbackPrototypeReport RestoreLatestCheckpoint()
        {
            var checkpoint = CombatStateSnapshotService.Instance.GetRecentCheckpoints().FirstOrDefault();
            if (checkpoint == null)
            {
                return RollbackPrototypeReport.Failed(
                    "No combat checkpoint is available yet. Wait until a player turn has started, then press F11 again.");
            }

            return RestoreCheckpoint(checkpoint);
        }

        private RollbackPrototypeReport RestoreCheckpoint(CombatCheckpoint checkpoint)
        {
            var applied = new List<string>();
            var warnings = new List<string>();
            var runManager = RunManager.Instance;

            if (!runManager.IsInProgress)
                return RollbackPrototypeReport.Failed("No run is currently in progress.", checkpoint);

            var runState = Traverse.Create(runManager).Property("State").GetValue<RunState?>();
            if (runState == null)
                return RollbackPrototypeReport.Failed("RunManager.State is unavailable.", checkpoint);

            var actionExecutor = GetMember(runManager, "ActionExecutor");
            var currentlyRunningAction = actionExecutor == null
                ? null
                : GetMember(actionExecutor, "CurrentlyRunningAction");

            if (currentlyRunningAction != null)
            {
                return RollbackPrototypeReport.Failed(
                    "An action is currently executing. F11 only restores while combat is idle.",
                    checkpoint);
            }

            var pausedExecutor = false;
            var pausedQueues = false;

            try
            {
                if (actionExecutor != null && TryInvoke(actionExecutor, "Pause", Array.Empty<object?>(), out _))
                {
                    pausedExecutor = true;
                    applied.Add("Paused ActionExecutor");
                }

                var actionQueueSet = GetMember(runManager, "ActionQueueSet");
                if (actionQueueSet != null &&
                    TryInvoke(actionQueueSet, "PauseAllPlayerQueues", Array.Empty<object?>(), out _))
                {
                    pausedQueues = true;
                    applied.Add("Paused player action queues");
                }

                // v0.110.1 owns the live CombatState in CombatManager, not RunState.
                // DebugOnlyGetState() is a public instance method and is the authoritative way to obtain it.
                var combatState = CombatManager.Instance.DebugOnlyGetState();
                if (combatState == null)
                {
                    return RollbackPrototypeReport.Failed(
                        "CombatManager.DebugOnlyGetState() returned null.",
                        checkpoint);
                }

                applied.Add("Located live CombatState through CombatManager.DebugOnlyGetState()");

                RestoreCreatures(combatState, checkpoint.State, applied, warnings);
                RestorePlayers(runState, combatState, checkpoint.State, applied, warnings);
                RestoreRunRng(runState, checkpoint.State, applied, warnings);

                warnings.Add(
                    "Power, potion, relic, orb and network sequence reconstruction is intentionally deferred in this prototype.");

                var afterState = NetFullCombatState.FromRun(runState, null!);
                var afterSnapshot = FilterVolatileCheckpointFields(CombatStateFlattener.Flatten(afterState));
                var expectedSnapshot = FilterVolatileCheckpointFields(checkpoint.Snapshot);
                var differences = CombatStateFlattener.Diff(
                    expectedSnapshot,
                    afterSnapshot,
                    MaxReportedDifferences,
                    out var totalDifferences);

                var status = totalDifferences == 0
                    ? RollbackPrototypeStatus.Restored
                    : RollbackPrototypeStatus.Partial;

                var report = new RollbackPrototypeReport(
                    status,
                    checkpoint.Sequence,
                    checkpoint.ChecksumId,
                    checkpoint.Context,
                    applied.ToArray(),
                    warnings.ToArray(),
                    totalDifferences,
                    differences,
                    null);

                if (status == RollbackPrototypeStatus.Restored)
                    GD.Print(report.ToLogText());
                else
                    GD.PushWarning(report.ToLogText());

                return report;
            }
            catch (Exception exception)
            {
                var report = new RollbackPrototypeReport(
                    RollbackPrototypeStatus.Failed,
                    checkpoint.Sequence,
                    checkpoint.ChecksumId,
                    checkpoint.Context,
                    applied.ToArray(),
                    warnings.ToArray(),
                    -1,
                    Array.Empty<StateDifference>(),
                    exception.ToString());

                GD.PushWarning(report.ToLogText());
                return report;
            }
            finally
            {
                try
                {
                    var actionQueueSet = GetMember(runManager, "ActionQueueSet");
                    if (pausedQueues && actionQueueSet != null)
                        TryInvoke(actionQueueSet, "UnpauseAllPlayerQueues", Array.Empty<object?>(), out _);
                }
                catch (Exception exception)
                {
                    GD.PushWarning(
                        $"[LAN Multiplayer][RollbackPrototype] Could not unpause player queues: {exception}");
                }

                try
                {
                    if (pausedExecutor && actionExecutor != null)
                        TryInvoke(actionExecutor, "Unpause", Array.Empty<object?>(), out _);
                }
                catch (Exception exception)
                {
                    GD.PushWarning(
                        $"[LAN Multiplayer][RollbackPrototype] Could not unpause ActionExecutor: {exception}");
                }
            }
        }

        private static void RestoreCreatures(
            object combatState,
            NetFullCombatState checkpointState,
            ICollection<string> applied,
            ICollection<string> warnings)
        {
            var sourceCreatures = EnumerateValues(GetMember(checkpointState, "Creatures", "creatures")).ToArray();
            var liveCreatures = EnumerateValues(GetMember(combatState, "Creatures", "creatures")).ToArray();
            var used = new HashSet<object>(ReferenceEqualityComparer.Instance);

            for (var i = 0; i < sourceCreatures.Length; i++)
            {
                var source = sourceCreatures[i];
                if (source == null)
                    continue;

                var live = FindLiveCreature(combatState, source, liveCreatures, used, i);
                if (live == null)
                {
                    warnings.Add($"Creature[{i}] could not be matched to a live creature.");
                    continue;
                }

                used.Add(live);
                var label = DescribeCreature(source, i);

                RestoreCreatureInteger(
                    live, source, "maxHp", "MaxHp", "SetMaxHpInternal", label, applied, warnings);
                RestoreCreatureInteger(
                    live, source, "currentHp", "CurrentHp", "SetCurrentHpInternal", label, applied, warnings);

                var block = GetMember(source, "block", "Block");
                if (block != null)
                {
                    if (TrySetMember(live, block, "Block", "block"))
                        applied.Add($"{label}: Block={FormatValue(block)}");
                    else
                        warnings.Add($"{label}: Block could not be restored.");
                }
            }
        }

        private static object? FindLiveCreature(
            object combatState,
            object source,
            IReadOnlyList<object?> liveCreatures,
            ISet<object> used,
            int fallbackIndex)
        {
            var playerId = GetNullableUInt64(GetMember(source, "playerId", "PlayerId"));
            if (playerId.HasValue &&
                TryInvoke(combatState, "GetPlayer", new object?[] { playerId.Value }, out var player) &&
                player != null)
            {
                var playerCreature = GetMember(player, "Creature", "creature");
                if (playerCreature != null && !used.Contains(playerCreature))
                    return playerCreature;
            }

            var monsterId = NormalizeIdentity(GetMember(source, "monsterId", "MonsterId"));
            if (!string.IsNullOrWhiteSpace(monsterId))
            {
                foreach (var live in liveCreatures)
                {
                    if (live == null || used.Contains(live))
                        continue;

                    var liveId = NormalizeIdentity(
                        GetMember(live, "Id", "ModelId") ??
                        GetMember(GetMember(live, "Model", "CreatureModel"), "Id", "ModelId"));

                    if (string.Equals(monsterId, liveId, StringComparison.OrdinalIgnoreCase))
                        return live;
                }
            }

            if (fallbackIndex >= 0 && fallbackIndex < liveCreatures.Count)
            {
                var fallback = liveCreatures[fallbackIndex];
                if (fallback != null && !used.Contains(fallback))
                    return fallback;
            }

            return liveCreatures.FirstOrDefault(x => x != null && !used.Contains(x));
        }

        private static void RestoreCreatureInteger(
            object liveCreature,
            object source,
            string sourceName,
            string propertyName,
            string internalMethod,
            string label,
            ICollection<string> applied,
            ICollection<string> warnings)
        {
            var value = GetMember(source, sourceName, propertyName);
            if (value == null)
                return;

            if (TryInvoke(liveCreature, internalMethod, new[] { value }, out _) ||
                TrySetMember(liveCreature, value, propertyName, sourceName))
            {
                applied.Add($"{label}: {propertyName}={FormatValue(value)}");
            }
            else
            {
                warnings.Add($"{label}: {propertyName} could not be restored.");
            }
        }

        private static void RestorePlayers(
            RunState runState,
            object combatState,
            NetFullCombatState checkpointState,
            ICollection<string> applied,
            ICollection<string> warnings)
        {
            var sourcePlayers = EnumerateValues(GetMember(checkpointState, "Players", "players")).ToArray();
            var livePlayers = EnumerateValues(GetMember(combatState, "Players", "players")).ToArray();

            for (var i = 0; i < sourcePlayers.Length; i++)
            {
                var source = sourcePlayers[i];
                if (source == null)
                    continue;

                var playerId = GetNullableUInt64(GetMember(source, "playerId", "PlayerId"));
                object? livePlayer = null;

                if (playerId.HasValue)
                    TryInvoke(combatState, "GetPlayer", new object?[] { playerId.Value }, out livePlayer);

                livePlayer ??= FindPlayerByEnumeration(livePlayers, playerId, i);
                if (livePlayer == null)
                {
                    warnings.Add($"PlayerState[{i}] could not be matched to a live player.");
                    continue;
                }

                var label =
                    $"Player {playerId?.ToString(CultureInfo.InvariantCulture) ?? i.ToString(CultureInfo.InvariantCulture)}";

                var liveCombat = GetMember(livePlayer, "PlayerCombatState", "CombatState");
                if (liveCombat == null)
                {
                    warnings.Add($"{label}: PlayerCombatState could not be located.");
                }
                else
                {
                    RestorePlayerCombatValue(liveCombat, source, "turnNumber", "TurnNumber", label, applied, warnings);
                    RestorePlayerCombatValue(liveCombat, source, "phase", "Phase", label, applied, warnings);
                    RestorePlayerCombatValue(liveCombat, source, "energy", "Energy", label, applied, warnings);
                    RestorePlayerCombatValue(liveCombat, source, "stars", "Stars", label, applied, warnings);
                }

                var gold = GetMember(source, "gold", "Gold");
                if (gold != null)
                {
                    if (TrySetMember(livePlayer, gold, "Gold", "gold"))
                        applied.Add($"{label}: Gold={FormatValue(gold)}");
                    else
                        warnings.Add($"{label}: Gold could not be restored.");
                }

                RestorePlayerRng(livePlayer, source, label, applied, warnings);
                RestoreRelicGrabBag(livePlayer, source, label, applied, warnings);
                RestoreCardPiles(runState, livePlayer, source, label, applied, warnings);
            }
        }

        private static object? FindPlayerByEnumeration(
            IReadOnlyList<object?> players,
            ulong? playerId,
            int fallbackIndex)
        {
            if (playerId.HasValue)
            {
                foreach (var player in players)
                {
                    if (player == null)
                        continue;

                    var id = GetNullableUInt64(GetMember(player, "NetId", "PlayerId", "Id", "playerId"));
                    if (id == playerId)
                        return player;
                }
            }

            if (fallbackIndex >= 0 && fallbackIndex < players.Count)
                return players[fallbackIndex];

            return players.FirstOrDefault();
        }

        private static void RestorePlayerCombatValue(
            object liveCombat,
            object source,
            string sourceName,
            string targetName,
            string label,
            ICollection<string> applied,
            ICollection<string> warnings)
        {
            var value = GetMember(source, sourceName, targetName);
            if (value == null)
                return;

            if (TrySetMember(liveCombat, value, targetName, sourceName))
                applied.Add($"{label}: {targetName}={FormatValue(value)}");
            else
                warnings.Add($"{label}: {targetName} could not be restored.");
        }

        private static void RestoreRunRng(
            RunState runState,
            NetFullCombatState checkpointState,
            ICollection<string> applied,
            ICollection<string> warnings)
        {
            var serialized = GetMember(checkpointState, "Rng", "rng");
            if (serialized == null)
                return;

            var live = GetMember(runState, "RunRng", "Rng", "RngSet");
            if (live == null)
            {
                warnings.Add("Run RNG object could not be located.");
                return;
            }

            if (TryInvoke(live, "LoadFromSerializable", new[] { serialized }, out _))
                applied.Add("Run RNG restored");
            else
                warnings.Add("Run RNG LoadFromSerializable could not be invoked.");
        }

        private static void RestorePlayerRng(
            object livePlayer,
            object checkpointPlayer,
            string label,
            ICollection<string> applied,
            ICollection<string> warnings)
        {
            var serialized = GetMember(checkpointPlayer, "rngSet", "RngSet", "rng");
            if (serialized == null)
                return;

            var live = GetMember(livePlayer, "PlayerRng", "Rng", "RngSet");
            if (live == null)
            {
                warnings.Add($"{label}: PlayerRng could not be located.");
                return;
            }

            if (TryInvoke(live, "LoadFromSerializable", new[] { serialized }, out _))
                applied.Add($"{label}: RNG restored");
            else
                warnings.Add($"{label}: RNG LoadFromSerializable could not be invoked.");
        }

        private static void RestoreRelicGrabBag(
            object livePlayer,
            object checkpointPlayer,
            string label,
            ICollection<string> applied,
            ICollection<string> warnings)
        {
            var serialized = GetMember(checkpointPlayer, "relicGrabBag", "RelicGrabBag");
            if (serialized == null)
                return;

            var live = GetMember(livePlayer, "RelicGrabBag", "relicGrabBag");
            if (live == null)
            {
                warnings.Add($"{label}: RelicGrabBag could not be located.");
                return;
            }

            if (TryInvoke(live, "LoadFromSerializable", new[] { serialized }, out _))
                applied.Add($"{label}: relic grab bag restored");
            else
                warnings.Add($"{label}: RelicGrabBag LoadFromSerializable could not be invoked.");
        }

        private static void RestoreCardPiles(
            RunState runState,
            object livePlayer,
            object checkpointPlayer,
            string label,
            ICollection<string> applied,
            ICollection<string> warnings)
        {
            var sourcePiles = EnumerateValues(GetMember(checkpointPlayer, "piles", "Piles")).ToArray();
            if (sourcePiles.Length == 0)
                return;

            var livePiles = GetMember(livePlayer, "Piles", "piles");
            if (livePiles == null)
            {
                warnings.Add($"{label}: live Piles collection could not be located.");
                return;
            }

            foreach (var sourcePile in sourcePiles)
            {
                if (sourcePile == null)
                    continue;

                var pileType = GetMember(sourcePile, "pileType", "PileType", "Type");
                var livePile = FindLivePile(livePiles, pileType);
                if (livePile == null)
                {
                    warnings.Add($"{label}: pile {FormatValue(pileType)} could not be located.");
                    continue;
                }

                if (!TryInvoke(livePile, "Clear", new object?[] { false }, out _) &&
                    !TryInvoke(livePile, "Clear", Array.Empty<object?>(), out _))
                {
                    warnings.Add($"{label}: pile {FormatValue(pileType)} could not be cleared.");
                    continue;
                }

                var cards = EnumerateValues(GetMember(sourcePile, "cards", "Cards")).ToArray();
                var restored = 0;

                for (var cardIndex = 0; cardIndex < cards.Length; cardIndex++)
                {
                    var sourceCard = cards[cardIndex];
                    if (sourceCard == null)
                        continue;

                    var serializedCard = GetMember(sourceCard, "card", "Card");
                    if (serializedCard == null ||
                        !TryInvoke(runState, "LoadCard", new[] { serializedCard, livePlayer }, out var liveCard) ||
                        liveCard == null)
                    {
                        warnings.Add(
                            $"{label}: {FormatValue(pileType)} card[{cardIndex}] could not be reconstructed.");
                        continue;
                    }

                    var energyCost = GetMember(sourceCard, "energyCost", "EnergyCost");
                    if (energyCost != null)
                        TrySetMember(liveCard, energyCost, "EnergyCost", "energyCost");

                    if (!TryInvoke(livePile, "AddInternal", new object?[] { liveCard, restored, false }, out _) &&
                        !TryInvoke(livePile, "AddInternal", new object?[] { liveCard, restored }, out _) &&
                        !TryInvoke(livePile, "Add", new object?[] { liveCard }, out _))
                    {
                        warnings.Add(
                            $"{label}: {FormatValue(pileType)} card[{cardIndex}] could not be inserted.");
                        continue;
                    }

                    restored++;
                }

                applied.Add($"{label}: rebuilt {FormatValue(pileType)} pile with {restored}/{cards.Length} card(s)");
            }
        }

        private static object? FindLivePile(object livePiles, object? pileType)
        {
            if (livePiles is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (ValuesEquivalent(entry.Key, pileType))
                        return entry.Value;
                }
            }

            foreach (var pile in EnumerateValues(livePiles))
            {
                if (pile == null)
                    continue;

                var liveType = GetMember(pile, "Type", "PileType", "pileType");
                if (ValuesEquivalent(liveType, pileType))
                    return pile;
            }

            if (pileType != null &&
                TryInvoke(livePiles, "GetPile", new[] { pileType }, out var byMethod) &&
                byMethod != null)
            {
                return byMethod;
            }

            return null;
        }

        private static IReadOnlyDictionary<string, string> FilterVolatileCheckpointFields(
            IReadOnlyDictionary<string, string> source)
        {
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);

            foreach (var pair in source)
            {
                var lower = pair.Key.ToLowerInvariant();
                if (lower.Contains("lastexecutedactionid") ||
                    lower.Contains("lastexecutedhookid") ||
                    lower.Contains("nextchoiceids") ||
                    lower.Contains("nextrewardids"))
                {
                    continue;
                }

                result[pair.Key] = pair.Value;
            }

            return result;
        }

        private static object? GetMember(object? instance, params string[] names)
        {
            if (instance == null)
                return null;

            var type = instance.GetType();
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            foreach (var name in names)
            {
                try
                {
                    var property = type.GetProperty(name, flags);
                    if (property != null && property.GetIndexParameters().Length == 0)
                        return property.GetValue(instance);

                    var field = type.GetField(name, flags);
                    if (field != null)
                        return field.GetValue(instance);
                }
                catch
                {
                    // Probe the next candidate.
                }
            }

            return null;
        }

        private static bool TrySetMember(object instance, object? value, params string[] names)
        {
            var type = instance.GetType();
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

            foreach (var name in names)
            {
                try
                {
                    var property = type.GetProperty(name, flags);
                    var setter = property?.GetSetMethod(true);
                    if (property != null && setter != null && property.GetIndexParameters().Length == 0)
                    {
                        setter.Invoke(instance, new[] { ConvertForTarget(value, property.PropertyType) });
                        return true;
                    }

                    var field = type.GetField(name, flags);
                    if (field != null && !field.IsInitOnly)
                    {
                        field.SetValue(instance, ConvertForTarget(value, field.FieldType));
                        return true;
                    }
                }
                catch
                {
                    // Probe the next candidate.
                }
            }

            return false;
        }

        private static bool TryInvoke(
            object instance,
            string methodName,
            object?[] arguments,
            out object? result)
        {
            result = null;
            const BindingFlags flags =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            var methods = instance.GetType()
                .GetMethods(flags)
                .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                .Where(m => m.GetParameters().Length == arguments.Length);

            foreach (var method in methods)
            {
                try
                {
                    var parameters = method.GetParameters();
                    var converted = new object?[arguments.Length];

                    for (var i = 0; i < arguments.Length; i++)
                        converted[i] = ConvertForTarget(arguments[i], parameters[i].ParameterType);

                    result = method.Invoke(instance, converted);
                    return true;
                }
                catch
                {
                    // Try another overload.
                }
            }

            return false;
        }

        private static object? ConvertForTarget(object? value, Type targetType)
        {
            if (value == null)
            {
                return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null
                    ? null
                    : Activator.CreateInstance(targetType);
            }

            var effectiveTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
            var sourceType = value.GetType();

            if (effectiveTarget.IsAssignableFrom(sourceType))
                return value;

            if (effectiveTarget.IsEnum)
            {
                if (value is string text)
                    return Enum.Parse(effectiveTarget, text, true);

                var numeric = Convert.ChangeType(
                    value,
                    Enum.GetUnderlyingType(effectiveTarget),
                    CultureInfo.InvariantCulture);
                return Enum.ToObject(effectiveTarget, numeric!);
            }

            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(effectiveTarget))
                return Convert.ChangeType(value, effectiveTarget, CultureInfo.InvariantCulture);

            return value;
        }

        private static IEnumerable<object?> EnumerateValues(object? value)
        {
            if (value == null || value is string)
                yield break;

            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                    yield return entry.Value;
                yield break;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                    yield return item;
                yield break;
            }

            yield return value;
        }

        private static bool ValuesEquivalent(object? left, object? right)
        {
            if (left == null || right == null)
                return left == null && right == null;

            if (Equals(left, right))
                return true;

            return string.Equals(
                NormalizeIdentity(left),
                NormalizeIdentity(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeIdentity(object? value)
        {
            if (value == null)
                return string.Empty;

            var nested = GetMember(value, "Id", "ModelId", "Value");
            if (nested != null && !ReferenceEquals(nested, value))
                return NormalizeIdentity(nested);

            return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        }

        private static ulong? GetNullableUInt64(object? value)
        {
            if (value == null)
                return null;

            try
            {
                return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static string DescribeCreature(object source, int fallbackIndex)
        {
            var playerId = GetNullableUInt64(GetMember(source, "playerId", "PlayerId"));
            if (playerId.HasValue)
                return $"PlayerCreature {playerId.Value}";

            var monsterId = NormalizeIdentity(GetMember(source, "monsterId", "MonsterId"));
            return string.IsNullOrWhiteSpace(monsterId)
                ? $"Creature[{fallbackIndex}]"
                : $"Monster {monsterId}";
        }

        private static string FormatValue(object? value)
        {
            if (value == null)
                return "<null>";

            return value is IFormattable formattable
                ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? "<null>"
                : value.ToString() ?? "<null>";
        }
    }

    internal enum RollbackPrototypeStatus
    {
        Failed,
        Partial,
        Restored
    }

    internal sealed record RollbackPrototypeReport(
        RollbackPrototypeStatus Status,
        long? CheckpointSequence,
        uint? CheckpointChecksumId,
        string? CheckpointContext,
        IReadOnlyList<string> AppliedMutations,
        IReadOnlyList<string> Warnings,
        int RemainingDifferenceCount,
        IReadOnlyList<StateDifference> RemainingDifferences,
        string? Failure)
    {
        public static RollbackPrototypeReport Failed(string reason, CombatCheckpoint? checkpoint = null)
        {
            return new RollbackPrototypeReport(
                RollbackPrototypeStatus.Failed,
                checkpoint?.Sequence,
                checkpoint?.ChecksumId,
                checkpoint?.Context,
                Array.Empty<string>(),
                Array.Empty<string>(),
                -1,
                Array.Empty<StateDifference>(),
                reason);
        }

        public string ToLogText()
        {
            var lines = new List<string>
            {
                $"[LAN Multiplayer][RollbackPrototype] Status={Status}",
                $"Checkpoint={(CheckpointSequence.HasValue ? $"#{CheckpointSequence} checksum={CheckpointChecksumId}" : "none")}",
                $"Applied mutations={AppliedMutations.Count}",
                $"Remaining differences={RemainingDifferenceCount}"
            };

            if (!string.IsNullOrWhiteSpace(Failure))
                lines.Add($"Failure={Failure}");

            if (Warnings.Count > 0)
            {
                lines.Add("Warnings:");
                foreach (var warning in Warnings.Take(16))
                    lines.Add($"  - {warning}");
            }

            if (RemainingDifferences.Count > 0)
            {
                lines.Add("Remaining checkpoint differences (expected | live):");
                foreach (var difference in RemainingDifferences)
                    lines.Add($"  {difference.Path}: {difference.LocalValue} | {difference.RemoteValue}");
            }

            return string.Join(System.Environment.NewLine, lines);
        }

        public string ToPlayerText()
        {
            var lines = new List<string>();

            lines.Add(Status switch
            {
                RollbackPrototypeStatus.Restored =>
                    "Local checkpoint restore completed and the reconstructed combat snapshot matches the checkpoint.",
                RollbackPrototypeStatus.Partial =>
                    "Local checkpoint restore ran, but the reconstructed combat snapshot still differs from the checkpoint.",
                _ =>
                    "Local checkpoint restore was not performed."
            });

            if (CheckpointSequence.HasValue)
            {
                lines.Add(string.Empty);
                lines.Add(
                    $"Checkpoint: #{CheckpointSequence}, checksum {CheckpointChecksumId} ({CheckpointContext})");
            }

            if (!string.IsNullOrWhiteSpace(Failure))
            {
                lines.Add(string.Empty);
                lines.Add("Reason:");
                lines.Add(Failure);
            }

            lines.Add(string.Empty);
            lines.Add($"Applied mutations: {AppliedMutations.Count}");

            foreach (var mutation in AppliedMutations.Take(10))
                lines.Add($"• {mutation}");

            if (AppliedMutations.Count > 10)
                lines.Add($"• ... {AppliedMutations.Count - 10} more");

            if (Status != RollbackPrototypeStatus.Failed)
            {
                lines.Add(string.Empty);
                lines.Add($"Remaining checkpoint differences: {RemainingDifferenceCount}");

                foreach (var difference in RemainingDifferences.Take(8))
                {
                    lines.Add(
                        $"• {ShortenPath(difference.Path)}: expected {difference.LocalValue} / live {difference.RemoteValue}");
                }

                if (RemainingDifferenceCount > RemainingDifferences.Count)
                    lines.Add($"• ... {RemainingDifferenceCount - RemainingDifferences.Count} more");
            }

            if (Warnings.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("Prototype warnings:");
                foreach (var warning in Warnings.Take(6))
                    lines.Add($"• {warning}");
            }

            lines.Add(string.Empty);
            lines.Add(
                "This is a local developer prototype only. It does not suppress real desync teardown or send rollback commands to peers.");

            return string.Join(System.Environment.NewLine, lines);
        }

        private static string ShortenPath(string path)
        {
            const int max = 76;
            return path.Length <= max ? path : "…" + path[(path.Length - (max - 1))..];
        }
    }
}
