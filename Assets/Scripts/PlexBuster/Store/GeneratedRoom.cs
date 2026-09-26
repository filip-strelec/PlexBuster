using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

        const float PosterWidth = 0.6f;
        const int MaxPosters = 10;

        readonly List<ShelfUnit> shelves = new();
        readonly Dictionary<LibraryItem, string> haystacks = new();
        List<LibraryItem> items;
        StoreTheme theme;
        PosterCache posters;
        Coroutine filling;
        string filter = "";

        public RoomLayout Layout { get; private set; }
        public SortOrder Sort { get; private set; }
        public IReadOnlyList<LibraryItem> Items => items;
        public int MatchCount { get; private set; }
        public DoorTrigger Exit { get; private set; }
        public Pose EntryPose => new(transform.TransformPoint(RoomLayout.Entry.position), transform.rotation * RoomLayout.Entry.rotation);

        public static GeneratedRoom Build(string title, IReadOnlyList<LibraryItem> items, SortOrder sort,
            StoreTheme theme, PosterCache posters, Vector3 origin)
        {
            var room = new GameObject($"Room: {title}").AddComponent<GeneratedRoom>();
            room.transform.position = origin;
            room.items = new List<LibraryItem>(items);
            room.MatchCount = items.Count;
            room.Sort = sort;
            room.theme = theme;
            room.posters = posters;
            room.Layout = RoomLayout.For(items.Count);
            room.BuildShell();
            room.BuildSigns(title);
            room.BuildShelves();
            room.BuildDecor();
            if (items.Count > 1)
                SearchTerminal.Create(room, title, new Vector3(1.15f, 0, 1.3f), new Vector3(-1, 0, -0.35f));
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

        /// <summary>Hides shelved tapes that don't match <paramref name="query"/> (see <see cref="SearchFilter"/>).</summary>
        public void SetFilter(string query)
        {
            filter = SearchFilter.Normalize(query).Trim();
            MatchCount = items.Count(Matches);
            foreach (var unit in shelves)
            foreach (var tape in unit.Tapes)
                if (tape != null && tape.Item != null) tape.SetFilteredOut(!Matches(tape.Item));
        }

        /// <summary>The room's items that match the current search, in shelf order.</summary>
        public IEnumerable<LibraryItem> Matching() => items.Where(Matches);

        bool Matches(LibraryItem item)
        {
            if (filter.Length == 0) return true;
            if (!haystacks.TryGetValue(item, out var haystack)) haystacks[item] = haystack = SearchFilter.Haystack(item);
            return SearchFilter.Matches(haystack, filter);
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
                    var item = items[index++];
                    var tape = unit.AddTape(item, theme.tapePrefab, posters);
                    if (tape != null && !Matches(item)) tape.SetFilteredOut(true);
                    if (index % theme.tapesPerFrame == 0) yield return null;
                }
            }
            filling = null;
        }

        /// <summary>
        /// Framed posters of the room's best-rated titles on wall space the shelves leave free, and a cardboard
        /// standee of the top one beside the entrance.
        /// </summary>
        void BuildDecor()
        {
            if (items.Count == 0 || theme.tapePrefab == null) return;
            var template = theme.tapePrefab.CoverTemplate;
            var loading = theme.tapePrefab.LoadingMaterial;
            var best = items.Where(i => !string.IsNullOrEmpty(i.PosterPath))
                .OrderByDescending(i => i.BestRating).ThenBy(i => i.DisplaySortTitle).ToList();
            if (best.Count == 0) return;

            var decor = new GameObject("Decor").transform;
            decor.SetParent(transform, false);
            var frame = StoreMaterials.Lit("PosterFrame", new Color(0.03f, 0.03f, 0.035f), 0.5f);

            BuildStandee(decor, best[0], template, loading);

            var next = 1;
            void Poster(Vector3 point, Vector3 outward, float height)
            {
                var item = best[next++ % best.Count];
                var poster = new GameObject($"Poster: {item.Title}").transform;
                poster.SetParent(decor, false);
                poster.SetLocalPositionAndRotation(point + outward * 0.015f + Vector3.up * height, Quaternion.LookRotation(outward));

                var box = Signage.Box(poster, "Frame", new Vector3(0, 0, -0.005f), new Vector3(PosterWidth + 0.06f, PosterWidth * 1.5f + 0.06f, 0.02f), frame);
                Destroy(box.GetComponent<Collider>());
                CoverDisplay.Show(Picture(poster, PosterWidth, 0.006f), item, posters, template, loading);
            }

            var spots = Layout.PosterSpots(PosterWidth + 0.06f, 0.5f);
            for (var i = 0; i < Mathf.Min(spots.Count, MaxPosters); i++) Poster(spots[i].Position, spots[i].Inward, 1.55f);

            // A single-sided island shows its back to the room: dress it with a poster too.
            foreach (var (point, outward) in Layout.LoneIslandBacks()) Poster(point, outward, 0.95f);
        }

        void BuildStandee(Transform parent, LibraryItem item, Material template, Material loading)
        {
            // Left of the entrance, turned towards the door (the search terminal stands on the right).
            var standee = new GameObject($"Standee: {item.Title}").transform;
            standee.SetParent(parent, false);
            standee.SetLocalPositionAndRotation(new Vector3(-1.25f, 0, 1.35f), Quaternion.LookRotation(new Vector3(1, 0, -0.35f)));

            var card = StoreMaterials.Lit("Cardboard", new Color(0.72f, 0.64f, 0.5f), 0.1f);
            const float width = 0.9f, height = width * 1.5f, lift = 0.18f;
            Signage.Box(standee, "Board", new Vector3(0, lift + height / 2, -0.008f), new Vector3(width + 0.04f, height + 0.04f, 0.012f), card);
            Signage.Box(standee, "Foot", new Vector3(0, lift / 2, -0.06f), new Vector3(width * 0.7f, lift, 0.24f), card);
            var picture = Picture(standee, width, 0f);
            picture.transform.localPosition += Vector3.up * (lift + height / 2);
            CoverDisplay.Show(picture, item, posters, template, loading);
        }

        /// <summary>A poster-shaped quad (2:3) facing +Z of <paramref name="parent"/>.</summary>
        static Renderer Picture(Transform parent, float width, float z)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Picture";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            // Unity's quad faces -Z; turn it around so the picture faces the room.
            quad.transform.SetLocalPositionAndRotation(new Vector3(0, 0, z), Quaternion.Euler(0, 180, 0));
            quad.transform.localScale = new Vector3(width, width * 1.5f, 1);
            return quad.GetComponent<MeshRenderer>();
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
