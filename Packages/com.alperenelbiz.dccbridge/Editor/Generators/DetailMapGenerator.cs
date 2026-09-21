using System.IO;
using UnityEditor;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// Generates seamless tiling detail normal maps.
    ///
    /// Generated rather than painted: a hand-painted tile shows its repeat, while periodic noise
    /// tiles perfectly by construction and can be re-derived from its parameters instead of being
    /// an image someone made once and can no longer reproduce.
    ///
    /// Output is OpenGL (+Y up), which is what Unity and URP expect — and what Blender bakes — so
    /// nothing in this pipeline ever needs a green-channel flip.
    /// </summary>
    public static class DetailMapGenerator
    {
        public const string OutputDir = "Assets/Art/Textures/Detail";
        private const string ReferenceDir = "art-source/blender/_lib";

        public static string Build(string projectRoot, int size = 512, int seed = 7)
        {
            var height = LeatherHeight(size, seed);
            var normal = HeightToNormal(height, size, size * 0.006f);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(normal);
            texture.Apply();

            var outputPath = Path.Combine(projectRoot, OutputDir, "T_LeatherNormal.png");
            PaletteGenerator.Write(outputPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            WriteHeightReference(projectRoot, height, size);

            AssetDatabase.ImportAsset($"{OutputDir}/T_LeatherNormal.png", ImportAssetOptions.ForceUpdate);
            return $"T_LeatherNormal.png ({size}, seamless, OpenGL +Y) -> {OutputDir}";
        }

        /// <summary>
        /// Leather grain: pebbled cells meeting at creases.
        ///
        /// The height is F2 - F1, not raw F1. F1 peaks at the cell *boundaries*, which builds
        /// ridges between pits and reads as speckle; F2 - F1 falls to zero exactly on the
        /// boundaries and rises toward each cell centre, giving domes separated by creases.
        /// </summary>
        private static float[] LeatherHeight(int size, int seed)
        {
            var random = new System.Random(seed);

            var coarse = VoronoiDomes(size, 18, random);
            var fine = VoronoiDomes(size, 38, random);
            var grain = PeriodicNoise(size, 128, random);

            var height = new float[size * size];
            var min = float.MaxValue;
            var max = float.MinValue;

            for (var i = 0; i < height.Length; i++)
            {
                // Creases are the read, so the coarse layer is sharpened to keep boundaries tight
                // rather than blurring into a soft lumpy field.
                var value = 0.72f * Mathf.Clamp01(coarse[i] * 1.8f) + 0.22f * fine[i] + 0.06f * grain[i];
                height[i] = value;
                min = Mathf.Min(min, value);
                max = Mathf.Max(max, value);
            }

            var range = Mathf.Max(1e-6f, max - min);
            for (var i = 0; i < height.Length; i++)
            {
                // Flatten the tops so pebbles read as domes meeting at creases, not as cones.
                height[i] = Mathf.Pow((height[i] - min) / range, 0.55f);
            }

            return height;
        }

        /// <summary>
        /// F2 - F1 over scattered points, with every distance measured on a torus.
        ///
        /// Wrapping is what makes the result seamless: the left edge genuinely continues into the
        /// right, so the tile has no seam to hide.
        /// </summary>
        private static float[] VoronoiDomes(int size, int cells, System.Random random)
        {
            var points = new Vector2[cells * cells];
            for (var y = 0; y < cells; y++)
            {
                for (var x = 0; x < cells; x++)
                {
                    points[y * cells + x] = new Vector2(
                        (x + (float)random.NextDouble() * 0.9f) / cells,
                        (y + (float)random.NextDouble() * 0.9f) / cells);
                }
            }

            var result = new float[size * size];
            var max = 0f;

            for (var py = 0; py < size; py++)
            {
                var v = (py + 0.5f) / size;
                for (var px = 0; px < size; px++)
                {
                    var u = (px + 0.5f) / size;

                    var f1 = float.MaxValue;
                    var f2 = float.MaxValue;

                    foreach (var point in points)
                    {
                        var dx = Mathf.Abs(u - point.x);
                        var dy = Mathf.Abs(v - point.y);
                        dx = Mathf.Min(dx, 1f - dx);
                        dy = Mathf.Min(dy, 1f - dy);

                        var distance = dx * dx + dy * dy;   // squared is enough to rank
                        if (distance < f1)
                        {
                            f2 = f1;
                            f1 = distance;
                        }
                        else if (distance < f2)
                        {
                            f2 = distance;
                        }
                    }

                    var dome = Mathf.Sqrt(f2) - Mathf.Sqrt(f1);
                    result[py * size + px] = dome;
                    max = Mathf.Max(max, dome);
                }
            }

            if (max > 0f)
            {
                for (var i = 0; i < result.Length; i++)
                {
                    result[i] /= max;
                }
            }

            return result;
        }

        /// <summary>Value noise that tiles, by sampling a low-resolution grid cyclically.</summary>
        private static float[] PeriodicNoise(int size, int frequency, System.Random random)
        {
            var grid = new float[frequency * frequency];
            for (var i = 0; i < grid.Length; i++)
            {
                grid[i] = (float)random.NextDouble();
            }

            var result = new float[size * size];
            var scale = frequency / (float)size;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var fx = x * scale;
                    var fy = y * scale;

                    var x0 = Mathf.FloorToInt(fx);
                    var y0 = Mathf.FloorToInt(fy);
                    var tx = fx - x0;
                    var ty = fy - y0;

                    // Modulo on the lookup is what keeps the noise periodic across the tile edge.
                    var x1 = (x0 + 1) % frequency;
                    var y1 = (y0 + 1) % frequency;
                    x0 %= frequency;
                    y0 %= frequency;

                    tx = tx * tx * (3f - 2f * tx);   // smoothstep
                    ty = ty * ty * (3f - 2f * ty);

                    var top = Mathf.Lerp(grid[y0 * frequency + x0], grid[y0 * frequency + x1], tx);
                    var bottom = Mathf.Lerp(grid[y1 * frequency + x0], grid[y1 * frequency + x1], tx);
                    result[y * size + x] = Mathf.Lerp(top, bottom, ty);
                }
            }

            return result;
        }

        /// <summary>Central differences with wrapping, so the normal map tiles like its height map.</summary>
        private static Color32[] HeightToNormal(float[] height, int size, float strength)
        {
            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                var up = ((y + 1) % size) * size;
                var down = ((y - 1 + size) % size) * size;
                var row = y * size;

                for (var x = 0; x < size; x++)
                {
                    var right = (x + 1) % size;
                    var left = (x - 1 + size) % size;

                    var dx = (height[row + right] - height[row + left]) * strength;
                    var dy = (height[up + x] - height[down + x]) * strength;

                    var normal = new Vector3(-dx, dy, 1f).normalized;

                    pixels[row + x] = new Color32(
                        (byte)Mathf.RoundToInt((normal.x * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((normal.y * 0.5f + 0.5f) * 255f),
                        (byte)Mathf.RoundToInt((normal.z * 0.5f + 0.5f) * 255f),
                        255);
                }
            }

            return pixels;
        }

        private static void WriteHeightReference(string projectRoot, float[] height, int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[height.Length];

            for (var i = 0; i < height.Length; i++)
            {
                var value = (byte)Mathf.RoundToInt(Mathf.Clamp01(height[i]) * 255f);
                pixels[i] = new Color32(value, value, value, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            PaletteGenerator.Write(
                Path.Combine(projectRoot, ReferenceDir, "leather_height_reference.png"),
                texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
