using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>A generated space the navigator can move the player into: a department hall or a room.</summary>
    public interface IStorePlace
    {
        GameObject gameObject { get; }

        /// <summary>Where the player stands on arrival.</summary>
        Pose EntryPose { get; }

        /// <summary>Fires when the player walks out; the navigator then takes them back one level.</summary>
        DoorTrigger Exit { get; }
    }
}
