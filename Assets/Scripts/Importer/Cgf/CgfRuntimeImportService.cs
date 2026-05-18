using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace OpenFarCry.Importer.Cgf
{
    public sealed class CgfRuntimeImportService
    {
        readonly CgfResourceImportService _resourceService;
        readonly CgfRuntimeAssetCache _runtimeCache;

        readonly Dictionary<string, UniTaskCompletionSource<CgfFile>> _inFlightParsed =
            new Dictionary<string, UniTaskCompletionSource<CgfFile>>();
        readonly object _inFlightParsedLock = new object();
        readonly Dictionary<string, UniTaskCompletionSource<CgfRuntimeAssetCache.RuntimeModelArtifact>> _inFlightModel =
            new Dictionary<string, UniTaskCompletionSource<CgfRuntimeAssetCache.RuntimeModelArtifact>>();
        readonly object _inFlightModelLock = new object();

        // Worker count for PreloadAsync fan-out. The main-thread mesh upload
        // inside ImportAsync still serializes; this overlaps the I/O + parse.
        const int PreloadConcurrency = 8;

        public CgfRuntimeImportService(
            CgfResourceImportService resourceService = null,
            CgfRuntimeAssetCache runtimeCache = null)
        {
            _resourceService = resourceService ?? CgfResourceImportService.Instance;
            _runtimeCache = runtimeCache ?? new CgfRuntimeAssetCache();
        }

        public CgfRuntimeAssetCache RuntimeCache => _runtimeCache;

        // Normalized path + the two cache keys derived from a request. Built once
        // by PrepareRequest so the sync and async import paths share one source.
        readonly struct ImportKeys
        {
            public readonly string NormalizedVirtualPath;
            public readonly string ParsedCacheKey;
            public readonly string ModelCacheKey;

            public ImportKeys(string normalizedVirtualPath, string parsedCacheKey, string modelCacheKey)
            {
                NormalizedVirtualPath = normalizedVirtualPath;
                ParsedCacheKey = parsedCacheKey;
                ModelCacheKey = modelCacheKey;
            }
        }

        public CgfRuntimeImportResult Import(CgfRuntimeImportRequest request, string levelScopeId = null)
        {
            if (!TryPrepareRequest(request, out var keys, out var earlyResult))
                return earlyResult;

            try
            {
                if (TryReturnCachedModel(request, keys, levelScopeId, out var cachedResult))
                    return cachedResult;

                CgfFile parsedBase;
                bool usedRuntimeCache = false;
                if (request.UseRuntimeMemoryCache &&
                    _runtimeCache.TryRetainParsed(keys.ParsedCacheKey, levelScopeId, out parsedBase))
                {
                    usedRuntimeCache = true;
                }
                else
                {
                    byte[] sourceBytes = _resourceService.LoadRuntimeResourceBytes(keys.NormalizedVirtualPath);
                    parsedBase = CgfParser.Parse(sourceBytes);
                    parsedBase.SourceVirtualPath = keys.NormalizedVirtualPath;

                    if (request.UseRuntimeMemoryCache)
                        _runtimeCache.StoreParsed(keys.ParsedCacheKey, parsedBase, levelScopeId);
                }

                var parsedForBuild = PrepareParsedForBuild(parsedBase, request, keys);
                var prepared = CgfMeshBuilder.PrepareBuild(
                    parsedForBuild,
                    importSkeleton: request.ImportSkeleton,
                    importScale: request.ImportScale);
                var buildResult = CgfMeshBuilder.UploadPrepared(prepared);

                if (request.UseRuntimeMemoryCache)
                {
                    var artifact = new CgfRuntimeAssetCache.RuntimeModelArtifact(
                        parsedFile: parsedForBuild,
                        buildResult: buildResult,
                        parsedCacheKey: keys.ParsedCacheKey);
                    _runtimeCache.StoreModel(keys.ModelCacheKey, artifact, levelScopeId);
                }

                return CgfRuntimeImportResult.Completed(
                    virtualPath: keys.NormalizedVirtualPath,
                    parsedFile: parsedForBuild,
                    buildResult: buildResult,
                    usedRuntimeMemoryCache: usedRuntimeCache,
                    parsedCacheKey: keys.ParsedCacheKey,
                    modelCacheKey: keys.ModelCacheKey);
            }
            catch (Exception e)
            {
                return CgfRuntimeImportResult.Failed(request.VirtualPath, e.Message);
            }
        }

        public async UniTask<CgfRuntimeImportResult> ImportAsync(
            CgfRuntimeImportRequest request,
            string levelScopeId = null,
            CancellationToken ct = default)
        {
            if (!TryPrepareRequest(request, out var keys, out var earlyResult))
                return earlyResult;

            try
            {
                if (TryReturnCachedModel(request, keys, levelScopeId, out var cachedResult))
                    return cachedResult;

                CgfFile parsedBase;
                bool usedRuntimeCache = false;
                if (request.UseRuntimeMemoryCache &&
                    _runtimeCache.TryRetainParsed(keys.ParsedCacheKey, levelScopeId, out parsedBase))
                {
                    usedRuntimeCache = true;
                }
                else
                {
                    parsedBase = await ParseCoalescedAsync(request, keys, levelScopeId, ct);
                }

                ct.ThrowIfCancellationRequested();

                var parsedForBuild = PrepareParsedForBuild(parsedBase, request, keys);

                var artifact = await BuildModelArtifactAsync(
                    request,
                    parsedForBuild,
                    keys.ParsedCacheKey,
                    keys.ModelCacheKey,
                    levelScopeId,
                    ct);

                return CgfRuntimeImportResult.Completed(
                    virtualPath: keys.NormalizedVirtualPath,
                    parsedFile: artifact.ParsedFile,
                    buildResult: artifact.BuildResult,
                    usedRuntimeMemoryCache: usedRuntimeCache,
                    parsedCacheKey: keys.ParsedCacheKey,
                    modelCacheKey: keys.ModelCacheKey);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return CgfRuntimeImportResult.Failed(request.VirtualPath, e.Message);
            }
        }

        public async UniTask<IReadOnlyDictionary<string, CgfRuntimeImportResult>> PreloadAsync(
            IReadOnlyList<CgfRuntimeImportRequest> requests,
            string levelScopeId = null,
            CancellationToken ct = default)
        {
            var uniqueRequests = BuildUniquePreloadRequests(requests);
            var results = new Dictionary<string, CgfRuntimeImportResult>(uniqueRequests.Count, StringComparer.Ordinal);
            if (uniqueRequests.Count == 0)
                return results;

            // Fan out across a bounded worker pool: each worker pulls the next
            // request from a shared cursor, so I/O + parse overlap. ImportAsync's
            // own in-flight coalescing covers any same-key races; BuildUnique-
            // PreloadRequests has already deduped by model key.
            var imported = new CgfRuntimeImportResult[uniqueRequests.Count];
            int cursor = -1;

            async UniTask RunPreloadWorkerAsync()
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    int index = Interlocked.Increment(ref cursor);
                    if (index >= uniqueRequests.Count)
                        return;

                    imported[index] = await ImportAsync(uniqueRequests[index].Request, levelScopeId, ct);
                }
            }

            int workerCount = Math.Min(PreloadConcurrency, uniqueRequests.Count);
            var workers = new List<UniTask>(workerCount);
            for (int w = 0; w < workerCount; w++)
                workers.Add(RunPreloadWorkerAsync());
            await UniTask.WhenAll(workers);

            for (int i = 0; i < uniqueRequests.Count; i++)
                results[uniqueRequests[i].ModelCacheKey] = imported[i];

            return results;
        }

        public void Release(string parsedCacheKey)
        {
            _runtimeCache.ReleaseParsed(parsedCacheKey);
        }

        public void Release(CgfRuntimeImportResult importResult)
        {
            if (importResult == null)
                return;

            if (!string.IsNullOrWhiteSpace(importResult.ModelCacheKey))
                _runtimeCache.ReleaseModel(importResult.ModelCacheKey);

            if (!string.IsNullOrWhiteSpace(importResult.ParsedCacheKey))
                _runtimeCache.ReleaseParsed(importResult.ParsedCacheKey);
        }

        public void ReleaseModel(string modelCacheKey)
        {
            _runtimeCache.ReleaseModel(modelCacheKey);
        }

        public void ReleaseLevelScope(string levelScopeId)
        {
            _runtimeCache.ReleaseLevelScope(levelScopeId);
        }

        public int TrimUnused()
        {
            return _runtimeCache.TrimUnused();
        }

        public void ClearRuntimeCache()
        {
            _runtimeCache.ClearRuntimeCache();
        }

        public void DisposeParsedNativeData()
        {
            _runtimeCache.DisposeParsedNativeData();
        }

        public CgfRuntimeAssetCache.Stats GetCacheStats()
        {
            return _runtimeCache.GetStats();
        }

        // ── Shared request preparation ────────────────────────────────────────

        // Validates the request and builds the cache keys. Returns false with a
        // Failed result for unusable input; throws ArgumentNullException for a
        // null request (callers always pass one).
        bool TryPrepareRequest(
            CgfRuntimeImportRequest request,
            out ImportKeys keys,
            out CgfRuntimeImportResult failedResult)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            keys = default;
            failedResult = null;

            string virtualPath = request.VirtualPath;
            if (string.IsNullOrWhiteSpace(virtualPath))
            {
                failedResult = CgfRuntimeImportResult.Failed(virtualPath, "Virtual path is null or empty.");
                return false;
            }

            if (!_resourceService.IsSupportedVirtualPath(virtualPath))
            {
                failedResult = CgfRuntimeImportResult.Failed(virtualPath, $"Unsupported CGF/CGA path: '{virtualPath}'.");
                return false;
            }

            string normalizedVirtualPath = ImportAssetPaths.NormalizeVirtualPath(virtualPath);
            keys = new ImportKeys(
                normalizedVirtualPath,
                CgfCacheKeys.BuildParsedCacheKey(normalizedVirtualPath),
                CgfCacheKeys.BuildModelCacheKey(
                    normalizedVirtualPath,
                    request.SelectedMeshChunkId,
                    request.ImportSkeleton,
                    request.ImportScale));
            return true;
        }

        bool TryReturnCachedModel(
            CgfRuntimeImportRequest request,
            ImportKeys keys,
            string levelScopeId,
            out CgfRuntimeImportResult result)
        {
            result = null;
            if (!request.UseRuntimeMemoryCache ||
                !_runtimeCache.TryRetainModel(keys.ModelCacheKey, levelScopeId, out var cachedModel))
            {
                return false;
            }

            result = CgfRuntimeImportResult.Completed(
                virtualPath: keys.NormalizedVirtualPath,
                parsedFile: cachedModel.ParsedFile,
                buildResult: cachedModel.BuildResult,
                usedRuntimeMemoryCache: true,
                parsedCacheKey: cachedModel.ParsedCacheKey,
                modelCacheKey: keys.ModelCacheKey,
                usedModelRuntimeMemoryCache: true);
            return true;
        }

        static CgfFile PrepareParsedForBuild(CgfFile parsedBase, CgfRuntimeImportRequest request, ImportKeys keys)
        {
            if (string.IsNullOrEmpty(parsedBase.SourceVirtualPath))
                parsedBase.SourceVirtualPath = keys.NormalizedVirtualPath;

            return CreateSelectedMeshView(parsedBase, request.SelectedMeshChunkId);
        }

        // Reads + parses the CGF off the thread pool, coalescing concurrent
        // requests for the same path so duplicate I/O never runs.
        async UniTask<CgfFile> ParseCoalescedAsync(
            CgfRuntimeImportRequest request,
            ImportKeys keys,
            string levelScopeId,
            CancellationToken ct)
        {
            UniTaskCompletionSource<CgfFile> tcs;
            bool isOwner;
            lock (_inFlightParsedLock)
            {
                if (_inFlightParsed.TryGetValue(keys.ParsedCacheKey, out tcs))
                {
                    isOwner = false;
                }
                else
                {
                    tcs = new UniTaskCompletionSource<CgfFile>();
                    _inFlightParsed[keys.ParsedCacheKey] = tcs;
                    isOwner = true;
                }
            }

            if (!isOwner)
            {
                var joined = await tcs.Task;
                ct.ThrowIfCancellationRequested();
                return joined;
            }

            try
            {
                // Use CancellationToken.None so one consumer cancelling doesn't
                // discard I/O that other waiters can use.
                string pathForLambda = keys.NormalizedVirtualPath;
                var parsedBase = await UniTask.RunOnThreadPool(
                    () =>
                    {
                        byte[] bytes = _resourceService.LoadRuntimeResourceBytes(pathForLambda);
                        var parsed = CgfParser.Parse(bytes);
                        parsed.SourceVirtualPath = pathForLambda;
                        return parsed;
                    },
                    cancellationToken: CancellationToken.None);

                if (request.UseRuntimeMemoryCache)
                    _runtimeCache.StoreParsed(keys.ParsedCacheKey, parsedBase, levelScopeId);

                tcs.TrySetResult(parsedBase);
                return parsedBase;
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
                throw;
            }
            finally
            {
                lock (_inFlightParsedLock)
                    _inFlightParsed.Remove(keys.ParsedCacheKey);
            }
        }

        async UniTask<CgfRuntimeAssetCache.RuntimeModelArtifact> BuildModelArtifactAsync(
            CgfRuntimeImportRequest request,
            CgfFile parsedForBuild,
            string parsedCacheKey,
            string modelCacheKey,
            string levelScopeId,
            CancellationToken ct)
        {
            if (!request.UseRuntimeMemoryCache)
            {
                // For no-cache editor paths keep cancellation behavior unchanged.
                var preparedNoCache = await UniTask.RunOnThreadPool(
                    () => CgfMeshBuilder.PrepareBuild(
                        parsedForBuild,
                        importSkeleton: request.ImportSkeleton,
                        importScale: request.ImportScale),
                    cancellationToken: ct);

                ct.ThrowIfCancellationRequested();
                await UniTask.SwitchToMainThread(ct);
                var buildResultNoCache = CgfMeshBuilder.UploadPrepared(preparedNoCache);
                return new CgfRuntimeAssetCache.RuntimeModelArtifact(
                    parsedFile: parsedForBuild,
                    buildResult: buildResultNoCache,
                    parsedCacheKey: parsedCacheKey);
            }

            UniTaskCompletionSource<CgfRuntimeAssetCache.RuntimeModelArtifact> tcs;
            bool isOwner;
            lock (_inFlightModelLock)
            {
                if (_inFlightModel.TryGetValue(modelCacheKey, out tcs))
                {
                    isOwner = false;
                }
                else
                {
                    tcs = new UniTaskCompletionSource<CgfRuntimeAssetCache.RuntimeModelArtifact>();
                    _inFlightModel[modelCacheKey] = tcs;
                    isOwner = true;
                }
            }

            if (isOwner)
            {
                try
                {
                    var prepared = await UniTask.RunOnThreadPool(
                        () => CgfMeshBuilder.PrepareBuild(
                            parsedForBuild,
                            importSkeleton: request.ImportSkeleton,
                            importScale: request.ImportScale),
                        cancellationToken: CancellationToken.None);

                    await UniTask.SwitchToMainThread();
                    var buildResult = CgfMeshBuilder.UploadPrepared(prepared);
                    var artifact = new CgfRuntimeAssetCache.RuntimeModelArtifact(
                        parsedFile: parsedForBuild,
                        buildResult: buildResult,
                        parsedCacheKey: parsedCacheKey);

                    _runtimeCache.StoreModel(modelCacheKey, artifact, levelScopeId);
                    tcs.TrySetResult(artifact);
                    return artifact;
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                    throw;
                }
                finally
                {
                    lock (_inFlightModelLock)
                        _inFlightModel.Remove(modelCacheKey);
                }
            }

            var waitedArtifact = await tcs.Task;
            ct.ThrowIfCancellationRequested();
            return waitedArtifact;
        }

        internal static List<PreloadRequestEntry> BuildUniquePreloadRequests(
            IReadOnlyList<CgfRuntimeImportRequest> requests)
        {
            var result = new List<PreloadRequestEntry>();
            if (requests == null || requests.Count == 0)
                return result;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                if (request == null || string.IsNullOrWhiteSpace(request.VirtualPath))
                    continue;

                string normalizedVirtualPath = ImportAssetPaths.NormalizeVirtualPath(request.VirtualPath);
                string modelCacheKey = CgfCacheKeys.BuildModelCacheKey(
                    normalizedVirtualPath,
                    request.SelectedMeshChunkId,
                    request.ImportSkeleton,
                    request.ImportScale);

                if (!seen.Add(modelCacheKey))
                    continue;

                result.Add(new PreloadRequestEntry(request, modelCacheKey));
            }

            return result;
        }

        internal readonly struct PreloadRequestEntry
        {
            public readonly CgfRuntimeImportRequest Request;
            public readonly string ModelCacheKey;

            public PreloadRequestEntry(CgfRuntimeImportRequest request, string modelCacheKey)
            {
                Request = request;
                ModelCacheKey = modelCacheKey;
            }
        }

        static CgfFile CreateSelectedMeshView(CgfFile source, int selectedMeshChunkId)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            int meshId = selectedMeshChunkId;
            if (meshId < 0)
                meshId = source.SelectedMeshChunkID;

            var view = new CgfFile
            {
                FileType = source.FileType,
                Version = source.Version,
                SourceVirtualPath = source.SourceVirtualPath,
                SelectedMeshChunkID = meshId,
                MeshChunks = source.MeshChunks,
                MeshByChunkID = source.MeshByChunkID,
                BoneMeshChunks = source.BoneMeshChunks,
                BoneMeshByChunkID = source.BoneMeshByChunkID,
                NodeChunks = source.NodeChunks,
                NodeByChunkID = source.NodeByChunkID,
                BoneInitPosByMeshChunkID = source.BoneInitPosByMeshChunkID,
                BoneNames = source.BoneNames,
                BoneAnim = source.BoneAnim,
                MaterialChunks    = source.MaterialChunks,
                MaterialByChunkID = source.MaterialByChunkID,
                LeafMaterials     = source.LeafMaterials,
                MaterialChildrenByParentChunkID = source.MaterialChildrenByParentChunkID,
            };

            if (meshId != -1 && source.MeshByChunkID.TryGetValue(meshId, out var selectedMesh))
                view.MeshChunk = selectedMesh;
            else
                view.MeshChunk = source.MeshChunk;

            if (meshId != -1 && source.BoneInitPosByMeshChunkID.TryGetValue(meshId, out var selectedBindPoses))
            {
                view.BoneInitPos = selectedBindPoses;
                return view;
            }

            view.BoneInitPos = source.BoneInitPos;
            return view;
        }
    }
}
