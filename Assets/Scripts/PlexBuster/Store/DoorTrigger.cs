using System;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// Fires when the player's head moves into a box volume. Uses the head position rather than
    /// physics triggers, so it works however the player got there (walking, teleporting, leaning).
    /// </summary>
    public class DoorTrigger : MonoBehaviour
    {
        [SerializeField, Tooltip("Size of the box, centred on this transform.")]
        Vector3 size = new(1.6f, 2.4f, 1f);

        Transform head;
        bool inside;

        public event Action Entered;

        public Vector3 Size
        {
            get => size;
            set => size = value;
        }

        void OnEnable()
        {
            // Don't fire for a player who is already standing inside when the trigger appears.
            inside = HeadInside();
        }

        void Update()
        {
            var now = HeadInside();
            if (now && !inside) Entered?.Invoke();
            inside = now;
        }

        bool HeadInside()
        {
            if (head == null)
            {
                var camera = Camera.main;
                if (camera == null) return false;
                head = camera.transform;
            }
            var local = transform.InverseTransformPoint(head.position);
            return Mathf.Abs(local.x) <= size.x / 2 && Mathf.Abs(local.y) <= size.y / 2 && Mathf.Abs(local.z) <= size.z / 2;
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.4f);
            Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
