using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace OpenFarCry.FileSystem
{
    public static class FcFileSystem
    {
        // Mount order: index 0 = first mounted = lowest priority.
        // Lookup iterates in reverse (LIFO).
        private static readonly List<PakArchive> _archives = new List<PakArchive>();
        private static readonly HashSet<string> _mountedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly ReaderWriterLockSlim _mountLock = new ReaderWriterLockSlim();

        private static bool _initialized;

        public static int MountedCount
        {
            get
            {
                _mountLock.EnterReadLock();
                try { return _archives.Count; }
                finally { _mountLock.ExitReadLock(); }
            }
        }

        public static int TotalFileCount
        {
            get
            {
                _mountLock.EnterReadLock();
                try
                {
                    int n = 0;
                    foreach (var a in _archives) n += a.FileCount;
                    return n;
                }
                finally { _mountLock.ExitReadLock(); }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            var settings = Resources.Load<FcFileSystemSettings>("FcFileSystemSettings");
            if (settings == null)
            {
                Debug.LogError("[FcFileSystem] FcFileSystemSettings not found in Resources/. " +
                               "Create it via Assets > Create > OpenFarCry > File System Settings.");
                return;
            }

            var installPath = settings.gameInstallPath;
            if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
            {
                Debug.LogError($"[FcFileSystem] gameInstallPath is invalid: '{installPath}'");
                return;
            }

            var pakDir = Path.Combine(installPath, "FCData");
            if (!Directory.Exists(pakDir))
            {
                Debug.LogError($"[FcFileSystem] FCData directory not found at: {pakDir}");
                return;
            }

            // Alphabetical sort → alphabetically later file has higher LIFO priority,
            // matching CryEngine patch-pak convention.
            var pakFiles = Directory.GetFiles(pakDir, "*.pak");
            Array.Sort(pakFiles, StringComparer.OrdinalIgnoreCase);

            foreach (var pak in pakFiles)
                Mount(pak, bindRoot: "");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[FcFileSystem] Initialized: {_archives.Count} PAKs mounted.");
#endif
        }

        /// Mounts a PAK file. Safe to call from any thread.
        /// For level PAKs pass bindRoot e.g. "levels/farm".
        public static void Mount(string pakPath, string bindRoot = "")
        {
            if (!File.Exists(pakPath))
            {
                Debug.LogWarning($"[FcFileSystem] PAK not found, skipping: {pakPath}");
                return;
            }

            var fullPath = Path.GetFullPath(pakPath);

            _mountLock.EnterWriteLock();
            try
            {
                if (_mountedPaths.Contains(fullPath))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[FcFileSystem] Already mounted, skipping: {Path.GetFileName(pakPath)}");
#endif
                    return;
                }
                var archive = new PakArchive(fullPath, bindRoot);
                _archives.Add(archive);
                _mountedPaths.Add(fullPath);
            }
            finally { _mountLock.ExitWriteLock(); }
        }

        public static bool Exists(string virtualPath)
        {
            _mountLock.EnterReadLock();
            try
            {
                for (int i = _archives.Count - 1; i >= 0; i--)
                    if (_archives[i].Exists(virtualPath))
                        return true;
                return false;
            }
            finally { _mountLock.ExitReadLock(); }
        }

        /// Throws FileNotFoundException if the path is not found in any mounted PAK.
        public static byte[] ReadAllBytes(string virtualPath)
        {
            _mountLock.EnterReadLock();
            try
            {
                for (int i = _archives.Count - 1; i >= 0; i--)
                    if (_archives[i].TryRead(virtualPath, out var data))
                        return data;
            }
            finally { _mountLock.ExitReadLock(); }

            throw new FileNotFoundException($"[FcFileSystem] Virtual path not found: '{virtualPath}'");
        }

        /// Offloads decompression to the thread pool; returns to main thread when done.
        public static async UniTask<byte[]> ReadAllBytesAsync(
            string virtualPath,
            CancellationToken cancellationToken = default)
        {
            await UniTask.SwitchToThreadPool();
            cancellationToken.ThrowIfCancellationRequested();
            var result = ReadAllBytes(virtualPath);
            await UniTask.SwitchToMainThread(cancellationToken);
            return result;
        }

        /// Returns all virtual paths under the given virtual directory (across all mounted PAKs).
        public static IEnumerable<string> GetEntries(string directory)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            _mountLock.EnterReadLock();
            try
            {
                for (int i = _archives.Count - 1; i >= 0; i--)
                    foreach (var entry in _archives[i].GetEntriesInDirectory(directory))
                        seen.Add(entry);
            }
            finally { _mountLock.ExitReadLock(); }
            return seen;
        }
    }
}
