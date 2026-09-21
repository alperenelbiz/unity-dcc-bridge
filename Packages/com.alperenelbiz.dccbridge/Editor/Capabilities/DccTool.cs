using System;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>An external authoring tool the pipeline can drive. Unity itself is always present.</summary>
    /// <summary>
    /// Values are explicit and never reused. JsonUtility serialises an enum by its integer, so
    /// renumbering would silently reassign every already-saved project's settings — including 2,
    /// which was Python before its generators moved into C# in 0.3.0.
    /// </summary>
    public enum DccTool
    {
        /// <summary>Headless Blender: prop authoring, export and validation.</summary>
        Blender = 0,

        /// <summary>Photoshop through a local plugin proxy: template authoring and layer export.</summary>
        Photoshop = 1,

        // 2 was Python (uv). Removed in 0.3.0 — the generators it ran are now C#.

        /// <summary>Substance 3D Painter, driven through its remote scripting port.</summary>
        SubstancePainter = 3
    }

    /// <summary>
    /// What the project knows about one tool.
    ///
    /// <see cref="Enabled"/> and <see cref="Detected"/> are deliberately separate. A tool can be
    /// installed but switched off because this contributor does not use it, and it can be enabled
    /// but missing on this machine — which is the case worth reporting clearly rather than failing
    /// somewhere deep in a build.
    /// </summary>
    [Serializable]
    public class DccToolState
    {
        public DccTool Tool;

        /// <summary>The user opted into this tool. Shared with the team.</summary>
        public bool Enabled;

        /// <summary>Executable path. Machine-specific, so it is never shared.</summary>
        public string Path = string.Empty;

        /// <summary>Set by discovery, not by the user.</summary>
        [NonSerialized] public bool Detected;

        /// <summary>
        /// For tools driven over a local port, whether that port is currently accepting
        /// connections. Installed-but-not-listening is the common case and is worth naming:
        /// the application is there, it just was not started with remote control enabled.
        /// </summary>
        [NonSerialized] public bool Connected;

        /// <summary>Version string when discovery could read one.</summary>
        [NonSerialized] public string Version = string.Empty;

        /// <summary>Why the tool is unusable, when it is.</summary>
        [NonSerialized] public string Problem = string.Empty;

        /// <summary>Tools that are driven over a local port rather than by launching a binary.</summary>
        public bool NeedsConnection => Tool is DccTool.Photoshop or DccTool.SubstancePainter;

        /// <summary>The port its remote-control channel listens on, or 0.</summary>
        public int Port => Tool switch
        {
            DccTool.Photoshop => 3001,          // adb-mcp plugin proxy
            DccTool.SubstancePainter => 60041,  // --enable-remote-scripting
            _ => 0
        };

        /// <summary>Enabled, present, and with no reported problem.</summary>
        public bool Usable => Enabled && Detected && string.IsNullOrEmpty(Problem);

        /// <summary>Usable and, for a port-driven tool, actually reachable right now.</summary>
        public bool Ready => Usable && (!NeedsConnection || Connected);

        public string DisplayName => Tool switch
        {
            DccTool.Blender => "Blender",
            DccTool.Photoshop => "Photoshop",
            DccTool.SubstancePainter => "Substance 3D Painter",
            _ => Tool.ToString()
        };

        public string Purpose => Tool switch
        {
            DccTool.Blender => "Builds and exports 3D props, validates them before they reach Unity",
            DccTool.Photoshop => "Authors texture templates and exports their layers",
            DccTool.SubstancePainter => "Paints and exports PBR texture sets for hero props",
            _ => string.Empty
        };
    }
}
