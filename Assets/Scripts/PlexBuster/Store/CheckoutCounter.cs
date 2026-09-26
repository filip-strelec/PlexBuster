using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace PlexBuster.Store
{
    /// <summary>
    /// The lobby checkout: put a tape down on the counter and a till screen above it offers to play that title on
    /// a TV (<see cref="CheckoutPrompt"/>). Picking the tape back up closes it. Goes on the counter: its BoxCollider
    /// gives the counter top, and the customer side is the one facing the middle of the room (the parent's origin).
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public class CheckoutCounter : MonoBehaviour
    {
        const float ScanSeconds = 0.2f;
        // Till screen centre above the counter top: low enough that the screen, standing on the counter, stays
        // below eye level and under the store-name sign on the wall behind.
        const float ScreenHeight = 0.3f;

        [SerializeField, Tooltip("How far above the counter top a tape still counts as put on the counter (metres).")]
        float zoneHeight = 0.3f;

        readonly Collider[] overlaps = new Collider[32];
        readonly HashSet<VhsTape> onCounter = new();
        // Tapes whose prompt was closed: not offered again until they leave the counter.
        readonly HashSet<VhsTape> handled = new();
        BoxCollider box;
        float customerSide;   // +1 or -1: the local Z side the customer stands on
        float nextScan;
        CheckoutPrompt prompt;

        float Top => box.center.y + box.size.y / 2;

        void Start()
        {
            box = GetComponent<BoxCollider>();
            var room = transform.parent != null ? transform.parent.position : Vector3.zero;
            customerSide = transform.InverseTransformDirection(room - transform.position).z >= 0 ? 1 : -1;
            BuildMat();
        }

        /// <summary>A counter mat on the customer's half of the top, saying what it's for.</summary>
        void BuildMat()
        {
            var position = new Vector3(box.center.x, Top + 0.002f, box.center.z + customerSide * box.size.z * 0.22f);
            // Face up, reading upright from the customer's side.
            var rotation = Quaternion.LookRotation(Vector3.down, Vector3.back * customerSide);
            var mat = WorldUI.CreateCanvas(transform, "CheckoutMat", new Vector2(700, 220), position, rotation);
            Destroy(mat.GetComponent<TrackedDeviceGraphicRaycaster>());
            var label = WorldUI.Label(WorldUI.Area(mat, "Text", Vector2.zero, Vector2.one, 16), "PUT A TAPE HERE\nTO WATCH IT ON TV", 64);
            label.textWrappingMode = TMPro.TextWrappingModes.Normal;
        }

        void Update()
        {
            if (Time.time < nextScan) return;
            nextScan = Time.time + ScanSeconds;

            FindTapesOnCounter();
            handled.RemoveWhere(tape => !onCounter.Contains(tape));
            if (prompt != null && !onCounter.Contains(prompt.Tape)) Close();
            if (prompt != null) return;

            foreach (var tape in onCounter)
            {
                if (handled.Contains(tape)) continue;
                Open(tape);
                break;
            }
        }

        void FindTapesOnCounter()
        {
            onCounter.Clear();
            var centre = transform.TransformPoint(box.center + Vector3.up * (box.size.y + zoneHeight) / 2);
            var halfExtents = Vector3.Scale(new Vector3(box.size.x, zoneHeight, box.size.z) / 2, transform.lossyScale);
            var count = Physics.OverlapBoxNonAlloc(centre, halfExtents, overlaps, transform.rotation, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < count; i++)
            {
                var body = overlaps[i].attachedRigidbody;
                // Only tapes put down or dropped: stored tapes (shelf, basket, station slot) are kinematic,
                // and one still in hand is just passing over.
                if (body == null || body.isKinematic || !body.TryGetComponent<VhsTape>(out var tape)) continue;
                if (tape.Item != null && !tape.Grab.isSelected) onCounter.Add(tape);
            }
        }

        void Open(VhsTape tape)
        {
            // Upright on the far half of the counter, facing the customer.
            var position = new Vector3(box.center.x, Top + ScreenHeight, box.center.z - customerSide * box.size.z * 0.3f);
            prompt = CheckoutPrompt.Create(transform, position, Quaternion.LookRotation(Vector3.back * customerSide), tape, () =>
            {
                handled.Add(tape);
                Close();
            });
        }

        void Close()
        {
            if (prompt == null) return;
            Destroy(prompt.gameObject);
            prompt = null;
        }
    }
}
