using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace PlexBuster.Store
{
    /// <summary>
    /// Grab behaviour for tapes: they sit kinematic on the shelf, become physical when picked up,
    /// and snap back into their slot if released close to it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class TapeGrabInteractable : XRGrabInteractable
    {
        [SerializeField, Tooltip("Released within this distance of its slot, the tape goes back on the shelf.")]
        float snapBackDistance = 0.3f;

        Rigidbody body;
        Transform homeParent;
        Vector3 homePosition;
        Quaternion homeRotation;

        public bool IsOnShelf { get; private set; }

        protected override void Awake()
        {
            base.Awake();
            body = GetComponent<Rigidbody>();
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
            // Shelved tapes don't interpolate: an interpolated body would keep writing its old
            // pose back over the transform (and hundreds of them would cost for nothing).
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;
            transform.SetParent(homeParent, false);
            transform.SetLocalPositionAndRotation(homePosition, homeRotation);
            body.position = transform.position;
            body.rotation = transform.rotation;
            IsOnShelf = true;
        }

        protected override void OnSelectEntering(SelectEnterEventArgs args)
        {
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
            if (!isSelected && homeParent != null &&
                Vector3.Distance(transform.position, homeParent.TransformPoint(homePosition)) < snapBackDistance)
                StartCoroutine(SnapBackAfterDetach());
        }

        IEnumerator SnapBackAfterDetach()
        {
            // XRGrabInteractable detaches (and applies throw velocity) later this frame;
            // making the body kinematic before that would make it set velocity on a kinematic body.
            yield return null;
            if (!isSelected) ReturnToShelf();
        }
    }
}
