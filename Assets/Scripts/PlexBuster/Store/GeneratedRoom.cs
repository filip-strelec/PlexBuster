using System.Collections;
using System.Collections.Generic;
using PlexBuster.Data;
using TMPro;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// A graybox room built at runtime for one query result: shell, lights, signs, shelves sized to the
    /// result, and tapes streamed in a few per frame. Walking out through the door fires <see cref="Exit"/>.
    /// </summary>
    public class GeneratedRoom : MonoBehaviour, IStorePlace
    {
        const float WallThickness = StoreShell.WallThickness;

        readonly List<ShelfUnit> shelves = new();
        List<LibraryItem> items;
        StoreTheme theme;
        PosterCache posters;
        Coroutine filling;

        public RoomLayout Layout { get; private set; }
        public SortOrder Sort { get; private set; }
        public DoorTrigger Exit { get; private set; }
        public Pose EntryPose => new(transform.TransformPoint(RoomLayout.Entry.position), transform.rotation * RoomLayout.Entry.rotation);

        public static GeneratedRoom Build(string title, IReadOnlyList<LibraryItem> items, SortOrder sort,
            StoreTheme theme, PosterCache posters, Vector3 origin)
        {
            var room = new GameObject($"Room: {title}").AddComponent<GeneratedRoom>();
            room.transform.position = origin;
            room.items = new List<LibraryItem>(items);
            room.Sort = sort;
            room.theme = theme;
            room.posters = posters;
            room.Layout = RoomLayout.For(items.Count);
            room.BuildShell();
            room.BuildSigns(title);
            room.BuildShelves();
            if (items.Count > 1)
                SortPanel.Create(room, room.transform, new Vector3(1.1f, 0, 1.3f), new Vector3(-1, 0, -0.35f), theme);
            room.filling = room.StartCoroutine(room.Fill());
            return room;
        }

        /// <summary>Re-shelves the room in a different order. Posters are cached, so this is quick.</summary>
        public void Resort(SortOrder sort)
        {
            if (sort == Sort) return;
            Sort = sort;
            LibrarySorting.Apply(items, sort);

            if (filling != null) StopCoroutine(filling);
            foreach (var unit in shelves) unit.Clear();
            LabelShelves();
            filling = StartCoroutine(Fill());
        }

        void BuildShell()
        {
            var w = Layout.Width;
            var d = Layout.Depth;
            var h = theme.wallHeight;
            var shell = new GameObject("Shell").transform;
            shell.SetParent(transform, false);

            StoreShell.Floor(shell, new Vector3(0, -0.05f, d / 2), new Vector3(w + 2 * WallThickness, 0.1f, d + 2 * WallThickness), theme.floorMaterial, theme.floorTileMeters);
            StoreShell.Ceiling(shell, new Vector3(0, h + 0.05f, d / 2), new Vector3(w + 2 * WallThickness, 0.1f, d + 2 * WallThickness), theme);

            Signage.Box(shell, "Wall_Back", new Vector3(0, h / 2, d + WallThickness / 2), new Vector3(w, h, WallThickness), theme.wallMaterial);
            Signage.Box(shell, "Wall_Left", new Vector3(-(w + WallThickness) / 2, h / 2, d / 2), new Vector3(WallThickness, h, d + 2 * WallThickness), theme.wallMaterial);
            Signage.Box(shell, "Wall_Right", new Vector3((w + WallThickness) / 2, h / 2, d / 2), new Vector3(WallThickness, h, d + 2 * WallThickness), theme.wallMaterial);
            StoreShell.WallBand(shell, theme, new Vector3(-w / 2, 0, d), new Vector3(w / 2, 0, d), Vector3.back, h);
            StoreShell.WallBand(shell, theme, new Vector3(-w / 2, 0, 0), new Vector3(-w / 2, 0, d), Vector3.right, h);
            StoreShell.WallBand(shell, theme, new Vector3(w / 2, 0, 0), new Vector3(w / 2, 0, d), Vector3.left, h);
            Exit = StoreShell.FrontWallWithExit(shell, theme, w, h);

            var nx = Mathf.Max(1, Mathf.RoundToInt(w / theme.lightSpacing));
            var nz = Mathf.Max(1, Mathf.RoundToInt(d / theme.lightSpacing));
            for (var ix = 0; ix < nx; ix++)
            for (var iz = 0; iz < nz; iz++)
                StoreShell.CeilingLight(shell, theme, new Vector3((ix + 0.5f) * w / nx - w / 2, h, (iz + 0.5f) * d / nz));
        }

        void BuildSigns(string title)
        {
            var signs = new GameObject("Signs").transform;
            signs.SetParent(transform, false);
            var d = Layout.Depth;
            var h = theme.wallHeight;

            // Title on the back wall, above the shelves, facing the entrance.
            // On the wall band, above the shelves.
            var bandHeight = h - StoreShell.BandBottom;
            var titleSign = Signage.CreateText(signs, "Title", new Vector3(0, StoreShell.BandBottom + bandHeight / 2, d - 0.02f), Quaternion.identity,
                5f, theme.signColor, new Vector2(Layout.Width - 1f, bandHeight - 0.06f), material: theme.signTextMaterial);
            titleSign.text = title.ToUpperInvariant();
            titleSign.fontStyle = FontStyles.Bold;
            titleSign.fontSizeMin = 1f;

            if (items.Count == 0)
                Signage.CreateText(signs, "Empty", new Vector3(0, 1.4f, d - 0.02f), Quaternion.identity, 2f, theme.labelColor,
                    new Vector2(Layout.Width - 1f, 0.5f)).text = "Nothing on the shelves here yet";
        }

        void BuildShelves()
        {
            var root = new GameObject("Shelves").transform;
            root.SetParent(transform, false);

            for (var i = 0; i < Layout.Shelves.Count; i++)
            {
                var placement = Layout.Shelves[i];
                var go = new GameObject($"ShelfUnit_{i:00}");
                go.transform.SetParent(root, false);
                go.transform.SetLocalPositionAndRotation(placement.Position, placement.Rotation);
                var unit = go.AddComponent<ShelfUnit>();
                unit.Configure(RoomLayout.Columns, placement.Rows, theme.shelfMaterial, theme.shelfAccentMaterial);
                unit.EnsureBuilt();
                shelves.Add(unit);
            }
            LabelShelves();
        }

        void LabelShelves()
        {
            var start = 0;
            foreach (var unit in shelves)
            {
                var end = Mathf.Min(start + unit.Capacity, items.Count) - 1;
                unit.SetLabel(end >= start ? RangeLabel(items[start], items[end], Sort) : "", theme.labelColor);
                start += unit.Capacity;
            }
        }

        IEnumerator Fill()
        {
            var index = 0;
            foreach (var unit in shelves)
            {
                while (!unit.IsFull && index < items.Count)
                {
                    unit.AddTape(items[index++], theme.tapePrefab, posters);
                    if (index % theme.tapesPerFrame == 0) yield return null;
                }
            }
            filling = null;
        }

        static string RangeLabel(LibraryItem first, LibraryItem last, SortOrder sort)
        {
            string Span(string a, string b) => a == b ? a : $"{a} – {b}";
            return sort switch
            {
                SortOrder.Title => Span(Initial(first), Initial(last)),
                SortOrder.YearNewest or SortOrder.YearOldest => Span(first.Year.ToString(), last.Year.ToString()),
                SortOrder.Rating => Span($"{first.BestRating:0.0}", $"{last.BestRating:0.0}"),
                _ => "",
            };
        }

        static string Initial(LibraryItem item)
        {
            var title = item.DisplaySortTitle;
            if (string.IsNullOrEmpty(title)) return "#";
            var c = char.ToUpperInvariant(title[0]);
            return char.IsLetter(c) ? c.ToString() : "#";
        }
    }
}
