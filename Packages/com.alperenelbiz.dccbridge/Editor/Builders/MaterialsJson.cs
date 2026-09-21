using System;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// Mirrors the optional data/materials.json, which describes detail materials — the ones
    /// carrying a real tiling texture instead of a flat palette swatch.
    ///
    /// The atlas material is not listed here: every project has exactly one and it is built from
    /// the palette, so requiring it to be declared would be noise.
    /// </summary>
    [Serializable]
    public class MaterialsJson
    {
        public DetailMaterialJson[] materials = Array.Empty<DetailMaterialJson>();
    }

    [Serializable]
    public class DetailMaterialJson
    {
        /// <summary>Asset name, matching what a prop declares in its `materials` list.</summary>
        public string name;

        /// <summary>Palette swatch supplying the flat base colour.</summary>
        public string baseSwatch;

        /// <summary>Texture name under Assets/Art/Textures/Detail, without extension.</summary>
        public string normalMap;

        public float smoothness = 0.3f;
        public float metallic;
    }
}
