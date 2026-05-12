using System.Collections.Generic;
using System.IO;
using OpenFarCry.Importer;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Importer.Editor;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Entities;
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
        bool _cacheGeometryInEditMode = true;
        bool _attachCachedPrefabsToScene = true;
        bool _disableRuntimeLoaders = true;

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
            _cacheGeometryInEditMode = EditorGUILayout.Toggle("Cache Geometry To FCData", _cacheGeometryInEditMode);
            using (new EditorGUI.DisabledScope(!_cacheGeometryInEditMode))
            {
                _attachCachedPrefabsToScene = EditorGUILayout.Toggle("Attach Cached Prefabs In Scene", _attachCachedPrefabsToScene);
                _disableRuntimeLoaders = EditorGUILayout.Toggle("Disable Runtime Loader Components", _disableRuntimeLoaders);
            }
        }

        void DrawActionButtons()
        {
            bool canBuild = _registry != null && _levelNames.Count > 0 && _missionNames.Count > 0;

            using (new EditorGUI.DisabledScope(!canBuild))
            {
                if (GUILayout.Button("Build Scene", GUILayout.Height(30)))
                    BuildScene();

                if (GUILayout.Button("Build Scene + Cache Geometry (EditMode)", GUILayout.Height(30)))
                    BuildSceneAndCacheGeometry();
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

        void BuildSceneAndCacheGeometry()
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

            GeometryCacheStats cacheStats = _cacheGeometryInEditMode
                ? CacheSceneGeometryToProject(target)
                : default;

            _lastBuildStats = _cacheGeometryInEditMode
                ? $"Built '{levelName}/{missionName}':\n{stats}\n\nEditMode cache:\n{cacheStats}"
                : $"Built '{levelName}/{missionName}':\n{stats}";
            Debug.Log($"[FcLevelBuilder] {_lastBuildStats}");
        }

        void ClearScene()
        {
            var active = SceneManager.GetActiveScene();
            FcLevelSceneBuilder.ClearScene(active);
            _lastBuildStats = "Scene cleared.";
            _lastError = null;
        }

        readonly struct GeometryImportProfile
        {
            public readonly bool ImportSkeleton;
            public readonly float ImportScale;

            public GeometryImportProfile(bool importSkeleton, float importScale)
            {
                ImportSkeleton = importSkeleton;
                ImportScale = importScale;
            }
        }

        struct GeometryCacheStats
        {
            public int SourceInstances;
            public int UniquePaths;
            public int ImportedPrefabs;
            public int ReusedPrefabs;
            public int FailedImports;
            public int AttachedInstances;
            public int DisabledLoaderComponents;

            public override string ToString()
            {
                return
                    $"Instances scanned: {SourceInstances}\n" +
                    $"Unique CGF paths: {UniquePaths}\n" +
                    $"Imported prefabs: {ImportedPrefabs}\n" +
                    $"Reused prefabs: {ReusedPrefabs}\n" +
                    $"Failed imports: {FailedImports}\n" +
                    $"Attached instances: {AttachedInstances}\n" +
                    $"Disabled loaders: {DisabledLoaderComponents}";
            }
        }

        GeometryCacheStats CacheSceneGeometryToProject(Scene scene)
        {
            var stats = new GeometryCacheStats();
            var brushes = CollectComponentsInScene<FcBrushInstance>(scene);
            var vegetation = CollectComponentsInScene<FcVegetationInstance>(scene);
            var meshEntities = CollectComponentsInScene<FcMeshEntity>(scene);

            stats.SourceInstances = brushes.Count + vegetation.Count + meshEntities.Count;

            var profilesByPath = new Dictionary<string, GeometryImportProfile>(System.StringComparer.Ordinal);
            RegisterBrushAndVegetationProfiles(brushes, vegetation, profilesByPath);
            RegisterMeshEntityProfiles(meshEntities, profilesByPath);

            stats.UniquePaths = profilesByPath.Count;

            var importService = new CgfImportEditorService();
            var cacheService = new CgfAssetCacheService();
            var lodService = new CgfLodImportService();
            var cachedPrefabsByPath = new Dictionary<string, GameObject>(System.StringComparer.Ordinal);

            try
            {
                int index = 0;
                foreach (var kv in profilesByPath)
                {
                    index++;
                    string virtualPath = kv.Key;
                    var profile = kv.Value;
                    EditorUtility.DisplayProgressBar(
                        "Caching level geometry",
                        $"{index}/{profilesByPath.Count}: {virtualPath}",
                        profilesByPath.Count > 0 ? (float)index / profilesByPath.Count : 1f);

                    var cachePaths = cacheService.GetCachePaths(virtualPath);
                    var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cachePaths.PrefabPath);
                    if (existingPrefab != null)
                    {
                        cachedPrefabsByPath[virtualPath] = existingPrefab;
                        stats.ReusedPrefabs++;
                        continue;
                    }

                    if (!TryImportAndCachePrefab(virtualPath, profile, importService, cacheService, lodService, out var importedPrefab))
                    {
                        stats.FailedImports++;
                        continue;
                    }

                    cachedPrefabsByPath[virtualPath] = importedPrefab;
                    stats.ImportedPrefabs++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (_attachCachedPrefabsToScene)
            {
                stats.AttachedInstances += AttachCachedPrefabsToScene(brushes, cachedPrefabsByPath);
                stats.AttachedInstances += AttachCachedPrefabsToScene(vegetation, cachedPrefabsByPath);
                stats.AttachedInstances += AttachCachedPrefabsToScene(meshEntities, cachedPrefabsByPath);
            }

            if (_disableRuntimeLoaders)
            {
                stats.DisabledLoaderComponents += DisableLoadedComponents(brushes, cachedPrefabsByPath);
                stats.DisabledLoaderComponents += DisableLoadedComponents(vegetation, cachedPrefabsByPath);
                stats.DisabledLoaderComponents += DisableLoadedComponents(meshEntities, cachedPrefabsByPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.MarkSceneDirty(scene);
            return stats;
        }

        static void RegisterBrushAndVegetationProfiles(
            List<FcBrushInstance> brushes,
            List<FcVegetationInstance> vegetation,
            Dictionary<string, GeometryImportProfile> profilesByPath)
        {
            for (int i = 0; i < brushes.Count; i++)
                RegisterImportProfile(brushes[i] != null ? brushes[i].VirtualPath : null, false, 0.01f, profilesByPath);
            for (int i = 0; i < vegetation.Count; i++)
                RegisterImportProfile(vegetation[i] != null ? vegetation[i].VirtualPath : null, false, 0.01f, profilesByPath);
        }

        static void RegisterMeshEntityProfiles(
            List<FcMeshEntity> meshEntities,
            Dictionary<string, GeometryImportProfile> profilesByPath)
        {
            for (int i = 0; i < meshEntities.Count; i++)
            {
                var entity = meshEntities[i];
                if (entity == null)
                    continue;

                var so = new SerializedObject(entity);
                bool importSkeleton = so.FindProperty("_importSkeleton")?.boolValue ?? false;
                float importScale = so.FindProperty("_importScale")?.floatValue ?? 0.01f;
                if (importScale <= 0f)
                    importScale = 0.01f;

                RegisterImportProfile(entity.VirtualPath, importSkeleton, importScale, profilesByPath);
            }
        }

        static void RegisterImportProfile(
            string virtualPath,
            bool importSkeleton,
            float importScale,
            Dictionary<string, GeometryImportProfile> profilesByPath)
        {
            if (string.IsNullOrWhiteSpace(virtualPath))
                return;

            string normalized;
            try
            {
                normalized = ImportAssetPaths.NormalizeVirtualPath(virtualPath);
            }
            catch
            {
                return;
            }

            if (profilesByPath.TryGetValue(normalized, out var existing))
            {
                bool mergedSkeleton = existing.ImportSkeleton || importSkeleton;
                float mergedScale = existing.ImportScale;
                if (importScale > 0f && System.Math.Abs(importScale - 0.01f) > 1e-6f)
                    mergedScale = importScale;

                profilesByPath[normalized] = new GeometryImportProfile(mergedSkeleton, mergedScale);
                return;
            }

            profilesByPath[normalized] = new GeometryImportProfile(importSkeleton, importScale);
        }

        static bool TryImportAndCachePrefab(
            string virtualPath,
            GeometryImportProfile profile,
            CgfImportEditorService importService,
            CgfAssetCacheService cacheService,
            CgfLodImportService lodService,
            out GameObject cachedPrefab)
        {
            cachedPrefab = null;
            try
            {
                var sourceBytes = CgfResourceImportService.Instance.LoadRuntimeResourceBytes(virtualPath);
                var parsedFile = CgfParser.Parse(sourceBytes);
                parsedFile.SourceVirtualPath = virtualPath;

                var siblingLods = lodService.FindSiblingLodPaths(virtualPath);
                var request = new CgfImportRequest(
                    parsedPath: virtualPath,
                    parsedFile: parsedFile,
                    siblingLodPaths: siblingLods,
                    saveToProject: true,
                    importSkeleton: profile.ImportSkeleton,
                    importAnimations: false,
                    importPhysicsBoxColliders: false,
                    importRagdollBodies: false,
                    importScale: profile.ImportScale,
                    rigCachePolicy: default,
                    tryInstantiateCachedPrefab: TryInstantiateNever,
                    buildGameObject: BuildStaticGameObject,
                    configureLodGroup: (root, hasSkeleton, importScale, saveToProject) =>
                        lodService.ConfigureLodGroup(
                            root,
                            hasSkeleton,
                            importScale,
                            siblingLods,
                            persistMesh: cacheService.PersistMeshAssetForVirtualPath,
                            materialService: CgfRuntimeImporter.MaterialService),
                    attachAnimations: null,
                    applyPostTransform: ResetTransform,
                    saveAssets: (mesh, go, clips) => cacheService.SaveAssets(virtualPath, mesh, go, clips),
                    useRuntimeImportService: true,
                    useRuntimeMemoryCache: true,
                    preferProjectCache: false);

                var result = importService.ImportToScene(request);
                if (!result.Success)
                {
                    Debug.LogWarning($"[FcLevelBuilder] Failed to cache '{virtualPath}': {result.ErrorMessage}");
                    if (result.GameObject != null)
                        Object.DestroyImmediate(result.GameObject);
                    return false;
                }

                if (result.GameObject != null)
                    Object.DestroyImmediate(result.GameObject);

                var cachePaths = cacheService.GetCachePaths(virtualPath);
                cachedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cachePaths.PrefabPath);
                if (cachedPrefab == null)
                {
                    Debug.LogWarning($"[FcLevelBuilder] Cached prefab was not created for '{virtualPath}'.");
                    return false;
                }

                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[FcLevelBuilder] Exception while caching '{virtualPath}': {e.Message}");
                return false;
            }
        }

        static GameObject BuildStaticGameObject(
            BuildResult result,
            CgfFile parsedFile,
            CgfRigDefinition rigDefinition,
            string name,
            bool importPhysicsBoxColliders,
            bool importRagdollBodies,
            float importScale)
        {
            var builder = new CgfGameObjectBuilder();
            return builder.Build(new CgfGameObjectBuilder.BuildRequest(
                result,
                parsedFile,
                rigDefinition,
                name,
                materialService: CgfRuntimeImporter.MaterialService)).Root;
        }

        static bool TryInstantiateNever(out GameObject go)
        {
            go = null;
            return false;
        }

        static void ResetTransform(GameObject go)
        {
            if (go == null)
                return;

            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }

        static int AttachCachedPrefabsToScene<T>(
            List<T> components,
            IReadOnlyDictionary<string, GameObject> cachedPrefabsByPath)
            where T : Component
        {
            int attached = 0;
            for (int i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null)
                    continue;
                if (!TryGetVirtualPath(component, out var normalizedPath))
                    continue;
                if (!cachedPrefabsByPath.TryGetValue(normalizedPath, out var prefab) || prefab == null)
                    continue;

                RemoveExistingCachedChildren(component.transform);
                var instance = PrefabUtility.InstantiatePrefab(prefab, component.gameObject.scene) as GameObject;
                if (instance == null)
                    continue;

                instance.name = "__FCDataCached";
                instance.transform.SetParent(component.transform, worldPositionStays: false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                attached++;
            }

            return attached;
        }

        static int DisableLoadedComponents<T>(
            List<T> components,
            IReadOnlyDictionary<string, GameObject> cachedPrefabsByPath)
            where T : Behaviour
        {
            int disabled = 0;
            for (int i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (component == null || !component.enabled)
                    continue;
                if (!TryGetVirtualPath(component, out var normalizedPath))
                    continue;
                if (!cachedPrefabsByPath.TryGetValue(normalizedPath, out var prefab) || prefab == null)
                    continue;

                component.enabled = false;
                disabled++;
            }

            return disabled;
        }

        static void RemoveExistingCachedChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child != null && child.name == "__FCDataCached")
                    Object.DestroyImmediate(child.gameObject);
            }
        }

        static bool TryGetVirtualPath(Component component, out string normalizedPath)
        {
            normalizedPath = null;
            if (component == null)
                return false;

            string path = null;
            if (component is FcBrushInstance brush)
                path = brush.VirtualPath;
            else if (component is FcVegetationInstance vegetation)
                path = vegetation.VirtualPath;
            else if (component is FcMeshEntity meshEntity)
                path = meshEntity.VirtualPath;

            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                normalizedPath = ImportAssetPaths.NormalizeVirtualPath(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static List<T> CollectComponentsInScene<T>(Scene scene) where T : Component
        {
            var result = new List<T>();
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null)
                    continue;

                result.AddRange(root.GetComponentsInChildren<T>(includeInactive: true));
            }

            return result;
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
