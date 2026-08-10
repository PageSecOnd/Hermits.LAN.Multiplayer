using System.Text.Json;
using MegaCrit.Sts2.Core.Saves;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Helpers;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Integrations;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Models;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Services
{
    internal class SettingsService
    {
        private static readonly Lazy<SettingsService> Lazy = new(() => new SettingsService());

        public static SettingsService Instance => Lazy.Value;

        public readonly SettingsModel SettingsModel;

        private readonly GodotFileIo _modsDir =
            new(Path.Combine(UserDataPathProvider.GetAccountScopedBasePath(null), "mods"));

        private SettingsService()
        {
            if (_modsDir.FileExists("lan_settings.json"))
            {
                SettingsModel =
                    JsonSerializer.Deserialize<SettingsModel>(_modsDir.ReadFile("lan_settings.json") ?? string.Empty) ??
                    new SettingsModel();
            }
            else
            {
                SettingsModel = new SettingsModel();
            }

            SettingsModel.HostMaxPlayers = LanProtocolPolicy.ClampPlayerCount(SettingsModel.HostMaxPlayers);
        }

        public void WriteSettings()
        {
            SettingsModel.HostMaxPlayers = LanProtocolPolicy.ClampPlayerCount(SettingsModel.HostMaxPlayers);
            _modsDir.WriteFile("lan_settings.json",
                JsonSerializer.Serialize(SettingsModel, SettingsModelContext.Default.SettingsModel));

            // ModConfig may still contain a legacy value above the vanilla-safe range.
            // Push the normalized value back so it cannot re-enter the runtime later.
            ModConfigBridge.SetValue("hostMaxPlayers", (float)SettingsModel.HostMaxPlayers);
        }
    }
}
