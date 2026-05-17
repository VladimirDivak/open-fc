using System;

namespace OpenFarCry.Importer.Cgf
{
    // Single source for every CGF/CAF/animation cache key. Centralized so the
    // runtime importer, the level geometry planner, and the animation caches all
    // derive identical keys from identical inputs — no parallel copies to drift.
    public static class CgfCacheKeys
    {
        // ── CGF parsed + model keys ───────────────────────────────────────────

        public static string BuildParsedCacheKey(string normalizedVirtualPath)
        {
            return $"{normalizedVirtualPath}|parsed";
        }

        public static string BuildModelCacheKey(
            string normalizedVirtualPath,
            int selectedMeshChunkId,
            bool importSkeleton,
            float importScale)
        {
            return
                $"{normalizedVirtualPath}|builder:{CgfMeshBuilder.MeshCacheVersionName}|mesh:{selectedMeshChunkId}|skel:{(importSkeleton ? 1 : 0)}|scale:{importScale:R}";
        }

        // ── Animation keys ────────────────────────────────────────────────────

        const string ClipBuildVersion = "clip-v2";
        const string LoopPolicyVersion = "loop-v1";
        const string AnimationSetVersion = "animset-v1";
        const string SemanticClipVersion = "semclip-v1";

        public static string BuildCompatibilityCacheKey(string animationFingerprint, string controllerMapKey)
        {
            if (!string.IsNullOrWhiteSpace(animationFingerprint) &&
                !string.Equals(animationFingerprint, "none", StringComparison.OrdinalIgnoreCase))
            {
                return "fp:" + animationFingerprint;
            }

            return "map:" + (controllerMapKey ?? "<empty>");
        }

        public static string BuildLoopPolicyKey(string alias, bool shouldLoop)
        {
            string aliasKey = string.IsNullOrWhiteSpace(alias) ? "<empty>" : alias.ToLowerInvariant();
            return $"{LoopPolicyVersion}|alias:{aliasKey}|loop:{(shouldLoop ? 1 : 0)}";
        }

        public static string BuildClipCacheKey(
            string cafContentHash,
            string alias,
            float importScale,
            string compatibilityKey,
            string pathLayoutHash,
            string loopPolicyKey)
        {
            string aliasKey = string.IsNullOrWhiteSpace(alias) ? "<empty>" : alias.ToLowerInvariant();
            return
                $"v:{ClipBuildVersion}|caf:{cafContentHash}|alias:{aliasKey}|scale:{importScale:R}|compat:{compatibilityKey}|layout:{pathLayoutHash}|loop:{loopPolicyKey}";
        }

        public static string BuildAnimationSetModelLayoutKey(
            string modelVirtualPath,
            string animationFingerprint,
            string pathLayoutHash,
            float importScale)
        {
            string modelKey = string.IsNullOrWhiteSpace(modelVirtualPath) ? "<none>" : modelVirtualPath.ToLowerInvariant();
            string fpKey = string.IsNullOrWhiteSpace(animationFingerprint) ? "none" : animationFingerprint;
            string layoutKey = string.IsNullOrWhiteSpace(pathLayoutHash) ? "none" : pathLayoutHash;
            return $"model:{modelKey}|fp:{fpKey}|layout:{layoutKey}|scale:{importScale:R}";
        }

        public static string BuildAnimationSetCacheKey(
            string animationFingerprint,
            string pathLayoutHash,
            string animationSetHash,
            float importScale)
        {
            string fpKey = string.IsNullOrWhiteSpace(animationFingerprint) ? "none" : animationFingerprint;
            string layoutKey = string.IsNullOrWhiteSpace(pathLayoutHash) ? "none" : pathLayoutHash;
            string setKey = string.IsNullOrWhiteSpace(animationSetHash) ? "none" : animationSetHash;
            return $"v:{AnimationSetVersion}|fp:{fpKey}|layout:{layoutKey}|set:{setKey}|scale:{importScale:R}|clip:{ClipBuildVersion}";
        }

        public static string BuildSemanticClipCacheKey(
            string cafContentHash,
            string alias,
            float importScale,
            string loopPolicyKey)
        {
            string aliasKey = string.IsNullOrWhiteSpace(alias) ? "<empty>" : alias.ToLowerInvariant();
            string loopKey = string.IsNullOrWhiteSpace(loopPolicyKey) ? "<none>" : loopPolicyKey;
            return $"v:{SemanticClipVersion}|caf:{cafContentHash}|alias:{aliasKey}|scale:{importScale:R}|loop:{loopKey}";
        }

        public static string ExtractAnimationSetHashFromCacheKey(string cacheKey)
        {
            if (string.IsNullOrEmpty(cacheKey))
                return "none";

            const string marker = "|set:";
            int start = cacheKey.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
                return "none";
            start += marker.Length;
            int end = cacheKey.IndexOf("|scale:", start, StringComparison.Ordinal);
            if (end < 0 || end <= start)
                end = cacheKey.Length;
            return cacheKey.Substring(start, end - start);
        }
    }
}
