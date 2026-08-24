using System.Collections;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Compatibility;

/// <summary>
/// The only place where known game-version differences are resolved.
/// Everything outside this class targets the API shared by stable and beta.
/// </summary>
internal static class GameCompatibility
{
    private const string PeerVersionInfoName = "MegaCrit.Sts2.Core.Multiplayer.PeerVersionInfo";

    private static readonly PropertyInfo? JoinFlowNetService =
        AccessTools.Property(typeof(JoinFlow), "NetService");

    public static string RuntimeFlavor =>
        typeof(NetHostGameService).Assembly.GetType(PeerVersionInfoName) == null ? "stable" : "beta";

    public static NetHostGameService CreateHostGameService()
    {
        return (NetHostGameService)CreateVersionAwareService(typeof(NetHostGameService));
    }

    public static INetGameService? GetNetService(JoinFlow joinFlow)
    {
        return JoinFlowNetService?.GetValue(joinFlow) as INetGameService;
    }

    public static HashSet<ulong> GetConnectedPlayerIds(object? lobby)
    {
        if (lobby == null)
            return [];

        var type = lobby.GetType();
        var idsProperty = AccessTools.Property(type, "PlayerIds") ??
                          AccessTools.Property(type, "ConnectedPlayerIds");
        if (idsProperty?.GetValue(lobby) is IEnumerable ids)
        {
            return ids.Cast<object>().Select(ConvertPlayerId).Where(id => id.HasValue)
                .Select(id => id!.Value).ToHashSet();
        }

        if (AccessTools.Property(type, "Players")?.GetValue(lobby) is IEnumerable players)
        {
            return players.Cast<object>().Select(ConvertPlayerId).Where(id => id.HasValue)
                .Select(id => id!.Value).ToHashSet();
        }

        return [];
    }

    public static bool IsUsingController()
    {
        var manager = NControllerManager.Instance;
        if (manager == null)
            return false;

        var oldProperty = AccessTools.Property(manager.GetType(), "IsUsingController");
        if (oldProperty?.GetValue(manager) is bool oldValue)
            return oldValue;

        var inputType = AccessTools.Property(manager.GetType(), "InputType")?.GetValue(manager);
        return string.Equals(inputType?.ToString(), "Controller", StringComparison.OrdinalIgnoreCase);
    }

    public static string GetRunSavePath(int profileId, string fileName)
    {
        var method = typeof(RunSaveManager).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(candidate => candidate.Name == "GetRunSavePath")
            .OrderBy(candidate => candidate.GetParameters().Length)
            .FirstOrDefault();
        if (method == null)
            throw new MissingMethodException(typeof(RunSaveManager).FullName, "GetRunSavePath");

        return method.GetParameters().Length switch
        {
            2 => (string)method.Invoke(null, [profileId, fileName])!,
            3 => (string)method.Invoke(null, [profileId, fileName, null])!,
            _ => throw new NotSupportedException("Unsupported RunSaveManager.GetRunSavePath signature.")
        };
    }

    private static object CreateVersionAwareService(Type serviceType)
    {
        var parameterless = serviceType.GetConstructor(Type.EmptyTypes);
        if (parameterless != null)
            return parameterless.Invoke(null);

        var peerVersionType = serviceType.Assembly.GetType(PeerVersionInfoName);
        var localDefault = peerVersionType == null ? null :
            AccessTools.Method(peerVersionType, "LocalDefault", Type.EmptyTypes);
        var versionAware = peerVersionType == null ? null : serviceType.GetConstructor([peerVersionType]);

        if (localDefault == null || versionAware == null)
            throw new MissingMethodException(serviceType.FullName, ".ctor()/.ctor(PeerVersionInfo)");

        return versionAware.Invoke([localDefault.Invoke(null, null)]);
    }

    private static ulong? ConvertPlayerId(object item)
    {
        if (item is ulong id)
            return id;

        var type = item.GetType();
        var field = AccessTools.Field(type, "id") ?? AccessTools.Field(type, "playerId");
        var property = AccessTools.Property(type, "Id") ?? AccessTools.Property(type, "PlayerId");
        var value = field?.GetValue(item) ?? property?.GetValue(item);
        return value == null ? null : Convert.ToUInt64(value);
    }
}
