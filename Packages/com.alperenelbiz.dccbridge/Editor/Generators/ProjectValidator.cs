using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// Checks manifests and asset naming.
    ///
    /// Deliberately cheap and dependency-free: these are the mistakes that stay silent until
    /// something is visibly wrong in a scene, so the check has to be runnable at any time
    /// without launching Blender or any other tool.
    /// </summary>
    public static class ProjectValidator
    {
        private static readonly Regex IdPattern = new(@"^[a-z0-9]+(-[a-z0-9]+)*$");
        private static readonly Regex PropNamePattern = new(@"^[a-z0-9_]+$");
        private static readonly Regex HexPattern = new(@"^#[0-9a-fA-F]{6}$");
        private static readonly Regex ModelPattern = new(@"^SM_[A-Z][A-Za-z0-9]*\.fbx$");
        private static readonly Regex TexturePattern = new(@"^T_[A-Z][A-Za-z0-9]*\.(png|tga|jpg)$");
        private static readonly Regex PrefabPattern = new(@"^P_[A-Z][A-Za-z0-9]*\.prefab$");

        private static readonly HashSet<string> ValidColliders = new() { "box", "mesh", "none" };

        public static string Run(string projectRoot)
        {
            var errors = new List<string>();
            var warnings = new List<string>();

            var swatches = CheckPalette(projectRoot, errors, warnings);
            CheckProps(projectRoot, errors, warnings, swatches);
            CheckAssetNaming(projectRoot, errors, warnings);

            var report = new StringBuilder();
            foreach (var warning in warnings)
            {
                report.AppendLine($"warn  {warning}");
            }

            foreach (var error in errors)
            {
                report.AppendLine($"ERROR {error}");
            }

            report.Append($"{errors.Count} error(s), {warnings.Count} warning(s)");

            var text = report.ToString();
            if (errors.Count > 0)
            {
                Debug.LogError($"[DCC Bridge] validate\n{text}");
            }
            else
            {
                Debug.Log($"[DCC Bridge] validate\n{text}");
            }

            return text;
        }

        private static HashSet<string> CheckPalette(string projectRoot, List<string> errors, List<string> warnings)
        {
            var names = new HashSet<string>();
            var file = Path.Combine(projectRoot, "data", "palette.json");

            if (!File.Exists(file))
            {
                warnings.Add("data/palette.json not found — palette features are unavailable");
                return names;
            }

            PaletteJson palette;
            try
            {
                palette = JsonUtility.FromJson<PaletteJson>(File.ReadAllText(file));
            }
            catch (Exception e)
            {
                errors.Add($"palette.json could not be parsed: {e.Message}");
                return names;
            }

            var atlas = palette.atlas ?? new AtlasJson();
            if (palette.swatches.Length > atlas.columns * atlas.rows)
            {
                errors.Add($"palette.json: {palette.swatches.Length} swatches exceed the {atlas.columns}x{atlas.rows} grid");
            }

            foreach (var swatch in palette.swatches)
            {
                if (!names.Add(swatch.name))
                {
                    errors.Add($"palette.json: duplicate swatch '{swatch.name}'");
                }

                if (!HexPattern.IsMatch(swatch.color ?? string.Empty))
                {
                    errors.Add($"swatch '{swatch.name}': colour '{swatch.color}' is not #rrggbb");
                }
            }

            if (!File.Exists(Path.Combine(projectRoot, PaletteGenerator.AtlasPath)))
            {
                warnings.Add("palette atlas not generated yet (Tools > DCC Bridge > Generate > Palette Atlas)");
            }

            return names;
        }

        private static void CheckProps(string projectRoot, List<string> errors, List<string> warnings,
            HashSet<string> swatches)
        {
            var file = Path.Combine(projectRoot, "data", "props.json");
            if (!File.Exists(file))
            {
                warnings.Add("data/props.json not found — prop features are unavailable");
                return;
            }

            PropsManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<PropsManifest>(File.ReadAllText(file));
            }
            catch (Exception e)
            {
                errors.Add($"props.json could not be parsed: {e.Message}");
                return;
            }

            var blendRoot = Path.Combine(projectRoot, manifest.blend_root ?? "art-source/blender");
            var names = new HashSet<string>();

            foreach (var prop in manifest.props)
            {
                if (!names.Add(prop.name))
                {
                    errors.Add($"props.json: duplicate prop name '{prop.name}'");
                }

                if (!PropNamePattern.IsMatch(prop.name ?? string.Empty))
                {
                    errors.Add($"props.json: prop name '{prop.name}' must be lowercase_snake");
                }

                if (prop.size == null || prop.size.Length != 3 || prop.size.Any(v => v <= 0f))
                {
                    errors.Add($"prop '{prop.name}': size must be three positive metres");
                }

                if (!string.IsNullOrEmpty(prop.swatch) && swatches.Count > 0 && !swatches.Contains(prop.swatch))
                {
                    errors.Add($"prop '{prop.name}': unknown swatch '{prop.swatch}'");
                }

                var collider = string.IsNullOrEmpty(prop.collider) ? "box" : prop.collider;
                if (!ValidColliders.Contains(collider))
                {
                    errors.Add($"prop '{prop.name}': collider '{collider}' is not box, mesh or none");
                }

                if (!string.IsNullOrEmpty(prop.blend) && !File.Exists(Path.Combine(blendRoot, prop.blend)))
                {
                    warnings.Add($"prop '{prop.name}': source not built yet ({prop.blend})");
                }
            }
        }

        private static void CheckAssetNaming(string projectRoot, List<string> errors, List<string> warnings)
        {
            CheckNames(Path.Combine(projectRoot, "Assets/Art/Models"), "*.fbx", ModelPattern,
                "SM_PascalCase.fbx", errors);
            CheckNames(Path.Combine(projectRoot, "Assets/Art/Prefabs"), "*.prefab", PrefabPattern,
                "P_PascalCase.prefab", errors);

            var textures = Path.Combine(projectRoot, "Assets/Art/Textures");
            if (Directory.Exists(textures))
            {
                foreach (var path in Directory.EnumerateFiles(textures, "*.*", SearchOption.AllDirectories))
                {
                    var extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension is ".png" or ".tga" or ".jpg" && !TexturePattern.IsMatch(Path.GetFileName(path)))
                    {
                        errors.Add($"texture '{Path.GetFileName(path)}' does not match T_PascalCase.<ext>");
                    }
                }
            }
        }

        private static void CheckNames(string directory, string filter, Regex pattern, string shape,
            List<string> errors)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.EnumerateFiles(directory, filter))
            {
                var name = Path.GetFileName(path);
                if (!pattern.IsMatch(name))
                {
                    errors.Add($"'{name}' does not match {shape}");
                }
            }
        }
    }
}
