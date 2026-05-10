using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    // Per-model diagnostics snapshot captured by CgfAnimationRuntimeImportService
    // after each TryAttachAnimations call.
    public readonly struct RuntimeAttachDiagnosticsEntry
    {
        public readonly string ModelVirtualPath;
        public readonly string AnimationFingerprint;
        public readonly string PathLayoutHash;
        public readonly string AnimationSetHash;
        public readonly int SourceCount;
        public readonly int ExistingSourceCount;
        public readonly int ImportedClipCount;
        public readonly int CafHitDelta;
        public readonly int CafMissDelta;
        public readonly int ClipHitDelta;
        public readonly int ClipMissDelta;
        public readonly int AnimationSetHitDelta;
        public readonly int AnimationSetMissDelta;
        public readonly int SemanticClipHitDelta;
        public readonly int SemanticClipMissDelta;
        public readonly int ClipBuiltFromSemanticCache;
        public readonly int ClipBuiltFromSemanticFresh;
        public readonly int MissingControllerTrackCount;
        public readonly bool UsedRigDefinitionFingerprint;

        public RuntimeAttachDiagnosticsEntry(
            string modelVirtualPath,
            string animationFingerprint,
            string pathLayoutHash,
            string animationSetHash,
            int sourceCount,
            int existingSourceCount,
            int importedClipCount,
            int cafHitDelta,
            int cafMissDelta,
            int clipHitDelta,
            int clipMissDelta,
            int animationSetHitDelta,
            int animationSetMissDelta,
            int semanticClipHitDelta,
            int semanticClipMissDelta,
            int clipBuiltFromSemanticCache,
            int clipBuiltFromSemanticFresh,
            int missingControllerTrackCount,
            bool usedRigDefinitionFingerprint)
        {
            ModelVirtualPath = modelVirtualPath ?? string.Empty;
            AnimationFingerprint = animationFingerprint ?? "none";
            PathLayoutHash = pathLayoutHash ?? "none";
            AnimationSetHash = animationSetHash ?? "none";
            SourceCount = sourceCount;
            ExistingSourceCount = existingSourceCount;
            ImportedClipCount = importedClipCount;
            CafHitDelta = cafHitDelta;
            CafMissDelta = cafMissDelta;
            ClipHitDelta = clipHitDelta;
            ClipMissDelta = clipMissDelta;
            AnimationSetHitDelta = animationSetHitDelta;
            AnimationSetMissDelta = animationSetMissDelta;
            SemanticClipHitDelta = semanticClipHitDelta;
            SemanticClipMissDelta = semanticClipMissDelta;
            ClipBuiltFromSemanticCache = clipBuiltFromSemanticCache;
            ClipBuiltFromSemanticFresh = clipBuiltFromSemanticFresh;
            MissingControllerTrackCount = missingControllerTrackCount;
            UsedRigDefinitionFingerprint = usedRigDefinitionFingerprint;
        }
    }

    // Stores and reports per-model animation attach diagnostics.
    // Thread-safe; independent of CAF/clip cache state.
    public static class CgfAnimationDiagnostics
    {
        static readonly object s_sync = new object();
        static readonly List<RuntimeAttachDiagnosticsEntry> s_entries =
            new List<RuntimeAttachDiagnosticsEntry>(128);
        const int MaxEntries = 2048;

        public static bool EnableCompatibilityDiagnosticsLogging { get; set; }

        internal static void Store(RuntimeAttachDiagnosticsEntry entry)
        {
            lock (s_sync)
            {
                s_entries.Add(entry);
                int overflow = s_entries.Count - MaxEntries;
                if (overflow > 0)
                    s_entries.RemoveRange(0, overflow);
            }
        }

        public static void Clear()
        {
            lock (s_sync)
                s_entries.Clear();
        }

        public static string BuildReport(int maxEntries = 32)
        {
            RuntimeAttachDiagnosticsEntry[] snapshot;
            lock (s_sync)
                snapshot = s_entries.ToArray();

            if (snapshot.Length == 0)
                return "[CgfAnimDiag] No animation attach diagnostics captured.";

            maxEntries = Mathf.Max(1, maxEntries);

            var modelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fpSet = new HashSet<string>(StringComparer.Ordinal);
            var layoutSet = new HashSet<string>(StringComparer.Ordinal);
            var animSet = new HashSet<string>(StringComparer.Ordinal);
            int cafHits = 0, cafMisses = 0, clipHits = 0, clipMisses = 0;
            int setHits = 0, setMisses = 0;
            int semanticClipHits = 0, semanticClipMisses = 0;
            int clipBuiltFromSemCache = 0, clipBuiltFromSemFresh = 0;
            int missingTracks = 0;

            for (int i = 0; i < snapshot.Length; i++)
            {
                var e = snapshot[i];
                modelSet.Add(e.ModelVirtualPath ?? string.Empty);
                fpSet.Add(e.AnimationFingerprint ?? "none");
                layoutSet.Add(e.PathLayoutHash ?? "none");
                animSet.Add(e.AnimationSetHash ?? "none");
                cafHits += e.CafHitDelta;
                cafMisses += e.CafMissDelta;
                clipHits += e.ClipHitDelta;
                clipMisses += e.ClipMissDelta;
                setHits += e.AnimationSetHitDelta;
                setMisses += e.AnimationSetMissDelta;
                semanticClipHits += e.SemanticClipHitDelta;
                semanticClipMisses += e.SemanticClipMissDelta;
                clipBuiltFromSemCache += e.ClipBuiltFromSemanticCache;
                clipBuiltFromSemFresh += e.ClipBuiltFromSemanticFresh;
                missingTracks += e.MissingControllerTrackCount;
            }

            var sb = new StringBuilder(2048);
            sb.Append("[CgfAnimDiag] entries=").Append(snapshot.Length)
                .Append(", uniqueModels=").Append(modelSet.Count)
                .Append(", uniqueAnimFp=").Append(fpSet.Count)
                .Append(", uniquePathLayout=").Append(layoutSet.Count)
                .Append(", uniqueAnimSet=").Append(animSet.Count)
                .Append(", cafHit/miss=").Append(cafHits).Append('/').Append(cafMisses)
                .Append(", clipHit/miss=").Append(clipHits).Append('/').Append(clipMisses)
                .Append(", setHit/miss=").Append(setHits).Append('/').Append(setMisses)
                .Append(", semClipHit/miss=").Append(semanticClipHits).Append('/').Append(semanticClipMisses)
                .Append(", clipBuild(semHit/semMiss)=").Append(clipBuiltFromSemCache).Append('/').Append(clipBuiltFromSemFresh)
                .Append(", missingTracks=").Append(missingTracks)
                .AppendLine();

            int start = Mathf.Max(0, snapshot.Length - maxEntries);
            for (int i = start; i < snapshot.Length; i++)
            {
                var e = snapshot[i];
                sb.Append("[CgfAnimDiag][Model] path='").Append(e.ModelVirtualPath).Append('\'')
                    .Append(", sources=").Append(e.SourceCount)
                    .Append(", existing=").Append(e.ExistingSourceCount)
                    .Append(", imported=").Append(e.ImportedClipCount)
                    .Append(", cafHit/miss=").Append(e.CafHitDelta).Append('/').Append(e.CafMissDelta)
                    .Append(", clipHit/miss=").Append(e.ClipHitDelta).Append('/').Append(e.ClipMissDelta)
                    .Append(", setHit/miss=").Append(e.AnimationSetHitDelta).Append('/').Append(e.AnimationSetMissDelta)
                    .Append(", semClipHit/miss=").Append(e.SemanticClipHitDelta).Append('/').Append(e.SemanticClipMissDelta)
                    .Append(", clipBuild(semHit/semMiss)=").Append(e.ClipBuiltFromSemanticCache).Append('/').Append(e.ClipBuiltFromSemanticFresh)
                    .Append(", missingTracks=").Append(e.MissingControllerTrackCount)
                    .Append(", animFp=").Append(e.AnimationFingerprint)
                    .Append(", layout=").Append(e.PathLayoutHash)
                    .Append(", set=").Append(e.AnimationSetHash)
                    .Append(", fpSource=").Append(e.UsedRigDefinitionFingerprint ? "rigDef" : "derived")
                    .AppendLine();
            }

            return sb.ToString();
        }

        internal static string FormatControllerIdSummary(Dictionary<uint, int> ids)
        {
            if (ids == null || ids.Count == 0)
                return "<none>";

            const int maxItems = 8;
            var parts = ids
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .Take(maxItems)
                .Select(kv => kv.Value > 1
                    ? $"0x{kv.Key:X8} ({kv.Value}x)"
                    : $"0x{kv.Key:X8}")
                .ToArray();

            string suffix = ids.Count > maxItems ? $", +{ids.Count - maxItems} more" : string.Empty;
            return string.Join(", ", parts) + suffix;
        }
    }
}
