using System;
using System.Text;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// The pipeline's commands, exposed both as Editor menu items and to the `unity` CLI.
    ///
    /// Registering through com.unity.pipeline means an agent or CI job can drive these against a
    /// *running* Editor. That matters because Unity refuses to open a project in batch mode while
    /// its Editor holds the project lock, so without a live channel every automated step would
    /// require closing the Editor first.
    ///
    /// Generators run in-process. Only the Blender commands need an external tool, and they check
    /// for it and return a readable message rather than failing inside the toolchain.
    /// </summary>
    public static class DccCommands
    {
        [CliCommand("dcc_status", "Report which DCC tools are enabled and usable in this project.",
            Tags = new[] { "dcc" })]
        [MenuItem("Tools/DCC Bridge/Status", priority = 10)]
        public static string Status()
        {
            var settings = DccBridgeSettings.Instance;
            settings.ProbeAll();

            var report = new StringBuilder("DCC Bridge\n");
            foreach (var state in settings.Tools)
            {
                var status = !state.Enabled ? "disabled"
                    : !state.Detected ? "not installed"
                    : state.NeedsConnection && !state.Connected ? "not connected"
                    : "ready";

                var channel = state.NeedsConnection ? $"port {state.Port}" : string.Empty;
                report.AppendLine($"  {state.DisplayName,-22} {status,-14} {state.Version} {channel}".TrimEnd());

                if (state.Enabled && !string.IsNullOrEmpty(state.Problem))
                {
                    report.AppendLine($"    {state.Problem}");
                }
            }

            report.AppendLine("  Generators run inside Unity and need no external tool.");

            if (!settings.Configured)
            {
                report.AppendLine("  (not set up yet — Tools > DCC Bridge > Setup)");
            }

            var text = report.ToString().TrimEnd();
            Debug.Log(text);
            return text;
        }

        [CliCommand("dcc_palette", "Generate the palette atlas and its Blender UV lookup from data/palette.json.",
            Tags = new[] { "dcc", "dcc/generate" })]
        [MenuItem("Tools/DCC Bridge/Generate/Palette Atlas", priority = 20)]
        public static string Palette() => Log(PaletteGenerator.Build(ToolRunner.ProjectRoot));

        [CliCommand("dcc_detail", "Generate seamless tiling detail normal maps.",
            Tags = new[] { "dcc", "dcc/generate" })]
        [MenuItem("Tools/DCC Bridge/Generate/Detail Maps", priority = 21)]
        public static string DetailMaps() => Log(DetailMapGenerator.Build(ToolRunner.ProjectRoot));

        [CliCommand("dcc_validate", "Check manifests and asset naming conventions.",
            Tags = new[] { "dcc" })]
        [MenuItem("Tools/DCC Bridge/Validate", priority = 30)]
        public static string Validate() => ProjectValidator.Run(ToolRunner.ProjectRoot);

        [CliCommand("dcc_build_props", "Build prop .blend sources from data/props.json using headless Blender.",
            Tags = new[] { "dcc", "dcc/blender" })]
        [MenuItem("Tools/DCC Bridge/Blender/Build Prop Sources", priority = 40)]
        public static string BuildProps() => Report(ToolRunner.RunBlender("build_props.py"));

        [CliCommand("dcc_export_props", "Export prop FBX from .blend sources into Assets/Art/Models, with validation.",
            Tags = new[] { "dcc", "dcc/blender" })]
        [MenuItem("Tools/DCC Bridge/Blender/Export Props", priority = 41)]
        public static string ExportProps() =>
            Report(ToolRunner.RunBlender("blender_export.py", "--out",
                $"\"{System.IO.Path.Combine(ToolRunner.ProjectRoot, "Assets", "Art", "Models")}\""));

        [CliCommand("dcc_materials", "Build the shared materials from the palette and data/materials.json.",
            Tags = new[] { "dcc", "dcc/assemble" })]
        [MenuItem("Tools/DCC Bridge/Assemble/Materials", priority = 44)]
        public static string Materials()
        {
            var built = MaterialBuilder.RebuildAll(ToolRunner.ProjectRoot);
            AssetDatabase.SaveAssets();
            return Log($"{built.Count} material(s) -> {MaterialBuilder.MaterialDir}");
        }

        [CliCommand("dcc_prefabs", "Build a placeable prefab per prop: mesh, materials and collider.",
            Tags = new[] { "dcc", "dcc/assemble" })]
        [MenuItem("Tools/DCC Bridge/Assemble/Prefabs", priority = 45)]
        public static string Prefabs() => Log(PrefabBuilder.Rebuild(ToolRunner.ProjectRoot));

        [CliCommand("dcc_ps_describe", "Report the document currently open in Photoshop.",
            Tags = new[] { "dcc", "dcc/photoshop" })]
        [MenuItem("Tools/DCC Bridge/Photoshop/Describe Open Document", priority = 60)]
        public static string PhotoshopDescribe() => Report(PhotoshopBridge.Describe());

        [CliCommand("dcc_ps_export_layers",
            "Export each top-level layer of the open Photoshop document as a PNG into art-source/photoshop/export.",
            Tags = new[] { "dcc", "dcc/photoshop" })]
        [MenuItem("Tools/DCC Bridge/Photoshop/Export Layers", priority = 61)]
        public static string PhotoshopExportLayers()
        {
            var target = System.IO.Path.Combine(
                ToolRunner.ProjectRoot, "art-source", "photoshop", "export");
            return Report(PhotoshopBridge.ExportLayers(target));
        }

        [MenuItem("Tools/DCC Bridge/Photoshop/Describe Open Document", true)]
        [MenuItem("Tools/DCC Bridge/Photoshop/Export Layers", true)]
        private static bool PhotoshopMenuEnabled() => DccBridgeSettings.Instance.IsUsable(DccTool.Photoshop);

        [CliCommand("dcc_sp_status", "Report the Substance 3D Painter API version and open project.",
            Tags = new[] { "dcc", "dcc/substance" })]
        [MenuItem("Tools/DCC Bridge/Substance/Status", priority = 70)]
        public static string SubstanceStatus() => Report(SubstanceBridge.Describe());

        [CliCommand("dcc_sp_export",
            "Export the open Substance project's textures into Assets/Art/Textures/Substance.",
            Tags = new[] { "dcc", "dcc/substance" })]
        [MenuItem("Tools/DCC Bridge/Substance/Export Textures", priority = 71)]
        public static string SubstanceExport()
        {
            var target = System.IO.Path.Combine(
                ToolRunner.ProjectRoot, "Assets", "Art", "Textures", "Substance");

            // Metallic-roughness is Substance's own default. A URP project usually wants a
            // metallic-smoothness preset instead; name it here once the shelf has one.
            var result = SubstanceBridge.ExportTextures(target, "PBR Metallic Roughness");
            if (result.Ok)
            {
                AssetDatabase.Refresh();
            }

            return Report(result);
        }

        [MenuItem("Tools/DCC Bridge/Substance/Status", true)]
        [MenuItem("Tools/DCC Bridge/Substance/Export Textures", true)]
        private static bool SubstanceMenuEnabled() => DccBridgeSettings.Instance.IsUsable(DccTool.SubstancePainter);

        [CliCommand("dcc_run_all", "Run every step available with the tools this project has enabled.",
            Tags = new[] { "dcc" })]
        [MenuItem("Tools/DCC Bridge/Run Everything", priority = 50)]
        public static string RunAll()
        {
            var settings = DccBridgeSettings.Instance;
            var report = new StringBuilder();

            // Steps whose tool is absent are skipped rather than failed, so a contributor with no
            // DCC tools at all still gets every generator and the Unity-side assembly.
            var steps = new (string Name, DccTool? Needs, Func<string> Run)[]
            {
                ("validate", null, Validate),
                ("palette", null, Palette),
                ("detail", null, DetailMaps),
                ("build-props", DccTool.Blender, BuildProps),
                ("export-props", DccTool.Blender, ExportProps),
                // Assembly runs last and needs no external tool, so a project with no DCC tools
                // installed still ends up with materials and prefabs from whatever models exist.
                ("materials", null, Materials),
                ("prefabs", null, Prefabs)
            };

            foreach (var step in steps)
            {
                if (step.Needs.HasValue && !settings.IsUsable(step.Needs.Value))
                {
                    report.AppendLine($"[skip] {step.Name} — {settings.Get(step.Needs.Value).DisplayName} unavailable");
                    continue;
                }

                report.AppendLine($"[run]  {step.Name}");
                report.AppendLine(step.Run());
            }

            var text = report.ToString().TrimEnd();
            Debug.Log(text);
            return text;
        }

        [MenuItem("Tools/DCC Bridge/Blender/Build Prop Sources", true)]
        [MenuItem("Tools/DCC Bridge/Blender/Export Props", true)]
        private static bool BlenderMenuEnabled() => DccBridgeSettings.Instance.IsUsable(DccTool.Blender);

        private static string Log(string message)
        {
            Debug.Log($"[DCC Bridge] {message}");
            return message;
        }

        private static string Report(ToolResult result)
        {
            var text = result.ToString();

            if (result.Ok)
            {
                Debug.Log($"[DCC Bridge]\n{text}");
            }
            else
            {
                Debug.LogError($"[DCC Bridge] {text}");
            }

            return text;
        }
    }
}
