using System;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// A slot that takes one tape (the TV room's VCR, the cinema's projector feed): a tape let go close to it is
    /// pulled in, and taking it back out empties the slot. Putting in a second tape pushes the first one out.
    /// </summary>
    public class TapeSlot : TapeHolder
    {
        float captureRadius;
        Vector3 parkPosition;
        Quaternion parkRotation;
        Vector3 ejectPosition;
        Quaternion ejectRotation;

        public VhsTape Tape { get; private set; }

        public event Action<VhsTape> Inserted;
        public event Action<VhsTape> Removed;

        /// <param name="park">Where the tape sits once inserted, in the slot's space.</param>
        /// <param name="eject">Where a tape that is pushed out lands, in the slot's space.</param>
        public static TapeSlot Create(Transform parent, string name, Vector3 localPosition, Quaternion localRotation,
            float captureRadius, Pose park, Pose eject)
        {
            var slot = new GameObject(name).AddComponent<TapeSlot>();
            slot.transform.SetParent(parent, false);
            slot.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            slot.captureRadius = captureRadius;
            slot.parkPosition = park.position;
            slot.parkRotation = park.rotation;
            slot.ejectPosition = eject.position;
            slot.ejectRotation = eject.rotation;
            return slot;
        }

        protected override bool Captures(Vector3 worldPosition) =>
            Vector3.Distance(worldPosition, transform.TransformPoint(parkPosition)) < captureRadius;

        protected override bool TryTake(VhsTape tape)
        {
            if (tape.Item == null) return false;
            if (Tape != null && Tape != tape) Eject();
            Tape = tape;
            tape.Grab.ParkIn(this, transform, parkPosition, parkRotation);
            Inserted?.Invoke(tape);
            return true;
        }

        public override void Remove(VhsTape tape)
        {
            if (Tape != tape) return;
            Tape = null;
            Removed?.Invoke(tape);
        }

        /// <summary>Pushes the tape out (it lands next to the slot), as if the eject button was pressed.</summary>
        public void Eject()
        {
            var tape = Tape;
            if (tape == null) return;
            Tape = null;
            tape.Grab.DropAt(transform.TransformPoint(ejectPosition), transform.rotation * ejectRotation);
            Removed?.Invoke(tape);
        }
    }
}
