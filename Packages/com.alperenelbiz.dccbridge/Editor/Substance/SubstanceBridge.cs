using System;
using System.IO;
using System.Net;
using System.Text;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// Runs Python inside a live Substance 3D Painter over its remote scripting server.
    ///
    /// Protocol, established by probing a running instance rather than from documentation
    /// (Adobe's page is not publicly reachable):
    ///
    ///   POST http://localhost:60041/run.json
    ///   {"python": "&lt;base64&gt;"}   or   {"js": "&lt;base64&gt;"}
    ///
    /// Two behaviours matter and are not obvious:
    ///
    ///   * A **single expression** returns its value as JSON. A multi-line script runs but
    ///     always answers `null`, so anything needing a result has to be asked for separately.
    ///   * The interpreter's **globals persist between calls**, which is what makes that split
    ///     workable: run the script, then read the variable it left behind.
    ///
    /// Errors come back as {"error": {"description": "..."}} with HTTP 200, so a failure has to
    /// be detected by inspecting the body rather than the status code.
    /// </summary>
    public static class SubstanceBridge
    {
        private const string Endpoint = "http://localhost:60041/run.json";
        private const int TimeoutMs = 120 * 1000;
        private const string ResultVar = "_dcc_result";

        /// <summary>Evaluates a single Python expression and returns its JSON value.</summary>
        public static ToolResult Evaluate(string expression)
        {
            if (!ToolRunner.Require(DccTool.SubstancePainter, out var failure))
            {
                return failure;
            }

            return Post("python", expression);
        }

        /// <summary>
        /// Runs a multi-line script, then reads back whatever it assigned to <c>_dcc_result</c>.
        ///
        /// Two round trips, because the server will not return a value from a multi-line body.
        /// </summary>
        public static ToolResult Execute(string script)
        {
            if (!ToolRunner.Require(DccTool.SubstancePainter, out var failure))
            {
                return failure;
            }

            // Clear any result left by an earlier call, so a script that fails before assigning
            // cannot be reported with a stale success value.
            var reset = Post("python", $"{ResultVar} = None");
            if (!reset.Ok)
            {
                return reset;
            }

            var run = Post("python", script);
            if (!run.Ok)
            {
                return run;
            }

            return Post("python", ResultVar);
        }

        private static ToolResult Post(string language, string code)
        {
            try
            {
                var payload = Encoding.UTF8.GetBytes(
                    $"{{\"{language}\": \"{Convert.ToBase64String(Encoding.UTF8.GetBytes(code))}\"}}");

                var request = (HttpWebRequest)WebRequest.Create(Endpoint);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Timeout = TimeoutMs;
                request.ReadWriteTimeout = TimeoutMs;
                request.ContentLength = payload.Length;

                using (var stream = request.GetRequestStream())
                {
                    stream.Write(payload, 0, payload.Length);
                }

                using var response = (HttpWebResponse)request.GetResponse();
                using var reader = new StreamReader(response.GetResponseStream()!);
                var body = reader.ReadToEnd().Trim();

                // A script error arrives with HTTP 200 and an error object in the body.
                if (body.Contains("\"error\""))
                {
                    return new ToolResult(false, string.Empty, Describe(body));
                }

                return new ToolResult(true, Unquote(body), string.Empty);
            }
            catch (WebException e)
            {
                return new ToolResult(false, string.Empty,
                    $"Substance did not answer on {Endpoint}. Launch it with --enable-remote-scripting. ({e.Message})");
            }
            catch (Exception e)
            {
                return new ToolResult(false, string.Empty, e.Message);
            }
        }

        /// <summary>Pulls the human-readable part out of the server's error object.</summary>
        private static string Describe(string body)
        {
            const string key = "\"description\"";
            var start = body.IndexOf(key, StringComparison.Ordinal);
            if (start < 0)
            {
                return body;
            }

            var open = body.IndexOf('"', body.IndexOf(':', start) + 1);
            if (open < 0)
            {
                return body;
            }

            var text = new StringBuilder();
            for (var i = open + 1; i < body.Length && body[i] != '"'; i++)
            {
                if (body[i] == '\\' && i + 1 < body.Length)
                {
                    text.Append(body[++i] switch { 'n' => '\n', 't' => '\t', var c => c });
                    continue;
                }

                text.Append(body[i]);
            }

            return text.ToString().Trim();
        }

        private static string Unquote(string value) =>
            value.Length >= 2 && value[0] == '"' && value[^1] == '"'
                ? value[1..^1].Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\")
                : value;

        internal static string Escape(string value) =>
            (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary>API version and whether a project is open.</summary>
        public static ToolResult Describe() => Execute(@"
import substance_painter as sp
import substance_painter.project as project

if project.is_open():
    _dcc_result = 'API %s | project: %s' % (sp.__version__, project.file_path())
else:
    _dcc_result = 'API %s | no project open' % sp.__version__
");

        /// <summary>
        /// Exports the open project's texture sets with a named preset.
        ///
        /// The preset is a resource inside Substance's own shelf, so it is passed by name rather
        /// than hard-coded: which presets exist depends on the user's shelf, and Unity's
        /// metallic-smoothness layout is not the same as the default metallic-roughness one.
        /// </summary>
        public static ToolResult ExportTextures(string outputDir, string presetName)
        {
            Directory.CreateDirectory(outputDir);

            var script = $@"
import substance_painter.export as export
import substance_painter.project as project
import substance_painter.resource as resource
import substance_painter.textureset as textureset

if not project.is_open():
    _dcc_result = 'no project open'
else:
    preset = resource.ResourceID(context='starter_assets', name='{Escape(presetName)}')
    config = {{
        'exportShaderParams': False,
        'exportPath': '{Escape(outputDir)}',
        'defaultExportPreset': preset.url(),
        'exportPresets': [{{'name': 'default', 'maps': []}}],
        'exportList': [{{'rootPath': str(s.name())}} for s in textureset.all_texture_sets()],
        'exportParameters': [{{'parameters': {{'paddingAlgorithm': 'infinite'}}}}],
    }}
    result = export.export_project_textures(config)
    written = [f for files in result.textures.values() for f in files]
    _dcc_result = '%d texture(s) -> %s' % (len(written), '{Escape(outputDir)}')
";
            return Execute(script);
        }
    }
}
