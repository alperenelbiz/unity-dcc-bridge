using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>Project Settings > DCC Bridge — the same tool list, reachable at any time.</summary>
    internal static class DccSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider("Project/DCC Bridge", SettingsScope.Project)
            {
                label = "DCC Bridge",
                keywords = new HashSet<string> { "blender", "photoshop", "python", "uv", "pipeline", "dcc", "asset" },
                activateHandler = (_, __) => DccBridgeSettings.Instance.ProbeAll(),
                guiHandler = _ =>
                {
                    var settings = DccBridgeSettings.Instance;

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField(
                        "Only the tools enabled here are used. Paths are stored per machine and " +
                        "are never committed; which tools the project expects is shared with the team.",
                        EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.Space(6);

                    if (DccToolsGUI.Draw(settings))
                    {
                        settings.Save();
                    }

                    EditorGUILayout.Space(8);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Re-detect", GUILayout.Width(100)))
                    {
                        settings.ProbeAll();
                    }

                    if (GUILayout.Button("Open setup window", GUILayout.Width(150)))
                    {
                        DccSetupWindow.Open();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            };
        }
    }
}
