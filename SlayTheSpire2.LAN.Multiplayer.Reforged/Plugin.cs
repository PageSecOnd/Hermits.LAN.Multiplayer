using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
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

            // A second copy of the mod can otherwise PatchAll the same methods again.
            // Check Harmony ownership as well as the local guard so duplicate assembly
            // loads from another mod folder fail closed instead of stacking patches.
            var alreadyPatched = Harmony.GetAllPatchedMethods().Any(method =>
                Harmony.GetPatchInfo(method)?.Owners.Contains(HarmonyId) == true);

            if (alreadyPatched)
                return;

            new Harmony(HarmonyId).PatchAll(typeof(Plugin).Assembly);
            ModConfigBridge.DeferredRegister();
        }
    }
}

