using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PlexBuster.Data
{
    /// <summary>
    /// Reference-counted poster textures. Every <see cref="AcquireAsync"/> must be paired with a
    /// <see cref="Release"/>. Unused posters stay in memory up to a limit so revisiting a room is instant;
    /// loads nobody wants any more are cancelled.
    /// </summary>
    public class PosterCache
    {
        class Entry
        {
            public int Refs;
            public Task<Texture2D> Load;
            public Texture2D Texture;
            public CancellationTokenSource Cancel;
            public LinkedListNode<string> UnusedNode;
        }

        readonly ILibrarySource source;
        readonly int keepUnused;
        readonly Dictionary<string, Entry> entries = new();
        readonly LinkedList<string> unused = new(); // least recently released first

        public PosterCache(ILibrarySource source, int keepUnused = 400)
        {
            this.source = source;
            this.keepUnused = keepUnused;
        }

        public int LoadedCount => entries.Count;

        /// <returns>The poster, or null if it failed to load or was released before it finished.</returns>
        public async Task<Texture2D> AcquireAsync(LibraryItem item)
        {
            var key = item.PosterPath;
            if (string.IsNullOrEmpty(key)) return null;

            if (!entries.TryGetValue(key, out var entry))
            {
                entry = new Entry { Cancel = new CancellationTokenSource() };
                entries[key] = entry;
                entry.Load = LoadAsync(item, entry);
            }
            else if (entry.UnusedNode != null)
            {
                unused.Remove(entry.UnusedNode);
                entry.UnusedNode = null;
            }

            entry.Refs++;
            return await entry.Load;
        }

        public void Release(LibraryItem item)
        {
            var key = item.PosterPath;
            if (string.IsNullOrEmpty(key) || !entries.TryGetValue(key, out var entry)) return;
            if (--entry.Refs > 0) return;

            if (entry.Texture == null)
            {
                // Still loading (or failed): nobody needs it any more.
                entry.Cancel.Cancel();
                entries.Remove(key);
                return;
            }

            entry.UnusedNode = unused.AddLast(key);
            while (unused.Count > keepUnused)
            {
                var oldest = unused.First.Value;
                unused.RemoveFirst();
                Object.Destroy(entries[oldest].Texture);
                entries.Remove(oldest);
            }
        }

        public void Clear()
        {
            foreach (var entry in entries.Values)
            {
                entry.Cancel.Cancel();
                if (entry.Texture != null) Object.Destroy(entry.Texture);
            }
            entries.Clear();
            unused.Clear();
        }

        async Task<Texture2D> LoadAsync(LibraryItem item, Entry entry)
        {
            try
            {
                var texture = await source.LoadPosterAsync(item, entry.Cancel.Token);
                if (entry.Cancel.IsCancellationRequested)
                {
                    if (texture != null) Object.Destroy(texture);
                    return null;
                }
                entry.Texture = texture;
                return texture;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Posters] {item.Title}: {e.Message}");
                // Forget the failure so a later visit can retry.
                if (entries.TryGetValue(item.PosterPath, out var current) && current == entry && entry.Refs <= 0)
                    entries.Remove(item.PosterPath);
                return null;
            }
        }
    }
}
