using System;
using System.Diagnostics;
using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    public readonly struct FcAnimationLoadRequest
    {
        public readonly GameObject TargetRoot;
        public readonly CgfFile ParsedFile;
        public readonly string ModelVirtualPath;
        public readonly float ImportScale;

        public FcAnimationLoadRequest(
            GameObject targetRoot,
            CgfFile parsedFile,
            string modelVirtualPath,
            float importScale)
        {
            TargetRoot = targetRoot;
            ParsedFile = parsedFile;
            ModelVirtualPath = modelVirtualPath;
            ImportScale = importScale;
        }
    }

    public sealed class LoadedAnimationArtifact
    {
        public readonly bool Success;
        public readonly int ClipCount;
        public readonly string WarningMessage;
        public readonly double TotalMs;
        public readonly CgfAnimationRuntimeImportService.RuntimeCacheStats CacheStatsBefore;
        public readonly CgfAnimationRuntimeImportService.RuntimeCacheStats CacheStatsAfter;

        LoadedAnimationArtifact(
            bool success,
            int clipCount,
            string warningMessage,
            double totalMs,
            CgfAnimationRuntimeImportService.RuntimeCacheStats cacheStatsBefore,
            CgfAnimationRuntimeImportService.RuntimeCacheStats cacheStatsAfter)
        {
            Success = success;
            ClipCount = clipCount;
            WarningMessage = warningMessage;
            TotalMs = totalMs;
            CacheStatsBefore = cacheStatsBefore;
            CacheStatsAfter = cacheStatsAfter;
        }

        public static LoadedAnimationArtifact Completed(
            int clipCount,
            string warningMessage,
            double totalMs,
            CgfAnimationRuntimeImportService.RuntimeCacheStats cacheStatsBefore,
            CgfAnimationRuntimeImportService.RuntimeCacheStats cacheStatsAfter)
        {
            return new LoadedAnimationArtifact(
                success: true,
                clipCount: clipCount,
                warningMessage: warningMessage,
                totalMs: totalMs,
                cacheStatsBefore: cacheStatsBefore,
                cacheStatsAfter: cacheStatsAfter);
        }

        public static LoadedAnimationArtifact Failed(string warningMessage)
        {
            var stats = CgfAnimationRuntimeImportService.GetRuntimeCacheStats();
            return new LoadedAnimationArtifact(
                success: false,
                clipCount: 0,
                warningMessage: warningMessage,
                totalMs: 0,
                cacheStatsBefore: stats,
                cacheStatsAfter: stats);
        }
    }

    // Character animation attach service.
    // Keeps animation discovery/cache workflow out of entity component.
    [DefaultExecutionOrder(-97)]
    public sealed class FcAnimationLoadService : MonoBehaviour
    {
        public static FcAnimationLoadService Current { get; private set; }

        readonly CgfAnimationRuntimeImportService _animService = new CgfAnimationRuntimeImportService();
        FcLevelLoadReport _report;

        void Awake()
        {
            Current = this;
            var cache = GetComponent<FcLevelCacheService>() ?? GetComponentInParent<FcLevelCacheService>();
            string scopeId = cache != null ? cache.LevelScopeId : string.Empty;
            _report = FcLevelRuntimeReportRegistry.GetOrCreate(scopeId);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        public LoadedAnimationArtifact LoadAndAttach(FcAnimationLoadRequest request)
        {
            if (request.TargetRoot == null)
                return LoadedAnimationArtifact.Failed("Animation target root is null.");
            if (request.ParsedFile == null)
                return LoadedAnimationArtifact.Failed("Parsed CGF file is null.");
            if (string.IsNullOrWhiteSpace(request.ModelVirtualPath))
                return LoadedAnimationArtifact.Failed("Model virtual path is empty.");

            var cacheBefore = CgfAnimationRuntimeImportService.GetRuntimeCacheStats();
            var sw = Stopwatch.StartNew();
            var clips = _animService.TryAttachAnimations(
                request.TargetRoot,
                request.ParsedFile,
                rigDefinition: null,
                modelVirtualPath: request.ModelVirtualPath,
                importScale: request.ImportScale,
                out string warningMessage);
            var cacheAfter = CgfAnimationRuntimeImportService.GetRuntimeCacheStats();

            int clipCount = clips != null ? clips.Count : 0;
            _report?.RecordAnimationAttach(
                clipCount,
                sw.Elapsed.TotalMilliseconds,
                animationSetHitDelta: cacheAfter.AnimationSetHitCount - cacheBefore.AnimationSetHitCount,
                animationSetMissDelta: cacheAfter.AnimationSetMissCount - cacheBefore.AnimationSetMissCount,
                clipHitDelta: cacheAfter.ClipHitCount - cacheBefore.ClipHitCount,
                clipMissDelta: cacheAfter.ClipMissCount - cacheBefore.ClipMissCount,
                cafHitDelta: cacheAfter.CafHitCount - cacheBefore.CafHitCount,
                cafMissDelta: cacheAfter.CafMissCount - cacheBefore.CafMissCount);

            return LoadedAnimationArtifact.Completed(
                clipCount,
                warningMessage,
                sw.Elapsed.TotalMilliseconds,
                cacheBefore,
                cacheAfter);
        }
    }
}
