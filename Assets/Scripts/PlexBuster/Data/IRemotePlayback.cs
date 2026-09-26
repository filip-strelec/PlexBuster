using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlexBuster.Data
{
    /// <summary>A device that can play the library, such as a TV's Plex app.</summary>
    public class RemotePlayer
    {
        public string Id;
        public string Name;
        public string Details;    // vendor, model, address: tells similar names apart
        public string Address;    // base URL of its remote-control endpoint
        public bool Preferred;    // matches the PLEX_PLAYER setting

        public override string ToString() => $"{Name} ({Address})";
    }

    /// <summary>What pressing play starts: the movie itself, or for a show the episode to continue with.</summary>
    public class PlaybackPlan
    {
        public LibraryItem Item;
        public string Key;             // id of what plays: the movie, or the episode
        public string EpisodeLabel;    // "S1 · E3 · Title" for shows, else null
        public long ResumeMs;          // where it was left off, 0 = not started
    }

    /// <summary>Starts library items on a TV: finds the players on the network and tells one what to play.</summary>
    public interface IRemotePlayback
    {
        /// <summary>Players that can take a play command right now, the preferred one first.</summary>
        Task<IReadOnlyList<RemotePlayer>> FindPlayersAsync(CancellationToken ct);

        Task<PlaybackPlan> PlanAsync(LibraryItem item, CancellationToken ct);

        /// <summary>Completes once the player reports it is playing; throws if it refuses or doesn't start.</summary>
        Task PlayAsync(RemotePlayer player, PlaybackPlan plan, bool resume, CancellationToken ct);
    }
}
