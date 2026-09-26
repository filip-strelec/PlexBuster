using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PlexBuster.Data
{
    internal static class PosterTextures
    {
        const int Magic = 0x31545850; // "PXT1": a finished poster, format version 1
        const int HeaderInts = 5;     // magic, width, height, format, mip count
        const int HeaderSize = HeaderInts * sizeof(int);

        /// <summary>
        /// Block-compresses a readable poster texture (DXT1: an eighth of RGBA32) and drops its CPU copy.
        /// A room of every movie is thousands of posters, so this is the difference between ~1.5 GB and ~0.2 GB.
        /// </summary>
        public static void Finish(Texture2D texture)
        {
            if (CanCompress(texture)) texture.Compress(false);
            texture.Apply(false, true);
        }

        /// <summary>
        /// <see cref="Finish"/>, also returning the finished texture as bytes for <see cref="FromCache"/>: loading
        /// those skips decoding and compressing, the slow part of showing a poster. Null if the texture couldn't be
        /// compressed (not worth caching).
        /// </summary>
        public static byte[] FinishForCache(Texture2D texture)
        {
            byte[] bytes = null;
            if (CanCompress(texture))
            {
                texture.Compress(false);
                var data = texture.GetRawTextureData<byte>();
                bytes = new byte[HeaderSize + data.Length];
                var header = new[] { Magic, texture.width, texture.height, (int)texture.format, texture.mipmapCount };
                Buffer.BlockCopy(header, 0, bytes, 0, HeaderSize);
                NativeArray<byte>.Copy(data, 0, bytes, HeaderSize, data.Length);
            }
            texture.Apply(false, true);
            return bytes;
        }

        /// <summary>A finished poster from bytes made by <see cref="FinishForCache"/>, or null if they aren't whole.</summary>
        public static Texture2D FromCache(byte[] bytes)
        {
            if (bytes.Length <= HeaderSize) return null;
            var header = new int[HeaderInts];
            Buffer.BlockCopy(bytes, 0, header, 0, HeaderSize);
            var format = (TextureFormat)header[3];
            if (header[0] != Magic || header[1] is < 4 or > 4096 || header[2] is < 4 or > 4096 || header[4] is < 1 or > 13 ||
                format is not (TextureFormat.DXT1 or TextureFormat.DXT5))
                return null;

            Texture2D texture = null;
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                texture = new Texture2D(header[1], header[2], format, header[4], false);
                texture.LoadRawTextureData(IntPtr.Add(handle.AddrOfPinnedObject(), HeaderSize), bytes.Length - HeaderSize);
                texture.Apply(false, true);
                return texture;
            }
            catch (Exception)
            {
                // Cut short (the app quit while writing it) or otherwise wrong: made again from the JPEG.
                if (texture != null) Object.Destroy(texture);
                return null;
            }
            finally
            {
                handle.Free();
            }
        }

        static bool CanCompress(Texture2D texture) => texture.width % 4 == 0 && texture.height % 4 == 0;
    }
}
