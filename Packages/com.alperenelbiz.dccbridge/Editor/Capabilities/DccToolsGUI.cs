using UnityEditor;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// The tool list, drawn identically in the setup window and in Project Settings so there is
    /// one place to reason about how a tool's state is presented.
    /// </summary>
    internal static class DccToolsGUI
    {
        public static bool Draw(DccBridgeSettings settings)
        {
            var changed = false;

            foreach (var state in settings.Tools)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                var enabled = EditorGUILayout.ToggleLeft(
                    new GUIContent($"  {state.DisplayName}", state.Purpose),
                    state.Enabled,
                    EditorStyles.boldLabel);
                if (EditorGUI.EndChangeCheck())
                {
                    state.Enabled = enabled;
                    changed = true;
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label(StatusLabel(state), StatusStyle(state));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(state.Purpose, EditorStyles.miniLabel);

                if (state.Enabled)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.BeginChangeCheck();
                    var path = EditorGUILayout.TextField("Path", state.Path ?? string.Empty);
                    if (EditorGUI.EndChangeCheck())
                    {
                        state.Path = path;
                        ToolDiscovery.Probe(state);
                        changed = true;
                    }

                    if (GUILayout.Button("Browse", GUILayout.Width(70)))
                    {
                        var picked = EditorUtility.OpenFilePanel($"Locate {state.DisplayName}", "/", "");
                        if (!string.IsNullOrEmpty(picked))
                        {
                            state.Path = picked;
                            ToolDiscovery.Probe(state);
                            changed = true;
                        }
                    }

                    if (GUILayout.Button("Detect", GUILayout.Width(60)))
                    {
                        state.Path = string.Empty;
                        ToolDiscovery.Probe(state);
                        changed = true;
                    }

                    EditorGUILayout.EndHorizontal();

                    if (!string.IsNullOrEmpty(state.Problem))
                    {
                        EditorGUILayout.HelpBox(state.Problem, MessageType.Warning);
                    }
                    else if (!string.IsNullOrEmpty(state.Version))
                    {
                        EditorGUILayout.LabelField(" ", state.Version, EditorStyles.miniLabel);
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            return changed;
        }

        private static string StatusLabel(DccToolState state)
        {
            if (!state.Enabled)
            {
                return state.Detected ? "off (installed)" : "off";
            }

            return state.Detected ? "ready" : "missing";
        }

        private static GUIStyle StatusStyle(DccToolState state)
        {
            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = !state.Enabled
                ? Color.gray
                : state.Detected
                    ? new Color(0.35f, 0.72f, 0.42f)
                    : new Color(0.85f, 0.55f, 0.25f);
            return style;
        }
    }
}
