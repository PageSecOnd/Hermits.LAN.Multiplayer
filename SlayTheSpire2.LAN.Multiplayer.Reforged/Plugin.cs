using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Compatibility;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Integrations;

// ReSharper disable UnusedMember.Global
// ReSharper disable ClassNeverInstantiated.Global

namespace SlayTheSpire2.LAN.Multiplayer.Reforged
{
    [ModInitializer("Initialize")]
    public class Plugin
    {
        private const string HarmonyId = "SlayTheSpire2.LAN.Multiplayer.Reforged";
        private static int _initialized;

        private static void Initialize()
        {
            if (Interlocked.Exchange(ref _initialized, 1) != 0)
                return;

            var alreadyPatched = Harmony.GetAllPatchedMethods().Any(method =>
                Harmony.GetPatchInfo(method)?.Owners.Contains(HarmonyId) == true);
            if (alreadyPatched)
                return;

            var result = PatchBootstrap.ApplyIndependently(new Harmony(HarmonyId), typeof(Plugin).Assembly);
            GD.Print($"[LAN Multiplayer] v2 initialized for {GameCompatibility.RuntimeFlavor}: " +
                     $"{result.Applied} patch groups active, {result.Skipped} skipped.");
            ModConfigBridge.DeferredRegister();
        }
    }
}

