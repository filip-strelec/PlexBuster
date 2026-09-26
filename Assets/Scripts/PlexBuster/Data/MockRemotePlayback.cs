using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace PlexBuster.Data
{
    /// <summary>Pretend TVs for working without Plex: two are always found, and "playing" just takes a moment.</summary>
    public class MockRemotePlayback : IRemotePlayback
    {
        static readonly RemotePlayer[] Players =
        {
            new() { Id = "mock-living-room", Name = "Living Room TV", Details = "mock", Address = "mock://living-room", Preferred = true },
            new() { Id = "mock-bedroom", Name = "Bedroom TV", Details = "mock", Address = "mock://bedroom" },
        };

        public async Task<IReadOnlyList<RemotePlayer>> FindPlayersAsync(CancellationToken ct)
        {
            await Task.Delay(800, ct);
            return Players;
        }

        public Task<PlaybackPlan> PlanAsync(LibraryItem item, CancellationToken ct) => Task.FromResult(new PlaybackPlan
        {
            Item = item,
            Key = item.Id,
            EpisodeLabel = item.Kind == MediaKind.Show ? "S1 · E1 · Pilot" : null,
            // Every other title was "left off" partway, so both prompt layouts can be tried.
            ResumeMs = item.Title.Length % 2 == 0 ? 42 * 60_000 : 0,
        });

        public async Task PlayAsync(RemotePlayer player, PlaybackPlan plan, bool resume, CancellationToken ct)
        {
            await Task.Delay(1500, ct);
            Debug.Log($"[Playback] (mock) {player.Name} plays {plan.Item.Title}" + (resume ? $" from {plan.ResumeMs / 1000}s" : ""));
        }
    }
}
