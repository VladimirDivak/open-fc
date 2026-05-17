using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer;
using OpenFarCry.Importer.Cgf;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenFarCry.Level.Services
{
    public enum FcVegetationCollisionMode
    {
        None = 0,
        PrimitiveCapsule = 1,
        PrimitiveBox = 2,
        LowLodMesh = 3,
        PhysicsProxy = 4,
    }

    // Loads vegetation CGF at runtime from VFS and renders via GPU instancing with LOD.
    // No geometry is written to disk — all data stays in memory.
    public sealed class FcVegetationTerrainService : MonoBehaviour
    {
        public const int DefaultCollisionLodIndex = -1;
        public const float DefaultCollisionDistance = 30f;
        public const int DefaultMaxActiveCollidersPerType = 64;

        [Serializable]
        public struct VegetationTypeEntry
        {
            public int TypeIndex;
            public string VirtualPath;
            public FcVegetationCollisionMode CollisionMode;
            public int CollisionLodIndex;
            public float CollisionDistance;
            public int MaxActiveCollidersPerType;
            public float PrimitiveHeight;
            public float PrimitiveRadius;
            public Vector3 PrimitiveSize;
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
        [SerializeField, Min(8f)] float _cellSize = 64f;
        [SerializeField] bool _logResolvedCollisionPolicies;
        [SerializeField, Min(1)] int _maxActiveCollidersGlobal = 256;
        [SerializeField, Min(0.05f)] float _colliderUpdateInterval = 0.2f;
        [SerializeField, Min(0f)] float _lowLodMeshMinHeightForCollider = 4f;
        [SerializeField] bool _debugDrawCells;
        [SerializeField] bool _debugDrawColliderHosts;

        const int BatchSize = 1023;

        struct RuntimeType
        {
            public Mesh[] LodMeshes;          // [0]=base LOD, [1..n]=coarser LODs
            public Material[][] LodMaterials; // parallel to LodMeshes
            public Bounds BaseBounds;
            public Mesh PhysicsProxyMesh;
            public float[] LodThresholdSqr;   // transition distances per lod entry
            public float CullDistanceSqr;
            public bool IsValid;
        }

        struct RuntimeCell
        {
            public Bounds Bounds;
            public int[] InstanceIndices;
        }

        struct RuntimeCollisionPolicy
        {
            public FcVegetationCollisionMode Mode;
            public int CollisionLodIndex;
            public float CollisionDistance;
            public int MaxActiveCollidersPerType;
            public float PrimitiveHeight;
            public float PrimitiveRadius;
            public Vector3 PrimitiveSize;
        }

        sealed class ColliderHost
        {
            public GameObject GameObject;
            public CapsuleCollider CapsuleCollider;
            public BoxCollider BoxCollider;
            public MeshCollider MeshCollider;
            public int AssignedInstanceIndex = -1;
        }

        sealed class ColliderCandidateDistanceComparer : IComparer<FcVegetationColliderSelector.Candidate>
        {
            public int Compare(FcVegetationColliderSelector.Candidate x, FcVegetationColliderSelector.Candidate y)
            {
                return x.SqrDistance.CompareTo(y.SqrDistance);
            }
        }

        static readonly IComparer<FcVegetationColliderSelector.Candidate> s_colliderCandidateDistanceComparer =
            new ColliderCandidateDistanceComparer();
        static readonly int PropBaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int PropAlphaClip = Shader.PropertyToID("_AlphaClip");
        static readonly int PropCutoff = Shader.PropertyToID("_Cutoff");
        static readonly int PropSurface = Shader.PropertyToID("_Surface");
        static readonly int PropZWrite = Shader.PropertyToID("_ZWrite");

        RuntimeType[] _runtimeTypes;
        RuntimeCell[] _runtimeCells;
        RuntimeCollisionPolicy[] _runtimeCollisionPolicies;
        int[] _perTypeBudgets;
        float _maxCullDistanceSqr;

        // Flat per-instance data (computed once at Start).
        Vector3[] _positions;   // world positions
        float[] _scales;
        int[] _protoIndices;    // index into _runtimeTypes

        // Per-frame scratch: [protoIdx][lodLevel] -> growable list of matrices.
        // Pre-allocated to avoid GC each frame.
        List<Matrix4x4>[][] _scratch; // [proto][lod]
        readonly Matrix4x4[] _batchBuf = new Matrix4x4[BatchSize];
        readonly List<int> _visibleCellScratch = new List<int>(256);
        Plane[] _frustumPlanes;

        string _levelScopeId;
        bool _warnedMissingLevelPreloadService;
        bool _warnedFallbackImport;

        // Runtime counters for profiling/debug UI.
        int _totalCellCount;
        int _visibleCellCount;
        int _visibleInstanceCount;

        readonly List<ColliderHost> _colliderHosts = new List<ColliderHost>(128);
        readonly Dictionary<int, int> _activeHostByInstance = new Dictionary<int, int>();
        readonly List<int> _activeInstancesScratch = new List<int>(256);
        readonly HashSet<int> _wantedInstancesScratch = new HashSet<int>();
        readonly List<FcVegetationColliderSelector.Candidate> _candidateScratch = new List<FcVegetationColliderSelector.Candidate>(1024);
        readonly List<Mesh> _ownedVisualMeshes = new List<Mesh>(128);
        readonly List<Mesh> _ownedColliderMeshes = new List<Mesh>(128);
        Transform _colliderRoot;
        float _nextColliderUpdateTime;

        public int TotalCellCount => _totalCellCount;
        public int VisibleCellCount => _visibleCellCount;
        public int VisibleInstanceCount => _visibleInstanceCount;

        // Returns normalized virtual paths of all vegetation types registered in this service.
        // Used by FcLevelGeometryPreloadPlanner to build preload requests when no FcVegetationInstance objects exist.
        public IReadOnlyList<string> GetVegetationVirtualPaths()
        {
            if (_vegetationTypes == null || _vegetationTypes.Length == 0)
                return System.Array.Empty<string>();

            var paths = new List<string>(_vegetationTypes.Length);
            for (int i = 0; i < _vegetationTypes.Length; i++)
            {
                var vp = _vegetationTypes[i].VirtualPath;
                if (!string.IsNullOrWhiteSpace(vp))
                    paths.Add(vp);
            }
            return paths;
        }
        public int ActiveColliderCount => _activeHostByInstance.Count;

        void Start()
        {
            StartAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        async UniTaskVoid StartAsync(System.Threading.CancellationToken ct)
        {
            _levelScopeId = ResolveLevelScopeId();

            int typeCount = _vegetationTypes != null ? _vegetationTypes.Length : -1;
            int instanceCount = _instances != null ? _instances.Length : -1;
            Debug.Log($"[FcVegetationTerrainService] StartAsync: types={typeCount}, instances={instanceCount}");

            if (_vegetationTypes == null || _vegetationTypes.Length == 0)
            {
                Debug.LogWarning("[FcVegetationTerrainService] StartAsync: exit — no vegetation types");
                return;
            }
            if (_instances == null || _instances.Length == 0)
            {
                Debug.LogWarning("[FcVegetationTerrainService] StartAsync: exit — no vegetation instances");
                return;
            }

            // Wait only if LoadLevelAsync is actively running (IsLoadInProgress).
            // If no load in progress (not started or already done), proceed immediately.
            // If already complete (IsPreloadComplete), BuildRuntimeTypes will find preloaded handles.
            // If load never called, falls through to direct import fallback below.
            var levelLoadService = FcLevelLoadService.Current;
            Debug.Log($"[FcVegetationTerrainService] StartAsync: levelLoadService={(levelLoadService != null ? "found" : "null")}, IsLoadInProgress={levelLoadService?.IsLoadInProgress}, IsPreloadComplete={levelLoadService?.IsPreloadComplete}");

            if (levelLoadService != null && levelLoadService.IsLoadInProgress)
            {
                Debug.Log("[FcVegetationTerrainService] StartAsync: waiting for load to complete...");
                await UniTask.WaitUntil(
                    () => levelLoadService == null || !levelLoadService.IsLoadInProgress,
                    cancellationToken: ct);
                if (ct.IsCancellationRequested)
                    return;
                Debug.Log("[FcVegetationTerrainService] StartAsync: wait done, proceeding");
            }

            var lodService = new CgfLodImportService();
            var protoByType = BuildRuntimeTypes(lodService);
            Debug.Log($"[FcVegetationTerrainService] StartAsync: protoByType={protoByType.Count}, runtimeTypes={_runtimeTypes?.Length ?? -1}");

            if (_runtimeTypes == null || _runtimeTypes.Length == 0 || protoByType.Count == 0)
            {
                Debug.LogWarning("[FcVegetationTerrainService] StartAsync: exit — BuildRuntimeTypes produced nothing");
                return;
            }

            ResolveCollisionPolicies();
            BuildRuntimeInstances(protoByType);
            BuildSpatialCells();
            InitializeScratchBuffers();
            Debug.Log($"[FcVegetationTerrainService] StartAsync: done — positions={_positions?.Length ?? -1}");
        }

        void OnEnable()
        {
            _nextColliderUpdateTime = 0f;
        }

        bool _loggedCameraNull;

        void Update()
        {
            if (_positions == null || _runtimeTypes == null || _scratch == null)
                return;

            var cam = Camera.main;
            if (cam == null)
            {
                if (!_loggedCameraNull)
                {
                    _loggedCameraNull = true;
                    Debug.LogWarning("[FcVegetationTerrainService] Camera.main is null — GPU instancing disabled. Tag your camera as MainCamera.");
                }
                return;
            }
            _loggedCameraNull = false;
            var camPos = cam.transform.position;

            ClearScratchBuckets();
            CollectVisibleCells(cam, camPos);
            CollectVisibleInstances(camPos);
            SubmitInstancedDraws();

            if (Time.unscaledTime >= _nextColliderUpdateTime)
            {
                UpdateNearbyColliders(camPos);
                _nextColliderUpdateTime = Time.unscaledTime + Mathf.Max(0.05f, _colliderUpdateInterval);
            }
        }

        Dictionary<int, int> BuildRuntimeTypes(CgfLodImportService lodService)
        {
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

            return protoByType;
        }

        void BuildRuntimeInstances(Dictionary<int, int> protoByType)
        {
            var terrain = GetComponent<Terrain>();
            var terrainData = terrain != null ? terrain.terrainData : null;
            float sizeX = terrainData != null ? terrainData.size.x : 1f;
            float sizeZ = terrainData != null ? terrainData.size.z : 1f;
            var origin = terrain != null ? terrain.transform.position : Vector3.zero;

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

            if (count < total)
            {
                Array.Resize(ref _positions, count);
                Array.Resize(ref _scales, count);
                Array.Resize(ref _protoIndices, count);
            }
        }

        void BuildSpatialCells()
        {
            _runtimeCells = Array.Empty<RuntimeCell>();
            _totalCellCount = 0;
            _visibleCellCount = 0;
            _visibleInstanceCount = 0;

            if (_positions == null || _positions.Length == 0)
                return;

            var partitioned = FcVegetationSpatialPartitioner.Partition(_positions, _cellSize);
            _runtimeCells = new RuntimeCell[partitioned.Length];
            for (int i = 0; i < partitioned.Length; i++)
            {
                _runtimeCells[i] = new RuntimeCell
                {
                    Bounds = partitioned[i].Bounds,
                    InstanceIndices = partitioned[i].InstanceIndices,
                };
            }

            _totalCellCount = _runtimeCells.Length;
            _visibleCellScratch.Clear();

            _maxCullDistanceSqr = 0f;
            for (int i = 0; i < _runtimeTypes.Length; i++)
            {
                var rt = _runtimeTypes[i];
                if (!rt.IsValid)
                    continue;
                if (rt.CullDistanceSqr > _maxCullDistanceSqr)
                    _maxCullDistanceSqr = rt.CullDistanceSqr;
            }
        }

        void ResolveCollisionPolicies()
        {
            if (_vegetationTypes == null || _vegetationTypes.Length == 0)
            {
                _runtimeCollisionPolicies = Array.Empty<RuntimeCollisionPolicy>();
                return;
            }

            _runtimeCollisionPolicies = new RuntimeCollisionPolicy[_vegetationTypes.Length];
            for (int i = 0; i < _vegetationTypes.Length; i++)
            {
                var entry = _vegetationTypes[i];
                bool hasRuntimeLods = i >= 0 &&
                    i < _runtimeTypes.Length &&
                    _runtimeTypes[i].IsValid &&
                    _runtimeTypes[i].LodMeshes != null &&
                    _runtimeTypes[i].LodMeshes.Length > 0;
                bool hasPhysicsProxy = i >= 0 &&
                    i < _runtimeTypes.Length &&
                    _runtimeTypes[i].IsValid &&
                    _runtimeTypes[i].PhysicsProxyMesh != null;
                int maxLodIndex = hasRuntimeLods ? _runtimeTypes[i].LodMeshes.Length - 1 : -1;

                var mode = entry.CollisionMode != FcVegetationCollisionMode.None
                    ? entry.CollisionMode
                    : FcVegetationDefaultPolicy.ResolveCollisionMode(entry.VirtualPath, hasRuntimeLods);
                if (mode == FcVegetationCollisionMode.LowLodMesh && maxLodIndex < 0)
                    mode = FcVegetationCollisionMode.None;
                if (mode == FcVegetationCollisionMode.PhysicsProxy && !hasPhysicsProxy)
                    mode = FcVegetationCollisionMode.None;

                float collisionDistance = mode == FcVegetationCollisionMode.None
                    ? 0f
                    : Mathf.Max(1f, entry.CollisionDistance > 0f
                        ? entry.CollisionDistance
                        : DefaultCollisionDistance);
                int maxActiveCollidersPerType = mode == FcVegetationCollisionMode.None
                    ? 0
                    : Mathf.Max(1, entry.MaxActiveCollidersPerType > 0
                        ? entry.MaxActiveCollidersPerType
                        : DefaultMaxActiveCollidersPerType);

                int collisionLodIndex = DefaultCollisionLodIndex;
                if (mode == FcVegetationCollisionMode.LowLodMesh)
                {
                    collisionLodIndex = entry.CollisionLodIndex;
                    if (collisionLodIndex < 0 || collisionLodIndex > maxLodIndex)
                        collisionLodIndex = maxLodIndex;
                }

                float primitiveHeight = Mathf.Max(0f, entry.PrimitiveHeight);
                float primitiveRadius = Mathf.Max(0f, entry.PrimitiveRadius);
                var primitiveSize = new Vector3(
                    Mathf.Max(0f, entry.PrimitiveSize.x),
                    Mathf.Max(0f, entry.PrimitiveSize.y),
                    Mathf.Max(0f, entry.PrimitiveSize.z));

                _runtimeCollisionPolicies[i] = new RuntimeCollisionPolicy
                {
                    Mode = mode,
                    CollisionLodIndex = collisionLodIndex,
                    CollisionDistance = collisionDistance,
                    MaxActiveCollidersPerType = maxActiveCollidersPerType,
                    PrimitiveHeight = primitiveHeight,
                    PrimitiveRadius = primitiveRadius,
                    PrimitiveSize = primitiveSize,
                };

                if (_logResolvedCollisionPolicies)
                {
                    Debug.Log(
                        $"[FcVegetationTerrainService] Collision policy type={entry.TypeIndex} path='{entry.VirtualPath}' mode={mode} lod={collisionLodIndex} dist={collisionDistance:F1} budget={maxActiveCollidersPerType}");
                }
            }

            _perTypeBudgets = new int[_runtimeCollisionPolicies.Length];
            for (int i = 0; i < _runtimeCollisionPolicies.Length; i++)
                _perTypeBudgets[i] = _runtimeCollisionPolicies[i].MaxActiveCollidersPerType;
        }

        void InitializeScratchBuffers()
        {
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

        void ClearScratchBuckets()
        {
            for (int pi = 0; pi < _scratch.Length; pi++)
            {
                var perProto = _scratch[pi];
                for (int li = 0; li < perProto.Length; li++)
                    perProto[li].Clear();
            }
        }

        void CollectVisibleCells(Camera cam, Vector3 camPos)
        {
            _visibleCellScratch.Clear();
            _visibleCellCount = 0;
            _visibleInstanceCount = 0;

            if (_runtimeCells == null || _runtimeCells.Length == 0)
                return;

            _frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);
            float maxCullSqr = _maxCullDistanceSqr > 0f
                ? _maxCullDistanceSqr
                : BuildCullDistanceSqr(_lod0Distance, _cullDistance);

            for (int i = 0; i < _runtimeCells.Length; i++)
            {
                var cell = _runtimeCells[i];
                var closest = cell.Bounds.ClosestPoint(camPos);
                float sqr = (closest - camPos).sqrMagnitude;
                if (sqr >= maxCullSqr)
                    continue;
                if (_frustumPlanes != null && !GeometryUtility.TestPlanesAABB(_frustumPlanes, cell.Bounds))
                    continue;

                _visibleCellScratch.Add(i);
                _visibleCellCount++;
                _visibleInstanceCount += cell.InstanceIndices != null ? cell.InstanceIndices.Length : 0;
            }
        }

        void CollectVisibleInstances(Vector3 camPos)
        {
            if (_runtimeCells == null || _runtimeCells.Length == 0 || _visibleCellScratch.Count == 0)
            {
                // Fallback for old scenes/edge cases where cells are not available.
                _visibleInstanceCount = _positions != null ? _positions.Length : 0;
                for (int i = 0; i < _positions.Length; i++)
                    AddVisibleInstance(i, camPos);
                return;
            }

            for (int c = 0; c < _visibleCellScratch.Count; c++)
            {
                var cell = _runtimeCells[_visibleCellScratch[c]];
                var indices = cell.InstanceIndices;
                if (indices == null)
                    continue;

                for (int i = 0; i < indices.Length; i++)
                    AddVisibleInstance(indices[i], camPos);
            }
        }

        void AddVisibleInstance(int instanceIndex, Vector3 camPos)
        {
            var pos = _positions[instanceIndex];
            float sqr = (pos - camPos).sqrMagnitude;
            int pi = _protoIndices[instanceIndex];
            var rt = _runtimeTypes[pi];

            if (!rt.IsValid || rt.LodMeshes == null)
                return;

            int lod = FindLod(rt, sqr);
            if (lod < 0)
                return;

            _scratch[pi][lod].Add(Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one * _scales[instanceIndex]));
        }

        void UpdateNearbyColliders(Vector3 camPos)
        {
            if (_runtimeCollisionPolicies == null || _runtimeCollisionPolicies.Length == 0)
            {
                ReleaseAllColliderHosts();
                return;
            }

            _candidateScratch.Clear();
            CollectColliderCandidates(camPos);
            if (_candidateScratch.Count == 0)
            {
                ReleaseAllColliderHosts();
                return;
            }

            _candidateScratch.Sort(s_colliderCandidateDistanceComparer);
            BuildWantedColliderSet();
            ApplyWantedColliderSet();
        }

        void CollectColliderCandidates(Vector3 camPos)
        {
            if (_runtimeCells == null || _runtimeCells.Length == 0 || _visibleCellScratch.Count == 0)
            {
                for (int i = 0; i < _positions.Length; i++)
                    TryAddColliderCandidate(i, camPos);
                return;
            }

            for (int c = 0; c < _visibleCellScratch.Count; c++)
            {
                var cell = _runtimeCells[_visibleCellScratch[c]];
                var indices = cell.InstanceIndices;
                if (indices == null)
                    continue;

                for (int i = 0; i < indices.Length; i++)
                    TryAddColliderCandidate(indices[i], camPos);
            }
        }

        void TryAddColliderCandidate(int instanceIndex, Vector3 camPos)
        {
            int pi = _protoIndices[instanceIndex];
            if (pi < 0 || pi >= _runtimeCollisionPolicies.Length)
                return;

            var policy = _runtimeCollisionPolicies[pi];
            if (policy.Mode == FcVegetationCollisionMode.None)
                return;

            var pos = _positions[instanceIndex];
            float sqr = (pos - camPos).sqrMagnitude;
            float range = policy.CollisionDistance;
            if (range <= 0f || sqr > range * range)
                return;

            _candidateScratch.Add(new FcVegetationColliderSelector.Candidate(instanceIndex, pi, sqr));
        }

        void BuildWantedColliderSet()
        {
            var wanted = FcVegetationColliderSelector.Select(_candidateScratch, _perTypeBudgets, _maxActiveCollidersGlobal);
            _wantedInstancesScratch.Clear();
            foreach (int id in wanted)
                _wantedInstancesScratch.Add(id);
        }

        void ApplyWantedColliderSet()
        {
            _activeInstancesScratch.Clear();
            foreach (var kv in _activeHostByInstance)
                _activeInstancesScratch.Add(kv.Key);

            for (int i = 0; i < _activeInstancesScratch.Count; i++)
            {
                int instanceIndex = _activeInstancesScratch[i];
                if (_wantedInstancesScratch.Contains(instanceIndex))
                    continue;
                ReleaseColliderForInstance(instanceIndex);
            }

            foreach (int instanceIndex in _wantedInstancesScratch)
                EnsureColliderForInstance(instanceIndex);
        }

        void EnsureColliderForInstance(int instanceIndex)
        {
            if (_activeHostByInstance.TryGetValue(instanceIndex, out int existingHostIndex))
            {
                if (existingHostIndex >= 0 &&
                    existingHostIndex < _colliderHosts.Count &&
                    _colliderHosts[existingHostIndex].AssignedInstanceIndex == instanceIndex)
                {
                    return;
                }
                _activeHostByInstance.Remove(instanceIndex);
            }

            int hostIndex = FindFreeColliderHostIndex();
            if (hostIndex < 0)
            {
                hostIndex = _colliderHosts.Count;
                if (hostIndex >= Mathf.Max(1, _maxActiveCollidersGlobal))
                    return;
                CreateColliderHost();
            }

            if (hostIndex < 0 || hostIndex >= _colliderHosts.Count)
                return;

            var host = _colliderHosts[hostIndex];
            host.AssignedInstanceIndex = instanceIndex;
            _activeHostByInstance[instanceIndex] = hostIndex;
            ConfigureColliderHost(host, instanceIndex);
        }

        int FindFreeColliderHostIndex()
        {
            for (int i = 0; i < _colliderHosts.Count; i++)
            {
                if (_colliderHosts[i].AssignedInstanceIndex < 0)
                    return i;
            }
            return -1;
        }

        void CreateColliderHost()
        {
            EnsureColliderRoot();
            int index = _colliderHosts.Count;
            var go = new GameObject($"VegetationCollider_{index}");
            go.transform.SetParent(_colliderRoot, worldPositionStays: false);

            var capsule = go.AddComponent<CapsuleCollider>();
            var box = go.AddComponent<BoxCollider>();
            var mesh = go.AddComponent<MeshCollider>();
            capsule.enabled = false;
            box.enabled = false;
            mesh.enabled = false;

            _colliderHosts.Add(new ColliderHost
            {
                GameObject = go,
                CapsuleCollider = capsule,
                BoxCollider = box,
                MeshCollider = mesh,
                AssignedInstanceIndex = -1,
            });
        }

        void EnsureColliderRoot()
        {
            if (_colliderRoot != null)
                return;

            var root = new GameObject("VegetationColliders");
            root.transform.SetParent(transform, worldPositionStays: false);
            _colliderRoot = root.transform;
        }

        void ConfigureColliderHost(ColliderHost host, int instanceIndex)
        {
            if (host == null || host.GameObject == null)
                return;
            if (instanceIndex < 0 || instanceIndex >= _protoIndices.Length)
                return;

            int pi = _protoIndices[instanceIndex];
            if (pi < 0 || pi >= _runtimeCollisionPolicies.Length || pi >= _runtimeTypes.Length)
                return;

            var policy = _runtimeCollisionPolicies[pi];
            var rt = _runtimeTypes[pi];
            float scale = _scales[instanceIndex];

            host.GameObject.transform.position = _positions[instanceIndex];
            host.GameObject.transform.rotation = Quaternion.identity;
            host.GameObject.transform.localScale = Vector3.one;

            DisableHostColliders(host);

            var mode = policy.Mode;
            if (mode == FcVegetationCollisionMode.PhysicsProxy &&
                TryConfigurePhysicsProxyCollider(host, rt, scale))
            {
                return;
            }

            if (mode == FcVegetationCollisionMode.LowLodMesh &&
                TryConfigureLowLodMeshCollider(
                    host,
                    rt,
                    policy,
                    scale,
                    _lowLodMeshMinHeightForCollider))
            {
                return;
            }

            if (mode == FcVegetationCollisionMode.PrimitiveBox)
            {
                var box = host.BoxCollider;
                box.enabled = true;
                var size = policy.PrimitiveSize;
                if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
                {
                    var baseBounds = ResolveBaseBounds(rt);
                    size = Vector3.Max(baseBounds.size * 0.6f, new Vector3(0.5f, 1f, 0.5f));
                }
                size *= scale;
                box.size = size;
                box.center = new Vector3(0f, size.y * 0.5f, 0f);
                return;
            }

            var capsule = host.CapsuleCollider;
            capsule.enabled = true;
            capsule.direction = 1;

            var bounds = ResolveBaseBounds(rt);
            float radius = policy.PrimitiveRadius > 0f
                ? policy.PrimitiveRadius * scale
                : Mathf.Max(bounds.extents.x, bounds.extents.z) * Mathf.Max(0.1f, scale) * 0.4f;
            float height = policy.PrimitiveHeight > 0f
                ? policy.PrimitiveHeight * scale
                : bounds.size.y * Mathf.Max(0.1f, scale) * 0.9f;

            radius = Mathf.Max(0.1f, radius);
            height = Mathf.Max(radius * 2f, height);
            capsule.radius = radius;
            capsule.height = height;
            capsule.center = new Vector3(0f, height * 0.5f, 0f);
        }

        static bool TryConfigureLowLodMeshCollider(
            ColliderHost host,
            in RuntimeType runtimeType,
            in RuntimeCollisionPolicy policy,
            float scale,
            float minHeight)
        {
            if (host == null || host.MeshCollider == null)
                return false;
            if (!runtimeType.IsValid || runtimeType.LodMeshes == null || runtimeType.LodMeshes.Length == 0)
                return false;
            if (runtimeType.BaseBounds.size.y * Mathf.Max(0.01f, scale) < Mathf.Max(0f, minHeight))
                return false;

            int lodIndex = policy.CollisionLodIndex;
            if (lodIndex < 0 || lodIndex >= runtimeType.LodMeshes.Length)
                return false;

            var mesh = runtimeType.LodMeshes[lodIndex];
            if (mesh == null)
                return false;

            try
            {
                host.GameObject.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
                var mc = host.MeshCollider;
                mc.sharedMesh = mesh;
                mc.convex = false;
                mc.enabled = true;
                return true;
            }
            catch (Exception)
            {
                host.MeshCollider.sharedMesh = null;
                host.MeshCollider.enabled = false;
                host.GameObject.transform.localScale = Vector3.one;
                return false;
            }
        }

        static bool TryConfigurePhysicsProxyCollider(
            ColliderHost host,
            in RuntimeType runtimeType,
            float scale)
        {
            if (host == null || host.MeshCollider == null)
                return false;
            if (!runtimeType.IsValid || runtimeType.PhysicsProxyMesh == null)
                return false;

            try
            {
                host.GameObject.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
                var mc = host.MeshCollider;
                mc.sharedMesh = runtimeType.PhysicsProxyMesh;
                mc.convex = false;
                mc.enabled = true;
                return true;
            }
            catch (Exception)
            {
                host.MeshCollider.sharedMesh = null;
                host.MeshCollider.enabled = false;
                host.GameObject.transform.localScale = Vector3.one;
                return false;
            }
        }

        static Bounds ResolveBaseBounds(in RuntimeType rt)
        {
            if (!rt.IsValid)
                return new Bounds(Vector3.zero, Vector3.one);
            if (rt.BaseBounds.size.sqrMagnitude > 0f)
                return rt.BaseBounds;
            if (rt.LodMeshes == null || rt.LodMeshes.Length == 0 || rt.LodMeshes[0] == null)
                return new Bounds(Vector3.zero, Vector3.one);
            return rt.LodMeshes[0].bounds;
        }

        static void DisableHostColliders(ColliderHost host)
        {
            if (host == null)
                return;
            if (host.CapsuleCollider != null)
                host.CapsuleCollider.enabled = false;
            if (host.BoxCollider != null)
                host.BoxCollider.enabled = false;
            if (host.MeshCollider != null)
            {
                host.MeshCollider.sharedMesh = null;
                host.MeshCollider.enabled = false;
            }
        }

        void ReleaseColliderForInstance(int instanceIndex)
        {
            if (!_activeHostByInstance.TryGetValue(instanceIndex, out int hostIndex))
                return;

            _activeHostByInstance.Remove(instanceIndex);
            if (hostIndex < 0 || hostIndex >= _colliderHosts.Count)
                return;

            var host = _colliderHosts[hostIndex];
            host.AssignedInstanceIndex = -1;
            DisableHostColliders(host);
            if (host.GameObject != null)
            {
                host.GameObject.transform.position = Vector3.zero;
                host.GameObject.transform.rotation = Quaternion.identity;
                host.GameObject.transform.localScale = Vector3.one;
            }
        }

        void ReleaseAllColliderHosts()
        {
            _activeHostByInstance.Clear();
            for (int i = 0; i < _colliderHosts.Count; i++)
            {
                var host = _colliderHosts[i];
                host.AssignedInstanceIndex = -1;
                DisableHostColliders(host);
                if (host.GameObject != null)
                {
                    host.GameObject.transform.position = Vector3.zero;
                    host.GameObject.transform.rotation = Quaternion.identity;
                    host.GameObject.transform.localScale = Vector3.one;
                }
            }
        }

        void TeardownColliderRuntime(bool destroyRoot)
        {
            ReleaseAllColliderHosts();
            _colliderHosts.Clear();
            _activeHostByInstance.Clear();
            _activeInstancesScratch.Clear();
            _wantedInstancesScratch.Clear();
            _candidateScratch.Clear();

            if (destroyRoot && _colliderRoot != null)
            {
                Destroy(_colliderRoot.gameObject);
                _colliderRoot = null;
            }
        }

        void SubmitInstancedDraws()
        {
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
                var visualMesh = result.Mesh;
                var submeshMaterialIds = result.BuildResult?.SubmeshMaterialIds;

                if (TryCreateProxyFilteredVisualMesh(
                        result.ParsedFile,
                        visualMesh,
                        submeshMaterialIds,
                        out var filteredMesh,
                        out var filteredSubmeshMaterialIds))
                {
                    if (filteredMesh == null)
                    {
                        lodMeshes[i] = null;
                        lodMaterials[i] = Array.Empty<Material>();
                        continue;
                    }

                    visualMesh = RegisterOwnedVisualMesh(filteredMesh);
                    submeshMaterialIds = filteredSubmeshMaterialIds;
                }

                lodMeshes[i] = visualMesh;
                lodMaterials[i] = CgfRuntimeImporter.MaterialService.ResolveSubmeshMaterials(
                    result.ParsedFile,
                    visualMesh,
                    submeshMaterialIds,
                    textureScopeId);

                var mats = lodMaterials[i];
                if (mats == null)
                    continue;

                for (int mi = 0; mi < mats.Length; mi++)
                {
                    var mat = mats[mi];
                    if (mat != null)
                    {
                        mat.enableInstancing = true;
                        TryForceVegetationFoliageCutout(mat);
                    }
                }
            }

            runtimeType = new RuntimeType
            {
                LodMeshes = lodMeshes,
                LodMaterials = lodMaterials,
                BaseBounds = ResolveFirstValidLodBounds(lodMeshes),
                PhysicsProxyMesh = TryBuildPhysicsProxyMesh(baseResult, out var proxyMesh)
                    ? RegisterOwnedColliderMesh(proxyMesh)
                    : null,
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

        static void TryForceVegetationFoliageCutout(Material mat)
        {
            if (mat == null)
                return;
            if (!IsLikelyFoliageMaterial(mat.name))
                return;
            if (!mat.HasProperty(PropAlphaClip) || !mat.HasProperty(PropCutoff))
                return;

            // Skip transparent-blend materials; this path is only for foliage cards
            // that should be alpha-clipped but arrived as opaque.
            if (mat.HasProperty(PropSurface) && mat.GetFloat(PropSurface) > 0.5f)
                return;

            var baseMap = mat.HasProperty(PropBaseMap) ? mat.GetTexture(PropBaseMap) : null;
            if (baseMap == null)
                return;

            float currentClip = mat.GetFloat(PropAlphaClip);
            float currentCutoff = mat.GetFloat(PropCutoff);
            if (currentClip > 0.5f && currentCutoff >= 0.1f)
                return;

            mat.SetFloat(PropSurface, 0f);
            mat.SetFloat(PropAlphaClip, 1f);
            mat.SetFloat(PropCutoff, Mathf.Max(0.1f, currentCutoff > 0f ? currentCutoff : 0.33f));
            if (mat.HasProperty(PropZWrite))
                mat.SetFloat(PropZWrite, 1f);
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        static bool IsLikelyFoliageMaterial(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName))
                return false;
            string n = materialName.ToLowerInvariant();
            return n.Contains("plants") ||
                   n.Contains("leaf") ||
                   n.Contains("bush") ||
                   n.Contains("grass") ||
                   n.Contains("fern") ||
                   n.Contains("weed") ||
                   n.Contains("frond") ||
                   n.Contains("branch");
        }

        Mesh RegisterOwnedColliderMesh(Mesh mesh)
        {
            if (mesh != null)
                _ownedColliderMeshes.Add(mesh);
            return mesh;
        }

        Mesh RegisterOwnedVisualMesh(Mesh mesh)
        {
            if (mesh != null)
                _ownedVisualMeshes.Add(mesh);
            return mesh;
        }

        static Bounds ResolveFirstValidLodBounds(IReadOnlyList<Mesh> lodMeshes)
        {
            if (lodMeshes == null || lodMeshes.Count == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            for (int i = 0; i < lodMeshes.Count; i++)
            {
                var mesh = lodMeshes[i];
                if (mesh != null)
                    return mesh.bounds;
            }

            return new Bounds(Vector3.zero, Vector3.one);
        }

        static bool TryCreateProxyFilteredVisualMesh(
            CgfFile parsedFile,
            Mesh sourceMesh,
            int[] sourceSubmeshMaterialIds,
            out Mesh filteredMesh,
            out int[] filteredSubmeshMaterialIds)
        {
            filteredMesh = null;
            filteredSubmeshMaterialIds = sourceSubmeshMaterialIds;

            if (parsedFile == null || sourceMesh == null)
                return false;
            if (sourceSubmeshMaterialIds == null || sourceSubmeshMaterialIds.Length != sourceMesh.subMeshCount)
                return false;
            if (!TryResolveRootMaterial(parsedFile, out var rootMat) || rootMat == null)
                return false;

            var proxyMatIds = BuildProxyMaterialIds(parsedFile, rootMat);
            if (proxyMatIds == null || proxyMatIds.Count == 0)
                return false;

            var keepSubmeshIndices = new List<int>(sourceMesh.subMeshCount);
            for (int i = 0; i < sourceSubmeshMaterialIds.Length; i++)
            {
                if (!proxyMatIds.Contains(sourceSubmeshMaterialIds[i]))
                    keepSubmeshIndices.Add(i);
            }

            if (keepSubmeshIndices.Count == sourceMesh.subMeshCount)
                return false;
            if (keepSubmeshIndices.Count == 0)
            {
                filteredSubmeshMaterialIds = Array.Empty<int>();
                return true;
            }

            filteredMesh = UnityEngine.Object.Instantiate(sourceMesh);
            filteredMesh.name = sourceMesh.name + "_NoProxyVisual";
            filteredMesh.subMeshCount = keepSubmeshIndices.Count;

            filteredSubmeshMaterialIds = new int[keepSubmeshIndices.Count];
            for (int i = 0; i < keepSubmeshIndices.Count; i++)
            {
                int srcSubmesh = keepSubmeshIndices[i];
                filteredMesh.SetTriangles(sourceMesh.GetTriangles(srcSubmesh), i, true);
                filteredSubmeshMaterialIds[i] = sourceSubmeshMaterialIds[srcSubmesh];
            }
            filteredMesh.RecalculateBounds();
            return true;
        }

        static bool TryBuildPhysicsProxyMesh(CgfRuntimeImportResult baseResult, out Mesh mesh)
        {
            mesh = null;
            var parsedFile = baseResult?.ParsedFile;
            var source = parsedFile?.MeshChunk;
            if (parsedFile == null || source == null || source.Vertices == null || source.Faces == null || source.Faces.Length == 0)
                return false;

            if (!TryResolveRootMaterial(parsedFile, out var rootMat) || rootMat == null)
                return false;

            var proxyMatIds = BuildProxyMaterialIds(parsedFile, rootMat);
            if (proxyMatIds == null || proxyMatIds.Count == 0)
                return false;

            if (BuildColliderMeshFromFaces(parsedFile, source, importScale: 0.01f, proxyMatIds, "VegetationPhysicsProxy", out mesh))
                return true;

            var shifted = new HashSet<int>();
            foreach (int id in proxyMatIds)
                shifted.Add(id + 1);
            return BuildColliderMeshFromFaces(parsedFile, source, importScale: 0.01f, shifted, "VegetationPhysicsProxy", out mesh);
        }

        static bool BuildColliderMeshFromFaces(
            CgfFile parsedFile,
            CgfMeshChunk source,
            float importScale,
            HashSet<int> allowedMatIds,
            string meshName,
            out Mesh mesh)
        {
            mesh = null;
            if (source?.Vertices == null || source.Faces == null || source.Faces.Length == 0)
                return false;

            var nodeTransform = Matrix4x4.identity;
            if (parsedFile != null && source == parsedFile.MeshChunk)
            {
                var rawNodeTransform = CgfMeshBuilder.BuildStaticNodeTransform(parsedFile, source.ChunkID);
                nodeTransform = CryTransformConversion.NodeMatrixInImporterSpace(rawNodeTransform, importScale);
            }

            var vertices = new List<Vector3>(source.Vertices.Length);
            for (int i = 0; i < source.Vertices.Length; i++)
            {
                var v = source.Vertices[i];
                var pos = CryTransformConversion.PositionInImporterSpace(
                    new Vector3(v.PX, v.PY, v.PZ),
                    importScale);
                vertices.Add(nodeTransform.MultiplyPoint3x4(pos));
            }

            var triangles = new List<int>(source.Faces.Length * 3);
            for (int i = 0; i < source.Faces.Length; i++)
            {
                var f = source.Faces[i];
                if (allowedMatIds != null && !allowedMatIds.Contains(f.MatID))
                    continue;
                if (f.V0 < 0 || f.V1 < 0 || f.V2 < 0 ||
                    f.V0 >= vertices.Count || f.V1 >= vertices.Count || f.V2 >= vertices.Count)
                    continue;
                triangles.Add(f.V0);
                triangles.Add(f.V1);
                triangles.Add(f.V2);
            }

            if (triangles.Count < 3)
                return false;

            mesh = new Mesh { name = meshName };
            if (vertices.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0, true);
            mesh.RecalculateBounds();
            return true;
        }

        static bool TryResolveRootMaterial(CgfFile parsedFile, out CgfMaterialChunk rootMat)
        {
            rootMat = null;
            if (parsedFile == null)
                return false;

            CgfNodeChunk primaryNode = null;
            var nodes = parsedFile.NodeChunks;
            if (nodes != null)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    var node = nodes[i];
                    if (node.ObjectID == parsedFile.SelectedMeshChunkID)
                    {
                        primaryNode = node;
                        break;
                    }
                }
            }

            int matChunkId = primaryNode?.MatID ?? -1;
            return matChunkId >= 0 && parsedFile.MaterialByChunkID.TryGetValue(matChunkId, out rootMat);
        }

        static HashSet<int> BuildProxyMaterialIds(CgfFile parsedFile, CgfMaterialChunk rootMat)
        {
            if (rootMat == null)
                return new HashSet<int>();

            if (rootMat.MtlType != CgfMtlType.Multi)
            {
                var singleMaterialIds = new HashSet<int>();
                if (IsNoDrawProxyMaterial(rootMat.Name))
                    singleMaterialIds.Add(rootMat.TableIndex);
                return singleMaterialIds;
            }

            var ids = new HashSet<int>();
            if (!parsedFile.MaterialChildrenByParentChunkID.TryGetValue(rootMat.ChunkID, out var children) || children == null)
                return ids;

            for (int i = 0; i < children.Count; i++)
            {
                if (IsNoDrawProxyMaterial(children[i]?.Name))
                    ids.Add(i);
            }

            return ids;
        }

        static bool IsNoDrawProxyMaterial(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName))
                return false;

            string n = materialName.ToLowerInvariant();
            return n.Contains("nodraw") ||
                   n.Contains("no_draw") ||
                   n.Contains("physics_proxy") ||
                   n.Contains("phys_proxy") ||
                   n.Contains("$physics_proxy") ||
                   n.Contains("proxy");
        }

        static float BuildCullDistanceSqr(float lod0Distance, float cullDistance) =>
            FcVegetationLodMath.BuildCullDistanceSqr(lod0Distance, cullDistance);

        static float[] BuildLodThresholdsSqr(int lodCount, float lod0Distance, float cullDistance) =>
            FcVegetationLodMath.BuildLodThresholdsSqr(lodCount, lod0Distance, cullDistance);

        static int FindLod(in RuntimeType rt, float sqrDist)
        {
            if (!rt.IsValid || rt.LodMeshes == null || rt.LodMeshes.Length == 0)
                return -1;
            return FcVegetationLodMath.FindLod(rt.LodThresholdSqr, rt.CullDistanceSqr, rt.LodMeshes.Length, sqrDist);
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

        void OnDisable()
        {
            // Make sure collider hosts do not hold mesh references while service is disabled/unloading.
            ReleaseAllColliderHosts();
            _nextColliderUpdateTime = 0f;
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
                return;

            if (_debugDrawCells && _runtimeCells != null)
            {
                Gizmos.color = new Color(0.55f, 0.55f, 0.55f, 1f);
                for (int i = 0; i < _runtimeCells.Length; i++)
                {
                    var b = _runtimeCells[i].Bounds;
                    Gizmos.DrawWireCube(b.center, b.size);
                }

                Gizmos.color = new Color(0.2f, 0.9f, 0.35f, 1f);
                for (int i = 0; i < _visibleCellScratch.Count; i++)
                {
                    int idx = _visibleCellScratch[i];
                    if (idx < 0 || idx >= _runtimeCells.Length)
                        continue;
                    var b = _runtimeCells[idx].Bounds;
                    Gizmos.DrawWireCube(b.center, b.size);
                }
            }

            if (_debugDrawColliderHosts && _colliderHosts != null)
            {
                for (int i = 0; i < _colliderHosts.Count; i++)
                {
                    var host = _colliderHosts[i];
                    if (host == null || host.GameObject == null || host.AssignedInstanceIndex < 0)
                        continue;

                    var t = host.GameObject.transform;
                    Gizmos.color = new Color(1f, 0.7f, 0.15f, 1f);
                    Gizmos.DrawWireSphere(t.position, 0.3f);
                }
            }
        }

        void OnDestroy()
        {
            TeardownColliderRuntime(destroyRoot: true);
            for (int i = 0; i < _ownedColliderMeshes.Count; i++)
            {
                var mesh = _ownedColliderMeshes[i];
                if (mesh != null)
                    Destroy(mesh);
            }
            _ownedColliderMeshes.Clear();
            for (int i = 0; i < _ownedVisualMeshes.Count; i++)
            {
                var mesh = _ownedVisualMeshes[i];
                if (mesh != null)
                    Destroy(mesh);
            }
            _ownedVisualMeshes.Clear();

            _runtimeTypes = null;
            _runtimeCells = null;
            _runtimeCollisionPolicies = null;
            _scratch = null;
        }
    }
}
