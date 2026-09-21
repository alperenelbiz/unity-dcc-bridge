using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// Finds the external tools on this machine.
    ///
    /// Everything is probed by absolute path rather than by name on PATH. The Editor is usually
    /// launched from a desktop shell and inherits a minimal environment, and with
    /// UseShellExecute = false .NET resolves an executable against the *parent* process PATH
    /// before any environment set on the child applies — so a plain "blender" would simply not
    /// be found even when it is installed and on the user's interactive PATH.
    /// </summary>
    public static class ToolDiscovery
    {
        private const int ProbeTimeoutMs = 4000;

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private static IEnumerable<string> Candidates(DccTool tool)
        {
            switch (tool)
            {
                case DccTool.Blender:
                    // Steam installs land outside /Applications, where mdfind and `which` miss them.
                    yield return "/Applications/Blender.app/Contents/MacOS/Blender";
                    yield return Path.Combine(Home, "Library/Application Support/Steam/steamapps/common/Blender/Blender.app/Contents/MacOS/Blender");
                    yield return "/usr/local/bin/blender";
                    yield return "/opt/homebrew/bin/blender";
                    foreach (var version in new[] { "5.2", "5.1", "5.0", "4.5", "4.2" })
                    {
                        yield return $@"C:\Program Files\Blender Foundation\Blender {version}\blender.exe";
                        yield return Path.Combine(Home, $@"scoop\apps\blender\current\blender.exe");
                    }
                    break;

                case DccTool.Python:
                    yield return Path.Combine(Home, ".local/bin/uv");
                    yield return "/opt/homebrew/bin/uv";
                    yield return "/usr/local/bin/uv";
                    yield return Path.Combine(Home, ".cargo/bin/uv");
                    yield return Path.Combine(Home, @"AppData\Local\Programs\uv\uv.exe");
                    yield return Path.Combine(Home, @".local\bin\uv.exe");
                    break;

                case DccTool.Photoshop:
                    // Photoshop is driven through a plugin proxy, not by launching a binary, so
                    // presence of the application is what is probed here.
                    foreach (var year in new[] { "2027", "2026", "2025", "2024" })
                    {
                        yield return $"/Applications/Adobe Photoshop {year}/Adobe Photoshop {year}.app";
                        yield return $@"C:\Program Files\Adobe\Adobe Photoshop {year}\Photoshop.exe";
                    }
                    break;
            }
        }

        /// <summary>Fills in Detected / Version / Problem. Never changes what the user chose.</summary>
        public static void Probe(DccToolState state)
        {
            state.Detected = false;
            state.Version = string.Empty;
            state.Problem = string.Empty;

            var path = !string.IsNullOrEmpty(state.Path) && Exists(state.Path)
                ? state.Path
                : Candidates(state.Tool).FirstOrDefault(Exists);

            if (path == null)
            {
                state.Problem = string.IsNullOrEmpty(state.Path)
                    ? "Not found in the usual install locations. Set the path manually."
                    : $"Nothing at the configured path: {state.Path}";
                return;
            }

            state.Path = path;
            state.Detected = true;
            state.Version = ReadVersion(state.Tool, path);
        }

        private static bool Exists(string path) =>
            !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

        private static string ReadVersion(DccTool tool, string path)
        {
            switch (tool)
            {
                case DccTool.Blender:
                    return FirstLine(path, "--version");

                case DccTool.Python:
                    return FirstLine(path, "--version");

                case DccTool.Photoshop:
                    // An .app bundle cannot be run for --version; the folder name carries the year.
                    var name = Path.GetFileNameWithoutExtension(path.TrimEnd('/'));
                    return string.IsNullOrEmpty(name) ? "installed" : name;

                default:
                    return string.Empty;
            }
        }

        private static string FirstLine(string executable, string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null)
                {
                    return string.Empty;
                }

                var output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(ProbeTimeoutMs))
                {
                    process.Kill();
                    return string.Empty;
                }

                return output.Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DCC Bridge] could not read a version from {executable}: {e.Message}");
                return string.Empty;
            }
        }
    }
}
