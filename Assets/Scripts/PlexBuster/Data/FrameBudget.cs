using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace PlexBuster.Data
{
    /// <summary>
    /// A per-frame allowance of main-thread time for loading work (spawning tapes, turning downloaded posters into
    /// textures), so a room of thousands of titles fills in over a few seconds at full frame rate instead of
    /// stuttering. Work checks <see cref="HasTime"/> (or awaits <see cref="WaitAsync"/>) before each piece and wraps
    /// the piece in <see cref="Measure"/>. Main thread only.
    /// </summary>
    public static class FrameBudget
    {
        /// <summary>Main-thread milliseconds per frame that loading may use (a frame at 90 Hz is 11 ms).</summary>
        public static float Milliseconds = 2f;

        static int frame = -1;
        static long spentTicks;

        /// <summary>Whether this frame has time left. The first piece of work in a frame always goes ahead.</summary>
        public static bool HasTime
        {
            get
            {
                Roll();
                return spentTicks * 1000.0 / Stopwatch.Frequency < Milliseconds;
            }
        }

        /// <summary>Waits for a frame with time left.</summary>
        public static async Awaitable WaitAsync(CancellationToken ct = default)
        {
            while (!HasTime) await Awaitable.NextFrameAsync(ct);
        }

        /// <summary>Counts the time until the returned scope is disposed against this frame's allowance.</summary>
        public static Scope Measure() => new(Stopwatch.GetTimestamp());

        public readonly struct Scope : IDisposable
        {
            readonly long start;

            public Scope(long start) => this.start = start;

            public void Dispose()
            {
                Roll();
                spentTicks += Stopwatch.GetTimestamp() - start;
            }
        }

        static void Roll()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount;
            spentTicks = 0;
        }
    }
}
