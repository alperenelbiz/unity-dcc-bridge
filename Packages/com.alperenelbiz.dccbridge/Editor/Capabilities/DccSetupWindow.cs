using UnityEditor;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// First-run setup: pick the tools you actually have.
    ///
    /// Nothing is assumed to be installed. Every generator checks its tool before running and
    /// reports it as unavailable rather than failing part-way through, so a contributor with only
    /// Unity gets a working — if smaller — pipeline instead of a broken one.
    /// </summary>
    public class DccSetupWindow : EditorWindow
    {
        private DccBridgeSettings settings;
        private Vector2 scroll;

        [MenuItem("Tools/DCC Bridge/Setup", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<DccSetupWindow>(true, "DCC Bridge Setup");
            window.minSize = new Vector2(460, 460);
            window.settings = DccBridgeSettings.Instance;
            window.settings.ProbeAll();
            window.Show();
        }

        /// <summary>Opens once on a project that has never been set up, after the Editor settles.</summary>
        [InitializeOnLoadMethod]
        private static void MaybePromptOnFirstRun()
        {
            EditorApplication.delayCall += () =>
            {
                if (!DccBridgeSettings.Instance.Configured && !Application.isBatchMode)
                {
                    Open();
                }
            };
        }

        private void OnEnable() => settings ??= DccBridgeSettings.Instance;

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Which tools do you have?", EditorStyles.largeLabel);
            EditorGUILayout.LabelField(
                "Turn on only what you can run. Everything else stays hidden, and no feature that " +
                "needs a missing tool will be offered.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(8);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (DccToolsGUI.Draw(settings))
            {
                settings.Save();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Detect everything", GUILayout.Height(26)))
            {
                settings.EnableEverythingDetected();
                settings.Save();
            }

            GUI.backgroundColor = new Color(0.45f, 0.72f, 0.50f);
            if (GUILayout.Button("Done", GUILayout.Height(26)))
            {
                settings.MarkConfigured();
                Close();
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "Changeable later in Project Settings > DCC Bridge.",
                EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space(4);
        }
    }
}
