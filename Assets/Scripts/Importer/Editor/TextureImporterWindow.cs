using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenFarCry.FileSystem;
using OpenFarCry.Importer.Texture;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    public class TextureImporterWindow : EditorWindow
    {
        string _searchFilter = string.Empty;
        readonly List<string> _allPaths = new List<string>();
        List<string> _filteredPaths = new List<string>();
        readonly Dictionary<string, List<string>> _groupedPaths =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _expandedDirs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string _selectedPath;
        Vector2 _listScroll;

        bool _overwriteExisting = true;
        bool _readable = false;
        bool _autoDetectNormalMap = true;

        GUIStyle _selectedRowStyle;

        [MenuItem("OpenFarCry/Texture Importer")]
        public static void ShowWindow()
        {
            var win = GetWindow<TextureImporterWindow>("Texture Importer");
            win.minSize = new Vector2(420f, 520f);
        }

        void OnEnable()
        {
            RefreshFileList();
        }

        void OnGUI()
        {
            EnsureStyles();

            DrawToolbar();
            DrawSearchBar();
            DrawFileList();
            GUILayout.Space(4f);
            DrawInfoPanel();
            GUILayout.Space(4f);
            DrawOptions();
            GUILayout.Space(8f);
            DrawActions();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Обновить список", EditorStyles.toolbarButton, GUILayout.Width(120f)))
                    RefreshFileList();

                GUILayout.FlexibleSpace();
                GUILayout.Label($"{_filteredPaths.Count} / {_allPaths.Count} файлов", EditorStyles.toolbarButton);
            }
        }

        void DrawSearchBar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Фильтр:", GUILayout.Width(48f));
                string next = EditorGUILayout.TextField(_searchFilter);
                if (!string.Equals(next, _searchFilter, StringComparison.Ordinal))
                {
                    _searchFilter = next;
                    ApplyFilter();
                }
            }
        }

        void DrawFileList()
        {
            float listHeight = Mathf.Min(position.height * 0.42f, 260f);
            using var scroll = new EditorGUILayout.ScrollViewScope(_listScroll, GUILayout.Height(listHeight));
            _listScroll = scroll.scrollPosition;

            if (_groupedPaths.Count == 0)
            {
                EditorGUILayout.LabelField("Нет файлов по текущему фильтру.", EditorStyles.miniLabel);
                return;
            }

            foreach (var kv in _groupedPaths.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                bool expanded = _expandedDirs.Contains(kv.Key);
                bool nextExpanded = EditorGUILayout.Foldout(expanded, $"{kv.Key} ({kv.Value.Count})", true);
                if (nextExpanded) _expandedDirs.Add(kv.Key);
                else _expandedDirs.Remove(kv.Key);

                if (!nextExpanded)
                    continue;

                using (new EditorGUI.IndentLevelScope())
                {
                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        string path = kv.Value[i];
                        bool selected = string.Equals(path, _selectedPath, StringComparison.OrdinalIgnoreCase);
                        var style = selected ? _selectedRowStyle : EditorStyles.label;
                        string label = Path.GetFileName(path);
                        var rect = GUILayoutUtility.GetRect(new GUIContent(label), style, GUILayout.ExpandWidth(true));
                        if (GUI.Button(rect, new GUIContent(label, path), style))
                            _selectedPath = path;
                    }
                }
            }
        }

        void DrawInfoPanel()
        {
            EditorGUILayout.LabelField("Информация о выборе", EditorStyles.boldLabel);

            if (string.IsNullOrEmpty(_selectedPath))
            {
                EditorGUILayout.LabelField("— выберите файл из списка", EditorStyles.miniLabel);
                return;
            }

            string assetPath = OpenFarCry.Importer.ImportAssetPaths.GetAssetPathWithOriginalExtension(_selectedPath);
            bool exists = File.Exists(Path.GetFullPath(assetPath));

            EditorGUILayout.LabelField("Virtual path:", _selectedPath);
            EditorGUILayout.LabelField("Asset path:", assetPath);
            EditorGUILayout.LabelField("Extension:", Path.GetExtension(_selectedPath).ToLowerInvariant());
            EditorGUILayout.LabelField("В проекте:", exists ? "уже существует" : "не импортирован");
        }

        void DrawOptions()
        {
            EditorGUILayout.LabelField("Опции импорта", EditorStyles.boldLabel);
            _overwriteExisting = EditorGUILayout.ToggleLeft("Перезаписывать существующие", _overwriteExisting);
            _readable = EditorGUILayout.ToggleLeft("Сделать Texture readable", _readable);
            _autoDetectNormalMap = EditorGUILayout.ToggleLeft("Авто-детект normal map (_ddn, _bump)", _autoDetectNormalMap);
        }

        void DrawActions()
        {
            bool hasSelection = !string.IsNullOrEmpty(_selectedPath);
            bool hasFiltered = _filteredPaths.Count > 0;

            using (new EditorGUI.DisabledScope(!hasSelection))
            {
                if (GUILayout.Button("Импортировать выбранную", GUILayout.Height(30f)))
                    ImportSelected();
            }

            using (new EditorGUI.DisabledScope(!hasFiltered))
            {
                if (GUILayout.Button("Импортировать все по фильтру", GUILayout.Height(30f)))
                    ImportFiltered();
            }
        }

        void RefreshFileList()
        {
            _allPaths.Clear();

            if (FcFileSystem.MountedCount == 0)
            {
                Debug.LogWarning("[TextureImporter] VFS has no mounted PAKs. Check FcFileSystemSettings in Resources/.");
                ApplyFilter();
                return;
            }

            foreach (var path in FcFileSystem.GetEntries(string.Empty))
            {
                if (TextureImportService.IsSupportedVirtualPath(path))
                    _allPaths.Add(path);
            }

            _allPaths.Sort(StringComparer.OrdinalIgnoreCase);
            ApplyFilter();
        }

        void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(_searchFilter))
            {
                _filteredPaths = new List<string>(_allPaths);
            }
            else
            {
                _filteredPaths = _allPaths
                    .Where(p => p.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            RebuildGroupedPaths();
            if (!string.IsNullOrEmpty(_selectedPath) &&
                !_filteredPaths.Any(p => string.Equals(p, _selectedPath, StringComparison.OrdinalIgnoreCase)))
            {
                _selectedPath = null;
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
                if (!_groupedPaths.TryGetValue(dir, out var list))
                {
                    list = new List<string>();
                    _groupedPaths[dir] = list;
                    if (_expandedDirs.Count < 10)
                        _expandedDirs.Add(dir);
                }

                list.Add(path);
            }

            foreach (var pair in _groupedPaths)
                pair.Value.Sort(StringComparer.OrdinalIgnoreCase);
        }

        static string GetDirectoryLabel(string virtualPath)
        {
            int slash = virtualPath.LastIndexOf('/');
            if (slash <= 0)
                return "<root>";
            return virtualPath.Substring(0, slash);
        }

        TextureImportOptions GetOptions()
        {
            return new TextureImportOptions(
                _overwriteExisting,
                _readable,
                _autoDetectNormalMap);
        }

        void ImportSelected()
        {
            var result = TextureImportEditorService.ImportTexture(_selectedPath, GetOptions());
            ReportResult(result);
        }

        void ImportFiltered()
        {
            var options = GetOptions();
            int imported = 0;
            int skipped = 0;
            int failed = 0;
            var failedLines = new List<string>(8);

            try
            {
                for (int i = 0; i < _filteredPaths.Count; i++)
                {
                    string path = _filteredPaths[i];
                    EditorUtility.DisplayProgressBar(
                        "Texture Import",
                        path,
                        _filteredPaths.Count == 0 ? 1f : (float)i / _filteredPaths.Count);

                    var result = TextureImportEditorService.ImportTexture(path, options);
                    switch (result.Status)
                    {
                        case TextureImportStatus.Imported:
                            imported++;
                            break;
                        case TextureImportStatus.SkippedAlreadyExists:
                            skipped++;
                            break;
                        default:
                            failed++;
                            if (failedLines.Count < 8)
                                failedLines.Add($"{result.VirtualPath}: {result.Message}");
                            break;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[TextureImporter] Done. Imported={imported}, Skipped={skipped}, Failed={failed}.");

            if (failedLines.Count > 0)
            {
                Debug.LogWarning("[TextureImporter] Failures:\n" + string.Join("\n", failedLines));
            }
        }

        void ReportResult(TextureImportResult result)
        {
            switch (result.Status)
            {
                case TextureImportStatus.Imported:
                    if (result.Texture != null)
                        Selection.activeObject = result.Texture;
                    Debug.Log($"[TextureImporter] Imported '{result.VirtualPath}' -> {result.AssetPath}");
                    break;
                case TextureImportStatus.SkippedAlreadyExists:
                    Debug.Log($"[TextureImporter] Skipped existing: {result.AssetPath}");
                    break;
                default:
                    Debug.LogError($"[TextureImporter] Failed '{result.VirtualPath}': {result.Message}");
                    EditorUtility.DisplayDialog("Ошибка импорта текстуры", $"{result.VirtualPath}\n{result.Message}", "OK");
                    break;
            }
        }

        void EnsureStyles()
        {
            if (_selectedRowStyle != null)
                return;

            _selectedRowStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold
            };
        }
    }
}
