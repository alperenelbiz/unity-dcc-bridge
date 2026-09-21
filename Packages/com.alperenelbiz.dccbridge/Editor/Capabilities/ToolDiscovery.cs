using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
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
        private const int ConnectTimeoutMs = 250;

        private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private static readonly string[] AdobeYears = { "2027", "2026", "2025", "2024" };

        // Substance lagged the others for years, so it needs a longer tail — and an empty
        // entry for installs that carry no year in the folder name.
        private static readonly string[] SubstanceVersions =
            { "2027", "2026", "2025", "2024", "2023", "2022", "" };

        private static IEnumerable<string> Candidates(DccTool tool)
        {
            switch (tool)
            {
                case DccTool.Blender:
                    yield return "/Applications/Blender.app/Contents/MacOS/Blender";
                    // Steam installs land outside /Applications, where mdfind and `which` miss them.
                    yield return Path.Combine(Home, "Library/Application Support/Steam/steamapps/common/Blender/Blender.app/Contents/MacOS/Blender");
                    yield return "/usr/local/bin/blender";
                    yield return "/opt/homebrew/bin/blender";
                    foreach (var version in new[] { "5.2", "5.1", "5.0", "4.5", "4.2" })
                    {
                        yield return $@"C:\Program Files\Blender Foundation\Blender {version}\blender.exe";
                    }
                    yield return Path.Combine(Home, @"scoop\apps\blender\current\blender.exe");
                    break;

                case DccTool.Photoshop:
                    foreach (var year in AdobeYears)
                    {
                        yield return $"/Applications/Adobe Photoshop {year}/Adobe Photoshop {year}.app";
                        yield return $@"C:\Program Files\Adobe\Adobe Photoshop {year}\Photoshop.exe";
                    }
                    break;

                case DccTool.SubstancePainter:
                    // Sold both through Adobe and on Steam, and the two name their folders
                    // differently: Adobe uses "Adobe Substance 3D Painter <year>", Steam uses
                    // "Substance 3D Painter <year>" — and sometimes no year at all. Verified
                    // against a real Steam install that a year-less guess would have missed.
                    foreach (var year in SubstanceVersions)
                    {
                        var suffix = year.Length > 0 ? $" {year}" : string.Empty;

                        yield return $"/Applications/Adobe Substance 3D Painter{suffix}/Adobe Substance 3D Painter.app";
                        yield return Path.Combine(Home,
                            $"Library/Application Support/Steam/steamapps/common/Substance 3D Painter{suffix}/Adobe Substance 3D Painter.app");

                        yield return $@"C:\Program Files\Adobe\Adobe Substance 3D Painter{suffix}\Adobe Substance 3D Painter.exe";
                        yield return $@"C:\Program Files (x86)\Steam\steamapps\common\Substance 3D Painter{suffix}\Adobe Substance 3D Painter.exe";
                    }
                    break;
            }
        }

        /// <summary>Fills in Detected / Version / Connected / Problem. Never changes what the user chose.</summary>
        public static void Probe(DccToolState state)
        {
            state.Detected = false;
            state.Connected = false;
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

            if (!state.NeedsConnection)
            {
                return;
            }

            state.Connected = IsListening(state.Port);
            if (!state.Connected)
            {
                state.Problem = ConnectionHint(state.Tool, state.Port);
            }
        }

        private static bool Exists(string path) =>
            !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));

        /// <summary>
        /// Whether something is accepting connections on a local port.
        ///
        /// This is what separates "installed" from "usable" for Photoshop and Substance: both are
        /// driven over a socket and both stay silent unless launched with remote control enabled.
        /// Naming that here beats a timeout later that reads like the tool is broken.
        /// </summary>
        private static bool IsListening(int port)
        {
            if (port <= 0)
            {
                return false;
            }

            try
            {
                using var client = new TcpClient();
                return client.ConnectAsync("127.0.0.1", port).Wait(ConnectTimeoutMs) && client.Connected;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ConnectionHint(DccTool tool, int port) => tool switch
        {
            DccTool.SubstancePainter =>
                $"Installed, but nothing is listening on {port}. Launch Substance with --enable-remote-scripting.",
            DccTool.Photoshop =>
                $"Installed, but nothing is listening on {port}. Start the plugin proxy and check the Photoshop plugin is connected.",
            _ => $"Nothing is listening on {port}."
        };

        private static string ReadVersion(DccTool tool, string path)
        {
            switch (tool)
            {
                case DccTool.Blender:
                    return FirstLine(path, "--version");

                case DccTool.Photoshop:
                case DccTool.SubstancePainter:
                    // An .app bundle cannot be run for --version, and launching a GUI application
                    // just to read one would be worse; the install folder carries the year.
                    var name = Path.GetFileName(path.TrimEnd('/', '\\'));
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
