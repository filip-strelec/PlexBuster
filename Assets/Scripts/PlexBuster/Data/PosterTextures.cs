using UnityEngine;

namespace PlexBuster.Data
{
    internal static class PosterTextures
    {
        /// <summary>
        /// Block-compresses a readable poster texture (DXT1: an eighth of RGBA32) and drops its CPU copy.
        /// A room of every movie is thousands of posters, so this is the difference between ~1.5 GB and ~0.2 GB.
        /// </summary>
        public static void Finish(Texture2D texture)
        {
            if (texture.width % 4 == 0 && texture.height % 4 == 0) texture.Compress(false);
            texture.Apply(false, true);
        }
    }
}
