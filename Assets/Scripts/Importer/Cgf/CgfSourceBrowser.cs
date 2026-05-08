using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenFarCry.FileSystem;

namespace OpenFarCry.Importer.Cgf
{
    public sealed class CgfSourceBrowser
    {
        readonly CgfResourceImportService _resourceImportService;
        readonly CgfLodImportService _lodImportService;

        public CgfSourceBrowser(
            CgfResourceImportService resourceImportService = null,
            CgfLodImportService lodImportService = null)
        {
            _resourceImportService = resourceImportService ?? CgfResourceImportService.Instance;
            _lodImportService = lodImportService ?? new CgfLodImportService();
        }

        public sealed class FilterResult
        {
            public readonly List<string> FilteredPaths;
            public readonly Dictionary<string, List<string>> GroupedPaths;
            public readonly List<string> SuggestedExpandedDirectories;
            public readonly bool ContainsSelectedPath;

            public FilterResult(
                List<string> filteredPaths,
                Dictionary<string, List<string>> groupedPaths,
                List<string> suggestedExpandedDirectories,
                bool containsSelectedPath)
            {
                FilteredPaths = filteredPaths;
                GroupedPaths = groupedPaths;
                SuggestedExpandedDirectories = suggestedExpandedDirectories;
                ContainsSelectedPath = containsSelectedPath;
            }
        }

        public sealed class ParseSelectionResult
        {
            public readonly string ParsedPath;
            public readonly CgfFile ParsedFile;
            public readonly string ParseError;
            public readonly string ParseNote;
            public readonly List<string> SiblingLodPaths;
            public readonly int SelectedMeshListIndex;

            public ParseSelectionResult(
                string parsedPath,
                CgfFile parsedFile,
                string parseError,
                string parseNote,
                List<string> siblingLodPaths,
                int selectedMeshListIndex)
            {
                ParsedPath = parsedPath;
                ParsedFile = parsedFile;
                ParseError = parseError;
                ParseNote = parseNote;
                SiblingLodPaths = siblingLodPaths ?? new List<string>();
                SelectedMeshListIndex = selectedMeshListIndex;
            }
        }

        public List<string> LoadAllSupportedPaths()
        {
            var allPaths = new List<string>();
            foreach (var path in FcFileSystem.GetEntries(string.Empty))
            {
                if (_resourceImportService.IsSupportedVirtualPath(path))
                    allPaths.Add(path);
            }

            allPaths.Sort(StringComparer.OrdinalIgnoreCase);
            return allPaths;
        }

        public FilterResult ApplyFilter(IReadOnlyList<string> allPaths, string searchFilter, string selectedPath, int autoExpandLimit)
        {
            var filtered = string.IsNullOrWhiteSpace(searchFilter)
                ? new List<string>(allPaths ?? Array.Empty<string>())
                : (allPaths ?? Array.Empty<string>())
                    .Where(p => p.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

            var grouped = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var suggestedExpanded = new List<string>();

            for (int i = 0; i < filtered.Count; i++)
            {
                string path = filtered[i];
                string dir = GetDirectoryLabel(path);
                if (!grouped.TryGetValue(dir, out var files))
                {
                    files = new List<string>();
                    grouped[dir] = files;

                    if (suggestedExpanded.Count < autoExpandLimit)
                        suggestedExpanded.Add(dir);
                }

                files.Add(path);
            }

            foreach (var kv in grouped)
                kv.Value.Sort(StringComparer.OrdinalIgnoreCase);

            bool containsSelectedPath = !string.IsNullOrEmpty(selectedPath) &&
                                        (allPaths ?? Array.Empty<string>())
                                            .Contains(selectedPath, StringComparer.OrdinalIgnoreCase);

            return new FilterResult(filtered, grouped, suggestedExpanded, containsSelectedPath);
        }

        public ParseSelectionResult ParseSelection(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ParseSelectionResult(
                    parsedPath: path,
                    parsedFile: null,
                    parseError: "Path is null or empty.",
                    parseNote: null,
                    siblingLodPaths: new List<string>(),
                    selectedMeshListIndex: 0);
            }

            try
            {
                byte[] data = _resourceImportService.LoadProjectAssetSourceBytes(path);
                var parsedFile = CgfParser.Parse(data);
                parsedFile.SourceVirtualPath = path;
                var siblingLods = _lodImportService.FindSiblingLodPaths(path);
                string parseNote = BuildParseNote(path, parsedFile);
                int selectedMeshIndex = ResolveSelectedMeshListIndex(parsedFile);

                return new ParseSelectionResult(
                    parsedPath: path,
                    parsedFile: parsedFile,
                    parseError: null,
                    parseNote: parseNote,
                    siblingLodPaths: siblingLods,
                    selectedMeshListIndex: selectedMeshIndex);
            }
            catch (Exception e)
            {
                return new ParseSelectionResult(
                    parsedPath: path,
                    parsedFile: null,
                    parseError: e.Message,
                    parseNote: null,
                    siblingLodPaths: new List<string>(),
                    selectedMeshListIndex: 0);
            }
        }

        static string GetDirectoryLabel(string virtualPath)
        {
            int slash = virtualPath.LastIndexOf('/');
            if (slash <= 0)
                return "<root>";
            return virtualPath.Substring(0, slash);
        }

        static string BuildParseNote(string parsedPath, CgfFile parsedFile)
        {
            if (parsedFile == null)
                return null;

            if (parsedPath.EndsWith(".cga", StringComparison.OrdinalIgnoreCase))
            {
                return "CGA импортируется только как geometry preview. " +
                       "Controller/Timing/ANM связки пока не обрабатываются.";
            }

            if (parsedFile.MeshChunks.Count > 1)
                return "В файле несколько mesh-чанков. Выберите нужный в поле Mesh chunk.";

            return null;
        }

        static int ResolveSelectedMeshListIndex(CgfFile parsedFile)
        {
            if (parsedFile == null || parsedFile.MeshChunks == null || parsedFile.MeshChunks.Count == 0)
                return 0;

            int selectedId = parsedFile.SelectedMeshChunkID;
            if (selectedId < 0)
                return 0;

            for (int i = 0; i < parsedFile.MeshChunks.Count; i++)
            {
                if (parsedFile.MeshChunks[i].ChunkID == selectedId)
                    return i;
            }

            return 0;
        }
    }
}
