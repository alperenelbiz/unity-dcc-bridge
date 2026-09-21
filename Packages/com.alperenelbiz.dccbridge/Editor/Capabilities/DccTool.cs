using System;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>An external authoring tool the pipeline can drive. Unity itself is always present.</summary>
    public enum DccTool
    {
        /// <summary>Headless Blender: modelling, export, validation.</summary>
        Blender,

        /// <summary>Photoshop through a local plugin proxy: template authoring and layer export.</summary>
        Photoshop,

        /// <summary>A Python 3 runtime via uv, used by every generator that runs outside Unity.</summary>
        Python
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

        /// <summary>Version string when discovery could read one.</summary>
        [NonSerialized] public string Version = string.Empty;

        /// <summary>Why the tool is unusable, when it is.</summary>
        [NonSerialized] public string Problem = string.Empty;

        /// <summary>Enabled, present, and with no reported problem.</summary>
        public bool Usable => Enabled && Detected && string.IsNullOrEmpty(Problem);

        public string DisplayName => Tool switch
        {
            DccTool.Blender => "Blender",
            DccTool.Photoshop => "Photoshop",
            DccTool.Python => "Python (uv)",
            _ => Tool.ToString()
        };

        public string Purpose => Tool switch
        {
            DccTool.Blender => "Builds and exports 3D props, validates them before they reach Unity",
            DccTool.Photoshop => "Authors texture templates and exports their layers",
            DccTool.Python => "Runs the generators: palette atlases, detail maps, validation",
            _ => string.Empty
        };
    }
}
