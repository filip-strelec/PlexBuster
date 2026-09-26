using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlexBuster.Store
{
    /// <summary>
    /// The lobby's info desk: stand a tape in the slot on top and a hologram rises above the desk with everything
    /// about it, trailer included. Set a basket on the pad beside the desk and the arrows on the hologram step
    /// through its tapes too. A tape put in while another is in the slot swaps it out (into the basket on the
    /// pad if there is room, otherwise onto the desk). Built at runtime; origin on the floor, front facing +Z.
    /// </summary>
    [DisallowMultipleComponent]
    public class InfoStation : TapeHolder
    {
        const float DeskWidth = 0.9f;
        const float DeskDepth = 0.45f;
        const float DeskHeight = 0.95f;
        const float CaptureRadius = 0.3f;
        const float PadHalfSize = 0.42f;
        const float RefreshSeconds = 0.2f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Vector3 SlotPosition = new(0, DeskHeight, 0.09f);
        static readonly Vector3 PadCentre = new(0.95f, 0, 0.2f);
        static readonly Color Holo = new(0.35f, 0.9f, 1f);

        [SerializeField] StoreTheme theme;
        [SerializeField, Tooltip("Transparent (additive) material for the light beam under the hologram. Optional.")]
        Material beamMaterial;

        readonly List<VhsTape> queue = new();
        readonly List<Object> owned = new();
        Transform slot;
        VhsTape slotTape;
        ShoppingBasket padBasket;
        InfoHologram hologram;
        TextMeshPro slotHint, padHint;
        Material slotGlow;
        int index;
        float nextRefresh;

        void Start()
        {
            if (theme == null)
            {
                Debug.LogError("[Info] No StoreTheme assigned.", this);
                enabled = false;
                return;
            }
            Build();
            hologram = InfoHologram.Create(transform, new Vector3(0, DeskHeight + 0.1f + 0.525f, -0.3f),
                new Vector3(0, DeskHeight + 0.03f, -0.12f), beamMaterial, () => Step(-1), () => Step(+1));
        }

        void OnDestroy()
        {
            foreach (var o in owned)
                if (o != null) Destroy(o);
        }

        void Update()
        {
            var pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 3f);
            slotGlow.SetColor(BaseColorId, Holo * (slotTape == null ? 1.5f + pulse : 1f));
            slotHint.gameObject.SetActive(slotTape == null);
            slotHint.transform.localPosition = SlotPosition + new Vector3(0, 0.33f + 0.015f * Mathf.Sin(Time.time * 2f), 0);

            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            Refresh();
        }

        /// <summary>Rebuilds the list the hologram steps through: the slot's tape, then the basket on the pad's.</summary>
        void Refresh()
        {
            padBasket = ShoppingBasket.All.FirstOrDefault(b => !b.IsHeld && OnPad(b.transform.position));
            padHint.gameObject.SetActive(padBasket == null);

            queue.Clear();
            if (slotTape != null) queue.Add(slotTape);
            if (padBasket != null) queue.AddRange(padBasket.Tapes);

            if (queue.Count == 0)
            {
                if (hologram.Shown != null) hologram.Hide();
                return;
            }
            var shownAt = hologram.Shown != null ? queue.IndexOf(hologram.Shown) : -1;
            if (shownAt >= 0)
            {
                index = shownAt;
                hologram.SetPosition(index, queue.Count);
            }
            else Show(Mathf.Clamp(index, 0, queue.Count - 1));
        }

        void Show(int i)
        {
            index = i;
            hologram.Show(queue[index], index, queue.Count);
        }

        void Step(int direction)
        {
            if (queue.Count == 0) return;
            Show((index + direction + queue.Count) % queue.Count);
        }

        bool OnPad(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition) - PadCentre;
            return Mathf.Abs(local.x) < PadHalfSize && Mathf.Abs(local.z) < PadHalfSize && local.y < 0.6f;
        }

        protected override bool Captures(Vector3 worldPosition) =>
            Vector3.Distance(worldPosition, slot.TransformPoint(0, 0.1f, 0)) < CaptureRadius;

        protected override bool TryTake(VhsTape tape)
        {
            if (tape.Item == null) return false;
            if (slotTape != null && slotTape != tape) Eject(slotTape);

            slotTape = tape;
            tape.Grab.ParkIn(this, slot, new Vector3(0, 0.1f + 0.012f, 0), Quaternion.identity);
            // A new tape goes straight up on the hologram.
            nextRefresh = 0;
            index = 0;
            if (hologram.Shown != tape) hologram.Hide();
            return true;
        }

        public override void Remove(VhsTape tape)
        {
            if (slotTape == tape) slotTape = null;
            nextRefresh = 0;
        }

        /// <summary>Makes room in the slot: the old tape goes into the basket on the pad, or onto the desk.</summary>
        void Eject(VhsTape tape)
        {
            slotTape = null;
            if (padBasket != null && padBasket.TryAdd(tape)) return;
            var onDesk = transform.TransformPoint(new Vector3(DeskWidth / 2 - 0.12f, DeskHeight + 0.05f, 0.02f));
            tape.Grab.DropAt(onDesk, transform.rotation * Quaternion.Euler(-90, 20, 0));
        }

        void Build()
        {
            var body = Lit("Info Desk", new Color(0.06f, 0.07f, 0.1f), 0.2f, 0.7f);
            var chrome = Lit("Info Chrome", new Color(0.75f, 0.77f, 0.82f), 0.8f, 0.85f);
            var neon = Unlit("Info Neon", Holo * 2.5f);
            slotGlow = Unlit("Info Slot", Holo * 2f);

            var desk = new GameObject("Desk").transform;
            desk.SetParent(transform, false);
            Signage.Box(desk, "Plinth", new Vector3(0, 0.04f, 0), new Vector3(DeskWidth - 0.04f, 0.08f, DeskDepth - 0.04f), chrome);
            Signage.Box(desk, "Body", new Vector3(0, (DeskHeight - 0.04f + 0.08f) / 2, 0), new Vector3(DeskWidth, DeskHeight - 0.12f, DeskDepth), body);
            Signage.Box(desk, "Top", new Vector3(0, DeskHeight - 0.02f, 0), new Vector3(DeskWidth + 0.06f, 0.04f, DeskDepth + 0.06f), chrome);
            Trim(desk, "Neon", new Vector3(0, DeskHeight - 0.075f, DeskDepth / 2 + 0.003f), new Vector3(DeskWidth - 0.08f, 0.02f, 0.006f), neon);
            Trim(desk, "NeonLow", new Vector3(0, 0.1f, DeskDepth / 2 + 0.003f), new Vector3(DeskWidth - 0.08f, 0.012f, 0.006f), neon);

            // The projector on the back of the desk: a squat drum with a glowing lens.
            var projector = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            projector.name = "Projector";
            projector.transform.SetParent(desk, false);
            projector.transform.SetLocalPositionAndRotation(new Vector3(0, DeskHeight + 0.012f, -0.12f), Quaternion.identity);
            projector.transform.localScale = new Vector3(0.3f, 0.012f, 0.16f);
            projector.GetComponent<MeshRenderer>().sharedMaterial = chrome;
            var lens = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lens.name = "Lens";
            lens.transform.SetParent(desk, false);
            lens.transform.SetLocalPositionAndRotation(new Vector3(0, DeskHeight + 0.026f, -0.12f), Quaternion.identity);
            lens.transform.localScale = new Vector3(0.22f, 0.003f, 0.1f);
            lens.GetComponent<MeshRenderer>().sharedMaterial = neon;
            Destroy(lens.GetComponent<Collider>());

            // The slot: a small cradle the tape stands in, leaning back a little towards the projector.
            slot = new GameObject("Slot").transform;
            slot.SetParent(transform, false);
            slot.SetLocalPositionAndRotation(SlotPosition, Quaternion.Euler(-10, 0, 0));
            Signage.Box(slot, "Cradle", new Vector3(0, 0.012f, 0), new Vector3(0.17f, 0.024f, 0.06f), chrome);
            Trim(slot, "Glow", new Vector3(0, 0.0245f, 0), new Vector3(0.14f, 0.002f, 0.036f), slotGlow);

            var text = theme.signTextMaterial;
            Signage.CreateText(desk, "Sign", new Vector3(0, 0.55f, DeskDepth / 2 + 0.004f), Quaternion.Euler(0, 180, 0),
                1.6f, theme.signColor, new Vector2(DeskWidth - 0.1f, 0.14f), material: text).text = "MOVIE INFO";
            Signage.CreateText(desk, "Help", new Vector3(0, 0.44f, DeskDepth / 2 + 0.004f), Quaternion.Euler(0, 180, 0),
                0.55f, theme.labelColor, new Vector2(DeskWidth - 0.1f, 0.07f), material: text).text = "put a tape on top · basket on the pad";
            slotHint = Signage.CreateText(transform, "SlotHint", SlotPosition + new Vector3(0, 0.33f, 0), Quaternion.Euler(0, 180, 0),
                0.6f, Holo, new Vector2(0.5f, 0.08f), material: text);
            slotHint.text = "PUT A TAPE HERE";

            // The basket pad: a glowing square on the floor beside the desk.
            var pad = new GameObject("BasketPad").transform;
            pad.SetParent(transform, false);
            pad.localPosition = PadCentre;
            var size = PadHalfSize * 2 - 0.1f;
            for (var side = -1; side <= 1; side += 2)
            {
                Trim(pad, "Edge", new Vector3(side * size / 2, 0.003f, 0), new Vector3(0.03f, 0.006f, size + 0.03f), neon);
                Trim(pad, "Edge", new Vector3(0, 0.003f, side * size / 2), new Vector3(size + 0.03f, 0.006f, 0.03f), neon);
            }
            // Lying on the floor, readable from the front (the viewer looks down with the desk ahead).
            padHint = Signage.CreateText(pad, "Hint", new Vector3(0, 0.008f, 0), Quaternion.LookRotation(Vector3.down, Vector3.back),
                0.9f, Holo, new Vector2(size - 0.08f, 0.2f), material: text);
            padHint.text = "BASKET\nHERE";
            padHint.textWrappingMode = TextWrappingModes.Normal;
        }

        static void Trim(Transform parent, string name, Vector3 centre, Vector3 size, Material material)
        {
            var box = Signage.Box(parent, name, centre, size, material);
            Destroy(box.GetComponent<Collider>());
            box.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        Material Lit(string name, Color colour, float metallic, float smoothness)
        {
            // Copies of theme materials, so they use shaders that make it into builds.
            var material = new Material(theme.doorFrameMaterial) { name = name };
            material.SetColor(BaseColorId, colour);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            owned.Add(material);
            return material;
        }

        Material Unlit(string name, Color colour)
        {
            var material = new Material(theme.lightPanelMaterial) { name = name };
            material.SetColor(BaseColorId, colour);
            owned.Add(material);
            return material;
        }
    }
}
