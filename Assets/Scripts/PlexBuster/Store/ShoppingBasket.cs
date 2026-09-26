using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PlexBuster.Store
{
    /// <summary>
    /// A shop basket you carry around: drop tapes into it and they stand in it like records in a crate, going
    /// with it from room to room (rooms are discarded when you leave them, the tapes in the basket aren't).
    /// Set it on the info station's pad to browse its tapes on the hologram. Built at runtime; origin at the
    /// centre of its floor. A basket left behind in a discarded room falls away and turns up back on its stand.
    /// </summary>
    [DisallowMultipleComponent]
    public class ShoppingBasket : TapeHolder
    {
        public const int Capacity = 12;

        const float Width = 0.32f;
        const float Length = 0.46f;
        const float WallHeight = 0.13f;
        const float Wall = 0.012f;
        const float HandleHeight = 0.3f;
        const float SlotPitch = 0.032f;
        const float FirstSlotZ = -0.15f;
        const float TapeLean = 15f;
        const float TapeHalfHeight = 0.1f;
        const float StandHeight = 0.3f;

        static readonly List<ShoppingBasket> Baskets = new();
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] StoreTheme theme;
        [SerializeField] Color plastic = new(0.75f, 0.05f, 0.06f);

        readonly VhsTape[] slots = new VhsTape[Capacity];
        readonly List<Collider> ownColliders = new();
        readonly List<Object> owned = new();
        Transform tapeRoot;
        Rigidbody body;
        XRGrabInteractable grab;
        Pose home;
        TextMeshPro countLabel;

        public static IReadOnlyList<ShoppingBasket> All => Baskets;

        /// <summary>The tapes in the basket, front to back.</summary>
        public IEnumerable<VhsTape> Tapes => slots.Where(t => t != null);

        public int Count => slots.Count(t => t != null);
        public bool IsHeld => grab != null && grab.isSelected;

        void Awake()
        {
            home = new Pose(transform.position, transform.rotation);
            if (theme == null)
            {
                Debug.LogError("[Basket] No StoreTheme assigned.", this);
                enabled = false;
                return;
            }
            Build();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Baskets.Add(this);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Baskets.Remove(this);
        }

        void OnDestroy()
        {
            foreach (var o in owned)
                if (o != null) Destroy(o);
        }

        void Update()
        {
            // Left in a room that was discarded (or thrown out of the world): back to the stand, tapes and all.
            if (transform.position.y < -3f && !IsHeld) ReturnHome();
        }

        public void ReturnHome()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(home.position, home.rotation);
            body.position = home.position;
            body.rotation = home.rotation;
        }

        protected override bool Captures(Vector3 worldPosition)
        {
            var local = transform.InverseTransformPoint(worldPosition);
            return Mathf.Abs(local.x) < Width / 2 + 0.06f && local.y > -0.05f && local.y < HandleHeight + 0.15f &&
                   Mathf.Abs(local.z) < Length / 2 + 0.08f;
        }

        protected override bool TryTake(VhsTape tape) => TryAdd(tape);

        /// <summary>Puts a tape in the first free slot; false when the basket is full.</summary>
        public bool TryAdd(VhsTape tape)
        {
            var slot = System.Array.IndexOf(slots, null);
            if (slot < 0 || tape == null || tape.Item == null) return false;

            slots[slot] = tape;
            // The tape is a kinematic body inside the basket's colliders; without this it would shove the basket.
            // It stays ignored after the tape is taken out, so pulling it free doesn't knock the basket over.
            IgnoreCollisions(tape, ownColliders, true);
            var rotation = Quaternion.Euler(-TapeLean, 0, 0);
            var bottom = new Vector3(0, Wall + 0.002f, FirstSlotZ + slot * SlotPitch);
            tape.Grab.ParkIn(this, tapeRoot, bottom + rotation * new Vector3(0, TapeHalfHeight, 0), rotation);
            UpdateCount();
            return true;
        }

        public override void Remove(VhsTape tape)
        {
            var slot = System.Array.IndexOf(slots, tape);
            if (slot >= 0) slots[slot] = null;
            UpdateCount();
        }

        void UpdateCount()
        {
            var count = Count;
            countLabel.text = count == 0 ? "" : count.ToString();
        }

        void Build()
        {
            var shell = Lit("Basket Plastic", plastic, 0, 0.55f);
            var chrome = Lit("Basket Handle", new Color(0.78f, 0.78f, 0.82f), 0.8f, 0.8f);

            var model = new GameObject("Model").transform;
            model.SetParent(transform, false);
            Part(model, "Floor", new Vector3(0, Wall / 2, 0), new Vector3(Width, Wall, Length), shell);
            for (var side = -1; side <= 1; side += 2)
            {
                Part(model, "Side", new Vector3(side * (Width - Wall) / 2, WallHeight / 2, 0), new Vector3(Wall, WallHeight, Length), shell);
                Part(model, "End", new Vector3(0, WallHeight / 2, side * (Length - Wall) / 2), new Vector3(Width, WallHeight, Wall), shell);
                // Lattice slots cut into the sides, suggested with dark strips.
                for (var i = 0; i < 5; i++)
                    Trim(model, "Vent", new Vector3(side * (Width / 2 + 0.001f), WallHeight * 0.55f, -0.16f + i * 0.08f),
                        new Vector3(0.002f, 0.06f, 0.035f), theme.doorwayMaterial);
                // The handle: two uprights rising from the middle of the long sides, joined over the top.
                Part(model, "HandlePost", new Vector3(side * (Width / 2 - 0.02f), (WallHeight + HandleHeight) / 2, 0),
                    new Vector3(0.018f, HandleHeight - WallHeight, 0.03f), chrome);
            }
            Part(model, "HandleBar", new Vector3(0, HandleHeight, 0), new Vector3(Width - 0.02f, 0.03f, 0.035f), chrome);
            Part(model, "Grip", new Vector3(0, HandleHeight, 0), new Vector3(0.14f, 0.04f, 0.045f), Lit("Basket Grip", new Color(0.08f, 0.08f, 0.09f), 0, 0.3f));
            ownColliders.AddRange(model.GetComponentsInChildren<Collider>());

            // Tape count, on the end facing the player when the basket sits on its stand.
            countLabel = Signage.CreateText(model, "Count", new Vector3(0, WallHeight * 0.5f, Length / 2 + 0.002f), Quaternion.Euler(0, 180, 0),
                1f, Color.white, new Vector2(0.2f, 0.09f), material: theme.signTextMaterial);

            tapeRoot = new GameObject("Tapes").transform;
            tapeRoot.SetParent(transform, false);

            // Physics and grabbing come after the model, so the grab picks up its colliders.
            body = gameObject.AddComponent<Rigidbody>();
            body.mass = 1.2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            grab = gameObject.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.useDynamicAttach = true;
            grab.throwVelocityScale = 0.7f;
            grab.retainTransformParent = false;
            grab.selectMode = InteractableSelectMode.Single;
            // Carried at the hip, it would block the player's own body; you can walk through a basket on the floor.
            PlayerBody.IgnoreCollisions(ownColliders);

            BuildStand();
            UpdateCount();
        }

        /// <summary>The low stand the basket waits on (and returns to), with a sign.</summary>
        void BuildStand()
        {
            var stand = new GameObject("Basket Stand").transform;
            stand.SetParent(transform.parent, false);
            stand.SetPositionAndRotation(home.position - home.rotation * Vector3.up * StandHeight, home.rotation);
            var frame = theme.shelfAccentMaterial != null ? theme.shelfAccentMaterial : theme.shelfMaterial;
            Signage.Box(stand, "Base", new Vector3(0, StandHeight / 2, 0), new Vector3(Width + 0.1f, StandHeight, Length + 0.1f), frame);
            Signage.CreateText(stand, "Sign", new Vector3(0, StandHeight * 0.5f, (Length + 0.1f) / 2 + 0.003f), Quaternion.Euler(0, 180, 0),
                0.9f, theme.signColor, new Vector2(Width + 0.06f, 0.12f), material: theme.signTextMaterial).text = "BASKETS";
            owned.Add(stand.gameObject);
        }

        void Part(Transform parent, string name, Vector3 centre, Vector3 size, Material material)
        {
            var box = Signage.Box(parent, name, centre, size, material);
            box.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static void Trim(Transform parent, string name, Vector3 centre, Vector3 size, Material material)
        {
            var box = Signage.Box(parent, name, centre, size, material);
            Destroy(box.GetComponent<Collider>());
            box.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        Material Lit(string name, Color colour, float metallic, float smoothness)
        {
            // A copy of a theme material, so it's the same (URP Lit) shader and makes it into builds.
            var material = new Material(theme.doorFrameMaterial) { name = name };
            material.SetColor(BaseColorId, colour);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            owned.Add(material);
            return material;
        }
    }
}
