using System.IO;
using UnityEditor;
using UnityEngine;

namespace PlexBuster.Editor
{
    /// <summary>
    /// Generates the store's tileable textures (90s confetti carpet, acoustic ceiling tile) so they can be
    /// tweaked and regenerated instead of hand-painted. PlexBuster > Generate Store Textures.
    /// </summary>
    public static class StoreTextureGenerator
    {
        const string Folder = "Assets/Textures/Store";

        [MenuItem("PlexBuster/Generate Store Textures")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(Folder);
            Save("Carpet.png", Carpet(1024, 1990));
            Save("CeilingTile.png", CeilingTile(512, 7));
            Save("BeadCurtain.png", BeadCurtain(512, 21), wrap: TextureWrapMode.Clamp);
            SavePalette("CasePalette.png", new[]
            {
                new Color(0.02f, 0.03f, 0.09f), // case plastic
                new Color(1f, 0.75f, 0.05f),    // store sticker
                new Color(0.005f, 0.005f, 0.01f), // seam
                Color.white,
            });
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// A 4×1 colour strip the VHS case's UVs point into (one texel per part), so the whole case is a
        /// single material and a single draw call.
        /// </summary>
        static void SavePalette(string name, Color[] colours)
        {
            var tex = new Texture2D(colours.Length, 1, TextureFormat.RGB24, false);
            tex.SetPixels(colours);
            tex.Apply();
            var path = Path.Combine(Folder, name);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        static Color[] Carpet(int size, int seed)
        {
            var rng = new System.Random(seed);
            var px = new Color[size * size];
            var navy = new Color(0.035f, 0.045f, 0.15f);
            for (var i = 0; i < px.Length; i++)
            {
                var n = (float)rng.NextDouble() * 0.035f - 0.0175f; // fibre noise
                px[i] = new Color(navy.r + n, navy.g + n, navy.b + n * 2);
            }

            Color[] palette =
            {
                new(0.95f, 0.2f, 0.6f), new(0.1f, 0.8f, 0.85f), new(1f, 0.78f, 0.1f),
                new(0.55f, 0.35f, 0.95f), new(0.2f, 0.45f, 1f),
            };

            for (var s = 0; s < 170; s++)
            {
                var color = palette[rng.Next(palette.Length)];
                var x = (float)rng.NextDouble() * size;
                var y = (float)rng.NextDouble() * size;
                var angle = (float)(rng.NextDouble() * Mathf.PI * 2);
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var normal = new Vector2(-dir.y, dir.x);
                switch (rng.Next(4))
                {
                    case 0: // squiggle
                        for (var t = 0f; t < 70; t += 0.7f)
                        {
                            var p = new Vector2(x, y) + dir * t + normal * Mathf.Sin(t * 0.18f) * 9;
                            Disc(px, size, p.x, p.y, 3.2f, color);
                        }
                        break;
                    case 1: // triangle outline
                        var r = 14 + (float)rng.NextDouble() * 10;
                        for (var k = 0; k < 3; k++)
                        {
                            var a = new Vector2(x, y) + Rotate(dir, k * 120) * r;
                            var b = new Vector2(x, y) + Rotate(dir, (k + 1) * 120) * r;
                            for (var t = 0f; t <= 1; t += 0.02f)
                            {
                                var p = Vector2.Lerp(a, b, t);
                                Disc(px, size, p.x, p.y, 2.6f, color);
                            }
                        }
                        break;
                    case 2: // zigzag
                        for (var t = 0f; t < 60; t += 0.6f)
                        {
                            var p = new Vector2(x, y) + dir * t + normal * (Mathf.PingPong(t, 10) - 5) * 1.6f;
                            Disc(px, size, p.x, p.y, 2.8f, color);
                        }
                        break;
                    default: // dot cluster
                        for (var k = 0; k < 5; k++)
                            Disc(px, size, x + (float)rng.NextDouble() * 24 - 12, y + (float)rng.NextDouble() * 24 - 12, 3.5f, color);
                        break;
                }
            }
            return Blur(px, size);
        }

        static Color[] CeilingTile(int size, int seed)
        {
            var rng = new System.Random(seed);
            var px = new Color[size * size];
            var baseColor = new Color(0.86f, 0.86f, 0.83f);
            for (var i = 0; i < px.Length; i++)
            {
                var n = (float)rng.NextDouble() * 0.03f;
                px[i] = baseColor - new Color(n, n, n, 0);
            }
            // Fissures: small dark flecks typical of acoustic tiles.
            for (var s = 0; s < 1400; s++)
                Disc(px, size, (float)rng.NextDouble() * size, (float)rng.NextDouble() * size, 0.6f + (float)rng.NextDouble() * 1.1f,
                    new Color(0.62f, 0.62f, 0.6f));
            // T-bar grid along the edges.
            const int bar = 9;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                if (edge < bar) px[y * size + x] = edge < 2 ? new Color(0.55f, 0.55f, 0.54f) : new Color(0.93f, 0.93f, 0.92f);
            }
            return px;
        }

        /// <summary>
        /// Strands of glossy beads hanging over a dark doorway. Square texture stretched over a door
        /// (about 1.3 × 2.3 m), so beads are drawn as ovals that come out round once stretched.
        /// </summary>
        static Color[] BeadCurtain(int size, int seed)
        {
            var rng = new System.Random(seed);
            var px = new Color[size * size];
            var back = new Color(0.012f, 0.012f, 0.018f);
            for (var i = 0; i < px.Length; i++) px[i] = back;

            Color[] palette =
            {
                new(0.95f, 0.25f, 0.55f), new(1f, 0.72f, 0.12f), new(0.15f, 0.75f, 0.85f),
                new(0.6f, 0.35f, 0.95f), new(0.95f, 0.4f, 0.15f), new(0.85f, 0.85f, 0.9f),
            };

            const int strands = 18;
            const float aspect = 2.3f / 1.3f; // door height / width: stretch factor of the vertical axis
            var spacingX = size / (float)strands;
            var radiusX = spacingX * 0.32f;
            var radiusY = radiusX / aspect;
            var spacingY = radiusY * 2.6f;
            for (var s = 0; s < strands; s++)
            {
                var x0 = (s + 0.5f) * spacingX;
                var phase = (float)rng.NextDouble() * Mathf.PI * 2;
                var colour = palette[rng.Next(palette.Length)];
                for (var y = spacingY * 0.5f; y < size; y += spacingY)
                {
                    if (rng.NextDouble() < 0.15) colour = palette[rng.Next(palette.Length)];
                    var x = x0 + Mathf.Sin(y * 0.01f + phase) * spacingX * 0.12f; // a slight sway
                    Bead(px, size, x, y, radiusX, radiusY, colour);
                }
            }
            return px;
        }

        /// <summary>An oval bead with a darker rim and a highlight, so it reads as glossy and round.</summary>
        static void Bead(Color[] px, int size, float cx, float cy, float rx, float ry, Color colour)
        {
            for (var y = (int)(cy - ry - 1); y <= (int)(cy + ry + 1); y++)
            for (var x = (int)(cx - rx - 1); x <= (int)(cx + rx + 1); x++)
            {
                if (x < 0 || y < 0 || x >= size || y >= size) continue;
                var dx = (x - cx) / rx;
                var dy = (y - cy) / ry;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > 1) continue;
                var shade = Color.Lerp(colour, colour * 0.35f, d * d);
                var highlight = Mathf.Clamp01(1 - Vector2.Distance(new Vector2(dx, dy), new Vector2(-0.35f, 0.35f)) * 2.5f);
                px[y * size + x] = Color.Lerp(shade, Color.white, highlight * 0.8f);
            }
        }

        static Vector2 Rotate(Vector2 v, float degrees)
        {
            var r = degrees * Mathf.Deg2Rad;
            return new Vector2(v.x * Mathf.Cos(r) - v.y * Mathf.Sin(r), v.x * Mathf.Sin(r) + v.y * Mathf.Cos(r));
        }

        /// <summary>Soft disc, wrapping at the edges so the texture tiles.</summary>
        static void Disc(Color[] px, int size, float cx, float cy, float radius, Color color)
        {
            var r = Mathf.CeilToInt(radius + 1);
            for (var dy = -r; dy <= r; dy++)
            for (var dx = -r; dx <= r; dx++)
            {
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                var a = Mathf.Clamp01(radius + 0.5f - d);
                if (a <= 0) continue;
                var x = ((int)cx + dx) % size; if (x < 0) x += size;
                var y = ((int)cy + dy) % size; if (y < 0) y += size;
                var i = y * size + x;
                px[i] = Color.Lerp(px[i], color, a);
            }
        }

        static Color[] Blur(Color[] px, int size)
        {
            var result = new Color[px.Length];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var sum = Color.clear;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    sum += px[(y + dy + size) % size * size + (x + dx + size) % size];
                result[y * size + x] = sum / 9f;
            }
            return result;
        }

        static void Save(string name, Color[] pixels, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            var size = (int)Mathf.Sqrt(pixels.Length);
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.SetPixels(pixels);
            tex.Apply();
            var path = Path.Combine(Folder, name);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = wrap;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
    }
}
