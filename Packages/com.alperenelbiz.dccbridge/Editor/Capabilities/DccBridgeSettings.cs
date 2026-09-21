using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// Which tools this project uses, and where they live on this machine.
    ///
    /// The two are stored separately on purpose:
    ///
    ///   ProjectSettings/DccBridge.json  which tools the project expects  — committed, shared
    ///   EditorPrefs                     where they are installed         — per machine, never shared
    ///
    /// Committing a teammate's Blender path would break the project for everyone else, and every
    /// contributor having to re-pick which tools the project uses would make the setting useless.
    /// </summary>
    [Serializable]
    public class DccBridgeSettings
    {
        private const string SettingsFile = "ProjectSettings/DccBridge.json";
        private const string PathPrefKey = "AlperenElbiz.DccBridge.ToolPath.";
        private const string ConfiguredPrefKey = "AlperenElbiz.DccBridge.Configured";

        [SerializeField] private List<DccToolState> tools = new();

        [SerializeField]
        private bool configured;

        private static DccBridgeSettings instance;

        public static DccBridgeSettings Instance => instance ??= Load();

        /// <summary>True once the user has been through setup, even if they enabled nothing.</summary>
        public bool Configured => configured;

        public IReadOnlyList<DccToolState> Tools => tools;

        public DccToolState Get(DccTool tool) =>
            tools.FirstOrDefault(t => t.Tool == tool) ?? Add(tool);

        public bool IsUsable(DccTool tool) => Get(tool).Usable;

        /// <summary>
        /// The path to a usable tool, or null. Callers should report the tool as unavailable
        /// rather than falling back to a bare executable name, which would not resolve anyway.
        /// </summary>
        public string PathFor(DccTool tool)
        {
            var state = Get(tool);
            return state.Usable ? state.Path : null;
        }

        private DccToolState Add(DccTool tool)
        {
            var state = new DccToolState { Tool = tool };
            tools.Add(state);
            return state;
        }

        public void ProbeAll()
        {
            foreach (DccTool tool in Enum.GetValues(typeof(DccTool)))
            {
                ToolDiscovery.Probe(Get(tool));
            }
        }

        /// <summary>Turn on every tool that was actually found. Used by the first-run wizard.</summary>
        public void EnableEverythingDetected()
        {
            ProbeAll();
            foreach (var state in tools)
            {
                state.Enabled = state.Detected;
            }
        }

        public void MarkConfigured()
        {
            configured = true;
            Save();
        }

        private static DccBridgeSettings Load()
        {
            var loaded = new DccBridgeSettings();
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SettingsFile));

            if (File.Exists(path))
            {
                try
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(path), loaded);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DCC Bridge] {SettingsFile} could not be read ({e.Message}); starting from defaults");
                }
            }

            foreach (DccTool tool in Enum.GetValues(typeof(DccTool)))
            {
                var state = loaded.Get(tool);
                // Paths live per machine, so they are restored from EditorPrefs rather than the file.
                state.Path = EditorPrefs.GetString(PathPrefKey + tool, string.Empty);
            }

            loaded.configured = loaded.configured && EditorPrefs.GetBool(ConfiguredPrefKey, false);
            loaded.ProbeAll();
            return loaded;
        }

        public void Save()
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SettingsFile));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Blank the machine-specific paths before writing the shared file, then restore them.
            var saved = tools.ToDictionary(t => t.Tool, t => t.Path);
            foreach (var state in tools)
            {
                EditorPrefs.SetString(PathPrefKey + state.Tool, state.Path ?? string.Empty);
                state.Path = string.Empty;
            }

            File.WriteAllText(path, JsonUtility.ToJson(this, true));

            foreach (var state in tools)
            {
                state.Path = saved[state.Tool];
            }

            EditorPrefs.SetBool(ConfiguredPrefKey, configured);
        }

        internal static void Reload() => instance = null;
    }
}
