using System.Collections;
using System.Globalization;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Services
{
    /// <summary>
    /// Experimental, local-only combat rollback prototype.
    ///
    /// The service deliberately does not participate in the real desync/disconnect path yet. It is invoked only
    /// by the F11 developer hotkey so that restore behavior can be validated before any host/client coordination
    /// is added. The implementation uses reflection at the mutation boundary to avoid taking compile-time
    /// dependencies on private/internal combat DTO shapes that have already changed between STS2 versions.
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
                    "An action is currently executing. F11 only restores at an idle player-turn boundary.",
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

                var combatState = GetMember(runState, "CombatState", "Combat");
                if (combatState == null)
                    return RollbackPrototypeReport.Failed("The live CombatState could not be located.", checkpoint);

                RestoreCreatures(combatState, checkpoint.State, applied, warnings);
                RestorePlayers(runState, combatState, checkpoint.State, applied, warnings);
                RestoreRunRng(runState, checkpoint.State, applied, warnings);

                // Action/hook/choice/reward sequence numbers are intentionally not rewound in this first local
                // prototype. Rewinding them incorrectly while an action is still referenced by a queue is more
                // dangerous than leaving them monotonic. They will be added only after the live state mutation
                // path is proven stable.
                warnings.Add("Action/hook/choice/reward sequence counters are not rewound by this prototype yet.");

                var afterState = NetFullCombatState.FromRun(runState, null!);
                var afterSnapshot = CombatStateFlattener.Flatten(afterState);
                var expectedSnapshot = FilterVolatileCheckpointFields(checkpoint.Snapshot);
                var actualSnapshot = FilterVolatileCheckpointFields(afterSnapshot);
                var differences = CombatStateFlattener.Diff(
                    expectedSnapshot,
                    actualSnapshot,
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
                    applied,
                    warnings,
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
                    applied,
                    warnings,
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
                    GD.PushWarning($"[LAN Multiplayer][RollbackPrototype] Could not unpause player queues: {exception}");
                }

                try
                {
                    if (pausedExecutor && actionExecutor != null)
                        TryInvoke(actionExecutor, "Unpause", Array.Empty<object?>(), out _);
                }
                catch (Exception exception)
                {
                    GD.PushWarning($"[LAN Multiplayer][RollbackPrototype] Could not unpause ActionExecutor: {exception}");
                }
            }
        }

        private static void RestoreCreatures(
            object combatState,
            NetFullCombatState checkpointState,
            ICollection<string> applied,
            ICollection<string> warnings)
        {
            var checkpointCreatures = EnumerateValues(GetMember(checkpointState, "Creatures", "creatures")).ToArray();
            var liveCreatures = EnumerateValues(GetMember(combatState, "Creatures", "creatures")).ToArray();
            var used = new HashSet<object>(ReferenceEqualityComparer.Instance);

            for (var index = 0; index < checkpointCreatures.Length; index++)
            {
                var source = checkpointCreatures[index];
                if (source == null)
                    continue;

                var liveCreature = FindLiveCreature(combatState, source, liveCreatures, used, index);
                if (liveCreature == null)
                {
                    warnings.Add($"Creature[{index}] could not be matched to a live creature.");
                    continue;
                }

                used.Add(liveCreature);
                var label = DescribeCreature(source, index);

                RestoreCreatureInteger(liveCreature, source, "maxHp", "MaxHp", "SetMaxHpInternal", label, applied, warnings);
                RestoreCreatureInteger(liveCreature, source, "currentHp", "CurrentHp", "SetCurrentHpInternal", label, applied, warnings);

                var block = GetMember(source, "block", "Block");
                if (block != null)
                {
                    if (TrySetMember(liveCreature, block, "Block", "block"))
                        applied.Add($"{label}: block={FormatValue(block)}");
                    else
                        warnings.Add($"{label}: block could not be restored.");
                }

                // Power reconstruction is intentionally deferred. PowerModel creation has side effects and hook
                // registration; it must be restored only after we can atomically rebuild all powers for all peers.
            }
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

        private static object? FindLiveCreature(
            object combatState,
            object checkpointCreature,
            IReadOnlyList<object?> liveCreatures,
            ISet<object> used,
            int fallbackIndex)
        {
            var playerId = GetNullableUInt64(GetMember(checkpointCreature, "playerId", "PlayerId"));
            if (playerId.HasValue && TryInvoke(combatState, "GetPlayer", new object?[] { playerId.Value }, out var player))
            {
                var playerCreature = player == null ? null : GetMember(player, "Creature", "creature");
                if (playerCreature != null && !used.Contains(playerCreature))
                    return playerCreature;
            }

            var monsterId = NormalizeIdentity(GetMember(checkpointCreature, "monsterId", "MonsterId"));
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

            return liveCreatures.FirstOrDefault(live => live != null && !used.Contains(live));
        }

        private static void RestorePlayers(
            RunState runState,
            object combatState,
            NetFullCombatState checkpointState,
            ICollection<string> applied,
            ICollection<string> warnings)
        {
            var checkpointPlayers = EnumerateValues(GetMember(checkpointState, "Players", "players")).ToArray();
            for (var index = 0; index < checkpointPlayers.Length; index++)
            {
                var source = checkpointPlayers[index];
                if (source == null)
                    continue;

                var playerId = GetNullableUInt64(GetMember(source, "playerId", "PlayerId"));
                if (!playerId.HasValue ||
                    !TryInvoke(combatState, "GetPlayer", new object?[] { playerId.Value }, out var livePlayer) ||
                    livePlayer == null)
                {
                    livePlayer = FindLivePlayerByEnumeration(combatState, playerId, index);
                }

                if (livePlayer == null)
                {
                    warnings.Add($"PlayerState[{index}] could not be matched to a live player.");
                    continue;
                }

                var label = $"Player {playerId?.ToString(CultureInfo.InvariantCulture) ?? index.ToString(CultureInfo.InvariantCulture)}";
                var combat = GetMember(livePlayer, "PlayerCombatState", "CombatState");
                if (combat == null)
                {
                    warnings.Add($"{label}: PlayerCombatState is unavailable.");
                }
                else
                {
                    RestorePlayerCombatValue(combat, source, "turnNumber", "TurnNumber", label, applied, warnings);
                    RestorePlayerCombatValue(combat, source, "phase", "Phase", label, applied, warnings);
                    RestorePlayerCombatValue(combat, source, "energy", "Energy", label, applied, warnings);
                    RestorePlayerCombatValue(combat, source, "stars", "Stars", label, applied, warnings);
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

                // Potions/relics/orbs are not rebuilt in the first prototype because replacing those collections
                // can fire acquisition/removal hooks. The post-restore diff will expose any mismatch clearly.
            }
        }

        private static object? FindLivePlayerByEnumeration(object combatState, ulong? playerId, int fallbackIndex)
        {
            var players = EnumerateValues(GetMember(combatState, "Players", "players")).ToArray();
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

            return fallbackIndex >= 0 && fallbackIndex < players.Length ? players[fallbackIndex] : players.FirstOrDefault();
        }

        private static void RestorePlayerCombatValue(
            object liveCombatState,
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

            if (TrySetMember(liveCombatState, value, targetName, sourceName))
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
            var serializedRng = GetMember(checkpointState, "Rng", "rng");
            if (serializedRng == null)
                return;

            var liveRng = GetMember(runState, "RunRng", "Rng", "RngSet");
            if (liveRng == null)
            {
                warnings.Add("Run RNG object could not be located.");
                return;
            }

            if (TryInvoke(liveRng, "LoadFromSerializable", new[] { serializedRng }, out _))
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
            var checkpointPiles = EnumerateValues(GetMember(checkpointPlayer, "piles", "Piles")).ToArray();
            if (checkpointPiles.Length == 0)
                return;

            var livePilesContainer = GetMember(livePlayer, "Piles", "piles");
            if (livePilesContainer == null)
            {
                warnings.Add($"{label}: live Piles collection could not be located.");
                return;
            }

            foreach (var checkpointPile in checkpointPiles)
            {
                if (checkpointPile == null)
                    continue;

                var pileType = GetMember(checkpointPile, "pileType", "PileType", "Type");
                var livePile = FindLivePile(livePilesContainer, pileType);
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

                var checkpointCards = EnumerateValues(GetMember(checkpointPile, "cards", "Cards")).ToArray();
                var restoredCards = 0;
                for (var cardIndex = 0; cardIndex < checkpointCards.Length; cardIndex++)
                {
                    var checkpointCard = checkpointCards[cardIndex];
                    if (checkpointCard == null)
                        continue;

                    var serializedCard = GetMember(checkpointCard, "card", "Card");
                    if (serializedCard == null ||
                        !TryInvoke(runState, "LoadCard", new[] { serializedCard, livePlayer }, out var liveCard) ||
                        liveCard == null)
                    {
                        warnings.Add(
                            $"{label}: {FormatValue(pileType)} card[{cardIndex}] could not be reconstructed with RunState.LoadCard.");
                        continue;
                    }

                    var energyCost = GetMember(checkpointCard, "energyCost", "EnergyCost");
                    if (energyCost != null)
                        TrySetMember(liveCard, energyCost, "EnergyCost", "energyCost");

                    var affliction = GetMember(checkpointCard, "affliction", "Affliction");
                    var afflictionCount = GetMember(checkpointCard, "afflictionCount", "AfflictionCount");
                    if (affliction != null && afflictionCount != null)
                    {
                        // This is best-effort. If the current version exposes a compatible AfflictInternal overload,
                        // restore it; otherwise the post-restore diff will report the remaining mismatch.
                        TryInvoke(liveCard, "AfflictInternal", new[] { affliction, afflictionCount }, out _);
                    }

                    if (TryInvoke(livePile, "AddInternal", new object?[] { liveCard, cardIndex, false }, out _) ||
                        TryInvoke(livePile, "AddInternal", new object?[] { liveCard, cardIndex }, out _) ||
                        TryInvoke(livePile, "AddInternal", new object?[] { liveCard }, out _))
                    {
                        restoredCards++;
                    }
                    else
                    {
                        warnings.Add($"{label}: {FormatValue(pileType)} card[{cardIndex}] could not be added back to the pile.");
                    }
                }

                applied.Add($"{label}: rebuilt {FormatValue(pileType)} pile ({restoredCards}/{checkpointCards.Length} cards)");
            }
        }

        private static object? FindLivePile(object container, object? pileType)
        {
            var wanted = NormalizeIdentity(pileType);

            if (container is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (string.Equals(NormalizeIdentity(entry.Key), wanted, StringComparison.OrdinalIgnoreCase))
                        return entry.Value;
                }
            }

            foreach (var item in EnumerateValues(container))
            {
                if (item == null)
                    continue;

                var key = GetMember(item, "Key");
                var value = GetMember(item, "Value") ?? item;
                var candidateType = key ?? GetMember(value, "PileType", "Type", "pileType");
                if (string.Equals(NormalizeIdentity(candidateType), wanted, StringComparison.OrdinalIgnoreCase))
                    return value;
            }

            return null;
        }

        private static IReadOnlyDictionary<string, string> FilterVolatileCheckpointFields(
            IReadOnlyDictionary<string, string> source)
        {
            var filtered = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in source)
            {
                if (IsVolatilePath(pair.Key))
                    continue;
                filtered[pair.Key] = pair.Value;
            }
            return filtered;
        }

        private static bool IsVolatilePath(string path)
        {
            return path.EndsWith(".lastExecutedActionId", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".lastExecutedHookId", StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeCreature(object source, int index)
        {
            var playerId = GetNullableUInt64(GetMember(source, "playerId", "PlayerId"));
            if (playerId.HasValue)
                return $"PlayerCreature {playerId.Value}";

            var monsterId = NormalizeIdentity(GetMember(source, "monsterId", "MonsterId"));
            return string.IsNullOrWhiteSpace(monsterId) ? $"Creature[{index}]" : $"Monster {monsterId}";
        }

        private static object? GetMember(object? target, params string[] names)
        {
            if (target == null)
                return null;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = target.GetType();
            foreach (var name in names)
            {
                var property = type.GetProperty(name, flags);
                if (property != null)
                {
                    try
                    {
                        return property.GetValue(target);
                    }
                    catch
                    {
                        // Try the next candidate member name.
                    }
                }

                var field = type.GetField(name, flags);
                if (field != null)
                {
                    try
                    {
                        return field.GetValue(target);
                    }
                    catch
                    {
                        // Try the next candidate member name.
                    }
                }
            }

            return null;
        }

        private static bool TrySetMember(object target, object? value, params string[] names)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var type = target.GetType();

            foreach (var name in names)
            {
                var property = type.GetProperty(name, flags);
                if (property?.SetMethod != null)
                {
                    try
                    {
                        property.SetValue(target, ConvertValue(value, property.PropertyType));
                        return true;
                    }
                    catch
                    {
                        // Fall through to fields/alternate names.
                    }
                }

                var field = type.GetField(name, flags);
                if (field != null && !field.IsInitOnly)
                {
                    try
                    {
                        field.SetValue(target, ConvertValue(value, field.FieldType));
                        return true;
                    }
                    catch
                    {
                        // Try backing fields below.
                    }
                }

                var backingField = type.GetField($"<{name}>k__BackingField", flags);
                if (backingField != null && !backingField.IsInitOnly)
                {
                    try
                    {
                        backingField.SetValue(target, ConvertValue(value, backingField.FieldType));
                        return true;
                    }
                    catch
                    {
                        // Try the next name.
                    }
                }
            }

            return false;
        }

        private static bool TryInvoke(object target, string methodName, object?[] suppliedArgs, out object? result)
        {
            result = null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var methods = target.GetType()
                .GetMethods(flags)
                .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal))
                .OrderBy(method => method.GetParameters().Length)
                .ToArray();

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length < suppliedArgs.Length)
                    continue;

                var invocationArgs = new object?[parameters.Length];
                var compatible = true;

                for (var index = 0; index < parameters.Length; index++)
                {
                    if (index < suppliedArgs.Length)
                    {
                        try
                        {
                            invocationArgs[index] = ConvertValue(suppliedArgs[index], parameters[index].ParameterType);
                        }
                        catch
                        {
                            compatible = false;
                            break;
                        }
                    }
                    else if (parameters[index].HasDefaultValue)
                    {
                        invocationArgs[index] = parameters[index].DefaultValue;
                    }
                    else
                    {
                        invocationArgs[index] = GetDefault(parameters[index].ParameterType);
                    }
                }

                if (!compatible)
                    continue;

                try
                {
                    result = method.Invoke(target, invocationArgs);
                    return true;
                }
                catch
                {
                    // Try another overload.
                }
            }

            return false;
        }

        private static object? ConvertValue(object? value, Type targetType)
        {
            var nullableType = Nullable.GetUnderlyingType(targetType);
            var effectiveType = nullableType ?? targetType;

            if (value == null)
            {
                if (!effectiveType.IsValueType || nullableType != null)
                    return null;
                return Activator.CreateInstance(effectiveType);
            }

            if (targetType.IsInstanceOfType(value) || effectiveType.IsInstanceOfType(value))
                return value;

            if (effectiveType.IsEnum)
            {
                if (value.GetType().IsEnum)
                    return Enum.Parse(effectiveType, value.ToString()!, true);
                if (value is string text)
                    return Enum.Parse(effectiveType, text, true);
                var underlying = Enum.GetUnderlyingType(effectiveType);
                return Enum.ToObject(effectiveType, Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture)!);
            }

            return Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
        }

        private static object? GetDefault(Type type)
        {
            if (type.IsByRef)
                type = type.GetElementType() ?? type;
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private static IEnumerable<object?> EnumerateValues(object? value)
        {
            if (value == null)
                yield break;

            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                    yield return entry.Value;
                yield break;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                foreach (var item in enumerable)
                    yield return item;
                yield break;
            }

            yield return value;
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

        private static string NormalizeIdentity(object? value)
        {
            if (value == null)
                return string.Empty;

            var nested = GetMember(value, "Id", "ModelId", "Value");
            if (nested != null && !ReferenceEquals(nested, value))
                return NormalizeIdentity(nested);

            return value.ToString()?.Trim() ?? string.Empty;
        }

        private static string FormatValue(object? value)
        {
            return value switch
            {
                null => "<null>",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "<null>",
                _ => value.ToString() ?? "<null>"
            };
        }
    }

    internal enum RollbackPrototypeStatus
    {
        Restored,
        Partial,
        Failed
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
        public static RollbackPrototypeReport Failed(string message, CombatCheckpoint? checkpoint = null)
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
                message);
        }

        public string ToPlayerText()
        {
            var lines = new List<string>
            {
                Status switch
                {
                    RollbackPrototypeStatus.Restored => "Local checkpoint restore completed and post-restore state matches the checkpoint.",
                    RollbackPrototypeStatus.Partial => "Local checkpoint restore ran, but some state still differs from the checkpoint.",
                    _ => "Local checkpoint restore was not performed."
                }
            };

            if (CheckpointSequence.HasValue)
                lines.Add($"Checkpoint: #{CheckpointSequence}, checksum {CheckpointChecksumId} ({CheckpointContext})");

            if (!string.IsNullOrWhiteSpace(Failure))
            {
                lines.Add(string.Empty);
                lines.Add("Reason:");
                lines.Add(Failure!);
            }

            lines.Add(string.Empty);
            lines.Add($"Applied mutations: {AppliedMutations.Count}");
            foreach (var mutation in AppliedMutations.Take(10))
                lines.Add($"• {mutation}");
            if (AppliedMutations.Count > 10)
                lines.Add($"• … {AppliedMutations.Count - 10} more");

            if (RemainingDifferenceCount >= 0)
            {
                lines.Add(string.Empty);
                lines.Add($"Remaining checkpoint differences: {RemainingDifferenceCount}");
                foreach (var difference in RemainingDifferences.Take(8))
                    lines.Add($"• {difference.Path}: {difference.LocalValue} / {difference.RemoteValue}");
            }

            if (Warnings.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add($"Prototype warnings: {Warnings.Count}");
                foreach (var warning in Warnings.Take(8))
                    lines.Add($"• {warning}");
            }

            lines.Add(string.Empty);
            lines.Add("This is a local developer prototype only. It does not suppress real desync teardown or send rollback commands to peers.");
            return string.Join(System.Environment.NewLine, lines);
        }

        public string ToLogText()
        {
            var lines = new List<string>
            {
                $"[LAN Multiplayer][RollbackPrototype] Status={Status}",
                $"Checkpoint=#{CheckpointSequence?.ToString(CultureInfo.InvariantCulture) ?? "none"} checksum={CheckpointChecksumId?.ToString(CultureInfo.InvariantCulture) ?? "none"}",
                $"AppliedMutations={AppliedMutations.Count}",
                $"Warnings={Warnings.Count}",
                $"RemainingDifferences={RemainingDifferenceCount}"
            };

            foreach (var mutation in AppliedMutations)
                lines.Add($"  applied: {mutation}");
            foreach (var warning in Warnings)
                lines.Add($"  warning: {warning}");
            foreach (var difference in RemainingDifferences)
                lines.Add($"  difference: {difference.Path}: {difference.LocalValue} | {difference.RemoteValue}");
            if (!string.IsNullOrWhiteSpace(Failure))
                lines.Add($"  failure: {Failure}");

            return string.Join(System.Environment.NewLine, lines);
        }
    }
}
