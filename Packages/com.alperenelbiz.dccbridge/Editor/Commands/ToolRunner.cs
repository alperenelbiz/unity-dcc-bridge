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

        public override string ToString() => Ok ? Output : $"{Output}\n{Error}".Trim();
    }

    /// <summary>
    /// Runs Blender headlessly against the consuming project.
    ///
    /// Blender is the only tool invoked as a process. Everything that used to need an external
    /// Python runtime — palette atlases, detail maps, validation — is C# now, so a project with
    /// no DCC tools installed still has a working pipeline. The Python that remains runs inside
    /// Blender's own interpreter, where `bpy` leaves no alternative.
    ///
    /// The scripts live under Tools~ in the package; the trailing tilde keeps Unity from
    /// importing them, which is what lets a Python toolchain ship inside a UPM package.
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

                // Embedded in Packages/ while developing the package itself.
                var fallback = Path.Combine(ProjectRoot, "Packages", "com.alperenelbiz.dccbridge");
                return Directory.Exists(fallback) ? fallback : null;
            }
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

        /// <summary>
        /// Runs one of the package's Blender scripts with the project's props manifest.
        /// </summary>
        public static ToolResult RunBlender(string scriptName, params string[] extra)
        {
            if (!Require(DccTool.Blender, out var failure))
            {
                return failure;
            }

            var packageRoot = PackageRoot;
            if (packageRoot == null)
            {
                return new ToolResult(false, string.Empty,
                    "DCC Bridge: the package's Tools~ folder could not be located.");
            }

            var script = Path.Combine(packageRoot, "Tools~", "blender", scriptName);
            if (!File.Exists(script))
            {
                return new ToolResult(false, string.Empty, $"script not found: {script}");
            }

            var blender = DccBridgeSettings.Instance.PathFor(DccTool.Blender);
            var manifest = Path.Combine(ProjectRoot, "data", "props.json");

            var arguments = new StringBuilder();
            // --factory-startup makes a run reproducible by ignoring personal prefs and add-ons.
            arguments.Append("--background --factory-startup ");
            arguments.Append($"--python \"{script}\" -- ");
            arguments.Append($"--job \"{manifest}\" ");
            arguments.Append($"--project-root \"{ProjectRoot}\"");

            foreach (var token in extra)
            {
                arguments.Append(' ').Append(token);
            }

            return Execute(blender, arguments.ToString());
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
                    return new ToolResult(false, Filter(stdout), "timed out");
                }

                if (process.ExitCode != 0)
                {
                    return new ToolResult(false, Filter(stdout), stderr.Trim());
                }

                AssetDatabase.Refresh();
                return new ToolResult(true, Filter(stdout), string.Empty);
            }
            catch (Exception e)
            {
                Debug.LogError($"[DCC Bridge] {executable} threw: {e.Message}");
                return new ToolResult(false, string.Empty, e.Message);
            }
        }

        /// <summary>Blender is noisy on stdout; only the scripts' own tagged lines are useful.</summary>
        private static string Filter(string stdout)
        {
            var kept = new StringBuilder();
            foreach (var line in stdout.Split('\n'))
            {
                var trimmed = line.TrimEnd();
                if (trimmed.StartsWith("[export]") || trimmed.StartsWith("[build]") ||
                    trimmed.TrimStart().StartsWith("warn:") || trimmed.TrimStart().StartsWith("error:"))
                {
                    kept.AppendLine(trimmed);
                }
            }

            return kept.ToString().TrimEnd();
        }
    }
}
