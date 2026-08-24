using System.Reflection;
using Godot;
using HarmonyLib;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Compatibility;

internal static class PatchBootstrap
{
    public static (int Applied, int Skipped) ApplyIndependently(Harmony harmony, Assembly assembly)
    {
        var applied = 0;
        var skipped = 0;

        foreach (var type in GetLoadableTypes(assembly).Where(IsPatchContainer).OrderBy(type => type.FullName))
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                applied++;
            }
            catch (Exception exception)
            {
                skipped++;
                GD.PushWarning($"[LAN Multiplayer] Disabled incompatible patch {type.FullName}: " +
                               $"{exception.GetBaseException().Message}");
            }
        }

        return (applied, skipped);
    }

    private static bool IsPatchContainer(Type type)
    {
        return type.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length > 0;
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            foreach (var loaderException in exception.LoaderExceptions.Where(error => error != null))
                GD.PushWarning($"[LAN Multiplayer] Type load warning: {loaderException!.Message}");

            return exception.Types.Where(type => type != null)!;
        }
    }
}
