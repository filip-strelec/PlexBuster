using System;
using System.Collections;
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
    /// A retro search terminal by a room's entrance: green CRT screen and a physical keyboard. Typing filters
    /// the shelves (tapes that don't match vanish) and the function row sorts them. Keys can be poked with a
    /// controller tip or pointed at and clicked; invisible UI buttons sit over the 3D keycaps.
    /// Terminal space: origin on the floor, +Z points towards the person using it.
    /// </summary>
    public class SearchTerminal : MonoBehaviour
    {
        const float KeySize = 0.04f;
        const float Pitch = 0.046f;
        const float KeyHeight = 0.014f;
        const float BaseTop = 0.025f;
        const int MaxQuery = 24;
        const int ListedMatches = 6;

        static readonly (SortOrder Order, string Label)[] SortKeys =
        {
            (SortOrder.Title, "A–Z"), (SortOrder.YearNewest, "NEWEST"), (SortOrder.YearOldest, "OLDEST"),
            (SortOrder.Rating, "TOP"), (SortOrder.RecentlyAdded, "ADDED"),
        };

        static readonly string[] LetterRows = { "1234567890", "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" };

        readonly Dictionary<SortOrder, Renderer> sortKeycaps = new();
        GeneratedRoom room;
        string title;
        string query = "";
        TextMeshPro screen;
        Transform keyboard;
        RectTransform keyCanvas;
        Material keycapMaterial, keycapActiveMaterial;
        float nextBlink;
        bool cursorOn = true;

        /// <param name="facing">Direction (room space) from the terminal towards where its user stands.</param>
        public static SearchTerminal Create(GeneratedRoom room, string title, Vector3 localPosition, Vector3 facing)
        {
            var root = new GameObject("SearchTerminal");
            root.transform.SetParent(room.transform, false);
            root.transform.SetLocalPositionAndRotation(localPosition, Quaternion.LookRotation(facing.normalized));
            var terminal = root.AddComponent<SearchTerminal>();
            terminal.room = room;
            terminal.title = title;
            terminal.Build();
            terminal.Refresh();
            return terminal;
        }

        void Build()
        {
            var body = StoreMaterials.Lit("TerminalBody", new Color(0.55f, 0.52f, 0.45f), 0.35f);
            var dark = StoreMaterials.Lit("TerminalDark", new Color(0.12f, 0.12f, 0.13f), 0.4f);
            keycapMaterial = StoreMaterials.Lit("Keycap", new Color(0.8f, 0.77f, 0.68f), 0.3f);
            keycapActiveMaterial = StoreMaterials.Lit("KeycapActive", new Color(1f, 0.75f, 0.1f), 0.3f);

            // Pedestal, desk, monitor.
            // The monitor's front sits at z = 0; the tilted keyboard starts just in front of it.
            Solid("Pedestal", new Vector3(0, 0.45f, -0.1f), new Vector3(0.46f, 0.9f, 0.5f), dark);
            // Wider on the user's left (+X), where the re-shelve button sits beside the keyboard.
            Solid("Desk", new Vector3(0.11f, 0.915f, -0.05f), new Vector3(0.8f, 0.03f, 0.74f), body);
            Solid("Monitor", new Vector3(0, 1.13f, -0.17f), new Vector3(0.42f, 0.34f, 0.34f), body);
            Solid("MonitorBack", new Vector3(0, 1.12f, -0.42f), new Vector3(0.3f, 0.26f, 0.18f), body);

            var glass = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glass.name = "Screen";
            Destroy(glass.GetComponent<Collider>());
            glass.transform.SetParent(transform, false);
            glass.transform.SetLocalPositionAndRotation(new Vector3(0, 1.14f, 0.002f), Quaternion.Euler(0, 180, 0));
            glass.transform.localScale = new Vector3(0.35f, 0.26f, 1);
            glass.GetComponent<MeshRenderer>().sharedMaterial =
                StoreMaterials.Lit("TerminalScreen", new Color(0.01f, 0.03f, 0.015f), 0.9f, new Color(0f, 0.06f, 0.025f));

            screen = Signage.CreateText(transform, "ScreenText", new Vector3(0, 1.14f, 0.004f), Quaternion.Euler(0, 180, 0),
                0.16f, Color.white, new Vector2(0.32f, 0.235f), TextAlignmentOptions.TopLeft,
                StoreMaterials.GlowText("TerminalPhosphor", new Color(0.25f, 1f, 0.45f) * 1.6f));
            screen.enableAutoSizing = false;
            screen.textWrappingMode = TextWrappingModes.NoWrap;
            screen.overflowMode = TextOverflowModes.Truncate;
            screen.lineSpacing = -8;

            // Keyboard, tilted towards the user.
            keyboard = new GameObject("Keyboard").transform;
            keyboard.SetParent(transform, false);
            keyboard.SetLocalPositionAndRotation(new Vector3(0, 0.93f, 0.16f), Quaternion.Euler(8, 0, 0));
            var keyboardBase = Signage.Box(keyboard, "Base", new Vector3(0, BaseTop / 2, 0), new Vector3(0.52f, BaseTop, 0.31f), body);
            Destroy(keyboardBase.GetComponent<Collider>());

            // Invisible buttons over the keycaps: the XR UI pipeline handles poke and ray clicks.
            keyCanvas = WorldUI.CreateCanvas(keyboard, "Keys", new Vector2(520, 310),
                new Vector3(0, BaseTop + KeyHeight + 0.002f, 0), Quaternion.LookRotation(Vector3.down, Vector3.back));
            keyCanvas.GetComponent<Image>().color = Color.clear;

            // Rows from the far side (function keys) to the near side (space bar); u runs to the user's right.
            for (var i = 0; i < SortKeys.Length; i++)
            {
                var (order, label) = SortKeys[i];
                var width = 2 * Pitch - 0.006f;
                var key = Key(label, (i - (SortKeys.Length - 1) / 2f) * 2 * Pitch, -0.115f, width, 0.11f, () => ChooseSort(order));
                sortKeycaps[order] = key;
            }
            for (var r = 0; r < LetterRows.Length; r++)
            {
                var row = LetterRows[r];
                var offset = r switch { 2 => 0.25f, 3 => 0.5f, _ => 0f } * Pitch;
                for (var i = 0; i < row.Length; i++)
                {
                    var c = row[i];
                    Key(c.ToString(), (i - 4.5f) * Pitch + offset, -0.069f + r * Pitch, KeySize, 0.2f, () => Type(c));
                }
            }
            Key("DEL", (7 - 4.5f) * Pitch + Pitch, 0.069f, 2 * Pitch - 0.006f, 0.13f, Backspace);
            Key("CLEAR", -3.25f * Pitch, 0.115f, 2.5f * Pitch - 0.006f, 0.1f, Clear);
            Key("SPACE", 1.25f * Pitch, 0.115f, 6.5f * Pitch - 0.006f, 0.1f, () => Type(' '));

            // Tapes dropped or lost behind something: one press puts the room's tapes back on the shelves.
            ReshelveButton.Create(keyboard, new Vector3(0.35f, 0, 0), Quaternion.identity, room.Reshelve);
        }

        /// <param name="u">Position to the user's right of centre.</param>
        /// <param name="z">Position towards the user (keyboard space).</param>
        Renderer Key(string label, float u, float z, float width, float fontSize, Action onPress)
        {
            // An unscaled root so the label and cap move together when pressed.
            var key = new GameObject($"Key {label}").transform;
            key.SetParent(keyboard, false);
            key.localPosition = new Vector3(-u, 0, z); // the user faces -Z, so their right is -X
            var cap = Signage.Box(key, "Cap", new Vector3(0, BaseTop + KeyHeight / 2, 0), new Vector3(width, KeyHeight, KeySize), keycapMaterial);
            Destroy(cap.GetComponent<Collider>());
            var text = Signage.CreateText(key, "Legend", new Vector3(0, BaseTop + KeyHeight + 0.0006f, 0),
                Quaternion.LookRotation(Vector3.down, Vector3.back), fontSize, new Color(0.12f, 0.12f, 0.14f), new Vector2(width - 0.004f, KeySize - 0.006f));
            text.text = label;

            var hit = new GameObject($"Hit {label}", typeof(RectTransform)).GetComponent<RectTransform>();
            hit.SetParent(keyCanvas, false);
            hit.sizeDelta = new Vector2(width * 1000, KeySize * 1000);
            hit.anchoredPosition = new Vector2(u * 1000, -z * 1000); // canvas up points away from the user
            hit.gameObject.AddComponent<Image>().color = Color.clear;
            var button = hit.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() =>
            {
                StartCoroutine(Press(key));
                onPress();
            });
            return cap.GetComponent<Renderer>();
        }

        IEnumerator Press(Transform key)
        {
            key.localPosition += Vector3.down * 0.006f;
            yield return new WaitForSeconds(0.09f);
            key.localPosition += Vector3.up * 0.006f;
        }

        void Type(char c)
        {
            if (query.Length >= MaxQuery) return;
            if (c == ' ' && (query.Length == 0 || query.EndsWith(" "))) return;
            query += c;
            Apply();
        }

        void Backspace()
        {
            if (query.Length == 0) return;
            query = query.Substring(0, query.Length - 1);
            Apply();
        }

        void Clear()
        {
            query = "";
            Apply();
        }

        void ChooseSort(SortOrder order)
        {
            room.Resort(order);
            Refresh();
        }

        void Apply()
        {
            room.SetFilter(query);
            cursorOn = true;
            nextBlink = Time.time + 0.5f;
            Refresh();
        }

        void Update()
        {
            if (Time.time < nextBlink) return;
            nextBlink = Time.time + 0.5f;
            cursorOn = !cursorOn;
            Refresh();
        }

        void Refresh()
        {
            foreach (var (order, cap) in sortKeycaps)
                cap.sharedMaterial = order == room.Sort ? keycapActiveMaterial : keycapMaterial;

            var sortName = SortKeys.First(k => k.Order == room.Sort).Label;
            var sb = new StringBuilder("<mspace=0.56em>");
            sb.Append("PLEXBUSTER TERMINAL  v1.0\n");
            sb.Append(Clip(title.ToUpperInvariant(), 25)).Append('\n');
            sb.Append("SORT: ").Append(sortName).Append("\n\n");
            sb.Append("SEARCH> ").Append(Plain(query)).Append(cursorOn ? "_" : " ").Append('\n');
            sb.Append(room.MatchCount).Append(" OF ").Append(room.Items.Count).Append(" TITLES\n\n");
            foreach (var item in room.Matching().Take(ListedMatches))
            {
                var line = item.Year > 0 ? $"{item.Title} ({item.Year})" : item.Title;
                sb.Append(Plain(Clip(line, 25))).Append('\n');
            }
            screen.text = sb.ToString();
        }

        static string Clip(string text, int length) => text.Length <= length ? text : text.Substring(0, length - 1) + "…";

        static string Plain(string text) => "<noparse>" + text + "</noparse>";

        GameObject Solid(string name, Vector3 centre, Vector3 size, Material material) =>
            Signage.Box(transform, name, centre, size, material);
    }
}
