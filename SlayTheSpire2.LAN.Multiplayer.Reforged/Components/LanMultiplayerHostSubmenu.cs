using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Helpers;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Services;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Components
{
    internal partial class LanMultiplayerHostSubmenu : NMultiplayerHostSubmenu
    {
        private static readonly string ScenePath = SceneHelper.GetScenePath("screens/multiplayer_host_submenu");

        private Control? _loadingOverlay;

        public new static LanMultiplayerHostSubmenu? Create()
        {
            if (TestMode.IsOn)
                return null;

            var multiplayerHostSubmenu =
                PreloadManager.Cache.GetScene(ScenePath).Instantiate<NMultiplayerHostSubmenu>();

            var lanMultiplayerHostSubmenu = new LanMultiplayerHostSubmenu();

            lanMultiplayerHostSubmenu.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

            // Keep the scene's unique-name lookups valid after moving its nodes.
            var ownedNodes = multiplayerHostSubmenu.FindChildren("*", "", true, false)
                .Where(node => node.Owner == multiplayerHostSubmenu).ToArray();

            foreach (var child in multiplayerHostSubmenu.GetChildren())
            {
                child.Reparent(lanMultiplayerHostSubmenu, false);
            }

            foreach (var node in ownedNodes)
            {
                node.Owner = lanMultiplayerHostSubmenu;
            }

            multiplayerHostSubmenu.QueueFreeSafely();

            return lanMultiplayerHostSubmenu;
        }

        public override void _Ready()
        {
            ConnectSignals();

            var loadingOverlay = GetNode<Control>("%LoadingOverlay");
            Traverse.Create(this).Field("_loadingOverlay").SetValue(loadingOverlay);
            _loadingOverlay = loadingOverlay;

            var standardButton = GetNode<NSubmenuButton>("%StandardButton");
            Traverse.Create(this).Field("_standardButton").SetValue(standardButton);

            standardButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(OnStandardPressed));
            standardButton.SetLocalization("STANDARD_MP");
            standardButton.SetIcon("standard");

            var dailyButton = GetNode<NSubmenuButton>("%DailyButton");
            Traverse.Create(this).Field("_dailyButton").SetValue(dailyButton);

            dailyButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(OnDailyPressed));
            dailyButton.SetLocalization("DAILY_MP");
            dailyButton.SetIcon("daily");

            var customButton = GetNode<NSubmenuButton>("%CustomRunButton");
            Traverse.Create(this).Field("_customButton").SetValue(customButton);

            customButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(OnCustomPressed));
            customButton.SetLocalization("CUSTOM_MP");
            customButton.SetIcon("custom");

            var blitzButton = GetNode<NSubmenuButton>("%BlitzButton");
            Traverse.Create(this).Field("_blitzButton").SetValue(blitzButton);
            blitzButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(OnBlitzPressed));
            blitzButton.SetLocalization("BLITZ_MP");
            blitzButton.SetIcon("blitz");

            var dreadButton = GetNode<NSubmenuButton>("%DreadButton");
            Traverse.Create(this).Field("_dreadButton").SetValue(dreadButton);
            dreadButton.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(OnDreadPressed));
            dreadButton.SetLocalization("DREAD_MP");
            dreadButton.SetIcon("dread");

        }

        private void OnStandardPressed(NButton _)
        {
            StartLanHost(GameMode.Standard);
        }

        private void OnDailyPressed(NButton _)
        {
            StartLanHost(GameMode.Daily);
        }

        private void OnBlitzPressed(NButton _)
        {
            StartLanHost(GameMode.Blitz);
        }

        private void OnDreadPressed(NButton _)
        {
            StartLanHost(GameMode.Dread);
        }

        private void OnCustomPressed(NButton _)
        {
            StartLanHost(GameMode.Standard, isCustom: true);
        }

        private void StartLanHost(GameMode gameMode, bool isCustom = false)
        {
            if (_loadingOverlay != null)
            {
                var settingsModel = SettingsService.Instance.SettingsModel;

                LanHostHelper.StartHost(gameMode, _loadingOverlay, _stack, settingsModel.HostPort,
                    settingsModel.HostMaxPlayers, isCustom);
            }
        }
    }
}