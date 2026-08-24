using System.Text.Json;
using MegaCrit.Sts2.Core.Saves;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Compatibility;
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

            Normalize();
        }

        public void WriteSettings()
        {
            Normalize();
            _modsDir.WriteFile("lan_settings.json",
                JsonSerializer.Serialize(SettingsModel, SettingsModelContext.Default.SettingsModel));
        }

        private void Normalize()
        {
            SettingsModel.HostMaxPlayers = LanProtocolPolicy.ClampPlayerCount(SettingsModel.HostMaxPlayers);
            SettingsModel.ConnectTimeoutSeconds = Math.Clamp(SettingsModel.ConnectTimeoutSeconds, 3, 120);
            SettingsModel.IPAddress = string.IsNullOrWhiteSpace(SettingsModel.IPAddress)
                ? "127.0.0.1"
                : SettingsModel.IPAddress.Trim();
            SettingsModel.PlayerName = string.IsNullOrWhiteSpace(SettingsModel.PlayerName)
                ? PlatformNameProvider.GetDefaultPlayerName()
                : SettingsModel.PlayerName.Trim();
        }
    }
}
