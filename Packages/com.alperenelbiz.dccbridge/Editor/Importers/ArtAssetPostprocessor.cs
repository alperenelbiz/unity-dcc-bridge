using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// Applies import settings by path convention, so no generated asset depends on someone
    /// remembering to tick a box. This runs on every import, including the automatic refresh
    /// after a pipeline run.
    /// </summary>
    public class ArtAssetPostprocessor : AssetPostprocessor
    {
        private const string ModelsRoot = "Assets/Art/Models/";
        private const string TexturesRoot = "Assets/Art/Textures/";
        private const string AtlasRoot = "Assets/Art/Textures/Atlas/";
        private const string DetailRoot = "Assets/Art/Textures/Detail/";
        private const string SubstanceRoot = "Assets/Art/Textures/Substance/";

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(ModelsRoot, StringComparison.Ordinal))
            {
                return;
            }

            var importer = (ModelImporter)assetImporter;

            // The exporter already bakes scale and axes, so Unity must not re-apply anything. A
            // globalScale other than 1 here silently reintroduces the exact problem those export
            // settings exist to prevent.
            importer.globalScale = 1f;
            importer.useFileScale = false;
            importer.bakeAxisConversion = false;

            // Props are static set dressing sharing generated materials, so Unity should not
            // invent per-file materials or animation rigs.
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importVisibility = false;
            importer.importBlendShapes = false;

            importer.meshCompression = ModelImporterMeshCompression.Medium;
            importer.isReadable = false;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.weldVertices = true;
            importer.indexFormat = ModelImporterIndexFormat.UInt16;

            // Lightmap UVs only where they are needed; generating them for every prop roughly
            // doubles import time across a large set.
            importer.generateSecondaryUV = assetPath.Contains("/Static/", StringComparison.Ordinal);
        }

        private void OnPreprocessTexture()
        {
            var importer = (TextureImporter)assetImporter;

            if (assetPath.StartsWith(AtlasRoot, StringComparison.Ordinal))
            {
                ConfigurePaletteAtlas(importer);
            }
            else if (assetPath.StartsWith(DetailRoot, StringComparison.Ordinal))
            {
                ConfigureDetailMap(importer);
            }
            else if (assetPath.StartsWith(SubstanceRoot, StringComparison.Ordinal))
            {
                ConfigureSubstanceMap(importer);
            }
            else if (assetPath.StartsWith(TexturesRoot, StringComparison.Ordinal))
            {
                ConfigureGeneralTexture(importer);
            }
        }

        /// <summary>
        /// A palette atlas packs flat colour cells. Filtering or mipmapping it bleeds neighbouring
        /// colours across every UV island, which shows up as coloured fringing on props and is
        /// very hard to diagnose after the fact.
        /// </summary>
        private static void ConfigurePaletteAtlas(TextureImporter importer)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.npotScale = TextureImporterNPOTScale.None;
        }

        /// <summary>
        /// Seamless tiling maps. These must repeat, and must be treated as data rather than
        /// colour — an sRGB normal map lights subtly wrong everywhere.
        /// </summary>
        private static void ConfigureDetailMap(TextureImporter importer)
        {
            var isNormal = Path.GetFileNameWithoutExtension(importer.assetPath)
                .EndsWith("Normal", StringComparison.Ordinal);

            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.anisoLevel = 4;
        }

        /// <summary>
        /// Substance exports its own naming — `_BaseColor`, `_Normal`, `_Metallic`, `_Roughness`,
        /// `_Height` — which the general `_N` convention does not recognise. Getting this wrong
        /// is silent: an sRGB normal map or a colour-space-mangled roughness map lights subtly
        /// wrong everywhere without any error.
        /// </summary>
        private static void ConfigureSubstanceMap(TextureImporter importer)
        {
            var name = Path.GetFileNameWithoutExtension(importer.assetPath);
            var isNormal = name.EndsWith("_Normal", StringComparison.Ordinal);
            var isColour = name.EndsWith("_BaseColor", StringComparison.Ordinal)
                        || name.EndsWith("_Diffuse", StringComparison.Ordinal)
                        || name.EndsWith("_Emissive", StringComparison.Ordinal);

            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = isColour;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.anisoLevel = 4;
        }

        private static void ConfigureGeneralTexture(TextureImporter importer)
        {
            var isNormal = Path.GetFileNameWithoutExtension(importer.assetPath)
                .EndsWith("_N", StringComparison.Ordinal);

            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !isNormal;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }
    }
}
