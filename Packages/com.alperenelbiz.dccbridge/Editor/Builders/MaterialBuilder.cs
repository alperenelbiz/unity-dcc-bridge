using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// Builds the shared materials every prop uses.
    ///
    /// The atlas material is the point of the stylized approach: one material over one texture is
    /// what collapses a whole set into a handful of draw calls. Detail materials are the
    /// deliberate exception, declared per project in data/materials.json.
    /// </summary>
    public static class MaterialBuilder
    {
        public const string MaterialDir = "Assets/Art/Materials";
        public const string AtlasMaterialName = "M_PropAtlas";
        private const string DetailTextureDir = "Assets/Art/Textures/Detail";
        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string FallbackShader = "Standard";

        public static Dictionary<string, Material> RebuildAll(string projectRoot)
        {
            var materials = new Dictionary<string, Material>();

            var atlas = BuildAtlasMaterial();
            if (atlas != null)
            {
                materials[AtlasMaterialName] = atlas;
            }

            foreach (var pair in BuildDetailMaterials(projectRoot))
            {
                materials[pair.Key] = pair.Value;
            }

            return materials;
        }

        private static Material BuildAtlasMaterial()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(PaletteGenerator.AtlasPath);
            if (atlas == null)
            {
                Debug.LogError($"[DCC Bridge] {PaletteGenerator.AtlasPath} not found — generate the palette first");
                return null;
            }

            var material = LoadOrCreate($"{MaterialDir}/{AtlasMaterialName}.mat");
            if (material == null)
            {
                return null;
            }

            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Metallic", 0f);

            // Just enough smoothness to catch a highlight, so flat props do not read as cardboard.
            material.SetFloat("_Smoothness", 0.15f);

            // Reflections would pull scene colour into every flat swatch and muddy the palette.
            // URP gates this on a keyword: setting the float alone changes nothing while the
            // inspector shows the new value.
            material.SetFloat("_EnvironmentReflections", 0f);
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            material.SetFloat("_SpecularHighlights", 1f);
            material.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Dictionary<string, Material> BuildDetailMaterials(string projectRoot)
        {
            var result = new Dictionary<string, Material>();
            var file = Path.Combine(projectRoot, "data", "materials.json");

            if (!File.Exists(file))
            {
                return result;   // optional: a project may only ever need flat colour
            }

            MaterialsJson declared;
            try
            {
                declared = JsonUtility.FromJson<MaterialsJson>(File.ReadAllText(file));
            }
            catch (Exception e)
            {
                Debug.LogError($"[DCC Bridge] data/materials.json could not be parsed: {e.Message}");
                return result;
            }

            var swatches = PaletteSwatches(projectRoot);

            foreach (var spec in declared.materials)
            {
                var material = LoadOrCreate($"{MaterialDir}/{spec.name}.mat");
                if (material == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(spec.baseSwatch))
                {
                    if (swatches.TryGetValue(spec.baseSwatch, out var colour))
                    {
                        material.SetColor("_BaseColor", colour);
                    }
                    else
                    {
                        Debug.LogWarning($"[DCC Bridge] {spec.name}: unknown swatch '{spec.baseSwatch}'");
                    }
                }

                if (!string.IsNullOrEmpty(spec.normalMap))
                {
                    var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        $"{DetailTextureDir}/{spec.normalMap}.png");

                    if (normal == null)
                    {
                        Debug.LogWarning($"[DCC Bridge] {spec.name}: {spec.normalMap}.png not found in {DetailTextureDir}");
                    }
                    else
                    {
                        material.SetTexture("_BumpMap", normal);
                        material.SetFloat("_BumpScale", 1f);

                        // URP only samples _BumpMap when this keyword is on.
                        material.EnableKeyword("_NORMALMAP");
                    }
                }

                material.SetFloat("_Metallic", spec.metallic);
                material.SetFloat("_Smoothness", spec.smoothness);

                EditorUtility.SetDirty(material);
                result[spec.name] = material;
            }

            return result;
        }

        private static Dictionary<string, Color> PaletteSwatches(string projectRoot)
        {
            var swatches = new Dictionary<string, Color>();
            var file = Path.Combine(projectRoot, "data", "palette.json");

            if (!File.Exists(file))
            {
                return swatches;
            }

            try
            {
                var palette = JsonUtility.FromJson<PaletteJson>(File.ReadAllText(file));
                foreach (var swatch in palette.swatches)
                {
                    if (ColorUtility.TryParseHtmlString(swatch.color, out var colour))
                    {
                        swatches[swatch.name] = colour;
                    }
                }
            }
            catch (Exception)
            {
                // Validation reports a malformed palette; here it just means no swatch colours.
            }

            return swatches;
        }

        private static Material LoadOrCreate(string path)
        {
            var shader = Shader.Find(LitShader) ?? Shader.Find(FallbackShader);
            if (shader == null)
            {
                Debug.LogError($"[DCC Bridge] neither '{LitShader}' nor '{FallbackShader}' was found");
                return null;
            }

            EnsureFolder(MaterialDir);

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

            return material;
        }

        /// <summary>
        /// Creates a folder and every missing parent. AssetDatabase.CreateFolder returns an empty
        /// GUID instead of throwing when the parent is absent, so a flat call leaves the folder
        /// missing and every asset written under it then fails.
        /// </summary>
        internal static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folder)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
