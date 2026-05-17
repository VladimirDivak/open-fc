using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenFarCry.Importer;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Importer.Editor;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Entities;
using OpenFarCry.Level.Registry;
using OpenFarCry.Level.Services;
using OpenFarCry.Level.Volumes;
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
        bool _attachCachedPrefabsToScene = true;
        bool _disableRuntimeLoaders = true;
        bool _includeEntityObjectGeometryInAuthoringCache = false;

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

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Authoring Scene Options", EditorStyles.boldLabel);
            _attachCachedPrefabsToScene = EditorGUILayout.Toggle("Attach Cached Prefabs In Scene", _attachCachedPrefabsToScene);
            _disableRuntimeLoaders = EditorGUILayout.Toggle("Disable Runtime Loader Components", _disableRuntimeLoaders);
            _includeEntityObjectGeometryInAuthoringCache = EditorGUILayout.Toggle(
                "Include Entities/Objects Geometry In Authoring Cache",
                _includeEntityObjectGeometryInAuthoringCache);
        }

        void DrawActionButtons()
        {
            bool canBuild = _registry != null && _levelNames.Count > 0 && _missionNames.Count > 0;

            using (new EditorGUI.DisabledScope(!canBuild))
            {
                if (GUILayout.Button("Build Runtime Scene", GUILayout.Height(30)))
                    BuildRuntimeScene();

                if (GUILayout.Button("Build Authoring Scene (FCData Cache)", GUILayout.Height(30)))
                    BuildAuthoringScene();
            }

            if (GUILayout.Button("Clear Scene"))
                ClearScene();

            if (GUILayout.Button("Audit Scene Dependencies (FCData)"))
                AuditSceneDependencies();

            if (GUILayout.Button("Source Parity Audit"))
                RunSourceParityAudit();

            if (GUILayout.Button("Validate Authoring Scene"))
                ValidateAuthoringScene();

            if (GUILayout.Button("Finalize Authoring Scene (Promote FCData)"))
                FinalizeAuthoringScene();

            if (GUILayout.Button("Purge Level FCData Cache"))
                PurgeLevelFcDataCache();

            using (new EditorGUI.DisabledScope(_levelNames.Count == 0))
            {
                if (GUILayout.Button("Pre-Bake CGF Materials for Level"))
                    PreBakeLevelMaterials();
            }
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

        void BuildRuntimeScene()
        {
            _lastError = null;
            _lastBuildStats = null;

            string levelName = _levelNames[_levelIndex];
            string missionName = _missionNames[_missionIndex];

            Scene target = EnsureLevelScene(levelName);
            if (!target.IsValid()) { _lastError = "Failed to open/create level scene."; return; }

            FcLevelSceneBuilder.ClearScene(target);

            var stats = FcLevelSceneBuilder.BuildScene(
                levelName,
                missionName,
                _registry,
                target,
                _skipHidden,
                _buildBrushes,
                FcLevelSceneBuilder.SceneBuildMode.Runtime);

            BakeLevelMaterials(levelName);

            _lastBuildStats = $"Built '{levelName}/{missionName}':\n{stats}";
            Debug.Log($"[FcLevelBuilder] {_lastBuildStats}");
        }

        void BakeLevelMaterials(string levelName)
        {
            var profilesByPath = new Dictionary<string, GeometryImportProfile>(System.StringComparer.Ordinal);
            var dummyStats = new GeometryCacheStats();
            if (_buildBrushes)
                RegisterBrushProfilesFromLevelData(levelName, profilesByPath, ref dummyStats);
            RegisterVegetationProfilesFromLevelData(levelName, profilesByPath, ref dummyStats);
            RegisterMeshEntityProfilesFromLevelData(levelName, profilesByPath, ref dummyStats);

            FcMaterialManifest cgfManifest = null;

            try
            {
                EditorUtility.DisplayProgressBar("Baking level material manifests", levelName, 0f);

                if (profilesByPath.Count > 0)
                    cgfManifest = CgfMaterialEditorBakeService.BakeOrUpdateLevelManifest(
                        levelName, profilesByPath.Keys);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            string cgfInfo = cgfManifest != null
                ? $"\nCGF material manifest: {cgfManifest.EntryCount} entries"
                : "\nCGF material manifest: none";

            Debug.Log($"[FcLevelBuilder] Material bake complete for '{levelName}'.{cgfInfo}");
        }

        void BuildAuthoringScene()
        {
            _lastError = null;
            _lastBuildStats = null;

            string levelName = _levelNames[_levelIndex];
            string missionName = _missionNames[_missionIndex];

            Scene target = EnsureLevelScene(levelName);
            if (!target.IsValid()) { _lastError = "Failed to open/create level scene."; return; }

            var cacheContext = BuildDataDrivenCacheContext(levelName);

            FcLevelSceneBuilder.ClearScene(target);
            var stats = FcLevelSceneBuilder.BuildScene(
                levelName,
                missionName,
                _registry,
                target,
                _skipHidden,
                _buildBrushes,
                FcLevelSceneBuilder.SceneBuildMode.Authoring);

            var materialOverrideService = ConfigureSceneMaterialOverrides(target, levelName);
            var cacheStats = CacheSceneGeometryToProject(target, cacheContext, levelName, materialOverrideService);
            ApplyAuthoringStaticFlagsToTerrainAndWater(target);
            bool canStripRuntimeServices = cacheStats.AttachedInstances > 0;
            int removedServices = _disableRuntimeLoaders && canStripRuntimeServices
                ? StripRuntimeServicesForAuthoring(target)
                : 0;
            if (_disableRuntimeLoaders && !canStripRuntimeServices)
            {
                Debug.LogWarning(
                    "[FcLevelBuilder] Authoring cache did not attach any visuals. " +
                    "Runtime services were kept to avoid an empty scene.");
            }

            var slotDiagnosticsSummary = CollectMaterialSlotDiagnosticsSummary(target);
            PersistMaterialSlotDiagnosticsToLayoutData(levelName, missionName, slotDiagnosticsSummary);

            _lastBuildStats = $"Built authoring scene '{levelName}/{missionName}':\n{stats}\n\nFCData cache:\n{cacheStats}\n\nAuthoring:\nRuntime services removed: {removedServices}\nMaterial slot diagnostics persisted: total={slotDiagnosticsSummary.TotalCount}, applied={slotDiagnosticsSummary.AppliedCount}, targeted-miss={slotDiagnosticsSummary.TargetedMissCount}, unresolved={slotDiagnosticsSummary.UnresolvedCount}\nMaterial instance diagnostics persisted: total={slotDiagnosticsSummary.InstanceCount}, override-applied={slotDiagnosticsSummary.InstanceOverrideAppliedCount}, fallback={slotDiagnosticsSummary.InstanceFallbackCount}, targeted-miss={slotDiagnosticsSummary.InstanceTargetedMissCount}, unresolved={slotDiagnosticsSummary.InstanceUnresolvedCount}";
            Debug.Log($"[FcLevelBuilder] {_lastBuildStats}");
        }

        void ClearScene()
        {
            var active = SceneManager.GetActiveScene();
            FcLevelSceneBuilder.ClearScene(active);
            _lastBuildStats = "Scene cleared.";
            _lastError = null;
        }

        void AuditSceneDependencies()
        {
            _lastError = null;
            var active = SceneManager.GetActiveScene();
            if (!active.IsValid() || !active.isLoaded)
            {
                _lastError = "No active loaded scene.";
                return;
            }

            var report = BuildSceneDependencyAudit(active);
            _lastBuildStats = report;
            Debug.Log($"[FcLevelBuilder] {report}");
        }

        void RunSourceParityAudit()
        {
            _lastError = null;
            var active = SceneManager.GetActiveScene();
            if (!active.IsValid() || !active.isLoaded)
            {
                _lastError = "No active loaded scene.";
                return;
            }

            if (_levelNames.Count == 0 || _missionNames.Count == 0)
            {
                _lastError = "Select level + mission first.";
                return;
            }

            string levelName   = _levelNames[_levelIndex];
            string missionName = _missionNames[_missionIndex];
            var report = BuildSourceParityReport(levelName, missionName, active);
            _lastBuildStats = report;
            Debug.Log($"[FcLevelBuilder] Source Parity\n{report}");
        }

        static string BuildSourceParityReport(string levelName, string missionName, Scene scene)
        {
            // ── Source ────────────────────────────────────────────────────────────
            FcMissionDesc mission = null;
            try { mission = FcLevelLoader.LoadMission(levelName, missionName); }
            catch (System.Exception e) { Debug.LogWarning($"[FcLevelBuilder] Parity audit: LoadMission failed: {e.Message}"); }

            int srcEntities   = mission?.Entities?.Count ?? 0;
            int srcObjects    = mission?.Objects?.Count  ?? 0;
            int srcLevelObjs  = mission?.LevelObjects?.Count ?? 0;

            IReadOnlyList<FcBrushDesc> brushes = null;
            try { brushes = FcBrushLoader.LoadBrushes(levelName); } catch { }
            int srcBrushes = brushes?.Count ?? 0;

            FcLevelSupplementData supplement = null;
            try { supplement = FcLevelSupplementLoader.Load(levelName); } catch { }
            int srcVegetation = supplement?.VegetationInstances?.Length ?? 0;

            var editorLights = FcLevelLoader.LoadEditorXmlDynamicLights(levelName);
            int srcCryLights  = editorLights?.Count ?? 0;
            int srcMissionDL  = mission?.Entities?.Count(e => string.Equals(e.EntityClass, "DynamicLight", System.StringComparison.OrdinalIgnoreCase)) ?? 0;
            int srcLights     = srcCryLights > 0 ? srcCryLights : srcMissionDL;

            var sequences = FcLevelLoader.LoadMovieSequences(levelName);
            int srcSequences = sequences?.Count ?? 0;

            // ── Scene ─────────────────────────────────────────────────────────────
            var roots = scene.GetRootGameObjects();
            int scnEntities   = 0;
            int scnBrushes    = 0;
            int scnVegetation = 0;
            int scnLights     = 0;
            int scnSequences  = 0;

            foreach (var root in roots)
            {
                scnEntities   += root.GetComponentsInChildren<FcEntity>(true).Length;
                scnBrushes    += root.GetComponentsInChildren<FcBrushInstance>(true).Length;
                scnVegetation += root.GetComponentsInChildren<FcVegetationInstance>(true).Length;
                scnLights     += root.GetComponentsInChildren<Light>(true).Length;
                scnSequences  += root.GetComponentsInChildren<FcMovieSequencePlaceholder>(true).Length;
            }

            // ── Format ────────────────────────────────────────────────────────────
            var sb = new StringBuilder();
            sb.AppendLine($"Source parity: {levelName}/{missionName}");
            sb.AppendLine($"  (.cry lights={srcCryLights}, mission DL={srcMissionDL})");
            sb.AppendLine($"{"TYPE",-22} {"SRC",6} {"SCENE",6} {"DELTA",7}");
            AppendParityRow(sb, "Entities",         srcEntities,   scnEntities);
            AppendParityRow(sb, "Objects+LevelObjs",srcObjects + srcLevelObjs, 0);
            AppendParityRow(sb, "Brushes",          srcBrushes,    scnBrushes);
            AppendParityRow(sb, "Vegetation",       srcVegetation, scnVegetation);
            AppendParityRow(sb, "Lights",           srcLights,     scnLights);
            AppendParityRow(sb, "Sequences",        srcSequences,  scnSequences);
            return sb.ToString().TrimEnd();
        }

        static void AppendParityRow(StringBuilder sb, string label, int src, int scene)
        {
            string delta = src == 0 ? "n/a" : scene == src ? "OK" : $"{scene - src:+#;-#;0}";
            sb.AppendLine($"  {label,-20} {src,6} {scene,6} {delta,7}");
        }

        void FinalizeAuthoringScene()
        {
            _lastError = null;
            var active = SceneManager.GetActiveScene();
            if (!active.IsValid() || !active.isLoaded)
            {
                _lastError = "No active loaded scene.";
                return;
            }

            string levelName = GetLevelNameForActiveScene(active);
            var report = FinalizeSceneDependencies(active, levelName);
            _lastBuildStats = report;
            Debug.Log($"[FcLevelBuilder] {report}");
        }

        void ValidateAuthoringScene()
        {
            _lastError = null;
            var active = SceneManager.GetActiveScene();
            if (!active.IsValid() || !active.isLoaded)
            {
                _lastError = "No active loaded scene.";
                return;
            }

            string levelName = GetLevelNameForActiveScene(active);
            string missionName = (_missionNames != null &&
                                  _missionIndex >= 0 &&
                                  _missionIndex < _missionNames.Count)
                ? _missionNames[_missionIndex]
                : null;

            var report = BuildAuthoringSceneValidationReport(active, levelName, missionName);
            _lastBuildStats = report;
            Debug.Log($"[FcLevelBuilder] {report}");
        }

        void PurgeLevelFcDataCache()
        {
            _lastError = null;
            string levelName = GetSelectedLevelName();
            if (string.IsNullOrWhiteSpace(levelName))
            {
                _lastError = "No selected level.";
                return;
            }

            string levelCacheDir = GetLevelAssetCacheDir(levelName);
            if (!AssetDatabase.IsValidFolder(levelCacheDir))
            {
                _lastBuildStats = $"FCData cache not found for level '{levelName}'.";
                return;
            }

            var active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded)
            {
                var audit = BuildSceneDependencyAuditData(active);
                if (audit.FcDataPaths.Count > 0)
                {
                    _lastError = $"Active scene still has {audit.FcDataPaths.Count} FCData refs. Run Finalize first.";
                    return;
                }
            }

            if (!EditorUtility.DisplayDialog(
                    "Purge FCData Cache",
                    $"Delete '{levelCacheDir}' for level '{levelName}'?",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            if (!AssetDatabase.DeleteAsset(levelCacheDir))
            {
                _lastError = $"Failed to delete '{levelCacheDir}'.";
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _lastBuildStats = $"Deleted FCData cache for level '{levelName}'.";
        }

        void PreBakeLevelMaterials()
        {
            _lastError = null;
            string levelName = GetSelectedLevelName();
            if (string.IsNullOrWhiteSpace(levelName))
            {
                _lastError = "No selected level.";
                return;
            }

            int baked = 0;
            int reused = 0;
            int failed = 0;
            var profilesByPath = new Dictionary<string, GeometryImportProfile>(System.StringComparer.Ordinal);
            var dummyStats = new GeometryCacheStats();

            if (_buildBrushes)
                RegisterBrushProfilesFromLevelData(levelName, profilesByPath, ref dummyStats);
            RegisterVegetationProfilesFromLevelData(levelName, profilesByPath, ref dummyStats);
            RegisterMeshEntityProfilesFromLevelData(levelName, profilesByPath, ref dummyStats);

            var paths = new List<string>(profilesByPath.Keys);
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    string virtualPath = paths[i];
                    EditorUtility.DisplayProgressBar(
                        "Pre-baking CGF materials",
                        $"{i + 1}/{paths.Count}: {virtualPath}",
                        paths.Count > 0 ? (float)(i + 1) / paths.Count : 1f);

                    if (!TryLoadParsedFile(virtualPath, out var parsedFile))
                    {
                        failed++;
                        continue;
                    }

                    if (parsedFile.MaterialChunks == null)
                        continue;

                    for (int c = 0; c < parsedFile.MaterialChunks.Count; c++)
                    {
                        var chunk = parsedFile.MaterialChunks[c];
                        if (chunk == null || chunk.MtlType == CgfMtlType.Multi)
                            continue;

                        string assetPath = CgfMaterialEditorBakeService.GetBakedMaterialPath(virtualPath, chunk.TableIndex);
                        bool existed = AssetDatabase.LoadAssetAtPath<Material>(assetPath) != null;
                        var mat = CgfMaterialEditorBakeService.GetOrBakeMaterial(virtualPath, chunk);
                        if (mat != null)
                        {
                            if (existed) reused++; else baked++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _lastBuildStats = $"Pre-bake CGF materials for '{levelName}':\nBaked: {baked}, Reused: {reused}, Failed: {failed}";
            Debug.Log($"[FcLevelBuilder] {_lastBuildStats}");
        }

        [System.Flags]
        enum GeometryUsageFlags
        {
            None = 0,
            Brush = 1 << 0,
            Vegetation = 1 << 1,
            MeshEntity = 1 << 2,
        }

        readonly struct GeometryImportProfile
        {
            public readonly bool ImportSkeleton;
            public readonly float ImportScale;
            public readonly GeometryUsageFlags UsageFlags;
            public readonly bool RequiresPhysicsCollider;

            public GeometryImportProfile(
                bool importSkeleton,
                float importScale,
                GeometryUsageFlags usageFlags,
                bool requiresPhysicsCollider)
            {
                ImportSkeleton = importSkeleton;
                ImportScale = importScale;
                UsageFlags = usageFlags;
                RequiresPhysicsCollider = requiresPhysicsCollider;
            }

            public bool IsBrush => (UsageFlags & GeometryUsageFlags.Brush) != 0;
            public bool IsBrushOnly => UsageFlags == GeometryUsageFlags.Brush;
            public bool IsVegetationOnly => UsageFlags == GeometryUsageFlags.Vegetation;
            public bool IsBrushOrVegetation => (UsageFlags & (GeometryUsageFlags.Brush | GeometryUsageFlags.Vegetation)) != 0;
        }

        struct GeometryCacheStats
        {
            public int SourceInstances;
            public int UniquePaths;
            public int ImportedPrefabs;
            public int ReusedPrefabs;
            public int RebuiltPrefabs;
            public int FailedImports;
            public int AttachCandidates;
            public int AttachMisses;
            public int AttachedInstances;
            public int DisabledLoaderComponents;

            public override string ToString()
            {
                return
                    $"Instances scanned: {SourceInstances}\n" +
                    $"Unique CGF paths: {UniquePaths}\n" +
                    $"Imported prefabs: {ImportedPrefabs}\n" +
                    $"Reused prefabs: {ReusedPrefabs}\n" +
                    $"Rebuilt prefabs: {RebuiltPrefabs}\n" +
                    $"Failed imports: {FailedImports}\n" +
                    $"Attach candidates: {AttachCandidates}\n" +
                    $"Attach misses: {AttachMisses}\n" +
                    $"Attached instances: {AttachedInstances}\n" +
                    $"Disabled loaders: {DisabledLoaderComponents}";
            }
        }

        readonly struct SceneDependencyAuditData
        {
            public readonly HashSet<string> FcDataPaths;
            public readonly int NonFcDataAssetCount;

            public SceneDependencyAuditData(HashSet<string> fcDataPaths, int nonFcDataAssetCount)
            {
                FcDataPaths = fcDataPaths ?? new HashSet<string>(System.StringComparer.Ordinal);
                NonFcDataAssetCount = nonFcDataAssetCount;
            }
        }

        struct FinalizeStats
        {
            public int SceneFcDataDependencies;
            public int PromotedAssets;
            public int FailedPromotions;
            public int SceneRefsRewired;
            public int PromotedAssetRefsRewired;
            public int RemainingFcDataRefs;
        }

        struct AuthoringValidationStats
        {
            public bool HasLevelRoot;
            public bool HasTerrain;
            public bool HasBrushesAuthoringRoot;
            public bool HasVegetationAuthoringRoot;
            public bool HasBrushesRuntimeRoot;
            public int RuntimeServiceCount;
            public int CachedChildCount;
            public int CachedChildrenMissingStaticFlags;
            public int VegetationCachedCount;
            public int VegetationCachedWithEnabledCollider;
            public int VegetationCachedWithEnabledLodGroup;
            public int MaterialSlotDiagnosticCount;
            public int MaterialSlotAppliedCount;
            public int MaterialSlotUnresolvedCount;
            public int MaterialSlotTargetedMissCount;
            public int MaterialInstanceDiagnosticCount;
            public int MaterialInstanceOverrideAppliedCount;
            public int MaterialInstanceFallbackCount;
            public int MaterialInstanceTargetedMissCount;
            public int MaterialInstanceUnresolvedCount;
            public int MaterialResolutionSourceBucketCount;
            public string[] MaterialSlotUnresolvedSample;
            public bool HasLayoutData;
            public string LayoutDataPath;
            public int LayoutSlotDiagnosticCount;
            public int LayoutSlotAppliedCount;
            public int LayoutSlotTargetedMissCount;
            public int LayoutSlotUnresolvedCount;
            public int LayoutSlotOutcomeBucketCount;
            public int LayoutSlotUnresolvedSampleCount;
            public int LayoutInstanceDiagnosticCount;
            public int LayoutInstanceOverrideAppliedCount;
            public int LayoutInstanceFallbackCount;
            public int LayoutInstanceTargetedMissCount;
            public int LayoutInstanceUnresolvedCount;
            public int LayoutResolutionSourceBucketCount;
            public int FcDataDependencyCount;
            public string[] FcDataDependencySample;
            public bool IsLikelyRuntimeScene;
            public int Warnings;
        }

        readonly struct MaterialSlotDiagnosticsSummary
        {
            public readonly int TotalCount;
            public readonly int AppliedCount;
            public readonly int TargetedMissCount;
            public readonly int UnresolvedCount;
            public readonly int InstanceCount;
            public readonly int InstanceOverrideAppliedCount;
            public readonly int InstanceFallbackCount;
            public readonly int InstanceTargetedMissCount;
            public readonly int InstanceUnresolvedCount;
            public readonly Dictionary<string, int> OutcomeCounts;
            public readonly Dictionary<string, int> UnresolvedLineCounts;
            public readonly Dictionary<string, int> ResolutionSourceCounts;
            public readonly Dictionary<string, int> ShaderFamilyCounts;

            public MaterialSlotDiagnosticsSummary(
                int totalCount,
                int appliedCount,
                int targetedMissCount,
                int unresolvedCount,
                int instanceCount,
                int instanceOverrideAppliedCount,
                int instanceFallbackCount,
                int instanceTargetedMissCount,
                int instanceUnresolvedCount,
                Dictionary<string, int> outcomeCounts,
                Dictionary<string, int> unresolvedLineCounts,
                Dictionary<string, int> resolutionSourceCounts,
                Dictionary<string, int> shaderFamilyCounts = null)
            {
                TotalCount = totalCount;
                AppliedCount = appliedCount;
                TargetedMissCount = targetedMissCount;
                UnresolvedCount = unresolvedCount;
                InstanceCount = instanceCount;
                InstanceOverrideAppliedCount = instanceOverrideAppliedCount;
                InstanceFallbackCount = instanceFallbackCount;
                InstanceTargetedMissCount = instanceTargetedMissCount;
                InstanceUnresolvedCount = instanceUnresolvedCount;
                OutcomeCounts = outcomeCounts ?? new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
                UnresolvedLineCounts = unresolvedLineCounts ?? new Dictionary<string, int>(System.StringComparer.Ordinal);
                ResolutionSourceCounts = resolutionSourceCounts ?? new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
                ShaderFamilyCounts = shaderFamilyCounts ?? new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            }
        }

        sealed class GeometryCacheContext
        {
            public readonly Dictionary<string, GeometryImportProfile> ProfilesByPath =
                new Dictionary<string, GeometryImportProfile>(System.StringComparer.Ordinal);
            public readonly Dictionary<string, GameObject> CachedPrefabsByPath =
                new Dictionary<string, GameObject>(System.StringComparer.Ordinal);
            public GeometryCacheStats Stats;
        }

        sealed class MaterialOverridePersistContext
        {
            public readonly string MaterialDir;
            public readonly string TextureDir;
            public readonly Dictionary<string, Material> MaterialByKey =
                new Dictionary<string, Material>(System.StringComparer.Ordinal);
            public readonly Dictionary<string, Texture2D> TextureByKey =
                new Dictionary<string, Texture2D>(System.StringComparer.Ordinal);

            public MaterialOverridePersistContext(string materialDir, string textureDir)
            {
                MaterialDir = materialDir;
                TextureDir = textureDir;
            }
        }

        GeometryCacheContext BuildDataDrivenCacheContext(string levelName)
        {
            var context = new GeometryCacheContext();
            if (_buildBrushes)
                RegisterBrushProfilesFromLevelData(levelName, context.ProfilesByPath, ref context.Stats);
            RegisterVegetationProfilesFromLevelData(levelName, context.ProfilesByPath, ref context.Stats);
            context.Stats.UniquePaths = context.ProfilesByPath.Count;
            EnsureCachedPrefabsForProfiles(context.ProfilesByPath, context.CachedPrefabsByPath, ref context.Stats);
            return context;
        }

        GeometryCacheStats CacheSceneGeometryToProject(
            Scene scene,
            GeometryCacheContext preCacheContext,
            string levelName,
            FcLevelMaterialOverrideService materialOverrideService)
        {
            var context = preCacheContext ?? new GeometryCacheContext();
            var materialOverridePersistContext = BuildMaterialOverridePersistContext(levelName);
            var brushes = CollectComponentsInScene<FcBrushInstance>(scene);
            var vegetation = CollectComponentsInScene<FcVegetationInstance>(scene);
            var meshEntities = _includeEntityObjectGeometryInAuthoringCache
                ? CollectComponentsInScene<FcMeshEntity>(scene)
                : new List<FcMeshEntity>();

            context.Stats.SourceInstances = brushes.Count + vegetation.Count + meshEntities.Count;

            RegisterBrushAndVegetationProfiles(brushes, vegetation, context.ProfilesByPath);
            if (_includeEntityObjectGeometryInAuthoringCache)
                RegisterMeshEntityProfiles(meshEntities, context.ProfilesByPath);
            context.Stats.UniquePaths = context.ProfilesByPath.Count;

            EnsureCachedPrefabsForProfiles(
                context.ProfilesByPath,
                context.CachedPrefabsByPath,
                ref context.Stats);

            if (_attachCachedPrefabsToScene)
            {
                context.Stats.AttachedInstances += AttachCachedPrefabsToScene(
                    brushes,
                    context.CachedPrefabsByPath,
                    materialOverrideService,
                    levelName,
                    materialOverridePersistContext,
                    ref context.Stats);
                context.Stats.AttachedInstances += AttachCachedPrefabsToScene(
                    vegetation,
                    context.CachedPrefabsByPath,
                    null,
                    levelName,
                    materialOverridePersistContext,
                    ref context.Stats);
                if (_includeEntityObjectGeometryInAuthoringCache)
                {
                    context.Stats.AttachedInstances += AttachCachedPrefabsToScene(
                        meshEntities,
                        context.CachedPrefabsByPath,
                        null,
                        levelName,
                        materialOverridePersistContext,
                        ref context.Stats);
                }
            }

            if (_disableRuntimeLoaders)
            {
                context.Stats.DisabledLoaderComponents += DisableLoadedComponents(brushes, context.CachedPrefabsByPath);
                context.Stats.DisabledLoaderComponents += DisableLoadedComponents(vegetation, context.CachedPrefabsByPath);
                if (_includeEntityObjectGeometryInAuthoringCache)
                    context.Stats.DisabledLoaderComponents += DisableLoadedComponents(meshEntities, context.CachedPrefabsByPath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.MarkSceneDirty(scene);
            return context.Stats;
        }

        void EnsureCachedPrefabsForProfiles(
            IReadOnlyDictionary<string, GeometryImportProfile> profilesByPath,
            IDictionary<string, GameObject> cachedPrefabsByPath,
            ref GeometryCacheStats stats)
        {
            if (profilesByPath == null || profilesByPath.Count == 0)
                return;

            var importService = new CgfImportEditorService();
            var cacheService = new CgfAssetCacheService();
            var lodService = new CgfLodImportService();

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

                    if (cachedPrefabsByPath.TryGetValue(virtualPath, out var knownPrefab) && knownPrefab != null)
                        continue;

                    GameObject incompatiblePrefab;
                    if (!TryLoadParsedFile(virtualPath, out var parsedFile))
                    {
                        stats.FailedImports++;
                        continue;
                    }

                    bool hasCompatibleCache = cacheService.TryLoadCompatibleCachedPrefab(
                        virtualPath,
                        parsedFile,
                        out var existingPrefab);
                    bool isBrushParityCompatible = !profile.IsBrushOrVegetation ||
                                                   FcBrushGeometryPostProcessor.IsBrushRuntimeParityCompatible(existingPrefab);
                    bool isLod0OnlyCompatible = !profile.IsBrushOrVegetation ||
                                                !PrefabHasLodGroup(existingPrefab);

                    if (hasCompatibleCache && isBrushParityCompatible && isLod0OnlyCompatible)
                    {
                        cachedPrefabsByPath[virtualPath] = existingPrefab;
                        stats.ReusedPrefabs++;
                        continue;
                    }

                    var cachePaths = cacheService.GetCachePaths(virtualPath);
                    incompatiblePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cachePaths.PrefabPath);
                    bool hadIncompatibleCache = incompatiblePrefab != null;

                    if (!TryImportAndCachePrefab(
                            virtualPath,
                            parsedFile,
                            profile,
                            importService,
                            cacheService,
                            lodService,
                            out var importedPrefab))
                    {
                        stats.FailedImports++;
                        continue;
                    }

                    cachedPrefabsByPath[virtualPath] = importedPrefab;
                    stats.ImportedPrefabs++;
                    if (hadIncompatibleCache)
                        stats.RebuiltPrefabs++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static bool TryLoadParsedFile(string virtualPath, out CgfFile parsedFile)
        {
            parsedFile = null;
            try
            {
                var sourceBytes = CgfResourceImportService.Instance.LoadRuntimeResourceBytes(virtualPath);
                parsedFile = CgfParser.Parse(sourceBytes);
                parsedFile.SourceVirtualPath = virtualPath;
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[FcLevelBuilder] Failed to parse '{virtualPath}' for cache validation: {e.Message}");
                return false;
            }
        }

        static void RegisterBrushProfilesFromLevelData(
            string levelName,
            Dictionary<string, GeometryImportProfile> profilesByPath,
            ref GeometryCacheStats stats)
        {
            var brushes = FcBrushLoader.LoadBrushes(levelName);
            if (brushes == null || brushes.Count == 0)
                return;

            for (int i = 0; i < brushes.Count; i++)
            {
                var brush = brushes[i];
                if (brush == null)
                    continue;

                stats.SourceInstances++;
                RegisterImportProfile(
                    brush.VirtualPath,
                    importSkeleton: false,
                    importScale: 0.01f,
                    usageFlags: GeometryUsageFlags.Brush,
                    requiresPhysicsCollider: !brush.NoPhysics,
                    profilesByPath: profilesByPath);
            }
        }

        static void RegisterVegetationProfilesFromLevelData(
            string levelName,
            Dictionary<string, GeometryImportProfile> profilesByPath,
            ref GeometryCacheStats stats)
        {
            var supplement = FcLevelSupplementLoader.Load(levelName);
            if (supplement?.VegetationTypes == null || supplement.VegetationTypes.Length == 0 ||
                supplement.VegetationInstances == null || supplement.VegetationInstances.Length == 0)
                return;

            var usedTypeIndices = new HashSet<int>();
            for (int i = 0; i < supplement.VegetationInstances.Length; i++)
                usedTypeIndices.Add(supplement.VegetationInstances[i].Type);

            var pathByType = new Dictionary<int, string>();
            for (int i = 0; i < supplement.VegetationTypes.Length; i++)
            {
                var type = supplement.VegetationTypes[i];
                if (type.Index < 0 || string.IsNullOrWhiteSpace(type.FileName))
                    continue;
                pathByType[type.Index] = type.FileName;
            }

            for (int i = 0; i < supplement.VegetationInstances.Length; i++)
            {
                int typeIndex = supplement.VegetationInstances[i].Type;
                if (!usedTypeIndices.Contains(typeIndex))
                    continue;
                if (!pathByType.TryGetValue(typeIndex, out var virtualPath))
                    continue;

                stats.SourceInstances++;
                RegisterImportProfile(
                    virtualPath,
                    importSkeleton: false,
                    importScale: 0.01f,
                    usageFlags: GeometryUsageFlags.Vegetation,
                    requiresPhysicsCollider: false,
                    profilesByPath: profilesByPath);
            }
        }

        static void RegisterMeshEntityProfilesFromLevelData(
            string levelName,
            Dictionary<string, GeometryImportProfile> profilesByPath,
            ref GeometryCacheStats stats)
        {
            var missionNames = FcLevelLoader.ListMissionNames(levelName);
            if (missionNames == null)
                return;

            foreach (var missionName in missionNames)
            {
                FcMissionDesc mission;
                try { mission = FcLevelLoader.LoadMission(levelName, missionName); }
                catch { continue; }

                if (mission?.Entities == null)
                    continue;

                for (int i = 0; i < mission.Entities.Count; i++)
                {
                    var entity = mission.Entities[i];
                    string cgfPath = entity?.GetModelVirtualPath();
                    if (string.IsNullOrWhiteSpace(cgfPath))
                        continue;

                    stats.SourceInstances++;
                    RegisterImportProfile(
                        cgfPath,
                        importSkeleton: false,
                        importScale: 0.01f,
                        usageFlags: GeometryUsageFlags.MeshEntity,
                        requiresPhysicsCollider: false,
                        profilesByPath: profilesByPath);
                }
            }
        }

        static void RegisterBrushAndVegetationProfiles(
            List<FcBrushInstance> brushes,
            List<FcVegetationInstance> vegetation,
            Dictionary<string, GeometryImportProfile> profilesByPath)
        {
            for (int i = 0; i < brushes.Count; i++)
            {
                var brush = brushes[i];
                RegisterImportProfile(
                    brush != null ? brush.VirtualPath : null,
                    importSkeleton: false,
                    importScale: 0.01f,
                    usageFlags: GeometryUsageFlags.Brush,
                    requiresPhysicsCollider: brush != null && !brush.NoPhysics,
                    profilesByPath: profilesByPath);
            }

            for (int i = 0; i < vegetation.Count; i++)
            {
                RegisterImportProfile(
                    vegetation[i] != null ? vegetation[i].VirtualPath : null,
                    importSkeleton: false,
                    importScale: 0.01f,
                    usageFlags: GeometryUsageFlags.Vegetation,
                    requiresPhysicsCollider: false,
                    profilesByPath: profilesByPath);
            }
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

                RegisterImportProfile(
                    entity.VirtualPath,
                    importSkeleton,
                    importScale,
                    usageFlags: GeometryUsageFlags.MeshEntity,
                    requiresPhysicsCollider: false,
                    profilesByPath: profilesByPath);
            }
        }

        static void RegisterImportProfile(
            string virtualPath,
            bool importSkeleton,
            float importScale,
            GeometryUsageFlags usageFlags,
            bool requiresPhysicsCollider,
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
                var mergedUsage = existing.UsageFlags | usageFlags;
                bool mergedRequiresCollider = existing.RequiresPhysicsCollider || requiresPhysicsCollider;

                profilesByPath[normalized] = new GeometryImportProfile(
                    mergedSkeleton,
                    mergedScale,
                    mergedUsage,
                    mergedRequiresCollider);
                return;
            }

            profilesByPath[normalized] = new GeometryImportProfile(
                importSkeleton,
                importScale,
                usageFlags,
                requiresPhysicsCollider);
        }

        static bool TryImportAndCachePrefab(
            string virtualPath,
            CgfFile parsedFile,
            GeometryImportProfile profile,
            CgfImportEditorService importService,
            CgfAssetCacheService cacheService,
            CgfLodImportService lodService,
            out GameObject cachedPrefab)
        {
            cachedPrefab = null;
            try
            {
                bool lod0OnlyMode = profile.IsBrushOrVegetation;
                List<string> siblingLods = lod0OnlyMode
                    ? new List<string>()
                    : lodService.FindSiblingLodPaths(virtualPath);
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
                    buildGameObject: (result, parsed, rigDef, name, importPhysicsBoxColliders, importRagdollBodies, importScale) =>
                        BuildStaticGameObject(result, parsed, rigDef, name, importPhysicsBoxColliders, importRagdollBodies, importScale, profile),
                    configureLodGroup: lod0OnlyMode
                        ? ConfigureLodGroupNoop
                        : (root, hasSkeleton, importScale, saveToProject) =>
                            lodService.ConfigureLodGroup(
                                root,
                                hasSkeleton,
                                importScale,
                                siblingLods,
                                persistMesh: cacheService.PersistMeshAssetForVirtualPath,
                                materialService: CgfRuntimeImporter.MaterialService),
                    attachAnimations: null,
                    applyPostTransform: go =>
                    {
                        ApplyPostImportTransform(go, profile);
                        CgfMaterialEditorBakeService.PostProcessGameObjectMaterials(go, parsedFile, virtualPath);
                    },
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

        static bool PrefabHasLodGroup(GameObject prefab)
        {
            if (prefab == null)
                return false;

            var lodGroups = prefab.GetComponentsInChildren<LODGroup>(includeInactive: true);
            return lodGroups != null && lodGroups.Length > 0;
        }

        static GameObject BuildStaticGameObject(
            BuildResult result,
            CgfFile parsedFile,
            CgfRigDefinition rigDefinition,
            string name,
            bool importPhysicsBoxColliders,
            bool importRagdollBodies,
            float importScale,
            GeometryImportProfile profile)
        {
            var builder = new CgfGameObjectBuilder();
            var root = builder.Build(new CgfGameObjectBuilder.BuildRequest(
                result,
                parsedFile,
                rigDefinition,
                name,
                materialService: CgfRuntimeImporter.MaterialService)).Root;

            if (profile.IsBrushOrVegetation)
            {
                FcBrushGeometryPostProcessor.ApplyRuntimeParity(
                    root,
                    result,
                    parsedFile,
                    addPhysicsCollider: profile.RequiresPhysicsCollider,
                    importScale: importScale);
            }

            return root;
        }

        static bool TryInstantiateNever(out GameObject go)
        {
            go = null;
            return false;
        }

        static void ApplyPostImportTransform(GameObject go, GeometryImportProfile profile)
        {
            if (go == null)
                return;

            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            if (profile.IsBrushOnly)
                FcBrushGeometryPostProcessor.DisableBackfaceCulling(go);
            if (profile.IsBrushOrVegetation)
                StripLodGroupsKeepLod0(go);

            FcBrushGeometryPostProcessor.StampCacheMetadata(go, profile.IsBrushOrVegetation);
        }

        static void ConfigureLodGroupNoop(GameObject root, bool hasSkeleton, float importScale, bool saveToProject)
        {
            _ = root;
            _ = hasSkeleton;
            _ = importScale;
            _ = saveToProject;
        }

        static void StripLodGroupsKeepLod0(GameObject root)
        {
            if (root == null)
                return;

            var lodGroups = root.GetComponentsInChildren<LODGroup>(includeInactive: true);
            for (int i = 0; i < lodGroups.Length; i++)
            {
                var lodGroup = lodGroups[i];
                if (lodGroup == null)
                    continue;

                var lods = lodGroup.GetLODs();
                for (int li = 0; li < lods.Length; li++)
                {
                    bool keep = li == 0;
                    var renderers = lods[li].renderers;
                    for (int ri = 0; ri < renderers.Length; ri++)
                    {
                        var renderer = renderers[ri];
                        if (renderer != null)
                            renderer.enabled = keep;
                    }
                }

                Object.DestroyImmediate(lodGroup);
            }
        }

        static int AttachCachedPrefabsToScene<T>(
            List<T> components,
            IReadOnlyDictionary<string, GameObject> cachedPrefabsByPath,
            FcLevelMaterialOverrideService materialOverrideService,
            string materialOverrideScopeId,
            MaterialOverridePersistContext materialOverridePersistContext,
            ref GeometryCacheStats stats)
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
                stats.AttachCandidates++;
                if (!cachedPrefabsByPath.TryGetValue(normalizedPath, out var prefab) || prefab == null)
                {
                    stats.AttachMisses++;
                    continue;
                }

                RemoveExistingCachedChildren(component.transform);
                var instance = PrefabUtility.InstantiatePrefab(prefab, component.gameObject.scene) as GameObject;
                if (instance == null)
                {
                    instance = Object.Instantiate(prefab);
                    if (instance != null)
                        SceneManager.MoveGameObjectToScene(instance, component.gameObject.scene);
                }
                if (instance == null)
                {
                    stats.AttachMisses++;
                    Debug.LogWarning($"[FcLevelBuilder] Failed to instantiate cached prefab for '{normalizedPath}' on '{component.name}'.");
                    continue;
                }

                instance.name = "__FCDataCached";
                instance.transform.SetParent(component.transform, worldPositionStays: false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                ConfigureAttachedCachedInstance(
                    component,
                    instance,
                    materialOverrideService,
                    materialOverrideScopeId,
                    materialOverridePersistContext);
                attached++;
            }

            return attached;
        }

        static void ConfigureAttachedCachedInstance(
            Component owner,
            GameObject cachedChild,
            FcLevelMaterialOverrideService materialOverrideService,
            string materialOverrideScopeId,
            MaterialOverridePersistContext materialOverridePersistContext)
        {
            if (owner == null || cachedChild == null)
                return;

            ApplyAuthoringStaticFlags(cachedChild);

            if (owner is FcBrushInstance brush)
            {
                bool enablePhysics = !brush.NoPhysics;
                var colliders = cachedChild.GetComponentsInChildren<Collider>(includeInactive: true);
                for (int i = 0; i < colliders.Length; i++)
                    colliders[i].enabled = enablePhysics;

                ApplyBrushOverrideMaterialToCachedInstance(
                    brush,
                    cachedChild,
                    materialOverrideService,
                    materialOverrideScopeId,
                    materialOverridePersistContext);
                return;
            }

            if (owner is FcVegetationInstance)
            {
                ConfigureVegetationAuthoringCachedInstance(cachedChild);
            }
        }

        static void ApplyBrushOverrideMaterialToCachedInstance(
            FcBrushInstance brush,
            GameObject cachedChild,
            FcLevelMaterialOverrideService materialOverrideService,
            string scopeId,
            MaterialOverridePersistContext materialOverridePersistContext)
        {
            if (brush == null || cachedChild == null || materialOverrideService == null)
                return;

            if (string.IsNullOrWhiteSpace(brush.MaterialOverride) && brush.MaterialId < 0)
                return;

            materialOverrideService.TryResolveBrushMaterialMetadata(
                brush.MaterialOverride,
                brush.MaterialId,
                out var metadata);

            var renderers = cachedChild.GetComponentsInChildren<Renderer>(includeInactive: true);
            bool anyApplied = false;
            List<string> slotDiagnostics = null;
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                if (renderer == null)
                    continue;

                var mats = renderer.sharedMaterials;
                if (mats == null || mats.Length == 0)
                    continue;

                int[] submeshMaterialIds = ResolveRendererSubmeshMaterialIds(cachedChild, renderer);
                if (!materialOverrideService.TryApplyBrushOverrideToRendererSlots(
                        brush.MaterialOverride,
                        brush.MaterialId,
                        scopeId,
                        mats,
                        submeshMaterialIds,
                        out var diagnostics))
                {
                    AppendSlotDiagnostics(ref slotDiagnostics, renderer, diagnostics);
                    continue;
                }

                PersistRuntimeResolvedMaterials(
                    mats,
                    brush,
                    materialOverridePersistContext);
                renderer.sharedMaterials = mats;
                anyApplied = true;
                AppendSlotDiagnostics(ref slotDiagnostics, renderer, diagnostics);
            }

            if (anyApplied || (slotDiagnostics != null && slotDiagnostics.Count > 0))
                ApplyBrushMaterialMetadata(cachedChild, metadata, slotDiagnostics);
        }

        static void PersistRuntimeResolvedMaterials(
            Material[] materials,
            FcBrushInstance brush,
            MaterialOverridePersistContext context)
        {
            if (materials == null || materials.Length == 0)
                return;

            for (int i = 0; i < materials.Length; i++)
            {
                materials[i] = GetOrPersistBrushOverrideMaterial(materials[i], brush, context, i);
            }
        }

        static int[] ResolveRendererSubmeshMaterialIds(GameObject cachedRoot, Renderer renderer)
        {
            if (cachedRoot == null || renderer == null)
                return null;

            var rootMeta = cachedRoot.GetComponent<FcCachedGeometryMetadata>();
            if (rootMeta == null ||
                rootMeta.SubmeshMaterialIds == null ||
                renderer.transform != cachedRoot.transform)
            {
                return null;
            }

            return rootMeta.SubmeshMaterialIds;
        }

        static MaterialOverridePersistContext BuildMaterialOverridePersistContext(string levelName)
        {
            string levelDir = GetLevelAssetCacheDir(levelName);
            string materialDir = $"{levelDir}/MaterialOverrides";
            string textureDir = $"{materialDir}/Textures";
            EnsureDir(materialDir);
            EnsureDir(textureDir);
            return new MaterialOverridePersistContext(materialDir, textureDir);
        }

        static Material GetOrPersistBrushOverrideMaterial(
            Material sourceMaterial,
            FcBrushInstance brush,
            MaterialOverridePersistContext context,
            int slotIndex = -1)
        {
            if (sourceMaterial == null)
                return null;
            if (context == null)
                return sourceMaterial;
            if (EditorUtility.IsPersistent(sourceMaterial))
                return sourceMaterial;

            string key = BuildBrushOverrideKey(brush, sourceMaterial, slotIndex);
            if (context.MaterialByKey.TryGetValue(key, out var cached) && cached != null)
                return cached;

            string materialFile = $"{SanitizePathSegment(key)}.mat";
            string materialPath = $"{context.MaterialDir}/{materialFile}";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (existing != null)
            {
                context.MaterialByKey[key] = existing;
                return existing;
            }

            var persisted = PersistMaterialAsset(
                sourceMaterial,
                materialPath,
                context.TextureDir,
                context.TextureByKey);
            if (persisted != null)
                context.MaterialByKey[key] = persisted;
            return persisted != null ? persisted : sourceMaterial;
        }

        static string BuildBrushOverrideKey(FcBrushInstance brush, Material sourceMaterial, int slotIndex = -1)
        {
            if (brush == null)
                return "brush_override_unknown";

            string overrideToken = string.IsNullOrWhiteSpace(brush.MaterialOverride)
                ? "none"
                : ImportAssetPaths.NormalizeVirtualPath(brush.MaterialOverride);

            string sourceName = sourceMaterial == null || string.IsNullOrWhiteSpace(sourceMaterial.name)
                ? "material"
                : SanitizePathSegment(sourceMaterial.name.ToLowerInvariant());
            return $"brush_override_{overrideToken}_id_{brush.MaterialId}_slot_{slotIndex}_{sourceName}";
        }

        static Material PersistMaterialAsset(
            Material source,
            string materialPath,
            string textureDir,
            Dictionary<string, Texture2D> textureByKey)
        {
            if (source == null)
                return null;

            var clone = new Material(source) { name = source.name };

            var shader = clone.shader;
            if (shader != null)
            {
                int propertyCount = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < propertyCount; i++)
                {
                    if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                        continue;

                    string propertyName = ShaderUtil.GetPropertyName(shader, i);
                    if (string.IsNullOrEmpty(propertyName))
                        continue;

                    var sourceTexture = clone.GetTexture(propertyName) as Texture2D;
                    if (sourceTexture == null)
                        continue;

                    var persistedTexture = PersistTextureAsset(
                        sourceTexture,
                        propertyName,
                        textureDir,
                        textureByKey);
                    if (persistedTexture != null)
                        clone.SetTexture(propertyName, persistedTexture);
                }
            }

            if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) != null)
                AssetDatabase.DeleteAsset(materialPath);

            AssetDatabase.CreateAsset(clone, materialPath);
            return AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        }

        static Texture2D PersistTextureAsset(
            Texture2D source,
            string propertyName,
            string textureDir,
            Dictionary<string, Texture2D> textureByKey)
        {
            if (source == null)
                return null;
            if (EditorUtility.IsPersistent(source))
                return source;

            string key = source.GetInstanceID().ToString();
            if (textureByKey.TryGetValue(key, out var cached) && cached != null)
                return cached;

            string textureName = SanitizePathSegment(string.IsNullOrWhiteSpace(source.name) ? "tex" : source.name);
            string propertyToken = SanitizePathSegment(string.IsNullOrWhiteSpace(propertyName) ? "map" : propertyName);
            string texturePath = $"{textureDir}/{textureName}_{propertyToken}_{source.GetInstanceID()}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (existing != null)
            {
                textureByKey[key] = existing;
                return existing;
            }

            if (!TryDuplicateTextureForAsset(source, out var clone))
                return null;

            clone.name = source.name;
            AssetDatabase.CreateAsset(clone, texturePath);

            var persisted = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            textureByKey[key] = persisted;
            return persisted;
        }

        static bool TryDuplicateTextureForAsset(Texture2D source, out Texture2D clone)
        {
            clone = null;
            if (source == null)
                return false;

            if (source.isReadable)
            {
                clone = Object.Instantiate(source);
                return clone != null;
            }

            RenderTexture rt = null;
            var previous = RenderTexture.active;
            try
            {
                rt = RenderTexture.GetTemporary(
                    source.width,
                    source.height,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Default);

                Graphics.Blit(source, rt);
                RenderTexture.active = rt;

                clone = new Texture2D(
                    source.width,
                    source.height,
                    TextureFormat.RGBA32,
                    mipChain: source.mipmapCount > 1,
                    linear: false);

                clone.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                clone.Apply(updateMipmaps: source.mipmapCount > 1, makeNoLongerReadable: false);

                clone.wrapMode = source.wrapMode;
                clone.wrapModeU = source.wrapModeU;
                clone.wrapModeV = source.wrapModeV;
                clone.wrapModeW = source.wrapModeW;
                clone.filterMode = source.filterMode;
                clone.anisoLevel = source.anisoLevel;
                clone.mipMapBias = source.mipMapBias;
                return true;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
            }
        }

        static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "_";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '/' || c == '\\' || System.Array.IndexOf(invalid, c) >= 0)
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }

        static void ApplyBrushMaterialMetadata(
            GameObject visualRoot,
            FcLevelMaterialOverrideService.BrushMaterialMetadata metadata,
            List<string> slotDiagnostics = null)
        {
            if (visualRoot == null)
                return;

            var rootMeta = visualRoot.GetComponent<FcLevelMaterialMetadata>();
            if (rootMeta == null)
                rootMeta = visualRoot.AddComponent<FcLevelMaterialMetadata>();
            rootMeta.SetMetadata(metadata);
            if (slotDiagnostics != null && slotDiagnostics.Count > 0)
                rootMeta.SetSlotResolutionDiagnostics(slotDiagnostics.ToArray());

            var colliders = visualRoot.GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < colliders.Length; i++)
            {
                var col = colliders[i];
                if (col == null)
                    continue;

                var colMeta = col.gameObject.GetComponent<FcLevelMaterialMetadata>();
                if (colMeta == null)
                    colMeta = col.gameObject.AddComponent<FcLevelMaterialMetadata>();
                colMeta.SetMetadata(metadata);
            }
        }

        static void CollectMaterialSlotDiagnostics(
            GameObject cachedRoot,
            ref AuthoringValidationStats stats,
            List<string> unresolvedSample)
        {
            if (cachedRoot == null)
                return;

            var meta = cachedRoot.GetComponent<FcLevelMaterialMetadata>();
            if (meta == null)
                return;

            var lines = meta.SlotResolutionDiagnostics;
            if (lines == null || lines.Length == 0)
                return;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                stats.MaterialSlotDiagnosticCount++;

                bool applied = ContainsToken(line, "applied", "True");
                bool targeted = ContainsToken(line, "targeted", "True");
                string outcome = ExtractTokenValue(line, "outcome");

                if (applied)
                    stats.MaterialSlotAppliedCount++;
                if (targeted && !applied)
                    stats.MaterialSlotTargetedMissCount++;

                bool unresolved = string.Equals(outcome, "unresolved", System.StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(outcome, "fallback-failed", System.StringComparison.OrdinalIgnoreCase);
                if (unresolved)
                {
                    stats.MaterialSlotUnresolvedCount++;
                    unresolvedSample?.Add(line);
                }
            }
        }

        static void AppendSlotDiagnostics(
            ref List<string> destination,
            Renderer renderer,
            FcLevelMaterialOverrideService.BrushSlotResolutionInfo[] diagnostics)
        {
            if (diagnostics == null || diagnostics.Length == 0)
                return;

            if (destination == null)
                destination = new List<string>(diagnostics.Length);

            string rendererName = renderer != null && renderer.gameObject != null
                ? renderer.gameObject.name
                : "<null-renderer>";

            for (int i = 0; i < diagnostics.Length; i++)
            {
                var d = diagnostics[i];
                destination.Add(
                    $"renderer={rendererName};slot={d.SlotIndex};submeshMatId={d.SubmeshMaterialId};" +
                    $"targeted={d.Targeted};applied={d.Applied};outcome={d.Outcome};" +
                    $"in={d.InputMaterialName};out={d.OutputMaterialName};detail={d.Detail}");
            }
        }

        static bool ContainsToken(string line, string key, string expectedValue)
        {
            string value = ExtractTokenValue(line, key);
            return string.Equals(value, expectedValue, System.StringComparison.OrdinalIgnoreCase);
        }

        static string ExtractTokenValue(string line, string key)
        {
            if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(key))
                return string.Empty;

            string token = key + "=";
            int start = line.IndexOf(token, System.StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return string.Empty;

            start += token.Length;
            int end = line.IndexOf(';', start);
            if (end < 0)
                end = line.Length;

            if (end <= start)
                return string.Empty;

            return line.Substring(start, end - start).Trim();
        }

        static MaterialSlotDiagnosticsSummary CollectMaterialSlotDiagnosticsSummary(Scene scene)
        {
            var metas = CollectComponentsInScene<FcLevelMaterialMetadata>(scene);
            int total = 0;
            int applied = 0;
            int targetedMiss = 0;
            int unresolved = 0;
            int instanceCount = 0;
            int instanceOverrideApplied = 0;
            int instanceFallback = 0;
            int instanceTargetedMiss = 0;
            int instanceUnresolved = 0;
            var outcomes = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            var unresolvedLines = new Dictionary<string, int>(System.StringComparer.Ordinal);
            var resolutionSources = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            var shaderFamilies = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < metas.Count; i++)
            {
                var meta = metas[i];
                if (meta == null)
                    continue;

                string shaderFamily = ClassifyMaterialShaderFamily(meta.MaterialShader);
                if (shaderFamilies.TryGetValue(shaderFamily, out int sf))
                    shaderFamilies[shaderFamily] = sf + 1;
                else
                    shaderFamilies[shaderFamily] = 1;

                var lines = meta.SlotResolutionDiagnostics;
                if (lines == null || lines.Length == 0)
                    continue;

                bool metaAnyApplied = false;
                bool metaAnyTargeted = false;
                bool metaAnyUnresolved = false;

                for (int j = 0; j < lines.Length; j++)
                {
                    string line = lines[j];
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    total++;
                    bool isApplied = ContainsToken(line, "applied", "True");
                    bool isTargeted = ContainsToken(line, "targeted", "True");
                    string outcome = ExtractTokenValue(line, "outcome");
                    if (string.IsNullOrWhiteSpace(outcome))
                        outcome = "<empty>";

                    if (isApplied)
                    {
                        applied++;
                        metaAnyApplied = true;
                    }
                    if (isTargeted && !isApplied)
                    {
                        targetedMiss++;
                        metaAnyTargeted = true;
                    }
                    else if (isTargeted)
                    {
                        metaAnyTargeted = true;
                    }

                    if (string.Equals(outcome, "unresolved", System.StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(outcome, "fallback-failed", System.StringComparison.OrdinalIgnoreCase))
                    {
                        unresolved++;
                        metaAnyUnresolved = true;
                        if (unresolvedLines.TryGetValue(line, out int unresolvedCount))
                            unresolvedLines[line] = unresolvedCount + 1;
                        else
                            unresolvedLines[line] = 1;
                    }

                    if (outcomes.TryGetValue(outcome, out int count))
                        outcomes[outcome] = count + 1;
                    else
                        outcomes[outcome] = 1;
                }

                instanceCount++;
                if (metaAnyApplied)
                    instanceOverrideApplied++;
                else if (metaAnyTargeted)
                    instanceTargetedMiss++;
                else
                    instanceFallback++;

                if (metaAnyUnresolved)
                    instanceUnresolved++;

                string resolutionSource = string.IsNullOrWhiteSpace(meta.ResolutionSource)
                    ? "<none>"
                    : meta.ResolutionSource.Trim();
                if (resolutionSources.TryGetValue(resolutionSource, out int sourceCount))
                    resolutionSources[resolutionSource] = sourceCount + 1;
                else
                    resolutionSources[resolutionSource] = 1;
            }

            return new MaterialSlotDiagnosticsSummary(
                total,
                applied,
                targetedMiss,
                unresolved,
                instanceCount,
                instanceOverrideApplied,
                instanceFallback,
                instanceTargetedMiss,
                instanceUnresolved,
                outcomes,
                unresolvedLines,
                resolutionSources,
                shaderFamilies);
        }

        static string ClassifyMaterialShaderFamily(string shader)
        {
            if (string.IsNullOrWhiteSpace(shader))
                return "none";
            string s = shader.Trim().ToLowerInvariant();
            if (s == "nodraw" || s == "no_draw") return "nodraw";
            if (s.Contains("decalmodulate")) return "modulate-decal";
            if (s.Contains("decal")) return "decal";
            if (s.Contains("plants")) return "plants";
            if (s.Contains("bark")) return "bark";
            if (s.Contains("glass") || s.Contains("refr")) return "glass";
            if (s.Contains("alphablend")) return "alphablend";
            if (s.Contains("glow") || s.Contains("selfillum")) return "glow-decal";
            if (s.Contains("textureshiftt05")) return "uvscroll";
            if (s.Contains("bumpspec")) return "bumpspec";
            if (s.Contains("bumpdiffuse")) return "bumpdiffuse";
            if (s.Contains("templmodel") || s.Contains("diffuse")) return "diffuse";
            return "unknown";
        }

        static void PersistMaterialSlotDiagnosticsToLayoutData(
            string levelName,
            string missionName,
            MaterialSlotDiagnosticsSummary summary)
        {
            if (!TryResolveLayoutDataPath(levelName, missionName, out string path))
                return;

            var layout = AssetDatabase.LoadAssetAtPath<FcLevelLayoutDataV2>(path);
            if (layout == null)
            {
                Debug.LogWarning($"[FcLevelBuilder] Layout data not found for material diagnostics at '{path}'.");
                return;
            }

            var report = layout.ImportReport;
            report.BrushMaterialSlotDiagnosticCount = summary.TotalCount;
            report.BrushMaterialSlotAppliedCount = summary.AppliedCount;
            report.BrushMaterialSlotTargetedMissCount = summary.TargetedMissCount;
            report.BrushMaterialSlotUnresolvedCount = summary.UnresolvedCount;
            report.BrushMaterialInstanceDiagnosticCount = summary.InstanceCount;
            report.BrushMaterialInstanceOverrideAppliedCount = summary.InstanceOverrideAppliedCount;
            report.BrushMaterialInstanceFallbackCount = summary.InstanceFallbackCount;
            report.BrushMaterialInstanceTargetedMissCount = summary.InstanceTargetedMissCount;
            report.BrushMaterialInstanceUnresolvedCount = summary.InstanceUnresolvedCount;
            report.BrushMaterialSlotOutcomeCounts = ToSortedCountEntries(summary.OutcomeCounts);
            report.BrushMaterialSlotUnresolvedSamples = ToTopCountEntries(summary.UnresolvedLineCounts, 12);
            report.BrushMaterialResolutionSourceCounts = ToSortedCountEntries(summary.ResolutionSourceCounts);
            report.BrushMaterialShaderFamilyCounts = ToSortedCountEntries(summary.ShaderFamilyCounts);
            layout.ImportReport = report;

            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();
        }

        static bool TryLoadLayoutImportReport(
            string levelName,
            string missionName,
            out FcLevelLayoutDataV2.ImportReportData report,
            out string path)
        {
            report = default;
            path = string.Empty;
            if (!TryResolveLayoutDataPath(levelName, missionName, out path))
                return false;

            var layout = AssetDatabase.LoadAssetAtPath<FcLevelLayoutDataV2>(path);
            if (layout == null)
                return false;

            report = layout.ImportReport;
            return true;
        }

        static bool TryResolveLayoutDataPath(string levelName, string missionName, out string path)
        {
            path = string.Empty;
            if (string.IsNullOrWhiteSpace(levelName))
                return false;

            const string dir = "Assets/FCData/Levels";
            if (!AssetDatabase.IsValidFolder(dir))
                return false;

            if (!string.IsNullOrWhiteSpace(missionName))
            {
                string exactPath = $"{dir}/{levelName}_{missionName}_v2.asset";
                if (AssetDatabase.LoadAssetAtPath<FcLevelLayoutDataV2>(exactPath) != null)
                {
                    path = exactPath;
                    return true;
                }
            }

            string prefix = $"{levelName}_";
            var guids = AssetDatabase.FindAssets("t:FcLevelLayoutDataV2", new[] { dir });
            for (int i = 0; i < guids.Length; i++)
            {
                string candidate = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                string fileName = Path.GetFileNameWithoutExtension(candidate);
                if (string.IsNullOrWhiteSpace(fileName))
                    continue;

                if (!fileName.EndsWith("_v2", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!fileName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                path = candidate;
                return true;
            }

            return false;
        }

        static FcLevelLayoutDataV2.ImportReportData.CountEntry[] ToSortedCountEntries(
            Dictionary<string, int> counts)
        {
            if (counts == null || counts.Count == 0)
                return System.Array.Empty<FcLevelLayoutDataV2.ImportReportData.CountEntry>();

            var keys = new List<string>(counts.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var result = new FcLevelLayoutDataV2.ImportReportData.CountEntry[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                result[i] = new FcLevelLayoutDataV2.ImportReportData.CountEntry
                {
                    Key = key,
                    Count = counts[key],
                };
            }

            return result;
        }

        static FcLevelLayoutDataV2.ImportReportData.CountEntry[] ToTopCountEntries(
            Dictionary<string, int> counts,
            int maxCount)
        {
            if (counts == null || counts.Count == 0 || maxCount <= 0)
                return System.Array.Empty<FcLevelLayoutDataV2.ImportReportData.CountEntry>();

            var entries = new List<KeyValuePair<string, int>>(counts);
            entries.Sort((a, b) =>
            {
                int byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : System.StringComparer.Ordinal.Compare(a.Key, b.Key);
            });

            int take = Mathf.Min(maxCount, entries.Count);
            var result = new FcLevelLayoutDataV2.ImportReportData.CountEntry[take];
            for (int i = 0; i < take; i++)
            {
                result[i] = new FcLevelLayoutDataV2.ImportReportData.CountEntry
                {
                    Key = entries[i].Key,
                    Count = entries[i].Value,
                };
            }

            return result;
        }

        static void ConfigureVegetationAuthoringCachedInstance(GameObject cachedChild)
        {
            if (cachedChild == null)
                return;

            // Vegetation authoring scene should not carry runtime collider/LOD behavior.
            var colliders = cachedChild.GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;

            var lodGroups = cachedChild.GetComponentsInChildren<LODGroup>(includeInactive: true);
            for (int i = 0; i < lodGroups.Length; i++)
            {
                var lodGroup = lodGroups[i];
                if (lodGroup == null)
                    continue;

                var lods = lodGroup.GetLODs();
                for (int li = 0; li < lods.Length; li++)
                {
                    bool isLod0 = li == 0;
                    var renderers = lods[li].renderers;
                    for (int ri = 0; ri < renderers.Length; ri++)
                    {
                        var renderer = renderers[ri];
                        if (renderer != null)
                            renderer.enabled = isLod0;
                    }
                }

                lodGroup.enabled = false;
            }
        }

        static void ApplyAuthoringStaticFlags(GameObject root)
        {
            if (root == null)
                return;

            var authoringFlags = GetAuthoringStaticFlags();

            var transforms = root.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var tr = transforms[i];
                if (tr == null)
                    continue;

                var go = tr.gameObject;
                var current = GameObjectUtility.GetStaticEditorFlags(go);
                GameObjectUtility.SetStaticEditorFlags(go, current | authoringFlags);
            }
        }

        static StaticEditorFlags GetAuthoringStaticFlags()
        {
            return
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.NavigationStatic |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic |
                StaticEditorFlags.ContributeGI;
        }

        static void ApplyAuthoringStaticFlagsToTerrainAndWater(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null)
                    continue;

                var terrain = root.GetComponentInChildren<Terrain>(includeInactive: true);
                if (terrain != null)
                    ApplyAuthoringStaticFlags(terrain.gameObject);

                var water = FindChildByName(root.transform, "Water");
                if (water != null)
                    ApplyAuthoringStaticFlags(water.gameObject);
            }
        }

        static Transform FindChildByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;

            var queue = new Queue<Transform>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current != null && string.Equals(current.name, name, System.StringComparison.Ordinal))
                    return current;

                if (current == null)
                    continue;

                for (int i = 0; i < current.childCount; i++)
                    queue.Enqueue(current.GetChild(i));
            }

            return null;
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
                if (FindChildByName(component.transform, "__FCDataCached") == null)
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

        static FcLevelMaterialOverrideService ConfigureSceneMaterialOverrides(Scene scene, string levelName)
        {
            var services = CollectComponentsInScene<FcLevelMaterialOverrideService>(scene);
            if (services.Count == 0 || services[0] == null)
            {
                Debug.LogWarning("[FcLevelBuilder] FcLevelMaterialOverrideService not found in scene.");
                return null;
            }

            var service = services[0];
            var supplement = FcLevelSupplementLoader.Load(levelName);
            service.Configure(levelName, supplement);
            return service;
        }

        static int StripRuntimeServicesForAuthoring(Scene scene)
        {
            int removed = 0;
            var services = CollectComponentsInScene<Behaviour>(scene);
            for (int i = 0; i < services.Count; i++)
            {
                var component = services[i];
                if (component == null)
                    continue;

                if (!IsAuthoringRuntimeService(component))
                    continue;

                Object.DestroyImmediate(component);
                removed++;
            }

            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                RemoveEmptyServiceObjectsRecursive(roots[i].transform);

            return removed;
        }

        static bool IsAuthoringRuntimeService(Behaviour behaviour)
        {
            return behaviour is FcLevelLoadService ||
                   behaviour is FcLevelCacheService ||
                   behaviour is FcLevelResourceService ||
                   behaviour is FcBrushLoadService ||
                   behaviour is FcVegetationLoadService ||
                   behaviour is FcMeshLoadService ||
                   behaviour is FcEntityLoadService ||
                   behaviour is FcAnimationLoadService ||
                   behaviour is FcVegetationTerrainService ||
                   behaviour is FcTerrainTextureService ||
                   behaviour is FcLevelMaterialOverrideService ||
                   behaviour is FcLevelEnvironment;
        }

        static string BuildAuthoringSceneValidationReport(
            Scene scene,
            string levelName,
            string missionName)
        {
            var stats = CollectAuthoringValidationStats(scene, levelName, missionName);
            var sb = new StringBuilder(512);
            sb.AppendLine($"Authoring validation for scene '{scene.name}':");
            if (stats.IsLikelyRuntimeScene)
                sb.AppendLine("[WARN] Scene looks like runtime build. Use 'Build Authoring Scene (FCData Cache)' first.");
            AppendValidationLine(sb, stats.HasLevelRoot, "Root object 'Level_<name>' exists");
            AppendValidationLine(sb, stats.HasTerrain, "Terrain exists");
            AppendValidationLine(sb, stats.HasBrushesAuthoringRoot, "Root 'Brushes_Authoring' exists");
            AppendValidationLine(sb, stats.HasVegetationAuthoringRoot, "Root 'Vegetation_Authoring' exists");
            AppendValidationLine(
                sb,
                stats.RuntimeServiceCount == 0,
                stats.RuntimeServiceCount == 0
                    ? "Runtime services stripped (0)"
                    : $"Runtime services present ({stats.RuntimeServiceCount})");
            AppendValidationLine(
                sb,
                stats.CachedChildrenMissingStaticFlags == 0,
                $"Cached visuals static-flagged ({stats.CachedChildrenMissingStaticFlags} missing of {stats.CachedChildCount})");
            AppendValidationLine(
                sb,
                stats.VegetationCachedWithEnabledCollider == 0,
                $"Vegetation cached colliders disabled ({stats.VegetationCachedWithEnabledCollider} enabled of {stats.VegetationCachedCount})");
            AppendValidationLine(
                sb,
                stats.VegetationCachedWithEnabledLodGroup == 0,
                $"Vegetation cached LODGroups disabled ({stats.VegetationCachedWithEnabledLodGroup} enabled of {stats.VegetationCachedCount})");
            AppendValidationLine(
                sb,
                stats.MaterialSlotDiagnosticCount > 0,
                $"Brush slot diagnostics captured ({stats.MaterialSlotDiagnosticCount} entries; applied={stats.MaterialSlotAppliedCount}; targeted-miss={stats.MaterialSlotTargetedMissCount})");
            AppendValidationLine(
                sb,
                stats.MaterialInstanceDiagnosticCount > 0,
                $"Brush instance diagnostics captured ({stats.MaterialInstanceDiagnosticCount}; override-applied={stats.MaterialInstanceOverrideAppliedCount}; fallback={stats.MaterialInstanceFallbackCount}; targeted-miss={stats.MaterialInstanceTargetedMissCount})");
            AppendValidationLine(
                sb,
                stats.MaterialSlotUnresolvedCount == 0,
                $"Brush slot unresolved diagnostics ({stats.MaterialSlotUnresolvedCount})");
            AppendValidationLine(
                sb,
                stats.MaterialInstanceUnresolvedCount == 0,
                $"Brush instance unresolved diagnostics ({stats.MaterialInstanceUnresolvedCount})");
            AppendValidationLine(
                sb,
                stats.HasLayoutData,
                stats.HasLayoutData
                    ? $"Layout data present ({stats.LayoutDataPath})"
                    : "Layout data present (missing)");
            if (stats.HasLayoutData)
            {
                AppendValidationLine(
                    sb,
                    stats.LayoutSlotDiagnosticCount == stats.MaterialSlotDiagnosticCount,
                    $"Layout slot diagnostics synced (layout={stats.LayoutSlotDiagnosticCount}, scene={stats.MaterialSlotDiagnosticCount})");
                AppendValidationLine(
                    sb,
                    stats.LayoutInstanceDiagnosticCount == stats.MaterialInstanceDiagnosticCount,
                    $"Layout instance diagnostics synced (layout={stats.LayoutInstanceDiagnosticCount}, scene={stats.MaterialInstanceDiagnosticCount})");
                AppendValidationLine(
                    sb,
                    stats.LayoutSlotDiagnosticCount == 0 || stats.LayoutSlotOutcomeBucketCount > 0,
                    $"Layout slot summary (applied={stats.LayoutSlotAppliedCount}; targeted-miss={stats.LayoutSlotTargetedMissCount}; outcomes={stats.LayoutSlotOutcomeBucketCount}; unresolved-samples={stats.LayoutSlotUnresolvedSampleCount})");
                AppendValidationLine(
                    sb,
                    stats.LayoutInstanceDiagnosticCount == 0 || stats.LayoutResolutionSourceBucketCount > 0,
                    $"Layout instance summary (override-applied={stats.LayoutInstanceOverrideAppliedCount}; fallback={stats.LayoutInstanceFallbackCount}; targeted-miss={stats.LayoutInstanceTargetedMissCount}; sources={stats.LayoutResolutionSourceBucketCount})");
                AppendValidationLine(
                    sb,
                    stats.LayoutSlotUnresolvedCount == 0,
                    $"Layout slot unresolved diagnostics ({stats.LayoutSlotUnresolvedCount})");
                AppendValidationLine(
                    sb,
                    stats.LayoutInstanceUnresolvedCount == 0,
                    $"Layout instance unresolved diagnostics ({stats.LayoutInstanceUnresolvedCount})");
            }
            AppendValidationLine(
                sb,
                stats.FcDataDependencyCount == 0,
                $"FCData dependencies after finalize ({stats.FcDataDependencyCount})");
            if (stats.MaterialSlotUnresolvedSample != null && stats.MaterialSlotUnresolvedSample.Length > 0)
            {
                sb.AppendLine("Material diagnostics sample:");
                for (int i = 0; i < stats.MaterialSlotUnresolvedSample.Length; i++)
                    sb.AppendLine($"- {stats.MaterialSlotUnresolvedSample[i]}");
            }
            if (stats.FcDataDependencySample != null && stats.FcDataDependencySample.Length > 0)
            {
                sb.AppendLine("FCData sample:");
                for (int i = 0; i < stats.FcDataDependencySample.Length; i++)
                    sb.AppendLine($"- {stats.FcDataDependencySample[i]}");
            }

            sb.AppendLine();
            sb.AppendLine(stats.Warnings == 0
                ? "Result: PASS"
                : $"Result: WARN ({stats.Warnings})");
            return sb.ToString().TrimEnd();
        }

        static AuthoringValidationStats CollectAuthoringValidationStats(
            Scene scene,
            string levelName,
            string missionName)
        {
            var stats = new AuthoringValidationStats();
            var roots = scene.GetRootGameObjects();
            GameObject levelRoot = null;
            GameObject brushesRoot = null;
            GameObject vegetationRoot = null;
            GameObject brushesRuntimeRoot = null;
            Terrain terrain = null;

            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null)
                    continue;

                if (levelRoot == null && root.name.StartsWith("Level_", System.StringComparison.Ordinal))
                    levelRoot = root;
                if (brushesRoot == null)
                {
                    var found = FindChildByName(root.transform, "Brushes_Authoring");
                    if (found != null)
                        brushesRoot = found.gameObject;
                }
                if (brushesRuntimeRoot == null)
                {
                    var found = FindChildByName(root.transform, "Brushes");
                    if (found != null)
                        brushesRuntimeRoot = found.gameObject;
                }
                if (vegetationRoot == null)
                {
                    var found = FindChildByName(root.transform, "Vegetation_Authoring");
                    if (found != null)
                        vegetationRoot = found.gameObject;
                }

                if (terrain == null)
                    terrain = root.GetComponentInChildren<Terrain>(includeInactive: true);
            }

            stats.HasLevelRoot = levelRoot != null;
            stats.HasBrushesAuthoringRoot = brushesRoot != null;
            stats.HasVegetationAuthoringRoot = vegetationRoot != null;
            stats.HasBrushesRuntimeRoot = brushesRuntimeRoot != null;
            stats.HasTerrain = terrain != null;

            var behaviours = CollectComponentsInScene<Behaviour>(scene);
            var unresolvedMaterialDiagnostics = new List<string>();
            for (int i = 0; i < behaviours.Count; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;
                if (IsAuthoringRuntimeService(behaviour))
                    stats.RuntimeServiceCount++;
            }

            var authoringFlags = GetAuthoringStaticFlags();
            for (int i = 0; i < behaviours.Count; i++)
            {
                if (behaviours[i] is FcBrushInstance || behaviours[i] is FcVegetationInstance || behaviours[i] is FcMeshEntity)
                {
                    var owner = behaviours[i];
                    if (owner == null)
                        continue;

                    var cached = FindChildByName(owner.transform, "__FCDataCached");
                    if (cached == null)
                        continue;

                    stats.CachedChildCount++;
                    CollectMaterialSlotDiagnostics(cached.gameObject, ref stats, unresolvedMaterialDiagnostics);
                    var all = cached.GetComponentsInChildren<Transform>(includeInactive: true);
                    for (int t = 0; t < all.Length; t++)
                    {
                        var go = all[t] != null ? all[t].gameObject : null;
                        if (go == null)
                            continue;
                        var current = GameObjectUtility.GetStaticEditorFlags(go);
                        if ((current & authoringFlags) != authoringFlags)
                        {
                            stats.CachedChildrenMissingStaticFlags++;
                            break;
                        }
                    }
                }
            }

            if (vegetationRoot != null)
            {
                var vegetationOwners = vegetationRoot.GetComponentsInChildren<FcVegetationInstance>(includeInactive: true);
                for (int i = 0; i < vegetationOwners.Length; i++)
                {
                    var owner = vegetationOwners[i];
                    if (owner == null)
                        continue;

                    var cached = FindChildByName(owner.transform, "__FCDataCached");
                    if (cached == null)
                        continue;

                    stats.VegetationCachedCount++;
                    var colliders = cached.GetComponentsInChildren<Collider>(includeInactive: true);
                    for (int c = 0; c < colliders.Length; c++)
                    {
                        if (colliders[c] != null && colliders[c].enabled)
                        {
                            stats.VegetationCachedWithEnabledCollider++;
                            break;
                        }
                    }

                    var lodGroups = cached.GetComponentsInChildren<LODGroup>(includeInactive: true);
                    for (int l = 0; l < lodGroups.Length; l++)
                    {
                        if (lodGroups[l] != null && lodGroups[l].enabled)
                        {
                            stats.VegetationCachedWithEnabledLodGroup++;
                            break;
                        }
                    }
                }
            }

            var audit = BuildSceneDependencyAuditData(scene);
            stats.FcDataDependencyCount = audit.FcDataPaths.Count;
            var fcSample = new List<string>(audit.FcDataPaths);
            fcSample.Sort(System.StringComparer.Ordinal);
            const int maxSample = 5;
            if (fcSample.Count > maxSample)
                fcSample.RemoveRange(maxSample, fcSample.Count - maxSample);
            stats.FcDataDependencySample = fcSample.ToArray();

            unresolvedMaterialDiagnostics.Sort(System.StringComparer.Ordinal);
            if (unresolvedMaterialDiagnostics.Count > maxSample)
                unresolvedMaterialDiagnostics.RemoveRange(maxSample, unresolvedMaterialDiagnostics.Count - maxSample);
            stats.MaterialSlotUnresolvedSample = unresolvedMaterialDiagnostics.ToArray();

            var slotSummary = CollectMaterialSlotDiagnosticsSummary(scene);
            stats.MaterialSlotDiagnosticCount = slotSummary.TotalCount;
            stats.MaterialSlotAppliedCount = slotSummary.AppliedCount;
            stats.MaterialSlotTargetedMissCount = slotSummary.TargetedMissCount;
            stats.MaterialSlotUnresolvedCount = slotSummary.UnresolvedCount;
            stats.MaterialInstanceDiagnosticCount = slotSummary.InstanceCount;
            stats.MaterialInstanceOverrideAppliedCount = slotSummary.InstanceOverrideAppliedCount;
            stats.MaterialInstanceFallbackCount = slotSummary.InstanceFallbackCount;
            stats.MaterialInstanceTargetedMissCount = slotSummary.InstanceTargetedMissCount;
            stats.MaterialInstanceUnresolvedCount = slotSummary.InstanceUnresolvedCount;
            stats.MaterialResolutionSourceBucketCount = slotSummary.ResolutionSourceCounts != null
                ? slotSummary.ResolutionSourceCounts.Count
                : 0;

            if (TryLoadLayoutImportReport(levelName, missionName, out var layoutReport, out var layoutPath))
            {
                stats.HasLayoutData = true;
                stats.LayoutDataPath = layoutPath ?? string.Empty;
                stats.LayoutSlotDiagnosticCount = layoutReport.BrushMaterialSlotDiagnosticCount;
                stats.LayoutSlotAppliedCount = layoutReport.BrushMaterialSlotAppliedCount;
                stats.LayoutSlotTargetedMissCount = layoutReport.BrushMaterialSlotTargetedMissCount;
                stats.LayoutSlotUnresolvedCount = layoutReport.BrushMaterialSlotUnresolvedCount;
                stats.LayoutSlotOutcomeBucketCount = layoutReport.BrushMaterialSlotOutcomeCounts?.Length ?? 0;
                stats.LayoutSlotUnresolvedSampleCount = layoutReport.BrushMaterialSlotUnresolvedSamples?.Length ?? 0;
                stats.LayoutInstanceDiagnosticCount = layoutReport.BrushMaterialInstanceDiagnosticCount;
                stats.LayoutInstanceOverrideAppliedCount = layoutReport.BrushMaterialInstanceOverrideAppliedCount;
                stats.LayoutInstanceFallbackCount = layoutReport.BrushMaterialInstanceFallbackCount;
                stats.LayoutInstanceTargetedMissCount = layoutReport.BrushMaterialInstanceTargetedMissCount;
                stats.LayoutInstanceUnresolvedCount = layoutReport.BrushMaterialInstanceUnresolvedCount;
                stats.LayoutResolutionSourceBucketCount = layoutReport.BrushMaterialResolutionSourceCounts?.Length ?? 0;
            }
            else
            {
                stats.HasLayoutData = false;
                stats.LayoutDataPath = string.Empty;
            }

            stats.IsLikelyRuntimeScene =
                stats.HasLevelRoot &&
                stats.HasTerrain &&
                stats.HasBrushesRuntimeRoot &&
                !stats.HasBrushesAuthoringRoot &&
                !stats.HasVegetationAuthoringRoot &&
                stats.RuntimeServiceCount > 0 &&
                stats.CachedChildCount == 0;

            stats.Warnings = 0;
            if (stats.IsLikelyRuntimeScene)
            {
                stats.Warnings++;
                if (stats.FcDataDependencyCount > 0)
                    stats.Warnings++;
                return stats;
            }

            if (!stats.HasLevelRoot) stats.Warnings++;
            if (!stats.HasTerrain) stats.Warnings++;
            if (!stats.HasBrushesAuthoringRoot) stats.Warnings++;
            if (!stats.HasVegetationAuthoringRoot) stats.Warnings++;
            if (stats.RuntimeServiceCount > 0) stats.Warnings++;
            if (stats.CachedChildrenMissingStaticFlags > 0) stats.Warnings++;
            if (stats.VegetationCachedWithEnabledCollider > 0) stats.Warnings++;
            if (stats.VegetationCachedWithEnabledLodGroup > 0) stats.Warnings++;
            if (stats.MaterialSlotUnresolvedCount > 0) stats.Warnings++;
            if (!stats.HasLayoutData) stats.Warnings++;
            if (stats.HasLayoutData &&
                stats.LayoutSlotDiagnosticCount != stats.MaterialSlotDiagnosticCount)
            {
                stats.Warnings++;
            }
            if (stats.HasLayoutData &&
                stats.LayoutInstanceDiagnosticCount != stats.MaterialInstanceDiagnosticCount)
            {
                stats.Warnings++;
            }
            if (stats.HasLayoutData && stats.LayoutSlotUnresolvedCount > 0) stats.Warnings++;
            if (stats.HasLayoutData && stats.LayoutInstanceUnresolvedCount > 0) stats.Warnings++;
            if (stats.FcDataDependencyCount > 0) stats.Warnings++;

            return stats;
        }

        static void AppendValidationLine(StringBuilder sb, bool ok, string message)
        {
            sb.Append(ok ? "[OK] " : "[WARN] ");
            sb.AppendLine(message);
        }

        static string BuildSceneDependencyAudit(Scene scene)
        {
            var audit = BuildSceneDependencyAuditData(scene);
            var top = new List<string>(audit.FcDataPaths);
            top.Sort(System.StringComparer.Ordinal);
            const int maxPreview = 12;
            int previewCount = Mathf.Min(maxPreview, top.Count);

            var sb = new StringBuilder(256);
            sb.AppendLine($"Dependency audit for scene '{scene.name}':");
            sb.AppendLine($"FCData asset refs: {top.Count}");
            sb.AppendLine($"Other asset refs: {audit.NonFcDataAssetCount}");
            if (top.Count > 0)
            {
                sb.AppendLine("FCData sample:");
                for (int i = 0; i < previewCount; i++)
                    sb.AppendLine($"- {top[i]}");
                if (top.Count > previewCount)
                    sb.AppendLine($"... and {top.Count - previewCount} more");
            }

            return sb.ToString().TrimEnd();
        }

        static SceneDependencyAuditData BuildSceneDependencyAuditData(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var deps = EditorUtility.CollectDependencies(roots);

            var fcDataPaths = new HashSet<string>(System.StringComparer.Ordinal);
            var nonFcDataAssetCount = 0;

            for (int i = 0; i < deps.Length; i++)
            {
                var dep = deps[i];
                if (dep == null)
                    continue;

                string path = AssetDatabase.GetAssetPath(dep);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                if (path.StartsWith("Assets/FCData/", System.StringComparison.Ordinal))
                {
                    fcDataPaths.Add(path);
                    continue;
                }

                nonFcDataAssetCount++;
            }

            return new SceneDependencyAuditData(fcDataPaths, nonFcDataAssetCount);
        }

        static string FinalizeSceneDependencies(Scene scene, string levelName)
        {
            var stats = new FinalizeStats();
            var auditBefore = BuildSceneDependencyAuditData(scene);
            stats.SceneFcDataDependencies = auditBefore.FcDataPaths.Count;
            if (stats.SceneFcDataDependencies == 0)
            {
                return $"Finalize scene '{scene.name}': no FCData dependencies found.";
            }

            var sourcePaths = CollectPromotedFcDataDependencies(auditBefore.FcDataPaths);
            var pathMap = new Dictionary<string, string>(System.StringComparer.Ordinal);
            var sortedPaths = new List<string>(sourcePaths);
            sortedPaths.Sort(System.StringComparer.Ordinal);

            string destinationRoot = $"Assets/Scenes/Levels/{SanitizePathSegment(levelName)}_Data";
            EnsureDir(destinationRoot);

            for (int i = 0; i < sortedPaths.Count; i++)
            {
                string sourcePath = sortedPaths[i];
                if (string.IsNullOrWhiteSpace(sourcePath))
                    continue;

                var sourceAsset = AssetDatabase.LoadMainAssetAtPath(sourcePath);
                if (sourceAsset == null)
                    continue;

                string destinationPath = BuildPromotionTargetPath(sourcePath, levelName, destinationRoot);
                if (string.IsNullOrWhiteSpace(destinationPath))
                    continue;

                string destDir = Path.GetDirectoryName(destinationPath)?.Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(destDir))
                    EnsureDir(destDir);

                if (AssetDatabase.LoadMainAssetAtPath(destinationPath) != null)
                    AssetDatabase.DeleteAsset(destinationPath);

                if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                {
                    stats.FailedPromotions++;
                    Debug.LogWarning($"[FcLevelBuilder] Failed to promote '{sourcePath}' -> '{destinationPath}'.");
                    continue;
                }

                pathMap[sourcePath] = destinationPath;
                stats.PromotedAssets++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var localIdLookupByPath = new Dictionary<string, Dictionary<long, Object>>(System.StringComparer.Ordinal);
            var promotedPaths = new List<string>(pathMap.Values);
            promotedPaths.Sort(System.StringComparer.Ordinal);
            for (int i = 0; i < promotedPaths.Count; i++)
                stats.PromotedAssetRefsRewired += ReplaceObjectReferencesInAsset(promotedPaths[i], pathMap, localIdLookupByPath);

            stats.SceneRefsRewired = ReplaceObjectReferencesInScene(scene, pathMap, localIdLookupByPath);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);

            var auditAfter = BuildSceneDependencyAuditData(scene);
            stats.RemainingFcDataRefs = auditAfter.FcDataPaths.Count;

            var sb = new StringBuilder(320);
            sb.AppendLine($"Finalize scene '{scene.name}' (level '{levelName}'):");
            sb.AppendLine($"Scene FCData refs before: {stats.SceneFcDataDependencies}");
            sb.AppendLine($"Promoted assets: {stats.PromotedAssets}");
            sb.AppendLine($"Failed promotions: {stats.FailedPromotions}");
            sb.AppendLine($"Rewired refs in promoted assets: {stats.PromotedAssetRefsRewired}");
            sb.AppendLine($"Rewired refs in scene: {stats.SceneRefsRewired}");
            sb.AppendLine($"Scene FCData refs after: {stats.RemainingFcDataRefs}");
            if (stats.RemainingFcDataRefs > 0)
                sb.AppendLine("Run dependency audit to inspect unresolved FCData refs.");
            return sb.ToString().TrimEnd();
        }

        static HashSet<string> CollectPromotedFcDataDependencies(IEnumerable<string> rootFcDataPaths)
        {
            var result = new HashSet<string>(System.StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (var path in rootFcDataPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                if (!result.Add(path))
                    continue;
                queue.Enqueue(path);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var deps = AssetDatabase.GetDependencies(current, true);
                for (int i = 0; i < deps.Length; i++)
                {
                    var depPath = deps[i];
                    if (string.IsNullOrWhiteSpace(depPath))
                        continue;
                    if (!depPath.StartsWith("Assets/FCData/", System.StringComparison.Ordinal))
                        continue;
                    if (!result.Add(depPath))
                        continue;
                    queue.Enqueue(depPath);
                }
            }

            return result;
        }

        static string BuildPromotionTargetPath(string sourcePath, string levelName, string destinationRoot)
        {
            const string fcDataRoot = "Assets/FCData/";
            if (string.IsNullOrWhiteSpace(sourcePath) ||
                !sourcePath.StartsWith(fcDataRoot, System.StringComparison.Ordinal))
                return null;

            string levelRoot = $"{GetLevelAssetCacheDir(levelName)}/";
            string legacyLevelRoot = $"Assets/FCData/Levels/{levelName}/";
            string relativePath;
            if (sourcePath.StartsWith(levelRoot, System.StringComparison.Ordinal))
            {
                relativePath = sourcePath.Substring(levelRoot.Length);
            }
            else if (sourcePath.StartsWith(legacyLevelRoot, System.StringComparison.Ordinal))
            {
                relativePath = sourcePath.Substring(legacyLevelRoot.Length);
            }
            else
            {
                relativePath = $"External/{sourcePath.Substring(fcDataRoot.Length)}";
            }

            return $"{destinationRoot}/{relativePath}";
        }

        static int ReplaceObjectReferencesInScene(
            Scene scene,
            IReadOnlyDictionary<string, string> pathMap,
            IDictionary<string, Dictionary<long, Object>> localIdLookupByPath)
        {
            int rewired = 0;
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null)
                    continue;

                rewired += ReplaceObjectReferencesInObject(root, pathMap, localIdLookupByPath);
                var components = root.GetComponentsInChildren<Component>(includeInactive: true);
                for (int c = 0; c < components.Length; c++)
                    rewired += ReplaceObjectReferencesInObject(components[c], pathMap, localIdLookupByPath);
            }

            return rewired;
        }

        static int ReplaceObjectReferencesInAsset(
            string assetPath,
            IReadOnlyDictionary<string, string> pathMap,
            IDictionary<string, Dictionary<long, Object>> localIdLookupByPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return 0;

            var objects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            int rewired = 0;
            for (int i = 0; i < objects.Length; i++)
            {
                var obj = objects[i];
                if (obj == null || obj is DefaultAsset)
                    continue;
                rewired += ReplaceObjectReferencesInObject(obj, pathMap, localIdLookupByPath);
            }
            return rewired;
        }

        static int ReplaceObjectReferencesInObject(
            Object target,
            IReadOnlyDictionary<string, string> pathMap,
            IDictionary<string, Dictionary<long, Object>> localIdLookupByPath)
        {
            if (target == null)
                return 0;

            try
            {
                int rewired = 0;
                var so = new SerializedObject(target);
                var prop = so.GetIterator();
                bool enterChildren = true;
                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = true;
                    if (prop.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    var oldRef = prop.objectReferenceValue;
                    if (oldRef == null)
                        continue;

                    if (!TryResolvePromotedReference(oldRef, pathMap, localIdLookupByPath, out var newRef))
                        continue;
                    if (newRef == null || ReferenceEquals(newRef, oldRef))
                        continue;

                    prop.objectReferenceValue = newRef;
                    rewired++;
                }

                if (rewired > 0)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(target);
                }

                return rewired;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[FcLevelBuilder] Skipped rewiring on '{target.name}' ({target.GetType().Name}): {e.Message}");
                return 0;
            }
        }

        static bool TryResolvePromotedReference(
            Object oldRef,
            IReadOnlyDictionary<string, string> pathMap,
            IDictionary<string, Dictionary<long, Object>> localIdLookupByPath,
            out Object newRef)
        {
            newRef = null;
            string oldPath = AssetDatabase.GetAssetPath(oldRef);
            if (string.IsNullOrWhiteSpace(oldPath))
                return false;
            if (!pathMap.TryGetValue(oldPath, out var newPath))
                return false;

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(oldRef, out _, out long oldLocalId))
                return false;

            if (!localIdLookupByPath.TryGetValue(newPath, out var localMap))
            {
                localMap = new Dictionary<long, Object>();
                var objects = AssetDatabase.LoadAllAssetsAtPath(newPath);
                for (int i = 0; i < objects.Length; i++)
                {
                    var obj = objects[i];
                    if (obj == null)
                        continue;
                    if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out _, out long localId))
                        localMap[localId] = obj;
                }

                localIdLookupByPath[newPath] = localMap;
            }

            if (localMap.TryGetValue(oldLocalId, out var mapped) && mapped != null)
            {
                newRef = mapped;
                return true;
            }

            newRef = AssetDatabase.LoadMainAssetAtPath(newPath);
            return newRef != null;
        }

        static void RemoveEmptyServiceObjectsRecursive(Transform root)
        {
            if (root == null)
                return;

            for (int i = root.childCount - 1; i >= 0; i--)
                RemoveEmptyServiceObjectsRecursive(root.GetChild(i));

            if (!string.Equals(root.name, "FcLevelServices", System.StringComparison.Ordinal))
                return;
            if (root.childCount != 0)
                return;

            var components = root.GetComponents<Component>();
            if (components.Length == 1 && components[0] is Transform)
                Object.DestroyImmediate(root.gameObject);
        }

        static void EnsureDir(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir))
                return;

            string parent = Path.GetDirectoryName(dir)?.Replace('\\', '/');
            string leaf = Path.GetFileName(dir);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static string GetLevelAssetCacheDir(string levelName)
        {
            return $"Assets/FCData/Levels/{NormalizeLevelAssetKey(levelName)}";
        }

        static string NormalizeLevelAssetKey(string levelName)
        {
            if (string.IsNullOrWhiteSpace(levelName))
                return "level";

            string key = levelName.Trim().ToLowerInvariant();
            var chars = key.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c == '/' || c == '\\' || char.IsControl(c) || char.IsWhiteSpace(c))
                    chars[i] = '_';
            }

            key = new string(chars).Trim('_', '.');
            return string.IsNullOrEmpty(key) ? "level" : key;
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        string GetSelectedLevelName()
        {
            if (_levelNames == null || _levelNames.Count == 0)
                return null;
            if (_levelIndex < 0 || _levelIndex >= _levelNames.Count)
                return null;
            return _levelNames[_levelIndex];
        }

        string GetLevelNameForActiveScene(Scene scene)
        {
            string selected = GetSelectedLevelName();
            if (!string.IsNullOrWhiteSpace(selected))
                return selected;
            return scene.name;
        }

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
