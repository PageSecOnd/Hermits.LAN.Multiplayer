using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Components
{
    internal partial class JoinButton : NJoinFriendRefreshButton
    {
        protected override string[] Hotkeys => [MegaInput.viewMap];

        public static JoinButton Create(NJoinFriendRefreshButton joinFriendRefreshButton)
        {
            var joinButton = new JoinButton
            {
                CustomMinimumSize = new Vector2(150, 50),
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Stop,
                Material = (Material)joinFriendRefreshButton.Material.Duplicate()
            };

            var background = new NinePatchRect
            {
                Name = "Background", MouseFilter = MouseFilterEnum.Ignore,
                Texture = GD.Load<CompressedTexture2D>("res://images/ui/tiny_nine_patch.png"), PatchMarginLeft = 12,
                PatchMarginTop = 12, PatchMarginRight = 12, PatchMarginBottom = 12,
                Modulate = joinFriendRefreshButton.SelfModulate,
                Material = joinButton.Material
            };

            joinButton.AddChildSafely(background);
            background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

            foreach (var child in joinFriendRefreshButton.GetChildren())
            {
                joinButton.AddChildSafely(child.Duplicate());
            }

            AssignOwnerRecursively(joinButton, joinButton);

            // v0.110.x moved ControllerIcon away from being a direct child and the
            // vanilla scene now references it as a unique node (%ControllerIcon).
            // Find it recursively so both the old and new scene layouts work.
            if (joinButton.FindChild("ControllerIcon", true, false) is TextureRect controllerIcon)
            {
                controllerIcon.Position = new Vector2(controllerIcon.Position.X - 12, controllerIcon.Position.Y);
            }

            return joinButton;
        }

        private static void AssignOwnerRecursively(Node node, Node owner)
        {
            foreach (var child in node.GetChildren())
            {
                child.Owner = owner;
                AssignOwnerRecursively(child, owner);
            }
        }

        public override void _Ready()
        {
            base._Ready();

            if (FindChild("Label", true, false) is MegaLabel node)
            {
                node.SetTextAutoSize(new LocString("main_menu_ui", "JOIN.title").GetFormattedText());
            }
        }
    }
}
