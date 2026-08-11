using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Integrations;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Services;

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

            LanProcessLogService.Instance.Initialize();
            LanProcessLogService.Instance.Info("Plugin initialization started.");

            // A second copy of the mod can otherwise PatchAll the same methods again.
            // Check Harmony ownership as well as the local guard so duplicate assembly
            // loads from another mod folder fail closed instead of stacking patches.
            var alreadyPatched = Harmony.GetAllPatchedMethods().Any(method =>
                Harmony.GetPatchInfo(method)?.Owners.Contains(HarmonyId) == true);

            if (alreadyPatched)
            {
                LanProcessLogService.Instance.Warn("Harmony owner already present; skipping duplicate PatchAll().");
                return;
            }

            new Harmony(HarmonyId).PatchAll(typeof(Plugin).Assembly);
            LanProcessLogService.Instance.Info("Harmony PatchAll completed.");
            ModConfigBridge.DeferredRegister();
        }
    }
}
