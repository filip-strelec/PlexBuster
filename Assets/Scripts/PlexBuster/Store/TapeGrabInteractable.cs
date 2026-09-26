using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PlexBuster.Store
{
    /// <summary>
    /// Grab behaviour for tapes. They sit kinematic on the shelf and become physical when picked up. Let go,
    /// a tape goes (in order) into a <see cref="TapeHolder"/> it was dropped into (basket, info slot), back into
    /// its shelf slot if released close to it, or falls where it is.
    /// While held it belongs to no room, so it can be carried out of one; one left lying around is tidied up
    /// with its room.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TapeGrabInteractable : XRGrabInteractable
    {
        [SerializeField, Tooltip("Released within this distance of its slot, the tape goes back on the shelf.")]
        float snapBackDistance = 0.3f;

        Rigidbody body;
        VhsTape tape;
        Transform homeParent;
        Vector3 homePosition;
        Quaternion homeRotation;
        Coroutine looseWatch;
        bool passesThroughPlayer;

        public bool IsOnShelf { get; private set; }

        /// <summary>The basket or info slot the tape is parked in, if any.</summary>
        public TapeHolder Holder { get; private set; }

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
            ReturnToShelf();
        }

        public void ReturnToShelf()
        {
            if (homeParent == null) return;
            Park(homeParent, homePosition, homeRotation);
            IsOnShelf = true;
        }

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

            if (homeParent != null &&
                Vector3.Distance(transform.position, homeParent.TransformPoint(homePosition)) < snapBackDistance)
            {
                ReturnToShelf();
                yield break;
            }

            BecomeLoose();
        }

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

        /// <summary>A tape left where its floor disappeared (its room was discarded) falls; clean it up.</summary>
        IEnumerator WatchWhileLoose()
        {
            var wait = new WaitForSeconds(1f);
            while (true)
            {
                yield return wait;
                if (transform.position.y < -5f)
                {
                    Destroy(gameObject);
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
