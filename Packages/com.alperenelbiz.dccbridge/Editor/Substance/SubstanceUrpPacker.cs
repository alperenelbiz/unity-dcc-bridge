using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// Repacks a Substance export into the layout URP's Lit shader actually reads.
    ///
    /// Substance writes separate Metallic and **Roughness** maps. URP wants one RGBA texture —
    /// R metallic, G occlusion, A **smoothness** — assigned to *both* the Metallic and Occlusion
    /// slots, with sRGB off. Roughness and smoothness are inverses, so handing URP a Substance
    /// export directly makes every rough surface glossy and every glossy one rough.
    ///
    /// Nothing errors when that happens, which is why this conversion is a pipeline step rather
    /// than a note in the documentation.
    /// </summary>
    public static class SubstanceUrpPacker
    {
        public const string SourceDir = "Assets/Art/Textures/Substance";

        public static string Pack(string projectRoot)
        {
            var dir = Path.Combine(projectRoot, SourceDir);
            if (!Directory.Exists(dir))
            {
                return $"{SourceDir} not found — export from Substance first.";
            }

            // Group by texture set: everything before the _Metallic / _Roughness suffix.
            var sets = Directory.EnumerateFiles(dir, "*_Metallic.png")
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .Select(name => name[..^"_Metallic".Length])
                .ToList();

            if (sets.Count == 0)
            {
                return "no *_Metallic.png found; nothing to pack.";
            }

            var packed = new List<string>();

            foreach (var set in sets)
            {
                var metallic = ReadLinear(Path.Combine(dir, $"{set}_Metallic.png"));
                var roughness = ReadLinear(Path.Combine(dir, $"{set}_Roughness.png"));
                var occlusion = ReadLinear(Path.Combine(dir, $"{set}_AmbientOcclusion.png"));

                if (metallic == null || roughness == null)
                {
                    Debug.LogWarning($"[DCC Bridge] {set}: needs both _Metallic and _Roughness");
                    continue;
                }

                var size = metallic.width;
                if (roughness.width != size || roughness.height != metallic.height)
                {
                    Debug.LogWarning($"[DCC Bridge] {set}: metallic and roughness differ in size");
                    continue;
                }

                var output = new Texture2D(size, metallic.height, TextureFormat.RGBA32, false, true);
                var pixels = new Color32[size * metallic.height];

                var m = metallic.GetPixels32();
                var r = roughness.GetPixels32();
                var o = occlusion != null && occlusion.width == size ? occlusion.GetPixels32() : null;

                for (var i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = new Color32(
                        m[i].r,                       // R: metallic
                        o?[i].r ?? (byte)255,         // G: occlusion, white when not exported
                        0,                            // B: unused by URP Lit
                        (byte)(255 - r[i].r));        // A: smoothness = 1 - roughness
                }

                output.SetPixels32(pixels);
                output.Apply();

                var outPath = Path.Combine(dir, $"{set}_MaskMap.png");
                File.WriteAllBytes(outPath, output.EncodeToPNG());
                Object.DestroyImmediate(output);

                packed.Add($"{set}_MaskMap.png");
            }

            AssetDatabase.Refresh();

            foreach (var name in packed)
            {
                // Mask maps are data, never colour.
                var assetPath = $"{SourceDir}/{name}";
                if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer && importer.sRGBTexture)
                {
                    importer.sRGBTexture = false;
                    importer.SaveAndReimport();
                }
            }

            return packed.Count == 0
                ? "nothing packed"
                : $"{packed.Count} mask map(s): {string.Join(", ", packed)}\n" +
                  "Assign each to BOTH the Metallic and Occlusion slots of a URP Lit material.";
        }

        /// <summary>Loads a PNG outside the AssetDatabase, so importer settings cannot alter it.</summary>
        private static Texture2D ReadLinear(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            return texture.LoadImage(File.ReadAllBytes(path)) ? texture : null;
        }
    }
}
