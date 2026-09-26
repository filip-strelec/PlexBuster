using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// The player's own colliders (the rig's character controller). Things carried close to the body, like the
    /// basket held at the hip, would otherwise stand in the way of the character controller, and the player
    /// couldn't walk.
    /// </summary>
    public static class PlayerBody
    {
        static readonly List<Collider> Body = new();

        /// <summary>Makes <paramref name="colliders"/> pass through the player.</summary>
        public static void IgnoreCollisions(IEnumerable<Collider> colliders)
        {
            if (Body.Count == 0 || Body.Exists(c => c == null)) Find();
            foreach (var collider in colliders)
            foreach (var body in Body)
                if (collider != null && body != null) Physics.IgnoreCollision(collider, body, true);
        }

        static void Find()
        {
            Body.Clear();
            var origin = Object.FindAnyObjectByType<XROrigin>();
            if (origin != null) Body.AddRange(origin.GetComponentsInChildren<Collider>(true));
        }
    }
}
