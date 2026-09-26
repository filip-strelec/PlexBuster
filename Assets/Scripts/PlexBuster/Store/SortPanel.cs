using System.Collections.Generic;
using PlexBuster.Data;
using UnityEngine;
using UnityEngine.UI;

namespace PlexBuster.Store
{
    /// <summary>A lectern by a room's entrance for choosing how the shelves are sorted.</summary>
    public class SortPanel : MonoBehaviour
    {
        static readonly (SortOrder Order, string Label)[] Options =
        {
            (SortOrder.Title, "A – Z"),
            (SortOrder.YearNewest, "Newest"),
            (SortOrder.YearOldest, "Oldest"),
            (SortOrder.Rating, "Top rated"),
            (SortOrder.RecentlyAdded, "Just added"),
        };

        readonly Dictionary<SortOrder, Button> buttons = new();
        GeneratedRoom room;

        /// <param name="localPosition">Floor point of the lectern in <paramref name="parent"/> space.</param>
        /// <param name="facing">Direction (parent space) the reader stands in, looking back at the panel.</param>
        public static SortPanel Create(GeneratedRoom room, Transform parent, Vector3 localPosition, Vector3 facing, StoreTheme theme)
        {
            var root = new GameObject("SortPanel").transform;
            root.SetParent(parent, false);
            root.SetLocalPositionAndRotation(localPosition, Quaternion.LookRotation(-facing));

            var post = Signage.Box(root, "Post", new Vector3(0, 0.5f, 0.02f), new Vector3(0.08f, 1f, 0.08f), theme.doorFrameMaterial);
            Object.Destroy(post.GetComponent<Collider>());

            // Tilted back like a lectern, readable from the facing side.
            var canvas = WorldUI.CreateCanvas(root, "Canvas", new Vector2(360, 470), new Vector3(0, 1.15f, 0), Quaternion.Euler(30, 0, 0));
            var panel = canvas.gameObject.AddComponent<SortPanel>();
            panel.room = room;

            WorldUI.Label(WorldUI.Area(canvas, "Title", new Vector2(0, 0.84f), new Vector2(1, 1), 12), "SORT SHELVES", 40);
            var list = WorldUI.Area(canvas, "Options", new Vector2(0, 0), new Vector2(1, 0.84f), 16);
            WorldUI.Grid(list, new Vector2(320, 64), new Vector2(0, 12), 1);
            foreach (var (order, label) in Options)
                panel.buttons[order] = WorldUI.Button(list, label, 32, () => panel.Choose(order));

            panel.Highlight(room.Sort);
            return panel;
        }

        void Choose(SortOrder order)
        {
            room.Resort(order);
            Highlight(order);
        }

        void Highlight(SortOrder current)
        {
            foreach (var (order, button) in buttons) WorldUI.SetSelected(button, order == current);
        }
    }
}
