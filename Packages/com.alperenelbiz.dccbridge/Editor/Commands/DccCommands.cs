using System.Linq;
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
    /// Every command that needs an external tool checks for it and returns a readable message
    /// when it is missing, rather than failing inside the toolchain.
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
                var status = state.Usable ? "ready"
                    : !state.Enabled ? "disabled"
                    : "unavailable";

                report.AppendLine($"  {state.DisplayName,-14} {status,-12} {state.Version}");
                if (state.Enabled && !string.IsNullOrEmpty(state.Problem))
                {
                    report.AppendLine($"    {state.Problem}");
                }
            }

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
        public static string Palette() => Report(ToolRunner.RunPipeline("palette"));

        [CliCommand("dcc_detail", "Generate seamless tiling detail normal maps.",
            Tags = new[] { "dcc", "dcc/generate" })]
        [MenuItem("Tools/DCC Bridge/Generate/Detail Maps", priority = 21)]
        public static string DetailMaps() => Report(ToolRunner.RunPipeline("detail"));

        [CliCommand("dcc_validate", "Check manifests and asset naming conventions. Needs no DCC tool beyond Python.",
            Tags = new[] { "dcc" })]
        [MenuItem("Tools/DCC Bridge/Validate", priority = 30)]
        public static string Validate() => Report(ToolRunner.RunPipeline("validate"));

        [CliCommand("dcc_build_props", "Build prop .blend sources from data/props.json using headless Blender.",
            Tags = new[] { "dcc", "dcc/blender" })]
        [MenuItem("Tools/DCC Bridge/Blender/Build Prop Sources", priority = 40)]
        public static string BuildProps()
        {
            return !ToolRunner.Require(DccTool.Blender, out var failure)
                ? Report(failure)
                : Report(ToolRunner.RunPipeline("build-props"));
        }

        [CliCommand("dcc_export_props", "Export prop FBX from .blend sources into Assets/Art/Models, with validation.",
            Tags = new[] { "dcc", "dcc/blender" })]
        [MenuItem("Tools/DCC Bridge/Blender/Export Props", priority = 41)]
        public static string ExportProps()
        {
            return !ToolRunner.Require(DccTool.Blender, out var failure)
                ? Report(failure)
                : Report(ToolRunner.RunPipeline("export-props"));
        }

        [CliCommand("dcc_run_all", "Run every step available with the tools this project has enabled.",
            Tags = new[] { "dcc" })]
        [MenuItem("Tools/DCC Bridge/Run Everything", priority = 50)]
        public static string RunAll()
        {
            var settings = DccBridgeSettings.Instance;
            var report = new StringBuilder();

            // Steps are skipped rather than failed when their tool is absent, so a contributor
            // with only Unity still gets everything that does not need Blender or Photoshop.
            var steps = new (string Name, DccTool? Needs, System.Func<string> Run)[]
            {
                ("validate", DccTool.Python, Validate),
                ("palette", DccTool.Python, Palette),
                ("detail", DccTool.Python, DetailMaps),
                ("build-props", DccTool.Blender, BuildProps),
                ("export-props", DccTool.Blender, ExportProps)
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

        [MenuItem("Tools/DCC Bridge/Generate/Palette Atlas", true)]
        [MenuItem("Tools/DCC Bridge/Generate/Detail Maps", true)]
        [MenuItem("Tools/DCC Bridge/Validate", true)]
        private static bool PythonMenuEnabled() => DccBridgeSettings.Instance.IsUsable(DccTool.Python);

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
