using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenFarCry.FileSystem;
using OpenFarCry.Importer.Cgf;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    public class CgfImporterWindow : EditorWindow
    {
        // ------------------------------------------------------------------ state

        string _searchFilter = "";
        List<string> _allPaths     = new List<string>();
        List<string> _filteredPaths = new List<string>();
        string       _selectedPath;
        Vector2      _listScroll;
        readonly Dictionary<string, List<string>> _groupedPaths =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _expandedDirs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CgfFile _parsedFile;
        string  _parsedPath;
        string  _parseError;
        string  _parseNote;
        int     _selectedMeshListIndex;
        List<string> _siblingLodPaths = new List<string>();

        bool   _saveToProject = false;
        bool   _importSkeleton = true;
        bool   _importAnimations = true;
        bool   _importPhysicsBoxColliders = true;
        bool   _importRagdollBodies = false;
        float  _importScale = 0.01f;
        CgfRigCachePolicy _rigCachePolicy;
        string _rigCacheNote;
        readonly CgfSourceBrowser _sourceBrowser = new CgfSourceBrowser();
        readonly CgfImportEditorService _importService = new CgfImportEditorService();
        readonly CgfLodImportService _lodImportService = new CgfLodImportService();
        readonly CgfRagdollBuilder _ragdollBuilder = new CgfRagdollBuilder();
        readonly CgfAnimationImportEditorService _animationImportService = new CgfAnimationImportEditorService();
        readonly CgfAssetCacheService _assetCacheService = new CgfAssetCacheService();

        // ------------------------------------------------------------------ style cache

        GUIStyle _errorStyle;
        GUIStyle _selectedRowStyle;

        // ------------------------------------------------------------------ menu

        [MenuItem("OpenFarCry/CGF Importer")]
        public static void ShowWindow()
        {
            var win = GetWindow<CgfImporterWindow>("CGF Importer");
            win.minSize = new Vector2(400, 520);
        }

        // ------------------------------------------------------------------ lifecycle

        void OnEnable()
        {
            ReloadRigCachePolicy();
            RefreshFileList();
        }

        void OnGUI()
        {
            EnsureStyles();

            DrawToolbar();
            DrawSearchBar();
            DrawFileList();
            GUILayout.Space(4);
            DrawInfoPanel();
            GUILayout.Space(4);
            DrawImportSettings();
            GUILayout.Space(4);
            DrawSaveSettings();
            GUILayout.Space(6);
            DrawActionButtons();
        }

        // ------------------------------------------------------------------ sections

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Обновить список", EditorStyles.toolbarButton, GUILayout.Width(120)))
                    RefreshFileList();

                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_filteredPaths.Count} / {_allPaths.Count} файлов",
                    EditorStyles.toolbarButton);
            }
        }

        void DrawSearchBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Фильтр:", GUILayout.Width(48));
                var newFilter = EditorGUILayout.TextField(_searchFilter);
                if (newFilter != _searchFilter)
                {
                    _searchFilter = newFilter;
                    ApplyFilter();
                }
            }
        }

        void DrawFileList()
        {
            float listHeight = Mathf.Min(position.height * 0.4f, 220f);
            using var scroll = new EditorGUILayout.ScrollViewScope(
                _listScroll, GUILayout.Height(listHeight));
            _listScroll = scroll.scrollPosition;

            if (_groupedPaths.Count == 0)
            {
                EditorGUILayout.LabelField("Нет файлов по текущему фильтру.", EditorStyles.miniLabel);
                return;
            }

            foreach (var kv in _groupedPaths.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                string dir = kv.Key;
                var files = kv.Value;
                bool expanded = _expandedDirs.Contains(dir);
                bool newExpanded = EditorGUILayout.Foldout(
                    expanded,
                    $"{dir} ({files.Count})",
                    toggleOnLabelClick: true);

                if (newExpanded) _expandedDirs.Add(dir);
                else _expandedDirs.Remove(dir);

                if (!newExpanded)
                    continue;

                using (new EditorGUI.IndentLevelScope())
                {
                    for (int i = 0; i < files.Count; i++)
                    {
                        string path = files[i];
                        bool selected = string.Equals(_selectedPath, path, StringComparison.OrdinalIgnoreCase);
                        var style = selected ? _selectedRowStyle : EditorStyles.label;
                        string label = Path.GetFileName(path);
                        var content = new GUIContent(label, path);
                        var rect = GUILayoutUtility.GetRect(content, style, GUILayout.ExpandWidth(true));
                        if (GUI.Button(rect, content, style))
                            OnFileSelected(path);
                    }
                }
            }
        }

        void DrawInfoPanel()
        {
            EditorGUILayout.LabelField("Информация о файле", EditorStyles.boldLabel);

            if (_parseError != null)
            {
                EditorGUILayout.LabelField(_parseError, _errorStyle,
                    GUILayout.ExpandWidth(true));
                return;
            }

            if (_parsedFile == null)
            {
                EditorGUILayout.LabelField("—  выберите файл из списка", EditorStyles.miniLabel);
                return;
            }

            var m = _parsedFile.MeshChunk;
            EditorGUILayout.LabelField($"FileType:   0x{_parsedFile.FileType:X8}");
            EditorGUILayout.LabelField($"Version:    0x{_parsedFile.Version:X4}");
            EditorGUILayout.LabelField($"Mesh chunks:{_parsedFile.MeshChunks.Count}");

            DrawMeshSelection();

            if (m != null)
            {
                int submeshCount = m.Faces.Length > 0
                    ? m.Faces.Select(f => f.MatID).Distinct().Count()
                    : 0;

                EditorGUILayout.LabelField($"Mesh ID:    {m.ChunkID}");
                EditorGUILayout.LabelField($"Вершин:     {m.Vertices.Length}");
                EditorGUILayout.LabelField($"Граней:     {m.Faces.Length}");
                EditorGUILayout.LabelField($"UV-вершин:  {m.UVs.Length}");
                EditorGUILayout.LabelField($"Submeshes:  {submeshCount}");
            }
            else
            {
                EditorGUILayout.LabelField("Mesh chunk: отсутствует", EditorStyles.miniLabel);
            }

            int boneCount = _parsedFile.BoneNames?.Names.Length ?? 0;
            EditorGUILayout.LabelField($"Костей:     {boneCount}");
            EditorGUILayout.LabelField($"Node-узлов: {_parsedFile.NodeChunks.Count}");
            EditorGUILayout.LabelField($"BoneMesh:   {_parsedFile.BoneMeshChunks.Count}");
            EditorGUILayout.LabelField($"LOD files:  {_siblingLodPaths.Count}");

            if (!string.IsNullOrEmpty(_parseNote))
                EditorGUILayout.HelpBox(_parseNote, MessageType.Info);
        }

        void DrawMeshSelection()
        {
            if (_parsedFile == null || _parsedFile.MeshChunks.Count <= 1)
                return;

            var options = _parsedFile.MeshChunks
                .Select((mesh, idx) => $"{idx}: ChunkID={mesh.ChunkID}, Faces={mesh.Faces.Length}")
                .ToArray();

            int newIndex = EditorGUILayout.Popup("Mesh chunk:", _selectedMeshListIndex, options);
            if (newIndex == _selectedMeshListIndex)
                return;

            _selectedMeshListIndex = newIndex;
            var selected = _parsedFile.MeshChunks[_selectedMeshListIndex];
            _parsedFile.MeshChunk = selected;
            _parsedFile.SelectedMeshChunkID = selected.ChunkID;

            if (_parsedFile.BoneInitPosByMeshChunkID.TryGetValue(selected.ChunkID, out var bindPoses))
                _parsedFile.BoneInitPos = bindPoses;
            else
                _parsedFile.BoneInitPos = null;
        }

        void DrawSaveSettings()
        {
            _saveToProject = EditorGUILayout.ToggleLeft("Сохранить в проект", _saveToProject);
            if (!_saveToProject || string.IsNullOrEmpty(_parsedPath))
                return;

            using (new EditorGUI.IndentLevelScope())
            {
                var paths = _assetCacheService.GetCachePaths(_parsedPath);
                EditorGUILayout.LabelField("Mesh:", paths.MeshPath);
                EditorGUILayout.LabelField("Prefab:", paths.PrefabPath);
            }
        }

        void DrawImportSettings()
        {
            EditorGUILayout.LabelField("Опции импорта", EditorStyles.boldLabel);
            _importSkeleton = EditorGUILayout.ToggleLeft(
                "Импортировать скелет",
                _importSkeleton);
            _importAnimations = EditorGUILayout.ToggleLeft(
                "Импортировать анимации (CAF/CAL)",
                _importAnimations);
            using (new EditorGUI.DisabledScope(!_importSkeleton))
            {
                _importPhysicsBoxColliders = EditorGUILayout.ToggleLeft(
                    "Импортировать Physics Box Colliders",
                    _importPhysicsBoxColliders);
            }
            using (new EditorGUI.DisabledScope(!_importSkeleton || !_importPhysicsBoxColliders))
            {
                _importRagdollBodies = EditorGUILayout.ToggleLeft(
                    "Импортировать Ragdoll Bodies/Joints",
                    _importRagdollBodies);
            }
            _importScale = EditorGUILayout.FloatField("Scale", _importScale);
            if (_importScale <= 0f)
                _importScale = 0.0001f;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Rig Cache", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Mode:", _rigCachePolicy.Mode.ToString());
            if (!string.IsNullOrEmpty(_rigCacheNote))
                EditorGUILayout.HelpBox(_rigCacheNote, MessageType.None);
        }

        void DrawActionButtons()
        {
            bool canLoad = _parsedFile?.MeshChunk != null && _parseError == null;

            using (new EditorGUI.DisabledScope(!canLoad))
            {
                if (GUILayout.Button("Загрузить в сцену", GUILayout.Height(30)))
                    LoadToScene();
            }
        }

        // ------------------------------------------------------------------ logic

        void RefreshFileList()
        {
            if (FcFileSystem.MountedCount == 0)
            {
                Debug.LogWarning("[CgfImporter] VFS has no mounted PAKs. " +
                    "Check FcFileSystemSettings in Resources/.");
                _allPaths = new List<string>();
                ApplyFilter();
                return;
            }

            _allPaths = _sourceBrowser.LoadAllSupportedPaths();
            ApplyFilter();
        }

        void ApplyFilter()
        {
            var filterResult = _sourceBrowser.ApplyFilter(
                _allPaths,
                _searchFilter,
                _selectedPath,
                autoExpandLimit: 8);

            _filteredPaths = filterResult.FilteredPaths;
            _groupedPaths.Clear();
            foreach (var kv in filterResult.GroupedPaths)
                _groupedPaths[kv.Key] = kv.Value;

            for (int i = 0; i < filterResult.SuggestedExpandedDirectories.Count; i++)
                _expandedDirs.Add(filterResult.SuggestedExpandedDirectories[i]);

            if (!filterResult.ContainsSelectedPath)
            {
                _selectedPath = null;
                _parsedFile = null;
                _parsedPath = null;
                _parseError = null;
                _parseNote = null;
                _selectedMeshListIndex = 0;
                _siblingLodPaths.Clear();
            }

            Repaint();
        }

        void OnFileSelected(string path)
        {
            _selectedPath = path;
            var parsed = _sourceBrowser.ParseSelection(path);

            _parsedPath = parsed.ParsedPath;
            _parsedFile = parsed.ParsedFile;
            _parseError = parsed.ParseError;
            _parseNote = parsed.ParseNote;
            _siblingLodPaths = parsed.SiblingLodPaths ?? new List<string>();
            _selectedMeshListIndex = parsed.SelectedMeshListIndex;

            if (!string.IsNullOrEmpty(parsed.ParseError))
                Debug.LogError($"[CgfImporter] Failed to parse '{path}': {parsed.ParseError}");

            Repaint();
        }

        void LoadToScene()
        {
            ReloadRigCachePolicy();
            _rigCacheNote = null;

            var request = new CgfImportRequest(
                parsedPath: _parsedPath,
                parsedFile: _parsedFile,
                siblingLodPaths: _siblingLodPaths,
                saveToProject: _saveToProject,
                importSkeleton: _importSkeleton,
                importAnimations: _importAnimations,
                importPhysicsBoxColliders: _importPhysicsBoxColliders,
                importRagdollBodies: _importRagdollBodies,
                importScale: _importScale,
                rigCachePolicy: _rigCachePolicy,
                tryInstantiateCachedPrefab: TryInstantiateCachedPrefab,
                buildGameObject: BuildGameObject,
                configureLodGroup: ConfigureLodGroup,
                attachAnimations: TryAttachAnimations,
                applyPostTransform: ApplyPostTransform,
                saveAssets: SaveAssets,
                useRuntimeImportService: true,
                useRuntimeMemoryCache: true,
                preferProjectCache: false);

            var result = _importService.ImportToScene(request);
            if (!result.Success)
            {
                EditorUtility.DisplayDialog("Ошибка импорта",
                    $"Не удалось загрузить модель:\n{result.ErrorMessage}", "OK");
                Debug.LogError($"[CgfImporter] Import failed: {result.ErrorMessage}");
                return;
            }

            _rigCacheNote = result.RigCacheNote;
            Selection.activeGameObject = result.GameObject;
            SceneView.FrameLastActiveSceneView();
        }

        bool TryInstantiateCachedPrefab(out GameObject go)
        {
            go = null;
            if (!_saveToProject || _parsedFile == null || string.IsNullOrEmpty(_parsedPath))
                return false;

            return _assetCacheService.TryInstantiateCachedPrefab(_parsedPath, _parsedFile, out go);
        }

        GameObject BuildGameObject(
            BuildResult result,
            CgfFile parsedFile,
            CgfRigDefinition rigDefinition,
            string name,
            bool importPhysicsBoxColliders,
            bool importRagdollBodies,
            float importScale)
        {
            var builder = new CgfGameObjectBuilder();
            var built = builder.Build(new CgfGameObjectBuilder.BuildRequest(
                result,
                parsedFile,
                rigDefinition,
                name));
            var go = built.Root;

            Undo.RegisterCreatedObjectUndo(go, "Import CGF");

            if (result.HasSkeleton)
            {
                var boneTransforms = built.BoneTransforms;

                if (importPhysicsBoxColliders)
                    _ragdollBuilder.AddBonePhysicsBoxColliders(parsedFile, result, boneTransforms, importScale);
                if (importPhysicsBoxColliders && importRagdollBodies)
                {
                    var ragdoll = _ragdollBuilder.AddRagdollBodiesAndJoints(go, boneTransforms, parsedFile, result);
                    CgfRagdollDiagnostics.LogImportDiagnostics(go, boneTransforms, result, ragdoll.PhysicsByRuntimeIndex);
                    if (ragdoll.JointDiagnostics != null && ragdoll.JointDiagnostics.Count > 0)
                        Debug.Log("[CgfImporter][JointDiag]\n" + string.Join("\n", ragdoll.JointDiagnostics));
                    if (ragdoll.JointCount > 0 || ragdoll.PhysicalBoneCount > 0)
                    {
                        Debug.Log(
                            $"[CgfImporter] Added ragdoll setup: {ragdoll.PhysicalBoneCount} rigidbody bone(s), {ragdoll.JointCount} joint(s), " +
                            $"physics-driven joints={ragdoll.PhysicsDrivenJointCount}, fallback joints={ragdoll.FallbackJointCount}.");
                    }
                }
            }

            return go;
        }

        void ConfigureLodGroup(GameObject root, bool hasSkeleton, float importScale, bool saveToProject)
        {
            _lodImportService.ConfigureLodGroup(
                root: root,
                hasSkeleton: hasSkeleton,
                importScale: importScale,
                siblingLodPaths: _siblingLodPaths,
                persistMesh: saveToProject ? PersistMeshAssetForVirtualPath : null);
        }

        List<CgfImportedAnimationClip> TryAttachAnimations(
            GameObject go,
            CgfFile parsedFile,
            CgfRigDefinition rigDefinition,
            string modelVirtualPath,
            float importScale)
        {
            var importedClips = _animationImportService.TryAttachAnimations(
                go,
                parsedFile,
                rigDefinition,
                modelVirtualPath,
                importScale,
                out var warningMessage);

            if (!string.IsNullOrEmpty(warningMessage))
                Debug.LogWarning(warningMessage);

            return importedClips;
        }

        void ReloadRigCachePolicy()
        {
            var settings = CgfRigCacheSettings.LoadOrDefault();
            _rigCachePolicy = CgfRigCachePolicyResolver.Resolve(settings);
        }

        void ApplyPostTransform(GameObject go)
        {
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }

        void SaveAssets(Mesh mesh, GameObject go, IReadOnlyList<CgfImportedAnimationClip> clips)
        {
            _assetCacheService.SaveAssets(_parsedPath, mesh, go, clips);
        }

        Mesh PersistMeshAssetForVirtualPath(Mesh mesh, string virtualPath)
        {
            return _assetCacheService.PersistMeshAssetForVirtualPath(mesh, virtualPath);
        }

        // ------------------------------------------------------------------ styles

        void EnsureStyles()
        {
            if (_errorStyle != null) return;

            _errorStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                normal = { textColor = new Color(0.9f, 0.2f, 0.2f) }
            };

            _selectedRowStyle = new GUIStyle(EditorStyles.label)
            {
                normal  = { background = MakeTex(1, 1, new Color(0.24f, 0.49f, 0.91f, 0.35f)) },
                focused = { background = MakeTex(1, 1, new Color(0.24f, 0.49f, 0.91f, 0.35f)) },
            };
        }

        static Texture2D MakeTex(int w, int h, Color col)
        {
            var tex = new Texture2D(w, h);
            tex.SetPixels(Enumerable.Repeat(col, w * h).ToArray());
            tex.Apply();
            return tex;
        }
    }
}
