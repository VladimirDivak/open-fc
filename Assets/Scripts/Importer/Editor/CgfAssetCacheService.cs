using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenFarCry.Importer.Cgf;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

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

        public bool TryLoadCompatibleCachedPrefab(
            string virtualPath,
            CgfFile parsedFile,
            out GameObject prefabAsset)
        {
            prefabAsset = null;
            if (string.IsNullOrWhiteSpace(virtualPath) || parsedFile == null)
                return false;

            var paths = GetCachePaths(virtualPath);
            var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(paths.PrefabPath);
            if (candidate == null || !IsCachedPrefabCompatible(candidate, parsedFile))
                return false;

            prefabAsset = candidate;
            return true;
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

                TryGenerateSecondaryUVSet(mesh, virtualPath);

                var existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(paths.MeshPath);
                if (existingMesh != null)
                    AssetDatabase.DeleteAsset(paths.MeshPath);
                AssetDatabase.CreateAsset(mesh, paths.MeshPath);

                PersistMaterialAndTextureAssets(virtualPath, go);
                _animationCacheService.RebindAnimationComponentToSharedClips(paths.MeshPath, go, clips);

                // Stamp the mesh-builder version so cache validation does not depend on
                // mesh.name (which now carries the original CGF-derived name instead).
                var buildStamp = go.GetComponent<CgfMeshBuildStamp>();
                if (buildStamp == null)
                    buildStamp = go.AddComponent<CgfMeshBuildStamp>();
                buildStamp.SetVersion(CgfMeshBuilder.MeshCacheVersionName);

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

        static void TryGenerateSecondaryUVSet(Mesh mesh, string virtualPath)
        {
            if (mesh == null) return;
            if (mesh.vertexCount == 0 || mesh.subMeshCount == 0)
                return;

            bool hasIndices = false;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetIndexCount(i) > 0)
                {
                    hasIndices = true;
                    break;
                }
            }
            if (!hasIndices)
                return;

            try
            {
                Unwrapping.GenerateSecondaryUVSet(mesh);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CgfImporter] UV2 generation skipped for '{virtualPath}': {e.Message}");
            }
        }

        void PersistMaterialAndTextureAssets(string virtualPath, GameObject go)
        {
            if (string.IsNullOrWhiteSpace(virtualPath) || go == null)
                return;

            string baseAssetPath = ImportAssetPaths.GetBaseAssetPathNoExtension(virtualPath);
            string materialsDir = $"{baseAssetPath}_materials";
            string texturesDir = $"{baseAssetPath}_textures";
            EnsureDirectory(materialsDir);
            EnsureDirectory(texturesDir);

            var materialBySourceId = new Dictionary<int, Material>();
            var textureByKey = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var renderer = renderers[r];
                var shared = renderer.sharedMaterials;
                if (shared == null || shared.Length == 0)
                    continue;

                bool changed = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    var source = shared[i];
                    if (source == null)
                        continue;

                    if (EditorUtility.IsPersistent(source))
                        continue;

                    int sourceId = source.GetInstanceID();
                    if (!materialBySourceId.TryGetValue(sourceId, out var persistedMaterial) || persistedMaterial == null)
                    {
                        string materialName = SanitizePathSegment(string.IsNullOrWhiteSpace(source.name) ? $"mat_{r}_{i}" : source.name);
                        string materialPath = $"{materialsDir}/{materialName}_{sourceId}.mat";
                        persistedMaterial = PersistMaterialAsset(source, materialPath, texturesDir, textureByKey);
                        if (persistedMaterial == null)
                            continue;

                        materialBySourceId[sourceId] = persistedMaterial;
                    }

                    shared[i] = persistedMaterial;
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = shared;
            }
        }

        static Material PersistMaterialAsset(
            Material source,
            string materialPath,
            string texturesDir,
            Dictionary<string, Texture2D> textureByKey)
        {
            if (source == null)
                return null;

            var clone = new Material(source)
            {
                name = source.name
            };

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
                        texturesDir,
                        textureByKey);
                    if (persistedTexture != null)
                        clone.SetTexture(propertyName, persistedTexture);
                }
            }

            if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) != null)
                AssetDatabase.DeleteAsset(materialPath);

            try
            {
                AssetDatabase.CreateAsset(clone, materialPath);
                return AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CgfImporter] Failed to persist material asset '{materialPath}': {e.Message}");
                Object.DestroyImmediate(clone);
                return null;
            }
        }

        internal static Texture2D PersistTextureAsset(
            Texture2D source,
            string propertyName,
            string texturesDir,
            Dictionary<string, Texture2D> textureByKey)
        {
            if (source == null)
                return null;

            if (EditorUtility.IsPersistent(source))
                return source;

            string textureName = SanitizePathSegment(string.IsNullOrWhiteSpace(source.name) ? "tex" : source.name);
            string propertyToken = SanitizePathSegment(string.IsNullOrWhiteSpace(propertyName) ? "map" : propertyName);
            string key = source.GetInstanceID().ToString();

            if (textureByKey.TryGetValue(key, out var cachedTexture) && cachedTexture != null)
                return cachedTexture;

            string texturePath = $"{texturesDir}/{textureName}_{propertyToken}_{source.GetInstanceID()}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (existing != null)
            {
                textureByKey[key] = existing;
                return existing;
            }

            if (!TryDuplicateTextureForAsset(source, out var clone))
            {
                Debug.LogWarning($"[CgfImporter] Failed to duplicate texture '{source.name}' for cache asset.");
                return null;
            }

            clone.name = source.name;
            try
            {
                AssetDatabase.CreateAsset(clone, texturePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CgfImporter] Failed to persist texture asset '{texturePath}': {e.Message}");
                Object.DestroyImmediate(clone);
                return null;
            }

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
            catch (Exception e)
            {
                if (clone != null)
                    Object.DestroyImmediate(clone);
                clone = null;
                Debug.LogWarning($"[CgfImporter] Texture duplication failed: {e.Message}");
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
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
            // Validate cache freshness via the build stamp, not mesh.name (mesh.name now
            // carries the original CGF name). A stamp-less prefab predates this scheme.
            var buildStamp = prefabAsset.GetComponentInChildren<CgfMeshBuildStamp>(true);
            if (buildStamp == null ||
                !string.Equals(buildStamp.MeshBuilderVersion, CgfMeshBuilder.MeshCacheVersionName, StringComparison.Ordinal))
                return false;

            int expectedSubmeshCount = parsedFile.MeshChunk.Faces
                .Select(f => f.MatID)
                .Distinct()
                .Count();
            // Proxy-stripped meshes have fewer submeshes than the raw CGF; allow equal or fewer.
            if (mesh.subMeshCount > expectedSubmeshCount)
                return false;

            bool expectedHasUv0 = parsedFile.MeshChunk.UVs.IsCreated && parsedFile.MeshChunk.UVs.Length > 0;
            bool actualHasUv0 = mesh.uv != null && mesh.uv.Length > 0;
            if (expectedHasUv0 != actualHasUv0)
                return false;

            return true;
        }

        internal static bool EnsureDirectory(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;
            string full = Path.GetFullPath(assetPath);
            if (Directory.Exists(full))
                return false;
            Directory.CreateDirectory(full);
            return true;
        }

        static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "asset";

            value = value.Trim();
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool isControlOrSpace = char.IsControl(c);
                bool isUnityUnsafe =
                    c == '"' || c == ':' || c == '*' || c == '?' ||
                    c == '<' || c == '>' || c == '|' || c == '#';
                if (c == '/' || c == '\\' || invalid.Contains(c) || isControlOrSpace || isUnityUnsafe)
                    chars[i] = '_';
            }

            string sanitized = new string(chars).Trim();
            while (sanitized.Contains("  "))
                sanitized = sanitized.Replace("  ", " ");
            sanitized = sanitized.Replace(' ', '_');
            sanitized = sanitized.Trim('.', '_');
            if (string.IsNullOrEmpty(sanitized))
                return "asset";
            if (sanitized.Length > 96)
                sanitized = sanitized.Substring(0, 96);
            return sanitized;
        }
    }
}
