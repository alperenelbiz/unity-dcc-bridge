using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// Builds a URP material per Substance texture set, completing the hero-prop path:
    /// Blender exports the mesh, Substance paints it, the packer converts roughness to
    /// smoothness, and this wires the result into something a prefab can use.
    ///
    /// Materials are named after the texture set, which Substance in turn took from the mesh's
    /// own material names. That is what lets the prefab builder pick them up without any
    /// additional mapping: a slot called M_PropLeather finds a material called M_PropLeather.
    ///
    /// The corollary is that a given material comes either from the palette or from Substance,
    /// never both. If a name appears in data/materials.json as well, whichever step runs last
    /// wins — so remove it there once a set is painted.
    /// </summary>
    public static class SubstanceMaterialBuilder
    {
        public static string Build(string projectRoot)
        {
            var dir = Path.Combine(projectRoot, SubstanceUrpPacker.SourceDir);
            if (!Directory.Exists(dir))
            {
                return $"{SubstanceUrpPacker.SourceDir} not found — export from Substance first.";
            }

            // One material per texture set, discovered from the BaseColor maps.
            var sets = Directory.EnumerateFiles(dir, "*_BaseColor.png")
                .Select(Path.GetFileNameWithoutExtension)
                .Select(name => name[..^"_BaseColor".Length])
                .ToList();

            if (sets.Count == 0)
                return "no *_BaseColor.png found; nothing to build.";

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
                return "no usable shader found.";

            MaterialBuilder.EnsureFolder(MaterialBuilder.MaterialDir);

            var built = new List<string>();

            foreach (var set in sets)
            {
                // Strip the mesh prefix Substance prepends, so SM_ChairGaming_M_PropLeather
                // becomes M_PropLeather — the name the mesh's slot actually carries.
                var index = set.IndexOf("_M_", System.StringComparison.Ordinal);
                var materialName = index >= 0 ? set[(index + 1)..] : set;

                var path = $"{MaterialBuilder.MaterialDir}/{materialName}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                else
                {
                    material.shader = shader;
                }

                Assign(material, "_BaseMap", Load(set, "BaseColor"));
                material.SetColor("_BaseColor", Color.white);

                var normal = Load(set, "Normal");
                if (normal != null)
                {
                    material.SetTexture("_BumpMap", normal);
                    material.SetFloat("_BumpScale", 1f);
                    // URP samples _BumpMap only with this keyword on; the texture alone does nothing.
                    material.EnableKeyword("_NORMALMAP");
                }

                // The packed mask goes into both slots: URP reads R+A from Metallic and G from
                // Occlusion, and expects the same texture in each.
                var mask = Load(set, "MaskMap");
                if (mask != null)
                {
                    material.SetTexture("_MetallicGlossMap", mask);
                    material.SetTexture("_OcclusionMap", mask);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                    material.EnableKeyword("_OCCLUSIONMAP");
                }
                else
                {
                    Debug.LogWarning($"[DCC Bridge] {materialName}: no mask map — run Pack for URP first.");
                }

                EditorUtility.SetDirty(material);
                built.Add(materialName);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return $"{built.Count} material(s) from Substance: {string.Join(", ", built)}";

            Texture2D Load(string set, string suffix) =>
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    $"{SubstanceUrpPacker.SourceDir}/{set}_{suffix}.png");

            void Assign(Material material, string property, Texture2D texture)
            {
                if (texture != null)
                {
                    material.SetTexture(property, texture);
                }
            }
        }
    }
}
