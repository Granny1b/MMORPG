using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MMORPGGranny.EditorTools
{
    /// <summary>
    /// A bookmark list of scenes, each with a "Start" button that opens the scene and enters play
    /// mode in one click. Opened from <c>Tools/Scene Shortcuts</c>.
    ///
    /// The list is stored through <see cref="EditorUserSettings"/>
    /// (<c>ProjectSettings/EditorUserSettings.asset</c>) rather than <c>EditorPrefs</c>: that file
    /// is per-project, so the key cannot collide with another project's, and it survives editor
    /// restarts without being committed to git.
    ///
    /// Entries are scene GUIDs, not paths, so moving or renaming a scene keeps its shortcut.
    /// </summary>
    public class SceneShortcutsWindow : EditorWindow
    {
        private const string ConfigKey = "SceneShortcuts.Guids";

        private List<string> _guids = new List<string>();
        private Vector2 _scroll;

        [MenuItem("Tools/Scene Shortcuts")]
        private static void Open()
        {
            GetWindow<SceneShortcutsWindow>("Scenes");
        }

        private void OnEnable()
        {
            string stored = EditorUserSettings.GetConfigValue(ConfigKey);
            _guids = string.IsNullOrEmpty(stored)
                ? new List<string>()
                : new List<string>(stored.Split(','));
        }

        private void Save()
        {
            EditorUserSettings.SetConfigValue(ConfigKey, string.Join(",", _guids));
        }

        private void OnGUI()
        {
            // Opening a scene mid-play would be thrown away as soon as play mode exits.
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                _scroll = EditorGUILayout.BeginScrollView(_scroll);

                int removeAt = -1;
                for (int i = 0; i < _guids.Count; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(_guids[i]);
                    SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);

                    EditorGUILayout.BeginHorizontal();

                    SceneAsset picked = (SceneAsset)EditorGUILayout.ObjectField(scene, typeof(SceneAsset), false);
                    if (picked != scene)
                    {
                        _guids[i] = picked == null
                            ? string.Empty
                            : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(picked));
                        Save();
                    }

                    using (new EditorGUI.DisabledScope(scene == null))
                    {
                        // Deferred: opening a scene from inside OnGUI breaks the layout groups
                        // these calls are still nested in.
                        string scenePath = path;

                        if (GUILayout.Button("Open", GUILayout.Width(50)))
                            EditorApplication.delayCall += () => OpenScene(scenePath);

                        if (GUILayout.Button("Play", GUILayout.Width(50)))
                            EditorApplication.delayCall += () => PlayScene(scenePath);
                    }

                    if (GUILayout.Button("X", GUILayout.Width(22)))
                        removeAt = i;

                    EditorGUILayout.EndHorizontal();
                }

                if (removeAt >= 0)
                {
                    _guids.RemoveAt(removeAt);
                    Save();
                }

                EditorGUILayout.EndScrollView();

                if (GUILayout.Button("Add Scene"))
                {
                    _guids.Add(string.Empty);
                    Save();
                }
            }
        }

        /// <summary>Returns false when the user cancelled the "save modified scenes" prompt.</summary>
        private static bool OpenScene(string scenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return false;
            EditorSceneManager.OpenScene(scenePath);
            return true;
        }

        private static void PlayScene(string scenePath)
        {
            if (OpenScene(scenePath))
                EditorApplication.isPlaying = true;
        }
    }
}
