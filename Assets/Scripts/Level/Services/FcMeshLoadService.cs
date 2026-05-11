using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer;
using OpenFarCry.Importer.Cgf;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OpenFarCry.Level.Services
{
    public readonly struct FcMeshLoadRequest
    {
        public readonly string VirtualPath;
        public readonly string LevelScopeId;
        public readonly int SelectedMeshChunkId;
        public readonly bool ImportSkeleton;
        public readonly float ImportScale;
        public readonly bool UseRuntimeMemoryCache;
        public readonly bool PreloadTextures;

        public FcMeshLoadRequest(
            string virtualPath,
            string levelScopeId,
            int selectedMeshChunkId = -1,
            bool importSkeleton = true,
            float importScale = 1f,
            bool useRuntimeMemoryCache = true,
            bool preloadTextures = true)
        {
            VirtualPath = virtualPath;
            LevelScopeId = levelScopeId;
            SelectedMeshChunkId = selectedMeshChunkId;
            ImportSkeleton = importSkeleton;
            ImportScale = importScale;
            UseRuntimeMemoryCache = useRuntimeMemoryCache;
            PreloadTextures = preloadTextures;
        }
    }

    public sealed class LoadedMeshArtifact
    {
        public readonly bool Success;
        public readonly string ErrorMessage;
        public readonly string LevelScopeId;
        public readonly CgfRuntimeImportResult ImportResult;
        public readonly double TotalMs;

        LoadedMeshArtifact(
            bool success,
            string errorMessage,
            string levelScopeId,
            CgfRuntimeImportResult importResult,
            double totalMs)
        {
            Success = success;
            ErrorMessage = errorMessage;
            LevelScopeId = levelScopeId;
            ImportResult = importResult;
            TotalMs = totalMs;
        }

        public static LoadedMeshArtifact Failed(string errorMessage, string levelScopeId = null)
        {
            return new LoadedMeshArtifact(
                success: false,
                errorMessage: errorMessage,
                levelScopeId: levelScopeId,
                importResult: null,
                totalMs: 0);
        }

        public static LoadedMeshArtifact Completed(
            CgfRuntimeImportResult importResult,
            string levelScopeId,
            double totalMs)
        {
            return new LoadedMeshArtifact(
                success: importResult != null && importResult.Success,
                errorMessage: importResult != null ? importResult.ErrorMessage : "Import result is null.",
                levelScopeId: levelScopeId,
                importResult: importResult,
                totalMs: totalMs);
        }
    }

    // Async CGF mesh loader used by FcEntityLoadService.
    // Coalesces in-flight requests by model key to avoid duplicate misses.
    [DefaultExecutionOrder(-99)]
    public sealed class FcMeshLoadService : MonoBehaviour, IFcMeshLoadService
    {
        sealed class InFlightEntry
        {
            public readonly UniTaskCompletionSource<LoadedMeshArtifact> Source = new UniTaskCompletionSource<LoadedMeshArtifact>();
            public CancellationToken ExecutionToken;
        }

        public static FcMeshLoadService Current { get; private set; }

        readonly object _sync = new object();
        readonly Dictionary<string, InFlightEntry> _inFlightByKey = new Dictionary<string, InFlightEntry>(StringComparer.Ordinal);
        string _levelScopeId;

        void Awake()
        {
            Current = this;
            var cache = GetComponent<FcLevelCacheService>() ?? GetComponentInParent<FcLevelCacheService>();
            _levelScopeId = cache != null ? cache.LevelScopeId : string.Empty;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        public UniTask<LoadedMeshArtifact> LoadAsync(FcMeshLoadRequest request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(request.VirtualPath))
                return UniTask.FromResult(LoadedMeshArtifact.Failed("VirtualPath is null or empty.", request.LevelScopeId));

            string normalizedPath;
            try
            {
                normalizedPath = ImportAssetPaths.NormalizeVirtualPath(request.VirtualPath);
            }
            catch (Exception e)
            {
                return UniTask.FromResult(LoadedMeshArtifact.Failed(e.Message, request.LevelScopeId));
            }

            string scopeId = string.IsNullOrWhiteSpace(request.LevelScopeId) ? _levelScopeId : request.LevelScopeId;
            var normalizedRequest = new FcMeshLoadRequest(
                virtualPath: normalizedPath,
                levelScopeId: scopeId,
                selectedMeshChunkId: request.SelectedMeshChunkId,
                importSkeleton: request.ImportSkeleton,
                importScale: request.ImportScale,
                useRuntimeMemoryCache: request.UseRuntimeMemoryCache,
                preloadTextures: request.PreloadTextures);

            string key = BuildInFlightKey(normalizedRequest);
            InFlightEntry entry;
            bool owner = false;
            lock (_sync)
            {
                if (!_inFlightByKey.TryGetValue(key, out entry))
                {
                    entry = new InFlightEntry();
                    entry.ExecutionToken = ct;
                    _inFlightByKey[key] = entry;
                    owner = true;
                }
            }

            if (owner)
                ExecuteAndCompleteAsync(key, entry, normalizedRequest).Forget();

            return AwaitInFlightAsync(entry, ct);
        }

        static async UniTask<LoadedMeshArtifact> AwaitInFlightAsync(InFlightEntry entry, CancellationToken ct)
        {
            if (!ct.CanBeCanceled)
                return await entry.Source.Task;
            return await entry.Source.Task.AttachExternalCancellation(ct);
        }

        async UniTaskVoid ExecuteAndCompleteAsync(
            string inFlightKey,
            InFlightEntry entry,
            FcMeshLoadRequest request)
        {
            try
            {
                var artifact = await LoadInternalAsync(request, entry.ExecutionToken);
                entry.Source.TrySetResult(artifact);
            }
            catch (OperationCanceledException)
            {
                entry.Source.TrySetCanceled();
            }
            catch (Exception e)
            {
                entry.Source.TrySetResult(LoadedMeshArtifact.Failed(e.Message, request.LevelScopeId));
            }
            finally
            {
                lock (_sync)
                    _inFlightByKey.Remove(inFlightKey);
            }
        }

        static async UniTask<LoadedMeshArtifact> LoadInternalAsync(FcMeshLoadRequest request, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var runtimeRequest = new CgfRuntimeImportRequest(
                virtualPath: request.VirtualPath,
                selectedMeshChunkId: request.SelectedMeshChunkId,
                importSkeleton: request.ImportSkeleton,
                importAnimations: false,
                importScale: request.ImportScale,
                useRuntimeMemoryCache: request.UseRuntimeMemoryCache);

            var result = await CgfRuntimeImporter.ImportAsync(
                runtimeRequest,
                request.LevelScopeId,
                ct);

            if (!result.Success)
            {
                Debug.LogWarning($"[FcMeshLoadService] '{request.VirtualPath}': {result.ErrorMessage}");
                return LoadedMeshArtifact.Failed(result.ErrorMessage, request.LevelScopeId);
            }

            if (request.PreloadTextures)
            {
                await CgfRuntimeImporter.MaterialService.PreloadTexturesAsync(
                    result.ParsedFile,
                    result.Mesh,
                    result.BuildResult?.SubmeshMaterialIds,
                    request.LevelScopeId,
                    ct);
            }

            return LoadedMeshArtifact.Completed(result, request.LevelScopeId, sw.Elapsed.TotalMilliseconds);
        }

        static string BuildInFlightKey(FcMeshLoadRequest request)
        {
            string scope = string.IsNullOrWhiteSpace(request.LevelScopeId) ? "<none>" : request.LevelScopeId;
            return
                $"{scope}|{request.VirtualPath}|mesh:{request.SelectedMeshChunkId}|skel:{(request.ImportSkeleton ? 1 : 0)}|scale:{request.ImportScale:R}|cache:{(request.UseRuntimeMemoryCache ? 1 : 0)}";
        }
    }
}
