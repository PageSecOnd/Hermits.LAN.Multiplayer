using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Helpers;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Services;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Components
{
    internal partial class LanMultiplayerHostSubmenu : NMultiplayerHostSubmenu
    {
        private static readonly string ScenePath = SceneHelper.GetScenePath("screens/multiplayer_host_submenu");

        private Control? _loadingOverlay;

        private NSubmenuButton? _lanDailyButton;
        private NSubmenuButton? _lanBlitzButton;
        private NSubmenuButton? _lanCustomButton;
        private NSubmenuButton? _lanDreadButton;

        public new static LanMultiplayerHostSubmenu? Create()
        {
            if (TestMode.IsOn)
                return null;

            var multiplayerHostSubmenu =
                PreloadManager.Cache.GetScene(ScenePath).Instantiate<NMultiplayerHostSubmenu>();

            var lanMultiplayerHostSubmenu = new LanMultiplayerHostSubmenu();

            lanMultiplayerHostSubmenu.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

            foreach (var child in multiplayerHostSubmenu.GetChildren())
            {
                child.Reparent(lanMultiplayerHostSubmenu, false);
            }

            multiplayerHostSubmenu.QueueFreeSafely();

            return lanMultiplayerHostSubmenu;
        }

        public override void _Ready()
        {
            ConnectSignals();

            var loadingOverlay = FindChild("LoadingOverlay", true, false) as Control;
            if (loadingOverlay == null)
            {
                GD.PushError("[LAN Multiplayer] LoadingOverlay not found in multiplayer host submenu");
                return;
            }

            Traverse.Create(this).Field("_loadingOverlay").SetValue(loadingOverlay);
            _loadingOverlay = loadingOverlay;

            BindButton("StandardButton", "_standardButton", "STANDARD_MP", "standard", OnStandardPressed);
            _lanDailyButton = BindButton("DailyButton", "_dailyButton", "DAILY_MP", "daily", OnDailyPressed);
            _lanBlitzButton = BindButton("BlitzButton", "_blitzButton", "BLITZ_MP", "blitz", OnBlitzPressed);
            _lanDreadButton = BindButton("DreadButton", "_dreadButton", "DREAD_MP", "dread", OnDreadPressed);
            _lanCustomButton = BindButton("CustomRunButton", "_customButton", "CUSTOM_MP", "custom", OnCustomPressed);
        }

        // NMultiplayerHostSubmenu.RefreshButtons relies on the "%ButtonContainer" unique-name lookup,
        // which no longer resolves after the scene children are reparented to this node.
        // Refresh the button states here without that lookup instead.
        public override void OnSubmenuOpened()
        {
            var saveManager = SaveManager.Instance;
            var dreadRevealed = saveManager.IsEpochRevealed<DreadEpoch>();

            _lanDailyButton?.SetEnabled(saveManager.IsEpochRevealed<DailyRunEpoch>());
            _lanBlitzButton?.SetEnabled(saveManager.IsEpochRevealed<BlitzEpoch>());
            _lanCustomButton?.SetEnabled(saveManager.IsEpochRevealed<CustomAndSeedsEpoch>());

            if (_lanDreadButton != null)
            {
                _lanDreadButton.SetEnabled(dreadRevealed);
                _lanDreadButton.Visible = dreadRevealed;
            }

            if (dreadRevealed && FindChild("ButtonContainer", true, false) is Control buttonContainer)
                buttonContainer.Scale = Vector2.One * 0.85f;
        }

        // Buttons live inside ButtonContainer in 0.113.x, so look them up by name recursively
        // instead of using a direct relative path.
        private NSubmenuButton? BindButton(string nodeName, string fieldName, string localizationKey, string iconName,
            Action<NButton> handler)
        {
            if (FindChild(nodeName, true, false) is not NSubmenuButton button)
            {
                GD.PushWarning($"[LAN Multiplayer] Button '{nodeName}' not found in multiplayer host submenu");
                return null;
            }

            Traverse.Create(this).Field(fieldName).SetValue(button);

            button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(handler));
            button.SetLocalization(localizationKey);
            button.SetIcon(iconName);

            return button;
        }

        private void OnStandardPressed(NButton _)
        {
            StartHost(GameMode.Standard, false);
        }

        private void OnDailyPressed(NButton _)
        {
            StartHost(GameMode.Daily, false);
        }

        private void OnBlitzPressed(NButton _)
        {
            StartHost(GameMode.Blitz, false);
        }

        private void OnDreadPressed(NButton _)
        {
            StartHost(GameMode.Dread, false);
        }

        private void OnCustomPressed(NButton _)
        {
            StartHost(GameMode.Standard, true);
        }

        private new void StartHost(GameMode gameMode, bool isCustom)
        {
            if (_loadingOverlay != null)
            {
                var settingsModel = SettingsService.Instance.SettingsModel;

                LanHostHelper.StartHost(gameMode, isCustom, _loadingOverlay, _stack, settingsModel.HostPort,
                    settingsModel.HostMaxPlayers);
            }
        }
    }
}