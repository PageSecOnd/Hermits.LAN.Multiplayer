using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedType.Global

namespace SlayTheSpire2.LAN.Multiplayer.Reforged.Patchs.Screens
{
    // 0.113.x groups relic holders into per-relic-count layout nodes and fills
    // _holdersInUse in InitializeRelics instead of _Ready, so extra layouts have
    // to be prepared before _Ready and the grid applied after InitializeRelics.
    [HarmonyPatch]
    internal class NTreasureRoomRelicCollectionReadyPatch
    {
        [HarmonyPatch(typeof(NTreasureRoomRelicCollection), "_Ready")]
        [HarmonyPrefix]
        private static void ReadyPrefix(NTreasureRoomRelicCollection __instance)
        {
            var runState = Traverse.Create(RunManager.Instance).Property("State").GetValue<RunState?>();

            if (runState is not { Players.Count: > 4 })
                return;

            var container = __instance.GetNode<Control>("Container");

            var layouts = container.GetChildren(false).OfType<Control>().ToList();
            if (layouts.Count == 0)
                return;

            var templateLayout = layouts
                .OrderByDescending(layout => layout.GetChildren(false).OfType<NTreasureRoomRelicHolder>().Count())
                .First();
            var templateHolder = templateLayout.GetChildren(false).OfType<NTreasureRoomRelicHolder>().FirstOrDefault();

            if (templateHolder == null)
                return;

            for (var count = layouts.Count + 1; count <= runState.Players.Count; count++)
            {
                var layout = (Control)templateLayout.Duplicate();
                layout.Name = $"RelicHolderLayout{count}";

                foreach (var existingHolder in layout.GetChildren(false).OfType<NTreasureRoomRelicHolder>().ToList())
                    layout.RemoveChild(existingHolder);

                for (var i = 0; i < count; i++)
                {
                    var holder = (NTreasureRoomRelicHolder)templateHolder.Duplicate();
                    holder.Name = $"MultiplayerRelicHolder{i + 1}";
                    layout.AddChildSafely(holder);
                }

                layout.Visible = false;
                container.AddChildSafely(layout);
            }
        }

        [HarmonyPatch(typeof(NTreasureRoomRelicCollection), "InitializeRelics")]
        [HarmonyPostfix]
        private static void InitializeRelicsPostfix(NTreasureRoomRelicCollection __instance)
        {
            var holders = __instance.HoldersInUse;

            if (holders.Count <= 4)
                return;

            var gridContainer = new GridContainer
                { Name = "RelicContainer", Columns = 4, CustomMinimumSize = new Vector2(0, 469) };

            gridContainer.AddThemeConstantOverride("h_separation", 42);
            gridContainer.AddThemeConstantOverride("v_separation", 42);
            gridContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopWide);

            var viewport = new Control { Name = "Viewport" };
            viewport.AddChildSafely(gridContainer);
            viewport.SetAnchorsPreset(Control.LayoutPreset.TopWide);
            viewport.OffsetLeft = 110;
            viewport.OffsetTop = 110;
            viewport.OffsetRight = -120;
            viewport.OffsetBottom = 580;

            var mask = new TextureRect { Name = "Mask", ClipContents = true };
            mask.AddChildSafely(viewport);
            mask.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
            mask.OffsetLeft = -450;
            mask.OffsetTop = 250;
            mask.OffsetRight = 450;
            mask.OffsetBottom = 830;

            var scrollbar = PreloadManager.Cache.GetScene(SceneHelper.GetScenePath("ui/scrollbar"))
                .Instantiate<NScrollbar>();
            scrollbar.AnchorLeft = 0.794f;
            scrollbar.AnchorTop = 0.187f;
            scrollbar.AnchorRight = 0.818f;
            scrollbar.AnchorBottom = 0.947f;
            scrollbar.OffsetLeft = -200;
            scrollbar.OffsetTop = 158;
            scrollbar.OffsetRight = -199;
            scrollbar.OffsetBottom = -163;

            var scrollContainer = new NScrollableContainer { Name = "ScrollableContainer" };
            scrollContainer.AddChildSafely(mask);
            scrollContainer.AddChildSafely(scrollbar);

            scrollContainer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            scrollContainer.OffsetLeft = -510;
            scrollContainer.OffsetTop = -250;
            scrollContainer.OffsetRight = 510;
            scrollContainer.OffsetBottom = 250;

            var container = __instance.GetNode("Container");
            container.AddChildSafely(scrollContainer);

            scrollContainer.SetContent(gridContainer, 20, 30);

            //The animation will change holder position. Manually fix the position.

            var position = new Vector2(0, 110);

            for (var i = 0; i < holders.Count; i++)
            {
                if (i > 0)
                {
                    position = i % 4 == 0
                        ? new Vector2(0, position.Y + 178)
                        : new Vector2(position.X + 178, position.Y);
                }

                var holder = holders[i];
                holder.Position = position;
                holder.Reparent(gridContainer, false);
            }
        }
    }
}
