using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PlexBuster.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PlexBuster.Store
{
    /// <summary>
    /// "Browse by…" board in a department hall: decades, the actors and directors who headline the most
    /// titles (plus an A–Z index of everyone), and collections. Picking an entry opens a room for it.
    /// </summary>
    public class BrowseKiosk : MonoBehaviour
    {
        enum Tab { Decades, Actors, Directors, Collections }

        const int Columns = 3;
        const int Rows = 7;
        const int PageSize = Columns * Rows;
        const string Letters = "#ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        class Entry
        {
            public FilterValue Value;
            public string Label;
        }

        readonly Dictionary<Tab, List<Entry>> top = new();
        readonly Dictionary<Tab, IReadOnlyList<FilterValue>> everyone = new();
        readonly Dictionary<Tab, Button> tabButtons = new();
        readonly Dictionary<char, Button> letterButtons = new();
        readonly List<Button> entryButtons = new();

        LibrarySection section;
        Pose returnPose;
        RectTransform letterRow;
        Button topButton, previous, next;
        TextMeshProUGUI pageLabel, status;
        List<Entry> shown = new();
        Tab tab;
        char? letter;
        int page;

        /// <param name="localPosition">Centre of the board in <paramref name="parent"/> space.</param>
        /// <param name="facing">Direction (parent space) the reader stands in.</param>
        public static BrowseKiosk Create(Transform parent, Vector3 localPosition, Vector3 facing, LibrarySection section)
        {
            var canvas = WorldUI.CreateCanvas(parent, "BrowseKiosk", new Vector2(1250, 960), localPosition, Quaternion.LookRotation(-facing));
            var kiosk = canvas.gameObject.AddComponent<BrowseKiosk>();
            kiosk.section = section;
            var world = canvas.parent;
            // Coming back from a room, stand in front of the board, facing it.
            kiosk.returnPose = new Pose(world.TransformPoint(localPosition + facing.normalized * 1.1f - Vector3.up * localPosition.y),
                world.rotation * Quaternion.LookRotation(-facing));
            kiosk.Build(canvas);
            return kiosk;
        }

        void Build(RectTransform canvas)
        {
            WorldUI.Label(WorldUI.Area(canvas, "Title", new Vector2(0, 0.9f), new Vector2(1, 1), 10), $"BROWSE {section.Title.ToUpperInvariant()} BY", 52);

            var tabs = WorldUI.Area(canvas, "Tabs", new Vector2(0, 0.8f), new Vector2(1, 0.9f), 10);
            WorldUI.Grid(tabs, new Vector2(290, 76), new Vector2(12, 0), 4);
            foreach (Tab t in Enum.GetValues(typeof(Tab)))
                tabButtons[t] = WorldUI.Button(tabs, t.ToString().ToUpperInvariant(), 34, () => ShowTab(t));

            letterRow = WorldUI.Area(canvas, "Letters", new Vector2(0, 0.72f), new Vector2(1, 0.8f), 8);
            WorldUI.Grid(letterRow, new Vector2(38, 56), new Vector2(3, 0), 28);
            topButton = WorldUI.Button(letterRow, "TOP", 22, () => ShowLetter(null));
            foreach (var c in Letters)
                letterButtons[c] = WorldUI.Button(letterRow, c.ToString(), 26, () => ShowLetter(c));

            var grid = WorldUI.Area(canvas, "Entries", new Vector2(0, 0.1f), new Vector2(1, 0.72f), 14);
            WorldUI.Grid(grid, new Vector2(390, 72), new Vector2(12, 10), Columns);
            for (var i = 0; i < PageSize; i++)
            {
                var index = i;
                entryButtons.Add(WorldUI.Button(grid, "", 30, () => Open(index)));
            }

            var footer = WorldUI.Area(canvas, "Footer", new Vector2(0, 0), new Vector2(1, 0.1f), 10);
            previous = WorldUI.Button(WorldUI.Area(footer, "Prev", new Vector2(0, 0), new Vector2(0.2f, 1)), "<  PREV", 30, () => ShowPage(page - 1));
            next = WorldUI.Button(WorldUI.Area(footer, "Next", new Vector2(0.8f, 0), new Vector2(1, 1)), "NEXT  >", 30, () => ShowPage(page + 1));
            pageLabel = WorldUI.Label(WorldUI.Area(footer, "Page", new Vector2(0.2f, 0), new Vector2(0.8f, 1)), "", 30);
            // A sibling of the grid, not a child, so the grid doesn't lay it out as a cell.
            status = WorldUI.Label(WorldUI.Area(canvas, "Status", new Vector2(0, 0.1f), new Vector2(1, 0.72f), 14), "Loading…", 40);

            foreach (var button in entryButtons) button.gameObject.SetActive(false);
            previous.gameObject.SetActive(false);
            next.gameObject.SetActive(false);
            letterRow.gameObject.SetActive(false);
        }

        async void Start()
        {
            var services = StoreServices.Instance;
            var library = services.Library;
            var ct = services.LifetimeToken;
            try
            {
                var decades = await library.GetFilterValuesAsync(FilterType.Decade, section.Id, ct);
                top[Tab.Decades] = decades.Select(v => new Entry { Value = v, Label = v.Title }).ToList();

                var collections = await library.GetFilterValuesAsync(FilterType.Collection, section.Id, ct);
                top[Tab.Collections] = collections.Select(v => new Entry { Value = v, Label = v.Title }).ToList();

                foreach (var (t, type) in new[] { (Tab.Actors, FilterType.Actor), (Tab.Directors, FilterType.Director) })
                {
                    var ranked = await FilterRanking.RankAsync(library, type, section.Id, ct);
                    top[t] = ranked.Take(PageSize * 3).Select(r => new Entry { Value = r.Value, Label = $"{r.Value.Title}  ({r.Count})" }).ToList();
                    everyone[t] = await library.GetFilterValuesAsync(type, section.Id, ct);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            if (this == null) return;

            foreach (var (t, button) in tabButtons)
                button.gameObject.SetActive(top.TryGetValue(t, out var list) && list.Count > 0);
            var first = tabButtons.Keys.FirstOrDefault(t => top.TryGetValue(t, out var list) && list.Count > 0);
            ShowTab(first);
        }

        void ShowTab(Tab t)
        {
            tab = t;
            foreach (var (other, button) in tabButtons) WorldUI.SetSelected(button, other == t);
            letterRow.gameObject.SetActive(everyone.ContainsKey(t));
            ShowLetter(null);
        }

        void ShowLetter(char? c)
        {
            letter = c;
            WorldUI.SetSelected(topButton, c == null);
            foreach (var (other, button) in letterButtons) WorldUI.SetSelected(button, other == c);

            if (c == null || !everyone.TryGetValue(tab, out var all))
                shown = top.TryGetValue(tab, out var list) ? list : new List<Entry>();
            else
                shown = all.Where(v => InitialOf(v.Title) == c).Select(v => new Entry { Value = v, Label = v.Title }).ToList();
            ShowPage(0);
        }

        void ShowPage(int p)
        {
            var pages = Mathf.Max(1, Mathf.CeilToInt(shown.Count / (float)PageSize));
            page = Mathf.Clamp(p, 0, pages - 1);
            for (var i = 0; i < entryButtons.Count; i++)
            {
                var index = page * PageSize + i;
                var visible = index < shown.Count;
                entryButtons[i].gameObject.SetActive(visible);
                if (visible) WorldUI.SetText(entryButtons[i], shown[index].Label);
            }
            previous.gameObject.SetActive(page > 0);
            next.gameObject.SetActive(page < pages - 1);
            pageLabel.text = pages > 1 ? $"{page + 1} / {pages}" : "";
            status.text = shown.Count == 0 ? "Nothing here" : "";
        }

        void Open(int slot)
        {
            var index = page * PageSize + slot;
            if (index >= shown.Count) return;
            var value = shown[index].Value;
            StoreNavigator.Instance.EnterRoom($"{value.Title} · {section.Title}", LibraryQuery.For(value, section.Id), returnPose);
        }

        static char InitialOf(string title)
        {
            if (string.IsNullOrEmpty(title)) return '#';
            // Fold accents so "Đorđe" and "Émile" land under D and E.
            var folded = title.Normalize(NormalizationForm.FormD)[0];
            var c = char.ToUpperInvariant(folded == 'Đ' || folded == 'đ' ? 'D' : folded);
            return c is >= 'A' and <= 'Z' ? c : '#';
        }
    }
}
