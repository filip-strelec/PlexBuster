using System.Collections.Generic;
using PlexBuster.Data;
using TMPro;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// Wall shelving that displays tapes face-out, leaning back on stepped rows, filled left to right
    /// from the top row down. Origin is on the floor at the front centre; +Z faces the aisle.
    /// </summary>
    public class ShelfUnit : MonoBehaviour
    {
        public const float SlotWidth = 0.15f;
        public const float Depth = 0.22f;
        const float SideThickness = 0.03f;
        const float BoardThickness = 0.02f;
        const float TapeHeight = 0.2f;
        const float TapeBottomZ = -0.07f;

        [Header("Layout")]
        [SerializeField, Min(1)] int columns = 8;
        [SerializeField, Min(1)] int rows = 6;
        [SerializeField] float rowHeight = 0.28f;
        [SerializeField] float baseHeight = 0.25f;
        [SerializeField, Range(0, 30)] float leanAngle = 12f;

        [Header("Graybox")]
        [SerializeField] bool buildGraybox = true;
        [SerializeField] Material frameMaterial;
        [SerializeField, Tooltip("Sides, top and plinth; falls back to the frame material.")]
        Material accentMaterial;

        readonly List<VhsTape> tapes = new();
        Transform tapeRoot;
        TextMeshPro label;

        public IReadOnlyList<VhsTape> Tapes => tapes;
        public int Capacity => columns * rows;
        public int Count => tapes.Count;
        public bool IsFull => tapes.Count >= Capacity;
        public float Width => WidthFor(columns);
        public float Height => baseHeight + rows * rowHeight;

        public static float WidthFor(int columns) => columns * SlotWidth + 2 * SideThickness;

        // Built in Start rather than Awake so units created from code can be configured first.
        void Start() => EnsureBuilt();

        public void Configure(int columns, int rows, Material frameMaterial, Material accentMaterial = null)
        {
            this.columns = columns;
            this.rows = rows;
            this.frameMaterial = frameMaterial;
            this.accentMaterial = accentMaterial;
        }

        public void EnsureBuilt()
        {
            if (tapeRoot != null) return;
            tapeRoot = new GameObject("Tapes").transform;
            tapeRoot.SetParent(transform, false);
            if (buildGraybox) BuildGraybox();
        }

        /// <summary>Shows a short range label (e.g. "A – C") along the top of the unit.</summary>
        public void SetLabel(string text, Color color)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (label != null) label.gameObject.SetActive(false);
                return;
            }
            if (label == null)
                label = Signage.CreateText(transform, "Label", new Vector3(0, Height + 0.1f, 0), Quaternion.Euler(0, 180, 0),
                    1.1f, color, new Vector2(Width, 0.16f));
            label.text = text;
            label.color = color;
            label.gameObject.SetActive(true);
        }

        public VhsTape AddTape(LibraryItem item, VhsTape prefab, PosterCache posters)
        {
            EnsureBuilt();
            if (IsFull) return null;

            var (position, rotation) = SlotPose(tapes.Count);
            var tape = Instantiate(prefab);
            tape.Grab.PlaceOnShelf(tapeRoot, position, rotation);
            tape.Bind(item, posters);
            tapes.Add(tape);
            return tape;
        }

        public void Clear()
        {
            foreach (var tape in tapes)
                if (tape != null) Destroy(tape.gameObject);
            tapes.Clear();
        }

        /// <summary>Local pose of a tape standing in a slot, leaning back against the row.</summary>
        public (Vector3 position, Quaternion rotation) SlotPose(int index)
        {
            var row = index / columns;             // 0 = top row
            var column = index % columns;          // 0 = leftmost as seen from the aisle (+X)
            var boardTop = baseHeight + (rows - 1 - row) * rowHeight;
            var x = ((columns - 1) * 0.5f - column) * SlotWidth;

            var rotation = Quaternion.Euler(-leanAngle, 0, 0);
            var bottomCentre = new Vector3(x, boardTop + 0.002f, TapeBottomZ);
            return (bottomCentre + rotation * new Vector3(0, TapeHeight * 0.5f, 0), rotation);
        }

        void BuildGraybox()
        {
            var frame = new GameObject("Frame").transform;
            frame.SetParent(transform, false);

            var innerWidth = Width - 2 * SideThickness;
            var accent = accentMaterial != null ? accentMaterial : frameMaterial;
            Box(frame, "Back", new Vector3(0, Height * 0.5f, -Depth + 0.01f), new Vector3(Width, Height, 0.02f));
            Box(frame, "Base", new Vector3(0, baseHeight * 0.5f, -Depth * 0.5f), new Vector3(Width, baseHeight, Depth), accent);
            for (var side = -1; side <= 1; side += 2)
                Box(frame, "Side", new Vector3(side * (Width - SideThickness) * 0.5f, Height * 0.5f, -Depth * 0.5f),
                    new Vector3(SideThickness, Height, Depth), accent);

            for (var row = 0; row < rows; row++)
            {
                var boardTop = baseHeight + row * rowHeight;
                if (row > 0)
                    Box(frame, "Board", new Vector3(0, boardTop - BoardThickness * 0.5f, -Depth * 0.5f),
                        new Vector3(innerWidth, BoardThickness, Depth));
                Box(frame, "Lip", new Vector3(0, boardTop + 0.0175f, -0.03f), new Vector3(innerWidth, 0.035f, 0.015f));
            }
            Box(frame, "Top", new Vector3(0, Height + 0.01f, -Depth * 0.5f), new Vector3(Width, 0.02f, Depth), accent);
        }

        void Box(Transform parent, string name, Vector3 centre, Vector3 size, Material material = null) =>
            Signage.Box(parent, name, centre, size, material != null ? material : frameMaterial);
    }
}
