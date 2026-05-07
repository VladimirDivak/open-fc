using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenFarCry.Importer.Cgf;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    public sealed class CgfAssetCacheService
    {
        readonly CgfAnimationCacheService _animationCacheService;

        public CgfAssetCacheService(CgfAnimationCacheService animationCacheService = null)
        {
            _animationCacheService = animationCacheService ?? new CgfAnimationCacheService();
        }

        public readonly struct CachePaths
        {
            public readonly string MeshPath;
            public readonly string PrefabPath;

            public CachePaths(string meshPath, string prefabPath)
            {
                MeshPath = meshPath;
                PrefabPath = prefabPath;
            }
        }

        public CachePaths GetCachePaths(string virtualPath)
        {
            var paths = ImportAssetPaths.GetCgfCachePaths(virtualPath);
            return new CachePaths(paths.meshPath, paths.prefabPath);
        }

        public bool TryInstantiateCachedPrefab(
            string virtualPath,
            CgfFile parsedFile,
            out GameObject go)
        {
            go = null;
            if (string.IsNullOrEmpty(virtualPath) || parsedFile == null)
                return false;

            var paths = GetCachePaths(virtualPath);
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(paths.PrefabPath);
            if (prefabAsset == null)
                return false;

            if (!IsCachedPrefabCompatible(prefabAsset, parsedFile))
                return false;

            go = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
            if (go == null)
                return false;

            Undo.RegisterCreatedObjectUndo(go, "Instantiate Cached CGF");
            return true;
        }

        public void SaveAssets(
            string virtualPath,
            Mesh mesh,
            GameObject go,
            IReadOnlyList<CgfImportedAnimationClip> clips)
        {
            if (string.IsNullOrEmpty(virtualPath) || mesh == null || go == null)
                return;

            try
            {
                var paths = GetCachePaths(virtualPath);
                bool dirCreated = EnsureDirectory(Path.GetDirectoryName(paths.MeshPath)) |
                                  EnsureDirectory(Path.GetDirectoryName(paths.PrefabPath));
                if (dirCreated)
                    AssetDatabase.Refresh();

                var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(paths.MeshPath);
                if (existingMesh != null)
                    AssetDatabase.DeleteAsset(paths.MeshPath);
                AssetDatabase.CreateAsset(mesh, paths.MeshPath);

                _animationCacheService.RebindAnimationComponentToSharedClips(paths.MeshPath, go, clips);
                PrefabUtility.SaveAsPrefabAsset(go, paths.PrefabPath);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log($"[CgfImporter] Saved mesh -> {paths.MeshPath}, prefab -> {paths.PrefabPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[CgfImporter] Save failed: {e}");
            }
        }

        public Mesh PersistMeshAssetForVirtualPath(Mesh mesh, string virtualPath)
        {
            if (mesh == null || string.IsNullOrEmpty(virtualPath))
                return mesh;

            var paths = GetCachePaths(virtualPath);
            bool dirCreated = EnsureDirectory(Path.GetDirectoryName(paths.MeshPath));
            if (dirCreated)
                AssetDatabase.Refresh();

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(paths.MeshPath);
            if (existing != null)
                AssetDatabase.DeleteAsset(paths.MeshPath);

            AssetDatabase.CreateAsset(mesh, paths.MeshPath);
            var persisted = AssetDatabase.LoadAssetAtPath<Mesh>(paths.MeshPath);
            return persisted != null ? persisted : mesh;
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

        static bool EnsureDirectory(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;
            string full = Path.GetFullPath(assetPath);
            if (Directory.Exists(full))
                return false;
            Directory.CreateDirectory(full);
            return true;
        }
    }
}
