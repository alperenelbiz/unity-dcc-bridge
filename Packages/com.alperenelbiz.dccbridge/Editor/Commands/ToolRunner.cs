using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>The captured outcome of running an external tool.</summary>
    public readonly struct ToolResult
    {
        public readonly bool Ok;
        public readonly string Output;
        public readonly string Error;

        public ToolResult(bool ok, string output, string error)
        {
            Ok = ok;
            Output = output;
            Error = error;
        }

        public override string ToString() =>
            Ok ? Output : $"{Output}\n{Error}".Trim();
    }

    /// <summary>
    /// Runs the package's Python tooling against the consuming project.
    ///
    /// The scripts live under Tools~ inside the package. The trailing tilde keeps Unity from
    /// importing them at all, which is what allows a Python toolchain to ship inside a UPM
    /// package without generating meta files or import errors.
    /// </summary>
    public static class ToolRunner
    {
        private const int DefaultTimeoutMs = 10 * 60 * 1000;

        public static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));

        /// <summary>The package's own folder, wherever the Package Manager put it.</summary>
        public static string PackageRoot
        {
            get
            {
                var info = PackageInfo.FindForAssembly(Assembly.GetExecutingAssembly());
                if (info != null)
                {
                    return info.resolvedPath;
                }

                // Embedded in Packages/ during development of the package itself.
                var fallback = Path.Combine(ProjectRoot, "Packages", "com.alperenelbiz.dccbridge");
                return Directory.Exists(fallback) ? fallback : null;
            }
        }

        private static string PipelineDir
        {
            get
            {
                var root = PackageRoot;
                return root == null ? null : Path.Combine(root, "Tools~", "pipeline");
            }
        }

        /// <summary>
        /// Runs a generator. Reports a missing tool as a clear message instead of failing
        /// somewhere inside the toolchain, which is the whole point of the capability system.
        /// </summary>
        public static ToolResult RunPipeline(string command, params string[] extra)
        {
            var settings = DccBridgeSettings.Instance;

            var uv = settings.PathFor(DccTool.Python);
            if (uv == null)
            {
                return Unavailable(DccTool.Python);
            }

            var pipelineDir = PipelineDir;
            if (pipelineDir == null || !Directory.Exists(pipelineDir))
            {
                return new ToolResult(false, string.Empty,
                    "DCC Bridge: the package's Tools~ folder could not be located.");
            }

            var arguments = new StringBuilder();
            arguments.Append($"run --project \"{pipelineDir}\" python \"{Path.Combine(pipelineDir, "cli.py")}\" ");
            arguments.Append($"--project-root \"{ProjectRoot}\" ");

            // Blender's path is resolved here, where the user's configuration is known, rather
            // than re-discovered inside Python with a different set of guesses.
            var blender = settings.PathFor(DccTool.Blender);
            if (blender != null)
            {
                arguments.Append($"--blender \"{blender}\" ");
            }

            arguments.Append(command);
            foreach (var token in extra)
            {
                arguments.Append(' ').Append(token);
            }

            return Execute(uv, arguments.ToString());
        }

        /// <summary>Guards a command that cannot work without a particular tool.</summary>
        public static bool Require(DccTool tool, out ToolResult failure)
        {
            if (DccBridgeSettings.Instance.IsUsable(tool))
            {
                failure = default;
                return true;
            }

            failure = Unavailable(tool);
            return false;
        }

        private static ToolResult Unavailable(DccTool tool)
        {
            var state = DccBridgeSettings.Instance.Get(tool);
            var reason = !state.Enabled
                ? "it is turned off for this project"
                : string.IsNullOrEmpty(state.Problem)
                    ? "it was not found on this machine"
                    : state.Problem;

            return new ToolResult(false, string.Empty,
                $"{state.DisplayName} is unavailable: {reason}. " +
                "Enable and locate it in Project Settings > DCC Bridge.");
        }

        private static ToolResult Execute(string executable, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = ProjectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return new ToolResult(false, string.Empty, $"could not start {executable}");
                }

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(DefaultTimeoutMs))
                {
                    process.Kill();
                    return new ToolResult(false, stdout, "timed out");
                }

                if (process.ExitCode != 0)
                {
                    return new ToolResult(false, stdout, stderr.Trim());
                }

                AssetDatabase.Refresh();
                return new ToolResult(true, stdout.Trim(), string.Empty);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DCC Bridge] {executable} threw: {e.Message}");
                return new ToolResult(false, string.Empty, e.Message);
            }
        }
    }
}
