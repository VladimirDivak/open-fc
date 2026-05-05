using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        bool   _saveToProject = false;
        bool   _importSkeleton = true;
        bool   _importAnimations = true;
        float  _importScale = 0.01f;
        CgfRigCachePolicy _rigCachePolicy;
        string _rigCacheNote;

        readonly struct AnimSourceEntry
        {
            public readonly string Alias;
            public readonly string VirtualPath;

            public AnimSourceEntry(string alias, string virtualPath)
            {
                Alias = alias;
                VirtualPath = virtualPath;
            }
        }

        sealed class ImportedAnimationClip
        {
            public string Alias;
            public AnimationClip Clip;
            public string SharedCacheKey;
        }

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
                var paths = GetCachePaths(_parsedPath);
                EditorGUILayout.LabelField("Mesh:", paths.meshPath);
                EditorGUILayout.LabelField("Prefab:", paths.prefabPath);
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
            _allPaths.Clear();

            if (FcFileSystem.MountedCount == 0)
            {
                Debug.LogWarning("[CgfImporter] VFS has no mounted PAKs. " +
                    "Check FcFileSystemSettings in Resources/.");
                ApplyFilter();
                return;
            }

            foreach (var path in FcFileSystem.GetEntries(""))
            {
                if (path.EndsWith(".cgf", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".cga", StringComparison.OrdinalIgnoreCase))
                    _allPaths.Add(path);
            }

            _allPaths.Sort(StringComparer.OrdinalIgnoreCase);
            ApplyFilter();
        }

        void ApplyFilter()
        {
            _filteredPaths = string.IsNullOrWhiteSpace(_searchFilter)
                ? new List<string>(_allPaths)
                : _allPaths
                    .Where(p => p.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

            RebuildGroupedPaths();

            if (!string.IsNullOrEmpty(_selectedPath) &&
                !_allPaths.Contains(_selectedPath, StringComparer.OrdinalIgnoreCase))
            {
                _selectedPath = null;
                _parsedFile = null;
                _parsedPath = null;
                _parseError = null;
                _parseNote = null;
            }

            Repaint();
        }

        void RebuildGroupedPaths()
        {
            _groupedPaths.Clear();

            for (int i = 0; i < _filteredPaths.Count; i++)
            {
                string path = _filteredPaths[i];
                string dir = GetDirectoryLabel(path);
                if (!_groupedPaths.TryGetValue(dir, out var files))
                {
                    files = new List<string>();
                    _groupedPaths[dir] = files;
                    if (_expandedDirs.Count < 8)
                        _expandedDirs.Add(dir);
                }
                files.Add(path);
            }

            foreach (var kv in _groupedPaths)
                kv.Value.Sort(StringComparer.OrdinalIgnoreCase);
        }

        static string GetDirectoryLabel(string virtualPath)
        {
            int slash = virtualPath.LastIndexOf('/');
            if (slash <= 0)
                return "<root>";
            return virtualPath.Substring(0, slash);
        }

        void OnFileSelected(string path)
        {
            _selectedPath = path;
            _parsedFile = null;
            _parseError = null;
            _parseNote = null;
            _parsedPath = path;

            try
            {
                byte[] data = FcFileSystem.ReadAllBytes(_parsedPath);
                _parsedFile = CgfParser.Parse(data);
            }
            catch (Exception e)
            {
                _parseError = e.Message;
                Debug.LogError($"[CgfImporter] Failed to parse '{_parsedPath}': {e}");
            }

            _parseNote = BuildParseNote();
            InitializeSelectedMeshIndex();

            Repaint();
        }

        string BuildParseNote()
        {
            if (_parsedFile == null)
                return null;

            if (_parsedPath.EndsWith(".cga", StringComparison.OrdinalIgnoreCase))
            {
                return "CGA импортируется только как geometry preview. " +
                       "Controller/Timing/ANM связки пока не обрабатываются.";
            }

            if (_parsedFile.MeshChunks.Count > 1)
            {
                return "В файле несколько mesh-чанков. Выберите нужный в поле Mesh chunk.";
            }

            return null;
        }

        void InitializeSelectedMeshIndex()
        {
            _selectedMeshListIndex = 0;
            if (_parsedFile == null || _parsedFile.MeshChunks.Count == 0)
                return;

            int selectedId = _parsedFile.SelectedMeshChunkID;
            if (selectedId == -1)
                return;

            for (int i = 0; i < _parsedFile.MeshChunks.Count; i++)
            {
                if (_parsedFile.MeshChunks[i].ChunkID == selectedId)
                {
                    _selectedMeshListIndex = i;
                    return;
                }
            }
        }

        void LoadToScene()
        {
            ReloadRigCachePolicy();
            _rigCacheNote = null;

            if (!_importAnimations && TryInstantiateCachedPrefab(out var cached))
            {
                Selection.activeGameObject = cached;
                SceneView.FrameLastActiveSceneView();
                return;
            }

            BuildResult result;
            try
            {
                result = CgfMeshBuilder.Build(_parsedFile, _importSkeleton, _importScale);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Ошибка импорта",
                    $"Не удалось построить меш:\n{e.Message}", "OK");
                Debug.LogError($"[CgfImporter] Build failed: {e}");
                return;
            }

            CgfRigDefinition rigDefinition = null;
            if (result.HasSkeleton && CgfRigSnapshotBuilder.TryBuild(_parsedFile, result, _parsedPath, out var snapshot))
            {
                rigDefinition = CgfRigRegistry.ResolveOrCreate(
                    snapshot,
                    _rigCachePolicy,
                    allowProjectWrite: _saveToProject,
                    out bool createdNow,
                    out var matchMode);

                if (rigDefinition != null)
                {
                    switch (matchMode)
                    {
                        case CgfRigRegistry.ResolveMatchMode.Created:
                            _rigCacheNote = $"Rig snapshot создан: {snapshot.RigFingerprint}";
                            break;
                        case CgfRigRegistry.ResolveMatchMode.AnimationCompatible:
                            _rigCacheNote =
                                $"Rig snapshot переиспользован по animation-compatible fingerprint: {rigDefinition.RigFingerprint}";
                            break;
                        default:
                            _rigCacheNote = createdNow
                                ? $"Rig snapshot создан: {snapshot.RigFingerprint}"
                                : $"Rig snapshot переиспользован: {snapshot.RigFingerprint}";
                            break;
                    }
                }
            }

            string baseName = Path.GetFileNameWithoutExtension(_parsedPath);
            var go = BuildGameObject(result, _parsedFile, rigDefinition, baseName);

            List<ImportedAnimationClip> importedClips = null;
            if (_importAnimations)
            {
                importedClips = TryAttachAnimations(go, _parsedFile, rigDefinition, _parsedPath, _importScale);
            }

            ApplyPostTransform(go);

            if (_saveToProject)
                SaveAssets(result.Mesh, go, importedClips);

            Selection.activeGameObject = go;
            SceneView.FrameLastActiveSceneView();
        }

        bool TryInstantiateCachedPrefab(out GameObject go)
        {
            go = null;
            if (!_saveToProject || _parsedFile == null || string.IsNullOrEmpty(_parsedPath))
                return false;

            var paths = GetCachePaths(_parsedPath);
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(paths.prefabPath);
            if (prefabAsset == null)
                return false;

            if (!IsCachedPrefabCompatible(prefabAsset, _parsedFile))
                return false;

            go = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
            if (go == null)
                return false;

            Undo.RegisterCreatedObjectUndo(go, "Instantiate Cached CGF");
            return true;
        }

        static bool IsCachedPrefabCompatible(GameObject prefabAsset, CgfFile parsedFile)
        {
            if (prefabAsset == null || parsedFile?.MeshChunk == null)
                return false;

            if (prefabAsset.transform.localPosition.sqrMagnitude > 1e-8f ||
                Quaternion.Dot(prefabAsset.transform.localRotation, Quaternion.identity) < 0.9999f ||
                (prefabAsset.transform.localScale - Vector3.one).sqrMagnitude > 1e-8f)
                return false;

            Mesh mesh = null;
            var smr = prefabAsset.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr != null) mesh = smr.sharedMesh;
            if (mesh == null)
            {
                var mf = prefabAsset.GetComponentInChildren<MeshFilter>(true);
                if (mf != null) mesh = mf.sharedMesh;
            }
            if (mesh == null)
                return false;
            if (mesh.name != CgfMeshBuilder.MeshCacheVersionName)
                return false;

            int expectedSubmeshCount = parsedFile.MeshChunk.Faces
                .Select(f => f.MatID)
                .Distinct()
                .Count();
            if (mesh.subMeshCount != expectedSubmeshCount)
                return false;

            bool expectedHasUv0 = parsedFile.MeshChunk.UVs != null && parsedFile.MeshChunk.UVs.Length > 0;
            bool actualHasUv0 = mesh.uv != null && mesh.uv.Length > 0;
            if (expectedHasUv0 != actualHasUv0)
                return false;

            return true;
        }

        GameObject BuildGameObject(BuildResult result, CgfFile parsedFile, CgfRigDefinition rigDefinition, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Import CGF");

            if (result.HasSkeleton)
            {
                var boneTransforms = CreateBoneTransforms(parsedFile, result, rigDefinition, go.transform);
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh      = result.Mesh;
                smr.bones           = boneTransforms;
                if (boneTransforms.Length > 0)
                {
                    var rootBone = boneTransforms.FirstOrDefault(t => t != null && t.parent == go.transform);
                    smr.rootBone = rootBone != null ? rootBone : boneTransforms[0];
                }
                smr.sharedMaterials = new Material[result.Mesh.subMeshCount];
            }
            else
            {
                go.AddComponent<MeshFilter>().sharedMesh = result.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials =
                    new Material[result.Mesh.subMeshCount];
            }

            return go;
        }

        List<ImportedAnimationClip> TryAttachAnimations(
            GameObject go,
            CgfFile parsedFile,
            CgfRigDefinition rigDefinition,
            string modelVirtualPath,
            float importScale)
        {
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null || smr.bones == null || smr.bones.Length == 0)
                return null;
            bool hasRigSnapshotControllers = rigDefinition != null &&
                rigDefinition.IsValid &&
                rigDefinition.ControllerIdsByBoneIndex != null &&
                rigDefinition.ControllerIdsByBoneIndex.Length > 0;
            bool hasParsedBoneAnim = parsedFile?.BoneNames?.Names != null &&
                parsedFile.BoneNames.Names.Length > 0 &&
                parsedFile.BoneAnim?.Bones != null &&
                parsedFile.BoneAnim.Bones.Length > 0;
            if (!hasRigSnapshotControllers && !hasParsedBoneAnim)
                return null;

            var controllerToPath = BuildControllerPathMap(go.transform, smr.bones, parsedFile, rigDefinition);
            if (controllerToPath.Count == 0)
                return null;

            var sources = CollectAnimationSources(modelVirtualPath);
            if (sources.Count == 0)
                return null;

            var imported = new List<ImportedAnimationClip>(sources.Count);
            var missingControllerIds = new Dictionary<uint, int>();
            int clipsWithMissingControllers = 0;
            int missingControllerTrackCount = 0;
            foreach (var source in sources)
            {
                try
                {
                    if (!FcFileSystem.Exists(source.VirtualPath))
                        continue;

                    byte[] bytes = FcFileSystem.ReadAllBytes(source.VirtualPath);
                    var caf = CafParser.Parse(bytes);
                    var clip = BuildAnimationClip(
                        source.Alias,
                        caf,
                        controllerToPath,
                        importScale,
                        missingControllerIds,
                        ref missingControllerTrackCount,
                        ref clipsWithMissingControllers);
                    if (clip == null)
                        continue;

                    imported.Add(new ImportedAnimationClip
                    {
                        Alias = source.Alias,
                        Clip = clip,
                        SharedCacheKey = BuildSharedAnimationClipContentKey(clip)
                    });
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CgfImporter] Failed to import animation '{source.VirtualPath}': {e.Message}");
                }
            }

            if (missingControllerTrackCount > 0)
            {
                Debug.LogWarning(
                    $"[CgfImporter] Animation import skipped {missingControllerTrackCount} controller track(s) " +
                    $"across {clipsWithMissingControllers} clip(s) because they were not mapped to skeleton bones. " +
                    $"Unique controller ids: {FormatControllerIdSummary(missingControllerIds)}.");
            }

            if (imported.Count == 0)
                return null;

            var anim = go.GetComponent<Animation>();
            if (anim == null)
                anim = go.AddComponent<Animation>();

            AnimationClip defaultClip = null;
            for (int i = 0; i < imported.Count; i++)
            {
                var item = imported[i];
                if (anim.GetClip(item.Alias) != null)
                    anim.RemoveClip(item.Alias);
                anim.AddClip(item.Clip, item.Alias);

                if (defaultClip == null ||
                    string.Equals(item.Alias, "default", StringComparison.OrdinalIgnoreCase))
                    defaultClip = item.Clip;
            }

            anim.playAutomatically = false;
            anim.clip = defaultClip ?? imported[0].Clip;
            return imported;
        }

        static string BuildSharedAnimationClipContentKey(AnimationClip clip)
        {
            if (clip == null)
                return Hash128.Compute("anim-clip-v2|null").ToString();

            var sb = new StringBuilder(4096);
            sb.Append("anim-clip-v2|");
            sb.Append(clip.legacy ? "1" : "0").Append('|');
            sb.Append((int)clip.wrapMode).Append('|');

            var curveBindings = AnimationUtility.GetCurveBindings(clip)
                .OrderBy(b => b.path, StringComparer.Ordinal)
                .ThenBy(b => b.type != null ? b.type.FullName : string.Empty, StringComparer.Ordinal)
                .ThenBy(b => b.propertyName, StringComparer.Ordinal)
                .ToArray();

            for (int i = 0; i < curveBindings.Length; i++)
            {
                var binding = curveBindings[i];
                sb.Append((binding.path ?? string.Empty).ToLowerInvariant()).Append('|');
                sb.Append(binding.type != null ? binding.type.FullName : string.Empty).Append('|');
                sb.Append(binding.propertyName ?? string.Empty).Append('|');

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AppendCurveSignature(sb, curve);
                sb.Append(';');
            }

            var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip)
                .OrderBy(b => b.path, StringComparer.Ordinal)
                .ThenBy(b => b.type != null ? b.type.FullName : string.Empty, StringComparer.Ordinal)
                .ThenBy(b => b.propertyName, StringComparer.Ordinal)
                .ToArray();

            for (int i = 0; i < objectBindings.Length; i++)
            {
                var binding = objectBindings[i];
                sb.Append("obj|");
                sb.Append((binding.path ?? string.Empty).ToLowerInvariant()).Append('|');
                sb.Append(binding.type != null ? binding.type.FullName : string.Empty).Append('|');
                sb.Append(binding.propertyName ?? string.Empty).Append('|');

                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                if (keys == null || keys.Length == 0)
                {
                    sb.Append("0;");
                    continue;
                }

                sb.Append(keys.Length).Append('|');
                for (int k = 0; k < keys.Length; k++)
                {
                    var key = keys[k];
                    AppendQuantizedFloat(sb, key.time);
                    sb.Append('=');
                    sb.Append(key.value != null ? key.value.name : "<null>");
                    sb.Append(',');
                }
                sb.Append(';');
            }

            return Hash128.Compute(sb.ToString()).ToString();
        }

        static void AppendCurveSignature(StringBuilder sb, AnimationCurve curve)
        {
            if (curve == null || curve.keys == null || curve.keys.Length == 0)
            {
                sb.Append("0");
                return;
            }

            var keys = curve.keys;
            sb.Append(keys.Length).Append('|');
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                AppendQuantizedFloat(sb, key.time);
                AppendQuantizedFloat(sb, key.value);
                AppendQuantizedFloat(sb, key.inTangent);
                AppendQuantizedFloat(sb, key.outTangent);
                AppendQuantizedFloat(sb, key.inWeight);
                AppendQuantizedFloat(sb, key.outWeight);
                sb.Append((int)key.weightedMode).Append(',');
            }
        }

        static void AppendQuantizedFloat(StringBuilder sb, float value)
        {
            int q = Mathf.RoundToInt(value * 1000000f);
            sb.Append(q).Append(',');
        }

        static Dictionary<uint, string> BuildControllerPathMap(
            Transform root,
            Transform[] bones,
            CgfFile parsedFile,
            CgfRigDefinition rigDefinition)
        {
            var pathByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone == null || pathByName.ContainsKey(bone.name))
                    continue;
                pathByName[bone.name] = GetTransformPath(root, bone);
            }

            var map = new Dictionary<uint, string>();
            if (rigDefinition != null &&
                rigDefinition.IsValid &&
                rigDefinition.ControllerIdsByBoneIndex != null &&
                rigDefinition.BoneNames != null &&
                rigDefinition.BoneNames.Length == rigDefinition.ControllerIdsByBoneIndex.Length)
            {
                for (int i = 0; i < rigDefinition.BoneNames.Length; i++)
                {
                    uint controllerId = rigDefinition.ControllerIdsByBoneIndex[i];
                    if (controllerId == 0)
                        continue;
                    string boneName = rigDefinition.BoneNames[i];
                    if (string.IsNullOrEmpty(boneName))
                        continue;
                    if (!pathByName.TryGetValue(boneName, out var path))
                        continue;
                    map[controllerId] = path;
                }

                if (map.Count > 0)
                    return map;
            }

            var boneNames = parsedFile?.BoneNames?.Names;
            var entities = parsedFile?.BoneAnim?.Bones;
            if (boneNames == null || entities == null)
                return map;

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (entity.BoneID < 0 || entity.BoneID >= boneNames.Length)
                    continue;

                string boneName = boneNames[entity.BoneID];
                if (string.IsNullOrEmpty(boneName))
                    continue;
                if (!pathByName.TryGetValue(boneName, out var path))
                    continue;

                map[entity.ControllerID] = path;
            }

            return map;
        }

        void ReloadRigCachePolicy()
        {
            var settings = CgfRigCacheSettings.LoadOrDefault();
            _rigCachePolicy = CgfRigCachePolicyResolver.Resolve(settings);
        }

        static string GetTransformPath(Transform root, Transform target)
        {
            if (target == null || target == root)
                return string.Empty;

            var segments = new List<string>(8);
            var current = target;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        static AnimationClip BuildAnimationClip(
            string alias,
            CafFile caf,
            Dictionary<uint, string> controllerToPath,
            float importScale,
            Dictionary<uint, int> missingControllerIds,
            ref int missingControllerTrackCount,
            ref int clipsWithMissingControllers)
        {
            if (caf == null || caf.Tracks == null || caf.Tracks.Count == 0)
                return null;

            bool shouldLoop = ShouldTreatClipAsLoop(alias, caf, controllerToPath, importScale);
            var clip = new AnimationClip
            {
                name = alias,
                legacy = true,
                wrapMode = shouldLoop ? WrapMode.Loop : WrapMode.Once
            };

            int baseTick = caf.GlobalStartTick;
            int curveCount = 0;
            int missingControllers = 0;
            for (int i = 0; i < caf.Tracks.Count; i++)
            {
                var track = caf.Tracks[i];
                if (track == null || track.Ticks == null || track.Ticks.Length == 0)
                    continue;
                if (!controllerToPath.TryGetValue(track.ControllerID, out var path))
                {
                    missingControllers++;
                    missingControllerTrackCount++;
                    if (!missingControllerIds.ContainsKey(track.ControllerID))
                        missingControllerIds[track.ControllerID] = 0;
                    missingControllerIds[track.ControllerID]++;
                    continue;
                }

                AddPositionCurves(clip, path, track, caf.SecsPerTick, baseTick, importScale);
                AddRotationCurves(clip, path, track, caf.SecsPerTick, baseTick);
                curveCount++;
            }

            if (curveCount == 0)
                return null;

            if (missingControllers > 0)
                clipsWithMissingControllers++;

            clip.EnsureQuaternionContinuity();
            ApplyLoopSettings(clip, shouldLoop);
            return clip;
        }

        static bool ShouldTreatClipAsLoop(
            string alias,
            CafFile caf,
            Dictionary<uint, string> controllerToPath,
            float importScale)
        {
            if (!string.IsNullOrEmpty(alias))
            {
                if (alias.IndexOf("loop", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (HasOneShotAliasHint(alias))
                    return false;
            }

            if (caf?.Tracks == null || caf.Tracks.Count == 0)
                return false;

            int considered = 0;
            int loopLike = 0;
            for (int i = 0; i < caf.Tracks.Count; i++)
            {
                var track = caf.Tracks[i];
                if (track?.Ticks == null || track.Positions == null || track.Rotations == null)
                    continue;
                if (track.Ticks.Length < 2 || track.Positions.Length < 2 || track.Rotations.Length < 2)
                    continue;

                if (controllerToPath != null && controllerToPath.Count > 0)
                {
                    if (!controllerToPath.TryGetValue(track.ControllerID, out var path))
                        continue;
                    // Ignore root motion track for loop detection; it often drifts by design.
                    if (string.IsNullOrEmpty(path) || path.Equals("Bip01", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                considered++;
                if (IsTrackLoopLike(track, importScale))
                    loopLike++;
            }

            if (considered == 0)
                return HasIdleLikeAliasHint(alias);

            float ratio = (float)loopLike / considered;
            if (HasIdleLikeAliasHint(alias) && ratio >= 0.4f)
                return true;

            if (considered < 3)
                return loopLike == considered;

            return ratio >= 0.72f;
        }

        static bool IsTrackLoopLike(CafControllerTrack track, float importScale)
        {
            int last = Mathf.Min(track.Ticks.Length, track.Positions.Length, track.Rotations.Length) - 1;
            if (last <= 0)
                return false;

            Vector3 p0 = track.Positions[0];
            Vector3 p1 = track.Positions[last];
            float posDelta = (p1 - p0).magnitude * importScale;

            Vector3 min = p0;
            Vector3 max = p0;
            for (int i = 1; i <= last; i++)
            {
                Vector3 p = track.Positions[i];
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            float posRange = (max - min).magnitude * importScale;
            float posTolerance = posRange < 0.001f
                ? 0.004f
                : Mathf.Max(0.008f, posRange * 0.2f);
            bool posLoopLike = posDelta <= posTolerance;

            Quaternion r0 = track.Rotations[0];
            Quaternion r1 = track.Rotations[last];
            if (Quaternion.Dot(r0, r1) < 0f)
                r1 = new Quaternion(-r1.x, -r1.y, -r1.z, -r1.w);
            float rotDelta = Quaternion.Angle(r0, r1);

            float rotRange = 0f;
            for (int i = 1; i <= last; i++)
                rotRange = Mathf.Max(rotRange, Quaternion.Angle(r0, track.Rotations[i]));

            float rotTolerance = rotRange < 4f
                ? 4f
                : Mathf.Max(7f, rotRange * 0.25f);
            bool rotLoopLike = rotDelta <= rotTolerance;

            return posLoopLike && rotLoopLike;
        }

        static bool HasIdleLikeAliasHint(string alias)
        {
            if (string.IsNullOrEmpty(alias))
                return false;

            return alias.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   alias.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   alias.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   alias.IndexOf("sidle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   alias.IndexOf("rotate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   alias.IndexOf("swim", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool HasOneShotAliasHint(string alias)
        {
            if (string.IsNullOrEmpty(alias))
                return false;

            string lower = alias.ToLowerInvariant();
            if (lower.Contains("jump") ||
                lower.Contains("reload") ||
                lower.Contains("pain") ||
                lower.Contains("death") ||
                lower.Contains("grenade") ||
                lower.Contains("throw"))
            {
                return true;
            }

            var tokens = lower.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i];
                if (t == "hit" || t == "in" || t == "out" || t == "start" || t == "end")
                    return true;
            }

            return false;
        }

        static void ApplyLoopSettings(AnimationClip clip, bool shouldLoop)
        {
            if (clip == null)
                return;

            clip.wrapMode = shouldLoop ? WrapMode.Loop : WrapMode.Once;

            try
            {
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = shouldLoop;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
            }
            catch
            {
                // Keep importer resilient across Unity API variants.
            }
        }

        static string FormatControllerIdSummary(Dictionary<uint, int> ids)
        {
            if (ids == null || ids.Count == 0)
                return "<none>";

            const int maxItems = 8;
            var parts = ids
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Take(maxItems)
                .Select(kv => kv.Value > 1
                    ? $"0x{kv.Key:X8} ({kv.Value}x)"
                    : $"0x{kv.Key:X8}")
                .ToArray();

            string suffix = ids.Count > maxItems ? $", +{ids.Count - maxItems} more" : string.Empty;
            return string.Join(", ", parts) + suffix;
        }

        static void AddPositionCurves(AnimationClip clip, string path, CafControllerTrack track, float secsPerTick, int baseTick, float importScale)
        {
            var cx = new AnimationCurve();
            var cy = new AnimationCurve();
            var cz = new AnimationCurve();

            for (int i = 0; i < track.Ticks.Length; i++)
            {
                float t = Mathf.Max(0f, (track.Ticks[i] - baseTick) * secsPerTick);
                var p = CryTransformConversion.PositionInImporterSpace(track.Positions[i], importScale);
                cx.AddKey(new Keyframe(t, p.x));
                cy.AddKey(new Keyframe(t, p.y));
                cz.AddKey(new Keyframe(t, p.z));
            }

            clip.SetCurve(path, typeof(Transform), "localPosition.x", cx);
            clip.SetCurve(path, typeof(Transform), "localPosition.y", cy);
            clip.SetCurve(path, typeof(Transform), "localPosition.z", cz);
        }

        static void AddRotationCurves(AnimationClip clip, string path, CafControllerTrack track, float secsPerTick, int baseTick)
        {
            var cx = new AnimationCurve();
            var cy = new AnimationCurve();
            var cz = new AnimationCurve();
            var cw = new AnimationCurve();

            for (int i = 0; i < track.Ticks.Length; i++)
            {
                float t = Mathf.Max(0f, (track.Ticks[i] - baseTick) * secsPerTick);
                var q = CryTransformConversion.LocalRotationInImporterSpace(track.Rotations[i]);
                cx.AddKey(new Keyframe(t, q.x));
                cy.AddKey(new Keyframe(t, q.y));
                cz.AddKey(new Keyframe(t, q.z));
                cw.AddKey(new Keyframe(t, q.w));
            }

            clip.SetCurve(path, typeof(Transform), "localRotation.x", cx);
            clip.SetCurve(path, typeof(Transform), "localRotation.y", cy);
            clip.SetCurve(path, typeof(Transform), "localRotation.z", cz);
            clip.SetCurve(path, typeof(Transform), "localRotation.w", cw);
        }

        List<AnimSourceEntry> CollectAnimationSources(string modelVirtualPath)
        {
            var result = new List<AnimSourceEntry>();
            if (string.IsNullOrEmpty(modelVirtualPath))
                return result;

            string modelNoExt = RemoveExtension(modelVirtualPath);
            string modelDir = GetDirectoryLabel(modelNoExt).Replace('\\', '/');
            string calPath = modelNoExt + ".cal";

            if (FcFileSystem.Exists(calPath))
            {
                try
                {
                    byte[] calBytes = FcFileSystem.ReadAllBytes(calPath);
                    string calText = System.Text.Encoding.UTF8.GetString(calBytes);
                    ParseCalEntries(calText, modelDir, result);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CgfImporter] Failed to parse CAL '{calPath}': {e.Message}");
                }
            }

            if (result.Count > 0)
                return DeduplicateAnimationSources(result);

            string baseNamePrefix = Path.GetFileName(modelNoExt).ToLowerInvariant() + "_";
            string directory = modelDir == "<root>" ? string.Empty : modelDir;
            foreach (var path in FcFileSystem.GetEntries(directory))
            {
                if (!path.EndsWith(".caf", StringComparison.OrdinalIgnoreCase))
                    continue;

                string fileName = Path.GetFileName(path).ToLowerInvariant();
                if (!fileName.StartsWith(baseNamePrefix, StringComparison.Ordinal))
                    continue;

                string file = Path.GetFileNameWithoutExtension(path);
                string alias = file.Length > baseNamePrefix.Length
                    ? file.Substring(baseNamePrefix.Length)
                    : file;
                result.Add(new AnimSourceEntry(alias, path.Replace('\\', '/')));
            }

            result.Sort((a, b) => string.Compare(a.Alias, b.Alias, StringComparison.OrdinalIgnoreCase));
            return DeduplicateAnimationSources(result);
        }

        static List<AnimSourceEntry> DeduplicateAnimationSources(List<AnimSourceEntry> input)
        {
            var unique = new List<AnimSourceEntry>(input.Count);
            var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < input.Count; i++)
            {
                var entry = input[i];
                if (string.IsNullOrWhiteSpace(entry.Alias) || string.IsNullOrWhiteSpace(entry.VirtualPath))
                    continue;
                if (usedAliases.Contains(entry.Alias))
                    continue;

                usedAliases.Add(entry.Alias);
                unique.Add(entry);
            }
            return unique;
        }

        static void ParseCalEntries(string calText, string modelDir, List<AnimSourceEntry> output)
        {
            if (string.IsNullOrEmpty(calText))
                return;

            string animDir = GetDefaultAnimDirectory(modelDir);
            var lines = calText.Replace('\r', '\n').Split('\n');
            foreach (var raw in lines)
            {
                var line = raw?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0 || eq >= line.Length - 1)
                    continue;

                string left = line.Substring(0, eq).Trim();
                string right = line.Substring(eq + 1).Trim();
                if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
                    continue;

                if (right[0] == '?')
                    continue;

                right = right.Replace('\\', '/').TrimStart('/', '\\');

                if (left.StartsWith("$", StringComparison.Ordinal))
                {
                    string directive = left.Substring(1);
                    if (directive.Equals("AnimationDir", StringComparison.OrdinalIgnoreCase) ||
                        directive.Equals("AnimDir", StringComparison.OrdinalIgnoreCase) ||
                        directive.Equals("AnimationDirectory", StringComparison.OrdinalIgnoreCase) ||
                        directive.Equals("AnimDirectory", StringComparison.OrdinalIgnoreCase))
                    {
                        animDir = $"{modelDir}/{right}".Replace('\\', '/').TrimEnd('/');
                    }
                    continue;
                }

                string resolved = $"{animDir}/{right}".Replace('\\', '/');
                output.Add(new AnimSourceEntry(left, resolved));
            }
        }

        static string GetDefaultAnimDirectory(string modelDir)
        {
            string dir = modelDir.Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(dir) || dir.Equals("<root>", StringComparison.OrdinalIgnoreCase))
                return "animations";

            int cut = dir.LastIndexOf('/');
            if (cut < 0)
                return "animations";
            string oneUp = dir.Substring(0, cut);
            cut = oneUp.LastIndexOf('/');
            if (cut < 0)
                return "animations";
            string twoUp = oneUp.Substring(0, cut);
            return $"{twoUp}/animations";
        }

        static string RemoveExtension(string virtualPath)
        {
            int ext = virtualPath.LastIndexOf('.');
            return ext > 0 ? virtualPath.Substring(0, ext) : virtualPath;
        }

        static Transform[] CreateBoneTransforms(
            CgfFile parsedFile,
            BuildResult result,
            CgfRigDefinition rigDefinition,
            Transform root)
        {
            string[] boneNames = result.BoneNames;
            Matrix4x4[] bindPoses = result.BindPoses;
            bool rigStructCompatible =
                TryUseRigDefinitionForSkeleton(result, rigDefinition, out var cachedBoneNames, out var cachedBindPoses);
            if (rigStructCompatible)
            {
                boneNames = cachedBoneNames;
                bindPoses = cachedBindPoses;
            }

            var transforms = new Transform[boneNames.Length];

            for (int i = 0; i < boneNames.Length; i++)
            {
                string boneName = boneNames[i];
                var boneGo = new GameObject(boneName);
                boneGo.transform.SetParent(root, worldPositionStays: false);
                transforms[i] = boneGo.transform;
            }

            bool hierarchyBuilt = false;
            if (rigStructCompatible && rigDefinition != null && rigDefinition.IsValid)
                hierarchyBuilt = TryBuildHierarchyFromRigDefinition(rigDefinition, transforms);
            if (!hierarchyBuilt)
                hierarchyBuilt = TryBuildHierarchyFromBoneAnim(parsedFile, transforms);
            if (!hierarchyBuilt)
                TryBuildHierarchyFromNodes(parsedFile, boneNames, transforms);

            // Put bones exactly into bind pose from inverse bind matrices.
            // This keeps skinning coherent regardless of source coordinate conventions.
            var worldBoneMatrices = new Matrix4x4[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                Matrix4x4 bind = (bindPoses != null && i < bindPoses.Length) ? bindPoses[i] : Matrix4x4.identity;
                worldBoneMatrices[i] = bind.inverse;
            }

            for (int i = 0; i < transforms.Length; i++)
            {
                Matrix4x4 local = worldBoneMatrices[i];
                var parent = transforms[i].parent;
                if (parent != null && parent != root)
                {
                    int parentIdx = Array.IndexOf(transforms, parent);
                    if (parentIdx >= 0)
                        local = worldBoneMatrices[parentIdx].inverse * worldBoneMatrices[i];
                }

                ApplyLocalMatrix(transforms[i], local);
            }

            return transforms;
        }

        static bool TryUseRigDefinitionForSkeleton(
            BuildResult result,
            CgfRigDefinition rigDefinition,
            out string[] boneNames,
            out Matrix4x4[] bindPoses)
        {
            boneNames = result.BoneNames;
            bindPoses = result.BindPoses;

            if (rigDefinition == null || !rigDefinition.IsValid)
                return false;
            if (rigDefinition.BoneNames == null || rigDefinition.BindPoses == null)
                return false;
            if (rigDefinition.BoneNames.Length != result.BoneNames.Length)
                return false;
            if (rigDefinition.BindPoses.Length != result.BoneNames.Length)
                return false;
            if (rigDefinition.BoneIndexToId != null && result.BoneIndexToId != null &&
                rigDefinition.BoneIndexToId.Length == result.BoneIndexToId.Length)
            {
                for (int i = 0; i < result.BoneIndexToId.Length; i++)
                {
                    if (rigDefinition.BoneIndexToId[i] != result.BoneIndexToId[i])
                        return false;
                }
            }

            for (int i = 0; i < result.BoneNames.Length; i++)
            {
                if (!string.Equals(rigDefinition.BoneNames[i], result.BoneNames[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            boneNames = rigDefinition.BoneNames;
            bindPoses = rigDefinition.BindPoses;
            return true;
        }

        static bool TryBuildHierarchyFromRigDefinition(CgfRigDefinition rigDefinition, Transform[] transforms)
        {
            var parentIndices = rigDefinition?.ParentIndices;
            if (parentIndices == null || parentIndices.Length != transforms.Length)
                return false;

            bool anyParentAssigned = false;
            for (int i = 0; i < transforms.Length; i++)
            {
                int parentIndex = parentIndices[i];
                if (parentIndex < 0 || parentIndex >= transforms.Length || parentIndex == i)
                    continue;

                var child = transforms[i];
                var parent = transforms[parentIndex];
                if (child == null || parent == null || child == parent)
                    continue;

                child.SetParent(parent, worldPositionStays: false);
                anyParentAssigned = true;
            }

            return anyParentAssigned;
        }

        static bool TryBuildHierarchyFromBoneAnim(CgfFile parsedFile, Transform[] transforms)
        {
            var bones = parsedFile?.BoneAnim?.Bones;
            if (bones == null || bones.Length == 0)
                return false;

            int cursor = 0;
            int nextBoneIndex = 0;
            bool anyParentAssigned = false;

            int Allocate(int count)
            {
                if (count < 0 || nextBoneIndex + count > transforms.Length)
                    return -1;

                int result = nextBoneIndex;
                nextBoneIndex += count;
                return result;
            }

            // Cry runtime reconstructs hierarchy from the linearized BONE_ENTITY
            // stream, allocating sibling ranges before descending into children.
            bool BuildSubtree(int parentIndex, int boneIndex)
            {
                if (cursor < 0 || cursor >= bones.Length || boneIndex < 0 || boneIndex >= transforms.Length)
                    return false;

                var entity = bones[cursor++];
                if (parentIndex >= 0 && parentIndex < transforms.Length)
                {
                    var child = transforms[boneIndex];
                    var parent = transforms[parentIndex];
                    if (child != null && parent != null && child != parent)
                    {
                        child.SetParent(parent, worldPositionStays: false);
                        anyParentAssigned = true;
                    }
                }

                int children = Mathf.Max(0, entity.ChildrenCount);
                int childrenBase = children > 0 ? Allocate(children) : -1;
                if (children > 0 && childrenBase < 0)
                    return false;

                for (int i = 0; i < children; i++)
                {
                    if (!BuildSubtree(boneIndex, childrenBase + i))
                        return false;
                }

                return true;
            }

            int rootIndex = Allocate(1);
            if (rootIndex != 0 || !BuildSubtree(-1, rootIndex))
                return false;

            if (cursor != bones.Length)
            {
                Debug.LogWarning($"[CgfImporter] BoneAnim hierarchy parse consumed {cursor}/{bones.Length} entities.");
            }

            return anyParentAssigned;
        }

        static void TryBuildHierarchyFromNodes(CgfFile parsedFile, string[] boneNames, Transform[] transforms)
        {
            if (parsedFile == null || parsedFile.NodeChunks == null || parsedFile.NodeChunks.Count == 0)
                return;

            var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var nodeByName = new Dictionary<string, CgfNodeChunk>(StringComparer.OrdinalIgnoreCase);
            var nodeByChunkId = new Dictionary<int, CgfNodeChunk>();

            for (int i = 0; i < boneNames.Length; i++)
                indexByName[boneNames[i]] = i;

            for (int i = 0; i < parsedFile.NodeChunks.Count; i++)
            {
                var node = parsedFile.NodeChunks[i];
                nodeByName[node.Name] = node;
                nodeByChunkId[node.ChunkID] = node;
            }

            for (int i = 0; i < boneNames.Length; i++)
            {
                if (!nodeByName.TryGetValue(boneNames[i], out var node))
                    continue;
                if (node.ParentID < 0)
                    continue;
                if (!nodeByChunkId.TryGetValue(node.ParentID, out var parentNode))
                    continue;
                if (!indexByName.TryGetValue(parentNode.Name, out int parentIdx))
                    continue;

                var child = transforms[i];
                var parent = transforms[parentIdx];
                if (child != null && parent != null && child != parent)
                    child.SetParent(parent, worldPositionStays: false);
            }
        }

        static void ApplyLocalMatrix(Transform t, Matrix4x4 m)
        {
            var pos = new Vector3(m.m03, m.m13, m.m23);

            var x = new Vector3(m.m00, m.m10, m.m20);
            var y = new Vector3(m.m01, m.m11, m.m21);
            var z = new Vector3(m.m02, m.m12, m.m22);

            float sx = x.magnitude;
            float sy = y.magnitude;
            float sz = z.magnitude;
            if (sx < 1e-8f || sy < 1e-8f || sz < 1e-8f)
            {
                t.localPosition = pos;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
                return;
            }

            var rx = x / sx;
            var ry = y / sy;
            var rz = z / sz;

            // Keep a proper rotation basis.
            if (Vector3.Dot(Vector3.Cross(rx, ry), rz) < 0f)
            {
                rx = -rx;
            }

            var rot = Quaternion.LookRotation(rz, ry);
            t.localPosition = pos;
            t.localRotation = rot.normalized;
            // Cry default pose matrices are expected to be orthonormal (NoScale in runtime).
            t.localScale = Vector3.one;
        }

        void ApplyPostTransform(GameObject go)
        {
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }

        void SaveAssets(Mesh mesh, GameObject go, IReadOnlyList<ImportedAnimationClip> clips)
        {
            try
            {
                var paths = GetCachePaths(_parsedPath);
                bool dirCreated = EnsureDirectory(Path.GetDirectoryName(paths.meshPath)) |
                                  EnsureDirectory(Path.GetDirectoryName(paths.prefabPath));
                if (dirCreated)
                    AssetDatabase.Refresh(); // let Unity discover new folders before creating assets

                var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(paths.meshPath);
                if (existingMesh != null)
                    AssetDatabase.DeleteAsset(paths.meshPath);
                AssetDatabase.CreateAsset(mesh, paths.meshPath);

                SaveAnimationClips(paths.meshPath, go, clips);
                PrefabUtility.SaveAsPrefabAsset(go, paths.prefabPath);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[CgfImporter] Saved mesh -> {paths.meshPath}, prefab -> {paths.prefabPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[CgfImporter] Save failed: {e}");
            }
        }

        static void SaveAnimationClips(string meshPath, GameObject go, IReadOnlyList<ImportedAnimationClip> clips)
        {
            if (clips == null || clips.Count == 0 || go == null)
                return;

            var animation = go.GetComponent<Animation>();
            if (animation == null)
                return;

            // Rebuild the clip list from scratch to avoid duplicated stale states.
            animation.clip = null;
            var existingNames = new List<string>();
            foreach (AnimationState state in animation)
                existingNames.Add(state.name);
            for (int i = 0; i < existingNames.Count; i++)
                animation.RemoveClip(existingNames[i]);

            AnimationClip defaultClip = null;
            for (int i = 0; i < clips.Count; i++)
            {
                var src = clips[i];
                if (src?.Clip == null || string.IsNullOrEmpty(src.Alias))
                    continue;

                string cacheKey = string.IsNullOrWhiteSpace(src.SharedCacheKey)
                    ? BuildSharedAnimationClipContentKey(src.Clip)
                    : src.SharedCacheKey;
                string sharedClipPath = OpenFarCry.Importer.ImportAssetPaths.GetSharedAnimationClipPath(cacheKey, src.Alias);
                EnsureDirectory(Path.GetDirectoryName(sharedClipPath));

                var sharedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(sharedClipPath);
                if (sharedClip == null)
                {
                    AssetDatabase.CreateAsset(src.Clip, sharedClipPath);
                    sharedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(sharedClipPath);
                }
                if (sharedClip == null)
                    continue;

                animation.AddClip(sharedClip, src.Alias);

                if (defaultClip == null ||
                    string.Equals(src.Alias, "default", StringComparison.OrdinalIgnoreCase))
                    defaultClip = sharedClip;

                // Cleanup old per-character duplicate clip path if it exists.
                string legacyClipPath = GetAnimationAssetPath(meshPath, src.Alias);
                if (!string.Equals(legacyClipPath, sharedClipPath, StringComparison.OrdinalIgnoreCase))
                {
                    var legacyClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(legacyClipPath);
                    if (legacyClip != null)
                        AssetDatabase.DeleteAsset(legacyClipPath);
                }
            }

            if (defaultClip != null)
                animation.clip = defaultClip;
        }

        // Returns true if directory was newly created
        static bool EnsureDirectory(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;
            string full = Path.GetFullPath(assetPath);
            if (Directory.Exists(full)) return false;
            Directory.CreateDirectory(full);
            return true;
        }

        static (string meshPath, string prefabPath) GetCachePaths(string virtualPath)
        {
            return OpenFarCry.Importer.ImportAssetPaths.GetCgfCachePaths(virtualPath);
        }

        static string GetAnimationAssetPath(string meshPath, string alias)
        {
            string noExt = meshPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                ? meshPath.Substring(0, meshPath.Length - ".asset".Length)
                : meshPath;
            return $"{noExt}@{SanitizeFileName(alias)}.anim";
        }

        static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "anim";

            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0 ||
                    chars[i] == '/' ||
                    chars[i] == '\\')
                    chars[i] = '_';
            }

            return new string(chars);
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
