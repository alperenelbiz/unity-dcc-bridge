using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// Runs ExtendScript inside a live Photoshop and returns what the script evaluated to.
    ///
    /// Photoshop has no remote-control server of its own, so it is scripted through the OS:
    /// AppleScript on macOS, the COM automation object on Windows. Both are Adobe-supported and
    /// need nothing installed beyond Photoshop itself — deliberately not a third-party plugin
    /// bridge, which would make the package depend on software most users do not have.
    ///
    /// Photoshop must be open. It is a GUI application and cannot be driven headlessly, which is
    /// why it belongs to authoring one of a kind rather than producing a thousand.
    /// </summary>
    public static class PhotoshopBridge
    {
        private const int TimeoutMs = 3 * 60 * 1000;
        private const string BundleId = "com.adobe.Photoshop";

        /// <summary>
        /// Evaluates ExtendScript and returns its final expression as a string.
        ///
        /// The script is written to a temporary file rather than inlined: quoting a multi-line
        /// program through AppleScript and a shell is a reliable way to corrupt it.
        /// </summary>
        public static ToolResult Evaluate(string extendScript)
        {
            if (!ToolRunner.Require(DccTool.Photoshop, out var failure))
            {
                return failure;
            }

            var scriptPath = Path.Combine(Path.GetTempPath(),
                $"dccbridge_{DateTime.UtcNow.Ticks}.jsx");

            try
            {
                File.WriteAllText(scriptPath, extendScript);
                return Application.platform == RuntimePlatform.WindowsEditor
                    ? RunWindows(scriptPath)
                    : RunMac(scriptPath);
            }
            catch (Exception e)
            {
                return new ToolResult(false, string.Empty, e.Message);
            }
            finally
            {
                try
                {
                    File.Delete(scriptPath);
                }
                catch (Exception)
                {
                    // A leftover temp file is not worth failing the command over.
                }
            }
        }

        private static ToolResult RunMac(string scriptPath)
        {
            var applescript =
                $"tell application id \"{BundleId}\" to do javascript file \"{scriptPath}\"";
            return Execute("/usr/bin/osascript", $"-e '{applescript.Replace("'", "'\\''")}'");
        }

        private static ToolResult RunWindows(string scriptPath)
        {
            // COM is the only scripting entry point on Windows; PowerShell is the least
            // awkward way to reach it without shipping an interop assembly.
            var command =
                "$ps = New-Object -ComObject Photoshop.Application; " +
                $"$ps.DoJavaScriptFile('{scriptPath.Replace("'", "''")}')";

            return Execute("powershell.exe",
                $"-NoProfile -NonInteractive -Command \"{command.Replace("\"", "\\\"")}\"");
        }

        private static ToolResult Execute(string executable, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new ToolResult(false, string.Empty, $"could not start {executable}");
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            if (!process.WaitForExit(TimeoutMs))
            {
                process.Kill();
                return new ToolResult(false, stdout.Trim(),
                    "Photoshop did not respond. A modal dialog in the application blocks scripting.");
            }

            if (process.ExitCode != 0)
            {
                return new ToolResult(false, stdout.Trim(), stderr.Trim());
            }

            return new ToolResult(true, stdout.Trim(), string.Empty);
        }

        /// <summary>Escapes a path for embedding in an ExtendScript string literal.</summary>
        internal static string Escape(string value) =>
            (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>Reports which document is open and how many layers it has.</summary>
        public static ToolResult Describe()
        {
            const string script = @"
var out;
if (app.documents.length === 0) {
    out = 'no document open';
} else {
    var doc = app.activeDocument;
    out = doc.name + ' | ' + doc.width.as('px') + 'x' + doc.height.as('px')
        + ' | layers:' + doc.layers.length;
}
out;";
            return Evaluate(script);
        }

        /// <summary>
        /// Exports each top-level layer of the open document as its own PNG.
        ///
        /// This is the handoff the pipeline is built around: Photoshop authors a template once,
        /// its layers become flat images, and a generator composites from them thousands of times
        /// with Photoshop closed.
        /// </summary>
        public static ToolResult ExportLayers(string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            var script = new StringBuilder();
            script.AppendLine("if (app.documents.length === 0) { 'no document open'; } else {");
            script.AppendLine($"var dir = \"{Escape(outputDir)}\";");
            script.AppendLine(@"
var doc = app.activeDocument;
var saved = [];

// Remember what was visible so the artist's document is left exactly as found.
var was = [];
for (var i = 0; i < doc.layers.length; i++) { was[i] = doc.layers[i].visible; }
for (var i = 0; i < doc.layers.length; i++) { doc.layers[i].visible = false; }

var options = new PNGSaveOptions();
options.compression = 6;
options.interlaced = false;

for (var i = 0; i < doc.layers.length; i++) {
    var layer = doc.layers[i];
    layer.visible = true;

    var safe = layer.name.replace(/[^A-Za-z0-9_\-]/g, '_');
    var file = new File(dir + '/' + safe + '.png');
    doc.saveAs(file, options, true, Extension.LOWERCASE);
    saved.push(safe + '.png');

    layer.visible = false;
}

for (var i = 0; i < doc.layers.length; i++) { doc.layers[i].visible = was[i]; }

saved.length + ' layer(s): ' + saved.join(', ');
}");
            return Evaluate(script.ToString());
        }
    }
}
