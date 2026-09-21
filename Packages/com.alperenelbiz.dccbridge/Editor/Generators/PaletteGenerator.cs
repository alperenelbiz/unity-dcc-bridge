using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// Builds the palette atlas from data/palette.json.
    ///
    /// The atlas is generated rather than painted because Blender has to know exactly where each
    /// swatch sits in order to snap UV islands onto it. Producing the texture and the UV lookup
    /// from one source means the two can never disagree — a hand-painted atlas breaks that link
    /// the first time a swatch moves by a pixel.
    /// </summary>
    public static class PaletteGenerator
    {
        public const string AtlasPath = "Assets/Art/Textures/Atlas/T_PaletteAtlas.png";
        public const string UvLookupPath = "art-source/blender/_lib/palette_uv.json";
        private const string ReferencePath = "art-source/blender/_lib/palette_reference.png";
        private const string SourcePath = "data/palette.json";

        public static string Build(string projectRoot)
        {
            var sourceFile = Path.Combine(projectRoot, SourcePath);
            if (!File.Exists(sourceFile))
            {
                return $"{SourcePath} not found. Import Samples > Basic Setup for a starting point.";
            }

            PaletteJson palette;
            try
            {
                palette = JsonUtility.FromJson<PaletteJson>(File.ReadAllText(sourceFile));
            }
            catch (Exception e)
            {
                return $"{SourcePath} could not be parsed: {e.Message}";
            }

            if (palette?.swatches == null || palette.swatches.Length == 0)
            {
                return $"{SourcePath} defines no swatches.";
            }

            var atlas = palette.atlas ?? new AtlasJson();
            if (palette.swatches.Length > atlas.columns * atlas.rows)
            {
                return $"{palette.swatches.Length} swatches will not fit a {atlas.columns}x{atlas.rows} grid.";
            }

            var cell = atlas.size / atlas.columns;

            // Magenta base: a UV that misses a defined swatch is then obvious in-engine instead of
            // quietly picking up whatever colour happens to be next to it.
            var texture = new Texture2D(atlas.size, atlas.size, TextureFormat.RGBA32, false);
            var pixels = new Color32[atlas.size * atlas.size];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 0, 255, 255);
            }

            var lookup = new StringBuilder();
            lookup.Append("{\n  \"atlas\": {\n");
            lookup.Append($"    \"size\": {atlas.size},\n    \"columns\": {atlas.columns},\n    \"rows\": {atlas.rows}\n  }},\n");
            lookup.Append("  \"swatches\": {\n");

            for (var index = 0; index < palette.swatches.Length; index++)
            {
                var swatch = palette.swatches[index];
                if (!ColorUtility.TryParseHtmlString(swatch.color, out var colour))
                {
                    return $"swatch '{swatch.name}': '{swatch.color}' is not a valid #rrggbb colour.";
                }

                var column = index % atlas.columns;
                var row = index / atlas.columns;

                FillCell(pixels, atlas.size, column * cell, row * cell, cell, colour);

                // Texture2D rows run bottom-up while the grid is read top-down, so V is flipped
                // here. This is the only place that conversion happens.
                var u = (column + 0.5f) / atlas.columns;
                var v = 1f - (row + 0.5f) / atlas.rows;

                lookup.Append($"    \"{swatch.name}\": {{ \"u\": {Round(u)}, \"v\": {Round(v)}, ");
                lookup.Append($"\"color\": \"{swatch.color}\", \"index\": {index} }}");
                lookup.Append(index == palette.swatches.Length - 1 ? "\n" : ",\n");
            }

            lookup.Append("  }\n}\n");

            texture.SetPixels32(pixels);
            texture.Apply();

            Write(Path.Combine(projectRoot, AtlasPath), texture.EncodeToPNG());
            WriteText(Path.Combine(projectRoot, UvLookupPath), lookup.ToString());
            WriteReferenceSheet(projectRoot, palette, atlas);

            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceUpdate);

            return $"{palette.swatches.Length} swatches -> {AtlasPath}\nUV lookup -> {UvLookupPath}";
        }

        /// <summary>
        /// A labelled sheet for humans, kept in art-source so Unity never imports it. Useful when
        /// picking a swatch name, since the atlas itself is unreadable at 64px a cell.
        /// </summary>
        private static void WriteReferenceSheet(string projectRoot, PaletteJson palette, AtlasJson atlas)
        {
            var cell = Mathf.Max(8, atlas.size / atlas.columns);
            var rowsUsed = Mathf.CeilToInt(palette.swatches.Length / (float)atlas.columns);
            var width = atlas.columns * cell;
            var height = rowsUsed * cell;

            var sheet = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(24, 24, 26, 255);
            }

            for (var index = 0; index < palette.swatches.Length; index++)
            {
                if (!ColorUtility.TryParseHtmlString(palette.swatches[index].color, out var colour))
                {
                    continue;
                }

                var column = index % atlas.columns;
                var row = index / atlas.columns;
                FillCell(pixels, width, column * cell, row * cell, cell - 2, colour, height);
            }

            sheet.SetPixels32(pixels);
            sheet.Apply();
            Write(Path.Combine(projectRoot, ReferencePath), sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
        }

        private static void FillCell(Color32[] pixels, int stride, int x0, int y0, int size,
            Color32 colour, int height = -1)
        {
            if (height < 0)
            {
                height = pixels.Length / stride;
            }

            for (var y = 0; y < size; y++)
            {
                // Grid row 0 is the top; the pixel buffer's row 0 is the bottom.
                var py = height - 1 - (y0 + y);
                if (py < 0 || py >= height)
                {
                    continue;
                }

                for (var x = 0; x < size; x++)
                {
                    var px = x0 + x;
                    if (px >= 0 && px < stride)
                    {
                        pixels[py * stride + px] = colour;
                    }
                }
            }
        }

        private static string Round(float value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);

        internal static void Write(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        internal static void WriteText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }
}
