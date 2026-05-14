using System;
using System.Collections.Generic;
using OpenFarCry.Importer;
using OpenFarCry.Importer.Cgf;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenFarCry.Level.Services
{
    // Loads vegetation CGF at runtime from VFS and renders via GPU instancing with LOD.
    // No geometry is written to disk — all data stays in memory.
    public sealed class FcVegetationTerrainService : MonoBehaviour
    {
        [Serializable]
        public struct VegetationTypeEntry
        {
            public int TypeIndex;
            public string VirtualPath;
        }

        [Serializable]
        public struct VegetationInstanceData
        {
            public int TypeIndex;
            public float PosX;   // normalized 0..1 in terrain space
            public float PosZ;   // normalized 0..1 in terrain space
            public float Scale;
        }

        [SerializeField] VegetationTypeEntry[] _vegetationTypes;
        [SerializeField] VegetationInstanceData[] _instances;
        [SerializeField, Min(1f)] float _lod0Distance = 50f;
        [SerializeField, Min(2f)] float _cullDistance = 300f;

        const int BatchSize = 1023;

        struct RuntimeType
        {
            public Mesh[] LodMeshes;          // [0]=base LOD, [1..n]=coarser LODs
            public Material[][] LodMaterials; // parallel to LodMeshes
            public float[] LodThresholdSqr;   // transition distances per lod entry
            public float CullDistanceSqr;
            public bool IsValid;
        }

        RuntimeType[] _runtimeTypes;

        // Flat per-instance data (computed once at Start).
        Vector3[] _positions;   // world positions
        float[] _scales;
        int[] _protoIndices;    // index into _runtimeTypes

        // Per-frame scratch: [protoIdx][lodLevel] -> growable list of matrices.
        // Pre-allocated to avoid GC each frame.
        List<Matrix4x4>[][] _scratch; // [proto][lod]
        readonly Matrix4x4[] _batchBuf = new Matrix4x4[BatchSize];

        string _levelScopeId;
        bool _warnedMissingLevelPreloadService;
        bool _warnedFallbackImport;

        void Start()
        {
            if (_vegetationTypes == null || _vegetationTypes.Length == 0)
                return;
            if (_instances == null || _instances.Length == 0)
                return;

            var terrain = GetComponent<Terrain>();
            float sizeX = terrain != null ? terrain.terrainData.size.x : 1f;
            float sizeZ = terrain != null ? terrain.terrainData.size.z : 1f;
            var origin = terrain != null ? terrain.transform.position : Vector3.zero;
            _levelScopeId = ResolveLevelScopeId();

            var lodService = new CgfLodImportService();
            var protoByType = new Dictionary<int, int>();
            _runtimeTypes = new RuntimeType[_vegetationTypes.Length];

            for (int i = 0; i < _vegetationTypes.Length; i++)
            {
                var entry = _vegetationTypes[i];
                protoByType[entry.TypeIndex] = i;

                if (string.IsNullOrEmpty(entry.VirtualPath))
                    continue;

                string normalizedPath;
                try
                {
                    normalizedPath = ImportAssetPaths.NormalizeVirtualPath(entry.VirtualPath);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[FcVegetationTerrainService] Invalid vegetation path '{entry.VirtualPath}': {e.Message}");
                    continue;
                }

                if (TryBuildRuntimeTypeFromPreloaded(normalizedPath, out var runtimeType))
                {
                    _runtimeTypes[i] = runtimeType;
                    continue;
                }

                // Keep local runtime fallback for standalone editor-play scenes where
                // level preload orchestration is not running.
                if (TryBuildRuntimeTypeByDirectImport(normalizedPath, lodService, out runtimeType))
                {
                    _runtimeTypes[i] = runtimeType;
                    if (!_warnedFallbackImport)
                    {
                        _warnedFallbackImport = true;
                        Debug.LogWarning(
                            "[FcVegetationTerrainService] Using direct vegetation import fallback because preloaded handles are unavailable.");
                    }
                }
            }

            // Build flat position/scale/proto arrays.
            int total = _instances.Length;
            _positions = new Vector3[total];
            _scales = new float[total];
            _protoIndices = new int[total];

            int count = 0;
            for (int i = 0; i < _instances.Length; i++)
            {
                var inst = _instances[i];
                if (!protoByType.TryGetValue(inst.TypeIndex, out int pi))
                    continue;
                if (pi < 0 || pi >= _runtimeTypes.Length)
                    continue;
                if (!_runtimeTypes[pi].IsValid)
                    continue;

                float wx = origin.x + inst.PosX * sizeX;
                float wz = origin.z + inst.PosZ * sizeZ;
                float wy = terrain != null ? terrain.SampleHeight(new Vector3(wx, 0f, wz)) : 0f;

                _positions[count] = new Vector3(wx, wy, wz);
                _scales[count] = inst.Scale > 0f ? inst.Scale : 1f;
                _protoIndices[count] = pi;
                count++;
            }

            // Trim to actual count (some instances may have been skipped due to unknown/invalid type).
            if (count < total)
            {
                Array.Resize(ref _positions, count);
                Array.Resize(ref _scales, count);
                Array.Resize(ref _protoIndices, count);
            }

            // Pre-allocate scratch lists.
            int maxLods = 0;
            for (int i = 0; i < _runtimeTypes.Length; i++)
            {
                var rt = _runtimeTypes[i];
                if (rt.IsValid && rt.LodMeshes != null)
                    maxLods = Mathf.Max(maxLods, rt.LodMeshes.Length);
            }
            maxLods = Mathf.Max(maxLods, 1);

            _scratch = new List<Matrix4x4>[_runtimeTypes.Length][];
            for (int pi = 0; pi < _runtimeTypes.Length; pi++)
            {
                _scratch[pi] = new List<Matrix4x4>[maxLods];
                for (int li = 0; li < maxLods; li++)
                    _scratch[pi][li] = new List<Matrix4x4>(capacity: 256);
            }
        }

        void Update()
        {
            if (_positions == null || _runtimeTypes == null || _scratch == null)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;
            var camPos = cam.transform.position;

            // Clear scratch.
            for (int pi = 0; pi < _scratch.Length; pi++)
            {
                var perProto = _scratch[pi];
                for (int li = 0; li < perProto.Length; li++)
                    perProto[li].Clear();
            }

            // Partition instances into (proto, lod) buckets.
            for (int i = 0; i < _positions.Length; i++)
            {
                var pos = _positions[i];
                float sqr = (pos - camPos).sqrMagnitude;
                int pi = _protoIndices[i];
                var rt = _runtimeTypes[pi];

                if (!rt.IsValid || rt.LodMeshes == null)
                    continue;

                int lod = FindLod(rt, sqr);
                if (lod < 0)
                    continue; // beyond cull distance

                _scratch[pi][lod].Add(Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * _scales[i]));
            }

            // Draw each (proto, lod, submesh) combination in batches of 1023.
            for (int pi = 0; pi < _runtimeTypes.Length; pi++)
            {
                var rt = _runtimeTypes[pi];
                if (!rt.IsValid || rt.LodMeshes == null)
                    continue;

                for (int li = 0; li < rt.LodMeshes.Length; li++)
                {
                    var mesh = rt.LodMeshes[li];
                    if (mesh == null)
                        continue;

                    var bucket = _scratch[pi][li];
                    if (bucket.Count == 0)
                        continue;

                    var mats = rt.LodMaterials[li];

                    for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    {
                        var mat = mats != null && sub < mats.Length ? mats[sub] : null;
                        if (mat == null)
                            continue;

                        DrawBucketInstanced(mesh, sub, mat, bucket);
                    }
                }
            }
        }

        bool TryBuildRuntimeTypeFromPreloaded(string normalizedPath, out RuntimeType runtimeType)
        {
            runtimeType = default;

            var levelLoadService = FcLevelLoadService.Current;
            if (levelLoadService == null)
            {
                if (!_warnedMissingLevelPreloadService)
                {
                    _warnedMissingLevelPreloadService = true;
                    Debug.LogWarning(
                        "[FcVegetationTerrainService] FcLevelLoadService is not available; vegetation will use direct import fallback.");
                }
                return false;
            }

            if (!levelLoadService.TryGetVegetationPreloadedHandle(normalizedPath, out var handle))
                return false;

            return TryBuildRuntimeTypeFromResults(
                handle.BaseResult,
                handle.LodResults,
                _levelScopeId,
                out runtimeType);
        }

        bool TryBuildRuntimeTypeByDirectImport(
            string normalizedPath,
            CgfLodImportService lodService,
            out RuntimeType runtimeType)
        {
            runtimeType = default;

            var baseResult = CgfRuntimeImporter.Service.Import(
                CreateStaticImportRequest(normalizedPath),
                _levelScopeId);
            if (baseResult == null || !baseResult.Success)
            {
                Debug.LogWarning(
                    $"[FcVegetationTerrainService] Failed to import base vegetation '{normalizedPath}': {baseResult?.ErrorMessage ?? "Unknown error"}");
                return false;
            }

            var lodPaths = lodService.FindSiblingLodPaths(normalizedPath);
            var lodResults = new List<CgfRuntimeImportResult>(lodPaths.Count);
            for (int i = 0; i < lodPaths.Count; i++)
            {
                var lodResult = CgfRuntimeImporter.Service.Import(
                    CreateStaticImportRequest(lodPaths[i]),
                    _levelScopeId);
                if (lodResult == null || !lodResult.Success)
                    continue;
                lodResults.Add(lodResult);
            }

            return TryBuildRuntimeTypeFromResults(
                baseResult,
                lodResults,
                _levelScopeId,
                out runtimeType);
        }

        bool TryBuildRuntimeTypeFromResults(
            CgfRuntimeImportResult baseResult,
            IReadOnlyList<CgfRuntimeImportResult> lodResults,
            string textureScopeId,
            out RuntimeType runtimeType)
        {
            runtimeType = default;
            if (baseResult == null || !baseResult.Success || baseResult.Mesh == null)
                return false;

            var chain = new List<CgfRuntimeImportResult>(1 + (lodResults?.Count ?? 0))
            {
                baseResult
            };

            if (lodResults != null)
            {
                for (int i = 0; i < lodResults.Count; i++)
                {
                    var lodResult = lodResults[i];
                    if (lodResult == null || !lodResult.Success || lodResult.Mesh == null)
                        continue;
                    chain.Add(lodResult);
                }
            }

            var lodMeshes = new Mesh[chain.Count];
            var lodMaterials = new Material[chain.Count][];

            for (int i = 0; i < chain.Count; i++)
            {
                var result = chain[i];
                lodMeshes[i] = result.Mesh;
                lodMaterials[i] = CgfRuntimeImporter.MaterialService.ResolveSubmeshMaterials(
                    result.ParsedFile,
                    result.Mesh,
                    result.BuildResult?.SubmeshMaterialIds,
                    textureScopeId);

                var mats = lodMaterials[i];
                if (mats == null)
                    continue;

                for (int mi = 0; mi < mats.Length; mi++)
                {
                    var mat = mats[mi];
                    if (mat != null)
                        mat.enableInstancing = true;
                }
            }

            runtimeType = new RuntimeType
            {
                LodMeshes = lodMeshes,
                LodMaterials = lodMaterials,
                LodThresholdSqr = BuildLodThresholdsSqr(chain.Count, _lod0Distance, _cullDistance),
                CullDistanceSqr = BuildCullDistanceSqr(_lod0Distance, _cullDistance),
                IsValid = true,
            };

            return true;
        }

        static CgfRuntimeImportRequest CreateStaticImportRequest(string virtualPath)
        {
            return new CgfRuntimeImportRequest(
                virtualPath: virtualPath,
                importSkeleton: false,
                importAnimations: false,
                importScale: 0.01f,
                useRuntimeMemoryCache: true);
        }

        static float BuildCullDistanceSqr(float lod0Distance, float cullDistance)
        {
            float lod0 = Mathf.Max(1f, lod0Distance);
            float cull = Mathf.Max(lod0 + 0.01f, cullDistance);
            return cull * cull;
        }

        static float[] BuildLodThresholdsSqr(int lodCount, float lod0Distance, float cullDistance)
        {
            var thresholds = new float[Mathf.Max(1, lodCount)];
            if (lodCount <= 1)
            {
                thresholds[0] = BuildCullDistanceSqr(lod0Distance, cullDistance);
                return thresholds;
            }

            float lod0 = Mathf.Max(1f, lod0Distance);
            float cull = Mathf.Max(lod0 + 0.01f, cullDistance);
            float ratio = Mathf.Pow(cull / lod0, 1f / (lodCount - 1));

            for (int i = 0; i < lodCount; i++)
            {
                float dist = i == lodCount - 1
                    ? cull
                    : lod0 * Mathf.Pow(ratio, i);
                thresholds[i] = dist * dist;
            }

            return thresholds;
        }

        static int FindLod(in RuntimeType rt, float sqrDist)
        {
            if (!rt.IsValid || rt.LodMeshes == null || rt.LodMeshes.Length == 0)
                return -1;
            if (sqrDist >= rt.CullDistanceSqr)
                return -1;

            var thresholds = rt.LodThresholdSqr;
            if (thresholds == null || thresholds.Length == 0)
                return 0;

            int max = Mathf.Min(rt.LodMeshes.Length, thresholds.Length);
            for (int li = 0; li < max; li++)
            {
                if (sqrDist < thresholds[li])
                    return li;
            }

            return rt.LodMeshes.Length - 1;
        }

        string ResolveLevelScopeId()
        {
            var cache = GetComponent<FcLevelCacheService>() ?? GetComponentInParent<FcLevelCacheService>();
            if (cache != null && !string.IsNullOrWhiteSpace(cache.LevelScopeId))
                return cache.LevelScopeId;

            var resource = FcLevelResourceService.Current;
            if (resource != null && !string.IsNullOrWhiteSpace(resource.LevelScopeId))
                return resource.LevelScopeId;

            return null;
        }

        void DrawBucketInstanced(Mesh mesh, int sub, Material mat, List<Matrix4x4> bucket)
        {
            int offset = 0;
            while (offset < bucket.Count)
            {
                int n = Mathf.Min(BatchSize, bucket.Count - offset);
                for (int j = 0; j < n; j++)
                    _batchBuf[j] = bucket[offset + j];
                Graphics.DrawMeshInstanced(
                    mesh,
                    sub,
                    mat,
                    _batchBuf,
                    n,
                    null,
                    ShadowCastingMode.On,
                    receiveShadows: true);
                offset += n;
            }
        }

        void OnDestroy()
        {
            _runtimeTypes = null;
            _scratch = null;
        }
    }
}
