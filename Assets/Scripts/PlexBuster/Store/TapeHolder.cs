using System.Collections.Generic;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// Something that keeps tapes you let go of inside it: the shopping basket, the info station's slot.
    /// A released tape is offered to every holder whose capture area it is in; the first one to take it keeps it
    /// (as a kinematic child) until the player grabs it again.
    /// </summary>
    public abstract class TapeHolder : MonoBehaviour
    {
        static readonly List<TapeHolder> Active = new();

        protected virtual void OnEnable() => Active.Add(this);
        protected virtual void OnDisable() => Active.Remove(this);

        public static TapeHolder Offer(VhsTape tape)
        {
            foreach (var holder in Active)
                if (holder.Captures(tape.transform.position) && holder.TryTake(tape))
                    return holder;
            return null;
        }

        /// <summary>Whether a tape released at <paramref name="worldPosition"/> should go into this holder.</summary>
        protected abstract bool Captures(Vector3 worldPosition);

        protected abstract bool TryTake(VhsTape tape);

        /// <summary>The player took the tape back out; forget it.</summary>
        public abstract void Remove(VhsTape tape);

        /// <summary>Stops a parked tape and the holder's own colliders from pushing each other.</summary>
        protected static void IgnoreCollisions(VhsTape tape, IEnumerable<Collider> holderColliders, bool ignore)
        {
            var tapeColliders = tape.GetComponentsInChildren<Collider>();
            foreach (var a in tapeColliders)
            foreach (var b in holderColliders)
                if (a != null && b != null) Physics.IgnoreCollision(a, b, ignore);
        }
    }
}
