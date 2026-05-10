using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace OpenFarCry.Importer.Cgf
{
    public sealed class CgfRuntimeImportService
    {
        readonly CgfResourceImportService _resourceService;
        readonly CgfRuntimeAssetCache _runtimeCache;

        public CgfRuntimeImportService(
            CgfResourceImportService resourceService = null,
            CgfRuntimeAssetCache runtimeCache = null)
        {
            _resourceService = resourceService ?? CgfResourceImportService.Instance;
            _runtimeCache = runtimeCache ?? new CgfRuntimeAssetCache();
        }

        public CgfRuntimeAssetCache RuntimeCache => _runtimeCache;

        public CgfRuntimeImportResult Import(CgfRuntimeImportRequest request, string levelScopeId = null)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            string virtualPath = request.VirtualPath;
            if (string.IsNullOrWhiteSpace(virtualPath))
                return CgfRuntimeImportResult.Failed(virtualPath, "Virtual path is null or empty.");

            if (!_resourceService.IsSupportedVirtualPath(virtualPath))
                return CgfRuntimeImportResult.Failed(virtualPath, $"Unsupported CGF/CGA path: '{virtualPath}'.");

            try
            {
                string normalizedVirtualPath = ImportAssetPaths.NormalizeVirtualPath(virtualPath);
                string parsedCacheKey = BuildParsedCacheKey(normalizedVirtualPath);
                string modelCacheKey = BuildModelCacheKey(
                    normalizedVirtualPath,
                    request.SelectedMeshChunkId,
                    request.ImportSkeleton,
                    request.ImportScale);

                if (request.UseRuntimeMemoryCache &&
                    _runtimeCache.TryRetainModel(modelCacheKey, levelScopeId, out var cachedModel))
                {
                    return CgfRuntimeImportResult.Completed(
                        virtualPath: normalizedVirtualPath,
                        parsedFile: cachedModel.ParsedFile,
                        buildResult: cachedModel.BuildResult,
                        usedRuntimeMemoryCache: true,
                        parsedCacheKey: cachedModel.ParsedCacheKey,
                        modelCacheKey: modelCacheKey);
                }

                CgfFile parsedBase;
                bool usedRuntimeCache = false;
                if (request.UseRuntimeMemoryCache &&
                    _runtimeCache.TryRetainParsed(parsedCacheKey, levelScopeId, out parsedBase))
                {
                    usedRuntimeCache = true;
                }
                else
                {
                    byte[] sourceBytes = _resourceService.LoadRuntimeResourceBytes(normalizedVirtualPath);
                    parsedBase = CgfParser.Parse(sourceBytes);
                    parsedBase.SourceVirtualPath = normalizedVirtualPath;

                    if (request.UseRuntimeMemoryCache)
                        _runtimeCache.StoreParsed(parsedCacheKey, parsedBase, levelScopeId);
                }

                if (string.IsNullOrEmpty(parsedBase.SourceVirtualPath))
                    parsedBase.SourceVirtualPath = normalizedVirtualPath;

                var parsedForBuild = CreateSelectedMeshView(parsedBase, request.SelectedMeshChunkId);
                var buildResult = CgfMeshBuilder.Build(
                    parsedForBuild,
                    importSkeleton: request.ImportSkeleton,
                    importScale: request.ImportScale);

                if (request.UseRuntimeMemoryCache)
                {
                    var artifact = new CgfRuntimeAssetCache.RuntimeModelArtifact(
                        parsedFile: parsedForBuild,
                        buildResult: buildResult,
                        parsedCacheKey: parsedCacheKey);
                    _runtimeCache.StoreModel(modelCacheKey, artifact, levelScopeId);
                }

                return CgfRuntimeImportResult.Completed(
                    virtualPath: normalizedVirtualPath,
                    parsedFile: parsedForBuild,
                    buildResult: buildResult,
                    usedRuntimeMemoryCache: usedRuntimeCache,
                    parsedCacheKey: parsedCacheKey,
                    modelCacheKey: modelCacheKey);
            }
            catch (Exception e)
            {
                return CgfRuntimeImportResult.Failed(virtualPath, e.Message);
            }
        }

        public async UniTask<CgfRuntimeImportResult> ImportAsync(
            CgfRuntimeImportRequest request,
            string levelScopeId = null,
            CancellationToken ct = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            string virtualPath = request.VirtualPath;
            if (string.IsNullOrWhiteSpace(virtualPath))
                return CgfRuntimeImportResult.Failed(virtualPath, "Virtual path is null or empty.");

            if (!_resourceService.IsSupportedVirtualPath(virtualPath))
                return CgfRuntimeImportResult.Failed(virtualPath, $"Unsupported CGF/CGA path: '{virtualPath}'.");

            try
            {
                string normalizedVirtualPath = ImportAssetPaths.NormalizeVirtualPath(virtualPath);
                string parsedCacheKey = BuildParsedCacheKey(normalizedVirtualPath);
                string modelCacheKey = BuildModelCacheKey(
                    normalizedVirtualPath,
                    request.SelectedMeshChunkId,
                    request.ImportSkeleton,
                    request.ImportScale);

                if (request.UseRuntimeMemoryCache &&
                    _runtimeCache.TryRetainModel(modelCacheKey, levelScopeId, out var cachedModel))
                {
                    return CgfRuntimeImportResult.Completed(
                        virtualPath: normalizedVirtualPath,
                        parsedFile: cachedModel.ParsedFile,
                        buildResult: cachedModel.BuildResult,
                        usedRuntimeMemoryCache: true,
                        parsedCacheKey: cachedModel.ParsedCacheKey,
                        modelCacheKey: modelCacheKey);
                }

                CgfFile parsedBase;
                bool usedRuntimeCache = false;
                if (request.UseRuntimeMemoryCache &&
                    _runtimeCache.TryRetainParsed(parsedCacheKey, levelScopeId, out parsedBase))
                {
                    usedRuntimeCache = true;
                }
                else
                {
                    // I/O + parsing off main thread — neither FcFileSystem.ReadAllBytes nor
                    // CgfParser.Parse touches Unity API, so thread pool is safe.
                    string pathForLambda = normalizedVirtualPath;
                    parsedBase = await UniTask.RunOnThreadPool(
                        () =>
                        {
                            byte[] bytes = _resourceService.LoadRuntimeResourceBytes(pathForLambda);
                            var parsed = CgfParser.Parse(bytes);
                            parsed.SourceVirtualPath = pathForLambda;
                            return parsed;
                        },
                        cancellationToken: ct);

                    if (request.UseRuntimeMemoryCache)
                        _runtimeCache.StoreParsed(parsedCacheKey, parsedBase, levelScopeId);
                }

                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(parsedBase.SourceVirtualPath))
                    parsedBase.SourceVirtualPath = normalizedVirtualPath;

                var parsedForBuild = CreateSelectedMeshView(parsedBase, request.SelectedMeshChunkId);
                var buildResult = CgfMeshBuilder.Build(
                    parsedForBuild,
                    importSkeleton: request.ImportSkeleton,
                    importScale: request.ImportScale);

                if (request.UseRuntimeMemoryCache)
                {
                    var artifact = new CgfRuntimeAssetCache.RuntimeModelArtifact(
                        parsedFile: parsedForBuild,
                        buildResult: buildResult,
                        parsedCacheKey: parsedCacheKey);
                    _runtimeCache.StoreModel(modelCacheKey, artifact, levelScopeId);
                }

                return CgfRuntimeImportResult.Completed(
                    virtualPath: normalizedVirtualPath,
                    parsedFile: parsedForBuild,
                    buildResult: buildResult,
                    usedRuntimeMemoryCache: usedRuntimeCache,
                    parsedCacheKey: parsedCacheKey,
                    modelCacheKey: modelCacheKey);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                return CgfRuntimeImportResult.Failed(virtualPath, e.Message);
            }
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

        public CgfRuntimeAssetCache.Stats GetCacheStats()
        {
            return _runtimeCache.GetStats();
        }

        static string BuildParsedCacheKey(string normalizedVirtualPath)
        {
            return $"{normalizedVirtualPath}|parsed";
        }

        static string BuildModelCacheKey(
            string normalizedVirtualPath,
            int selectedMeshChunkId,
            bool importSkeleton,
            float importScale)
        {
            return
                $"{normalizedVirtualPath}|mesh:{selectedMeshChunkId}|skel:{(importSkeleton ? 1 : 0)}|scale:{importScale:R}";
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
