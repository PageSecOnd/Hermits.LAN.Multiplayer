using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Components;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Integrations;
using SlayTheSpire2.LAN.Multiplayer.Reforged.Services;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Patchs.Screens
{
    [HarmonyPatch(typeof(NJoinFriendScreen), "_Ready")]
    internal class NJoinFriendScreenReadyPatch
    {
        private static void Postfix(NJoinFriendScreen __instance)
        {
            // Run after the vanilla _Ready so a LAN UI failure cannot leave the
            // original join screen half-initialized.
            var panel = __instance.GetNodeOrNull<NinePatchRect>("Panel") ??
                        __instance.FindChild("Panel", true, false) as NinePatchRect;
            var refreshButton = __instance.GetNodeOrNull<NJoinFriendRefreshButton>("RefreshButton") ??
                                __instance.FindChild("RefreshButton", true, false) as NJoinFriendRefreshButton;
            var titleLabel = __instance.GetNodeOrNull<MegaLabel>("TitleLabel") ??
                             __instance.FindChild("TitleLabel", true, false) as MegaLabel;

            if (panel == null || refreshButton == null || titleLabel == null)
            {
                GD.PushWarning("[LAN Multiplayer] Join-friend scene layout changed; LAN join panel was not injected.");
                return;
            }

            var lanPanel = new NinePatchRect
            {
                Name = "LANPanel",
                Texture = panel.Texture,
                SelfModulate = panel.SelfModulate,
                PatchMarginTop = 12,
                PatchMarginBottom = 12,
                PatchMarginLeft = 12,
                PatchMarginRight = 12
            };

            lanPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
            lanPanel.OffsetLeft = 450;
            lanPanel.OffsetTop = -338;
            lanPanel.OffsetRight = 790;
            lanPanel.OffsetBottom = 338;

            var vBoxContainer = new VBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center
            };

            vBoxContainer.AddThemeConstantOverride("separation", 24);
            lanPanel.AddChildSafely(vBoxContainer);
            vBoxContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

            var addressLineEdit = new AddressLineEdit
            {
                Name = "AddressInput", Text = SettingsService.Instance.SettingsModel.IPAddress,
                Alignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(300, 50),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter
            };

            vBoxContainer.AddChildSafely(addressLineEdit);

            var joinButton = JoinButton.Create(refreshButton);
            joinButton.Name = "JointButton";
            vBoxContainer.AddChildSafely(joinButton);

            joinButton.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ =>
            {
                var addressInfo = addressLineEdit.GetAddressInfo();

                if (!addressInfo.IsValid)
                    return;

                var settingsService = SettingsService.Instance;
                var settings = settingsService.SettingsModel;

                if (settings.RememberJoinAddress)
                {
                    settings.IPAddress = addressLineEdit.Text;
                    settingsService.WriteSettings();
                    ModConfigBridge.SetValue(ModConfigBridge.DefaultAddressKey, addressLineEdit.Text);
                }

                ushort port = settings.HostPort;

                if (addressInfo.Port.HasValue)
                {
                    port = addressInfo.Port.Value;
                }

                DisplayServer.WindowSetTitle("Slay The Spire 2 (Client)");
                if (addressInfo.Address != null)
                {
                    TaskHelper.RunSafely(
                        __instance.JoinGameAsync(new ENetClientConnectionInitializer(
                            SettingsService.Instance.SettingsModel.NetId, addressInfo.Address, port)));
                }
            }));

            var ipAddressLabel = (MegaLabel)titleLabel.Duplicate();
            ipAddressLabel.CustomMinimumSize = new Vector2(300, 0);
            ipAddressLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
            lanPanel.AddChildSafely(ipAddressLabel);
            ipAddressLabel.SetTextAutoSize("LAN IP:");

            __instance.AddChildSafely(lanPanel);
        }
    }
}
