using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PlexBuster.Store
{
    /// <summary>
    /// Grab behaviour for tapes. They sit kinematic on the shelf and become physical when picked up. Let go,
    /// a tape goes (in order) into a <see cref="TapeHolder"/> it was dropped into (basket, info slot), back into
    /// its shelf slot if released close to it or inside something solid, or falls where it is.
    /// While held it belongs to no room, so it can be carried out of one; one left lying around is tidied up
    /// with its room, and one that falls out of the world goes back to its slot.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TapeGrabInteractable : XRGrabInteractable
    {
        [SerializeField, Tooltip("Released within this distance of its slot, the tape goes back on the shelf.")]
        float snapBackDistance = 0.3f;

        static readonly Collider[] Overlaps = new Collider[16];

        Rigidbody body;
        VhsTape tape;
        Transform homeParent; // the shelf's tapes: where the slot is, and what a loose tape is tidied up with
        Vector3 homePosition;
        Quaternion homeRotation;
        bool hasSlot;         // false once the slot went to another title (the shelf was re-sorted)
        Coroutine looseWatch;
        bool passesThroughPlayer;

        public bool IsOnShelf { get; private set; }

        /// <summary>The basket or info slot the tape is parked in, if any.</summary>
        public TapeHolder Holder { get; private set; }

        bool CanGoHome => hasSlot && homeParent != null;

        protected override void Awake()
        {
            base.Awake();
            body = GetComponent<Rigidbody>();
            tape = GetComponent<VhsTape>();
            // Parents are managed here (shelf, holder, none while carried), not restored by the base class.
            retainTransformParent = false;
        }

        public void PlaceOnShelf(Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            homeParent = parent;
            homePosition = localPosition;
            homeRotation = localRotation;
            hasSlot = true;
            ReturnToShelf();
        }

        public void ReturnToShelf()
        {
            if (!CanGoHome) return;
            Park(homeParent, homePosition, homeRotation);
            IsOnShelf = true;
        }

        /// <summary>Back into its slot, unless it's in a hand, the basket or a slot, or already there.</summary>
        /// <returns>Whether the tape moved.</returns>
        public bool TryReturnToShelf()
        {
            if (isSelected || Holder != null || IsOnShelf || !CanGoHome) return false;
            ReturnToShelf();
            return true;
        }

        /// <summary>Its slot has gone to another title: it no longer goes back on the shelf (but is still tidied up with the room).</summary>
        public void ForgetSlot() => hasSlot = false;

        /// <summary>Holds the tape still as a child of a basket or slot.</summary>
        public void ParkIn(TapeHolder holder, Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            Park(parent, localPosition, localRotation);
            IsOnShelf = false;
            Holder = holder;
        }

        void Park(Transform parent, Vector3 localPosition, Quaternion localRotation)
        {
            StopLooseWatch();
            // Parked tapes don't interpolate: an interpolated body would keep writing its old
            // pose back over the transform (and hundreds of them would cost for nothing).
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            transform.SetParent(parent, false);
            transform.SetLocalPositionAndRotation(localPosition, localRotation);
            body.position = transform.position;
            body.rotation = transform.rotation;
        }

        protected override void OnSelectEntering(SelectEnterEventArgs args)
        {
            if (!passesThroughPlayer)
            {
                // Held against the chest, a tape mustn't block the player's own movement.
                PlayerBody.IgnoreCollisions(colliders);
                passesThroughPlayer = true;
            }
            if (Holder != null)
            {
                var holder = Holder;
                Holder = null;
                holder.Remove(tape);
            }
            StopLooseWatch();
            // Carried tapes belong to no room, so leaving the room doesn't destroy the tape in your hand.
            transform.SetParent(null, true);
            // Make the body dynamic before the base class records its state, so a released
            // tape falls or flies instead of freezing in mid-air.
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            IsOnShelf = false;
            base.OnSelectEntering(args);
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (!isSelected) StartCoroutine(AfterRelease());
        }

        IEnumerator AfterRelease()
        {
            // XRGrabInteractable detaches (and applies throw velocity) later this frame; parking the tape
            // before that would make it set velocity on a kinematic body.
            yield return null;
            if (isSelected || Holder != null) yield break;

            if (TapeHolder.Offer(tape) != null) yield break;

            // A held tape passes through everything. Let go inside a shelf, a wall or the floor, physics would shove
            // it out anywhere, often down through the floor or under the shelf where it can't be reached.
            if (CanGoHome && (Vector3.Distance(transform.position, homeParent.TransformPoint(homePosition)) < snapBackDistance ||
                              IsInsideScenery()))
            {
                ReturnToShelf();
                yield break;
            }

            BecomeLoose();
        }

        /// <summary>
        /// Whether the tape is more than <see cref="MaxResting"/> deep in static scenery (shelves, walls, floors,
        /// furniture). Less is a tape set down on a counter a little too low; physics lifts that out gently.
        /// </summary>
        bool IsInsideScenery()
        {
            foreach (var own in colliders)
            {
                if (own is not BoxCollider box || !box.enabled) continue;
                var t = box.transform;
                var scale = t.lossyScale;
                var halfExtents = 0.5f * new Vector3(Mathf.Abs(box.size.x * scale.x), Mathf.Abs(box.size.y * scale.y), Mathf.Abs(box.size.z * scale.z));
                var count = Physics.OverlapBoxNonAlloc(t.TransformPoint(box.center), halfExtents, Overlaps, t.rotation,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (var i = 0; i < count; i++)
                {
                    // Other tapes, the basket and the like have bodies; the player's rig isn't scenery.
                    var other = Overlaps[i];
                    if (other.attachedRigidbody != null || other.GetComponentInParent<XROrigin>() != null) continue;
                    var ot = other.transform;
                    if (Physics.ComputePenetration(box, t.position, t.rotation, other, ot.position, ot.rotation, out _, out var depth) &&
                        depth > MaxResting)
                        return true;
                }
            }
            return false;
        }

        const float MaxResting = 0.02f;

        /// <summary>Takes a parked tape out of its holder and sets it down at a pose, free to fall and be picked up.</summary>
        public void DropAt(Vector3 position, Quaternion rotation)
        {
            Holder = null;
            IsOnShelf = false;
            transform.SetParent(null, false);
            transform.SetPositionAndRotation(position, rotation);
            body.position = position;
            body.rotation = rotation;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            BecomeLoose();
        }

        /// <summary>Lying loose: belongs to its room again (tidied up with it), or to nowhere if that room is gone.</summary>
        void BecomeLoose()
        {
            if (homeParent != null) transform.SetParent(homeParent, true);
            StopLooseWatch();
            looseWatch = StartCoroutine(WatchWhileLoose());
        }

        /// <summary>
        /// A tape that fell through something, or was left where its floor disappeared (its room was discarded):
        /// back to its slot, or cleaned up if it has none.
        /// </summary>
        IEnumerator WatchWhileLoose()
        {
            var wait = new WaitForSeconds(1f);
            while (true)
            {
                yield return wait;
                if (transform.position.y < -5f)
                {
                    looseWatch = null;
                    if (CanGoHome) ReturnToShelf();
                    else Destroy(gameObject);
                    yield break;
                }
            }
        }

        void StopLooseWatch()
        {
            if (looseWatch == null) return;
            StopCoroutine(looseWatch);
            looseWatch = null;
        }
    }
}
