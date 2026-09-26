using PlexBuster.Data;
using UnityEngine;

namespace PlexBuster.Store
{
    /// <summary>
    /// Shows one item's poster on a renderer (wall posters, standees) using the shared poster cache,
    /// and gives the poster back when destroyed.
    /// </summary>
    public class CoverDisplay : MonoBehaviour
    {
        Renderer target;
        LibraryItem item;
        PosterCache posters;

        public static CoverDisplay Show(Renderer target, LibraryItem item, PosterCache posters, Material template, Material loading)
        {
            var display = target.gameObject.AddComponent<CoverDisplay>();
            display.target = target;
            display.item = item;
            display.posters = posters;
            target.sharedMaterial = loading;
            display.Load(template);
            return display;
        }

        async void Load(Material template)
        {
            var cover = await posters.AcquireCoverAsync(item, template);
            if (this != null && cover != null) target.sharedMaterial = cover;
        }

        void OnDestroy()
        {
            if (item != null) posters.Release(item);
            item = null;
        }
    }
}
