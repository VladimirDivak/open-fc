using System.Collections.Generic;
using System.IO;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Registry;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenFarCry.Level.Editor
{
    public sealed class FcLevelBuilderWindow : EditorWindow
    {
        [MenuItem("OpenFarCry/Level Builder")]
        static void Open() => GetWindow<FcLevelBuilderWindow>("Level Builder");

        // ── State ────────────────────────────────────────────────────────────────

        FcEntityPrefabRegistry _registry;
        bool _skipHidden   = true;
        bool _buildBrushes = true;

        List<string> _levelNames = new List<string>();
        List<string> _missionNames = new List<string>();
        int _levelIndex;
        int _missionIndex;

        string _lastBuildStats;
        string _lastError;

        Vector2 _scroll;

        // ── Lifecycle ────────────────────────────────────────────────────────────

        void OnEnable()
        {
            RefreshLevels();
            TryLoadRegistryFromAssets();
        }

        // ── GUI ──────────────────────────────────────────────────────────────────

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Far Cry Level Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawRegistryField();
            EditorGUILayout.Space(4);

            DrawLevelSelector();
            EditorGUILayout.Space(4);

            DrawOptions();
            EditorGUILayout.Space(8);

            DrawActionButtons();
            EditorGUILayout.Space(4);

            DrawStatus();

            EditorGUILayout.EndScrollView();
        }

        void DrawRegistryField()
        {
            EditorGUI.BeginChangeCheck();
            _registry = (FcEntityPrefabRegistry)EditorGUILayout.ObjectField(
                "Entity Prefab Registry", _registry, typeof(FcEntityPrefabRegistry), allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck() && _registry != null)
                _lastError = null;

            if (_registry == null)
            {
                EditorGUILayout.HelpBox("Assign an FcEntityPrefabRegistry asset.", MessageType.Warning);
            }
            else
            {
#if UNITY_EDITOR
                var missing = new List<string>(_registry.GetUnassignedPrefabNames());
                if (missing.Count > 0)
                    EditorGUILayout.HelpBox($"Unassigned prefabs: {string.Join(", ", missing)}", MessageType.Warning);
#endif
            }
        }

        void DrawLevelSelector()
        {
            EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);

            if (_levelNames.Count == 0)
            {
                EditorGUILayout.HelpBox("No levels found. Check game install path in FcFileSystemSettings.", MessageType.Info);
                if (GUILayout.Button("Refresh")) RefreshLevels();
                return;
            }

            EditorGUI.BeginChangeCheck();
            _levelIndex = EditorGUILayout.Popup("Level", _levelIndex, _levelNames.ToArray());
            if (EditorGUI.EndChangeCheck())
                RefreshMissions();

            if (_missionNames.Count > 0)
                _missionIndex = EditorGUILayout.Popup("Mission", _missionIndex, _missionNames.ToArray());

            if (GUILayout.Button("Refresh Level List")) RefreshLevels();
        }

        void DrawOptions()
        {
            _skipHidden   = EditorGUILayout.Toggle("Skip HiddenInGame Entities", _skipHidden);
            _buildBrushes = EditorGUILayout.Toggle("Include Brushes",            _buildBrushes);
        }

        void DrawActionButtons()
        {
            bool canBuild = _registry != null && _levelNames.Count > 0 && _missionNames.Count > 0;

            using (new EditorGUI.DisabledScope(!canBuild))
            {
                if (GUILayout.Button("Build Scene", GUILayout.Height(30)))
                    BuildScene();
            }

            if (GUILayout.Button("Clear Scene"))
                ClearScene();
        }

        void DrawStatus()
        {
            if (!string.IsNullOrEmpty(_lastBuildStats))
            {
                EditorGUILayout.HelpBox(_lastBuildStats, MessageType.Info);
            }

            if (!string.IsNullOrEmpty(_lastError))
            {
                EditorGUILayout.HelpBox(_lastError, MessageType.Error);
            }
        }

        // ── Actions ──────────────────────────────────────────────────────────────

        void BuildScene()
        {
            _lastError = null;
            _lastBuildStats = null;

            string levelName = _levelNames[_levelIndex];
            string missionName = _missionNames[_missionIndex];

            Scene target = EnsureLevelScene(levelName);
            if (!target.IsValid()) { _lastError = "Failed to open/create level scene."; return; }

            FcLevelSceneBuilder.ClearScene(target);

            var stats = FcLevelSceneBuilder.BuildScene(
                levelName, missionName, _registry, target, _skipHidden, _buildBrushes);

            _lastBuildStats = $"Built '{levelName}/{missionName}':\n{stats}";
            Debug.Log($"[FcLevelBuilder] {_lastBuildStats}");
        }

        void ClearScene()
        {
            var active = SceneManager.GetActiveScene();
            FcLevelSceneBuilder.ClearScene(active);
            _lastBuildStats = "Scene cleared.";
            _lastError = null;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        void RefreshLevels()
        {
            _levelNames = new List<string>(FcLevelLoader.ListLevelNames());
            _levelIndex = 0;
            RefreshMissions();
        }

        void RefreshMissions()
        {
            _missionNames.Clear();
            if (_levelNames.Count == 0) return;
            string level = _levelNames[_levelIndex];
            _missionNames = new List<string>(FcLevelLoader.ListMissionNames(level));
            _missionIndex = 0;
        }

        void TryLoadRegistryFromAssets()
        {
            if (_registry != null) return;
            var guids = AssetDatabase.FindAssets("t:FcEntityPrefabRegistry");
            if (guids.Length > 0)
                _registry = AssetDatabase.LoadAssetAtPath<FcEntityPrefabRegistry>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        static Scene EnsureLevelScene(string levelName)
        {
            string dir = "Assets/Scenes/Levels";
            string path = $"{dir}/{levelName}.unity";

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Scene scene;
            if (File.Exists(path))
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                EditorSceneManager.SaveScene(scene, path);
                AssetDatabase.Refresh();
            }

            SceneManager.SetActiveScene(scene);
            return scene;
        }
    }
}
