using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PlexBuster.Data
{
    /// <summary>
    /// Reference-counted cover materials, one per poster. Every <see cref="AcquireCoverAsync"/> must be paired
    /// with a <see cref="Release"/>. Unused posters stay in memory up to a limit so revisiting a room is instant;
    /// loads nobody wants any more are cancelled.
    /// </summary>
    /// <remarks>
    /// A material per poster (rather than one material plus a per-renderer property block) keeps the covers
    /// compatible with URP's SRP Batcher, which matters with thousands of tapes in a room.
    /// </remarks>
    public class PosterCache
    {
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        class Entry
        {
            public int Refs;
            public Task<Material> Load;
            public Texture2D Texture;
            public Material Material;
            public CancellationTokenSource Cancel;
            public LinkedListNode<string> UnusedNode;

            public void Destroy()
            {
                if (Material != null) Object.Destroy(Material);
                if (Texture != null) Object.Destroy(Texture);
            }
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

        /// <param name="template">Material the cover is made from; the poster becomes its base map.</param>
        /// <returns>The cover material, or null if the poster failed to load or was released before it finished.</returns>
        public async Task<Material> AcquireCoverAsync(LibraryItem item, Material template)
        {
            var key = item.PosterPath;
            if (string.IsNullOrEmpty(key)) return null;

            if (!entries.TryGetValue(key, out var entry))
            {
                entry = new Entry { Cancel = new CancellationTokenSource() };
                entries[key] = entry;
                entry.Load = LoadAsync(item, entry, template);
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

            if (entry.Material == null)
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
                entries[oldest].Destroy();
                entries.Remove(oldest);
            }
        }

        public void Clear()
        {
            foreach (var entry in entries.Values)
            {
                entry.Cancel.Cancel();
                entry.Destroy();
            }
            entries.Clear();
            unused.Clear();
        }

        async Task<Material> LoadAsync(LibraryItem item, Entry entry, Material template)
        {
            try
            {
                var texture = await source.LoadPosterAsync(item, entry.Cancel.Token);
                if (entry.Cancel.IsCancellationRequested || texture == null)
                {
                    if (texture != null) Object.Destroy(texture);
                    return null;
                }
                entry.Texture = texture;
                entry.Material = new Material(template) { name = $"Cover: {item.Title}" };
                entry.Material.SetTexture(BaseMapId, texture);
                return entry.Material;
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
