using System;
using System.Collections.Generic;
using System.IO;
using Unity.SharpZipLib.Zip;

namespace OpenFarCry.FileSystem
{
    public sealed class PakArchive : IDisposable
    {
        public string DiskPath { get; }
        public string BindRoot { get; }

        // Populated lazily on first EnsureOpen(); safe to read afterwards.
        public int FileCount => _index.Count;

        private ZipFile _zipFile;
        private FileStream _fileStream;
        private readonly object _lock = new object();

        // normalized virtual path → ZipEntry index in _zipFile
        private readonly Dictionary<string, int> _index =
            new Dictionary<string, int>(StringComparer.Ordinal);

        // normalized dir → list of all virtual paths whose immediate parent is that dir
        private readonly Dictionary<string, List<string>> _directoryIndex =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public PakArchive(string diskPath, string bindRoot = "")
        {
            DiskPath = diskPath;
            BindRoot = NormalizePath(bindRoot);
        }

        public bool Exists(string virtualPath)
        {
            EnsureOpen();
            return _index.ContainsKey(NormalizePath(virtualPath));
        }

        public bool TryRead(string virtualPath, out byte[] data)
        {
            EnsureOpen();
            var key = NormalizePath(virtualPath);

            lock (_lock)
            {
                if (!_index.TryGetValue(key, out int entryIndex))
                {
                    data = null;
                    return false;
                }

                var entry = _zipFile[entryIndex];
                data = new byte[entry.Size];
                using var stream = _zipFile.GetInputStream(entry);
                int offset = 0;
                while (offset < data.Length)
                {
                    int read = stream.Read(data, offset, data.Length - offset);
                    if (read == 0) break;
                    offset += read;
                }
                return true;
            }
        }

        public IEnumerable<string> GetEntriesInDirectory(string virtualDir)
        {
            EnsureOpen();
            var dir = NormalizePath(virtualDir);
            if (dir.Length == 0)
            {
                foreach (var list in _directoryIndex.Values)
                    foreach (var entry in list)
                        yield return entry;
                yield break;
            }
            string prefix = dir + "/";
            foreach (var kvp in _directoryIndex)
            {
                if (kvp.Key == dir || kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                    foreach (var entry in kvp.Value)
                        yield return entry;
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _zipFile?.Close();
                _fileStream?.Dispose();
                _zipFile = null;
                _fileStream = null;
            }
        }

        private void EnsureOpen()
        {
            if (_zipFile != null) return;
            lock (_lock)
            {
                if (_zipFile != null) return;
                _fileStream = new FileStream(DiskPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                _zipFile = new ZipFile(_fileStream);

                for (int i = 0; i < _zipFile.Count; i++)
                {
                    var entry = _zipFile[i];
                    if (!entry.IsFile) continue;
                    string vp = BuildVirtualPath(entry.Name);
                    _index[vp] = i;
                    int slash = vp.LastIndexOf('/');
                    string dir = slash > 0 ? vp.Substring(0, slash) : string.Empty;
                    if (!_directoryIndex.TryGetValue(dir, out var dirList))
                        _directoryIndex[dir] = dirList = new List<string>();
                    dirList.Add(vp);
                }
            }
        }

        private string BuildVirtualPath(string entryName)
        {
            var normalized = NormalizePath(entryName);
            return BindRoot.Length > 0 ? BindRoot + "/" + normalized : normalized;
        }

        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            path = path.Replace('\\', '/').ToLowerInvariant().Trim('/');
            // CGF/CAF asset references often contain collapsed "//" segments from
            // path concatenation; flatten them so lookups match the index.
            while (path.Contains("//"))
                path = path.Replace("//", "/");
            return path;
        }
    }
}
