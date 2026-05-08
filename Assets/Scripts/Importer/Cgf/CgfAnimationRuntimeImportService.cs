using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenFarCry.FileSystem;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public sealed class CgfAnimationRuntimeImportService
    {
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

        public readonly struct RuntimeCacheStats
        {
            public readonly int CafEntryCount;
            public readonly int CafPathEntryCount;
            public readonly int ClipEntryCount;
            public readonly int CafHitCount;
            public readonly int CafMissCount;
            public readonly int CafPathHitCount;
            public readonly int CafPathMissCount;
            public readonly int CafSemanticHitCount;
            public readonly int CafSemanticMissCount;
            public readonly int ClipHitCount;
            public readonly int ClipMissCount;
            public readonly int AnimationSetEntryCount;
            public readonly int AnimationSetHitCount;
            public readonly int AnimationSetMissCount;
            public readonly int SemanticClipEntryCount;
            public readonly int SemanticClipHitCount;
            public readonly int SemanticClipMissCount;

            public RuntimeCacheStats(
                int cafEntryCount,
                int cafPathEntryCount,
                int clipEntryCount,
                int cafHitCount,
                int cafMissCount,
                int cafPathHitCount,
                int cafPathMissCount,
                int cafSemanticHitCount,
                int cafSemanticMissCount,
                int clipHitCount,
                int clipMissCount,
                int animationSetEntryCount,
                int animationSetHitCount,
                int animationSetMissCount,
                int semanticClipEntryCount,
                int semanticClipHitCount,
                int semanticClipMissCount)
            {
                CafEntryCount = cafEntryCount;
                CafPathEntryCount = cafPathEntryCount;
                ClipEntryCount = clipEntryCount;
                CafHitCount = cafHitCount;
                CafMissCount = cafMissCount;
                CafPathHitCount = cafPathHitCount;
                CafPathMissCount = cafPathMissCount;
                CafSemanticHitCount = cafSemanticHitCount;
                CafSemanticMissCount = cafSemanticMissCount;
                ClipHitCount = clipHitCount;
                ClipMissCount = clipMissCount;
                AnimationSetEntryCount = animationSetEntryCount;
                AnimationSetHitCount = animationSetHitCount;
                AnimationSetMissCount = animationSetMissCount;
                SemanticClipEntryCount = semanticClipEntryCount;
                SemanticClipHitCount = semanticClipHitCount;
                SemanticClipMissCount = semanticClipMissCount;
            }
        }

        sealed class CachedCafEntry
        {
            public CafFile Caf;
            public string SourceBytesHash;
            public string ContentHash;
            public long LastAccessTick;
        }

        sealed class CachedClipEntry
        {
            public AnimationClip Clip;
            public long LastAccessTick;
        }

        sealed class CachedSemanticCafEntry
        {
            public CafFile Caf;
            public long LastAccessTick;
        }

        static readonly object CacheSync = new object();
        static readonly Dictionary<string, CachedCafEntry> CafByVirtualPath =
            new Dictionary<string, CachedCafEntry>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, CachedSemanticCafEntry> CafByContentHash =
            new Dictionary<string, CachedSemanticCafEntry>(StringComparer.Ordinal);
        static readonly Dictionary<string, string> ContentHashBySourceBytesHash =
            new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly Dictionary<string, CachedClipEntry> ClipByCacheKey =
            new Dictionary<string, CachedClipEntry>(StringComparer.Ordinal);
        static readonly Dictionary<string, CachedAnimationSetEntry> AnimationSetByCacheKey =
            new Dictionary<string, CachedAnimationSetEntry>(StringComparer.Ordinal);
        static readonly Dictionary<string, string> AnimationSetKeyByModelLayout =
            new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly Dictionary<string, CachedSemanticClipEntry> SemanticClipByCacheKey =
            new Dictionary<string, CachedSemanticClipEntry>(StringComparer.Ordinal);
        static readonly List<RuntimeAttachDiagnosticsEntry> RuntimeAttachDiagnostics =
            new List<RuntimeAttachDiagnosticsEntry>(128);

        static long s_cacheTick;
        static int s_cafPathHitCount;
        static int s_cafPathMissCount;
        static int s_cafSemanticHitCount;
        static int s_cafSemanticMissCount;
        static int s_clipHitCount;
        static int s_clipMissCount;
        static int s_animationSetHitCount;
        static int s_animationSetMissCount;
        static int s_semanticClipHitCount;
        static int s_semanticClipMissCount;

        const int MaxCachedCafEntries = 256;
        const int MaxCachedCafPathEntries = 1024;
        // Mercenary animation sets can exceed 1k clips per run. Keep headroom to avoid LRU thrash.
        const int MaxCachedClipEntries = 4096;
        const int MaxCachedAnimationSetEntries = 256;
        const int MaxCachedAnimationSetModelLinks = 2048;
        const int MaxCachedSemanticClipEntries = 4096;
        const int MaxRuntimeAttachDiagnosticsEntries = 2048;
        const string ClipBuildVersion = "clip-v2";
        const string LoopPolicyVersion = "loop-v1";
        const string AnimationSetVersion = "animset-v1";
        const string SemanticClipVersion = "semclip-v1";

        public static bool EnableCompatibilityDiagnosticsLogging { get; set; }

        public static RuntimeCacheStats GetRuntimeCacheStats()
        {
            lock (CacheSync)
            {
                return new RuntimeCacheStats(
                    cafEntryCount: CafByContentHash.Count,
                    cafPathEntryCount: CafByVirtualPath.Count,
                    clipEntryCount: ClipByCacheKey.Count,
                    cafHitCount: s_cafPathHitCount + s_cafSemanticHitCount,
                    cafMissCount: s_cafSemanticMissCount,
                    cafPathHitCount: s_cafPathHitCount,
                    cafPathMissCount: s_cafPathMissCount,
                    cafSemanticHitCount: s_cafSemanticHitCount,
                    cafSemanticMissCount: s_cafSemanticMissCount,
                    clipHitCount: s_clipHitCount,
                    clipMissCount: s_clipMissCount,
                    animationSetEntryCount: AnimationSetByCacheKey.Count,
                    animationSetHitCount: s_animationSetHitCount,
                    animationSetMissCount: s_animationSetMissCount,
                    semanticClipEntryCount: SemanticClipByCacheKey.Count,
                    semanticClipHitCount: s_semanticClipHitCount,
                    semanticClipMissCount: s_semanticClipMissCount);
            }
        }

        public static void ClearRuntimeCache()
        {
            lock (CacheSync)
            {
                CafByVirtualPath.Clear();
                CafByContentHash.Clear();
                ContentHashBySourceBytesHash.Clear();
                foreach (var entry in ClipByCacheKey.Values)
                    if (entry?.Clip != null)
                        DestroyUnityObject(entry.Clip);
                ClipByCacheKey.Clear();
                AnimationSetByCacheKey.Clear();
                AnimationSetKeyByModelLayout.Clear();
                SemanticClipByCacheKey.Clear();
                s_cacheTick = 0;
                s_cafPathHitCount = 0;
                s_cafPathMissCount = 0;
                s_cafSemanticHitCount = 0;
                s_cafSemanticMissCount = 0;
                s_clipHitCount = 0;
                s_clipMissCount = 0;
                s_animationSetHitCount = 0;
                s_animationSetMissCount = 0;
                s_semanticClipHitCount = 0;
                s_semanticClipMissCount = 0;
                RuntimeAttachDiagnostics.Clear();
            }
        }

        public static void ClearRuntimeAttachDiagnostics()
        {
            lock (CacheSync)
                RuntimeAttachDiagnostics.Clear();
        }

        public static string BuildRuntimeAttachDiagnosticsReport(int maxEntries = 32)
        {
            RuntimeAttachDiagnosticsEntry[] snapshot;
            lock (CacheSync)
                snapshot = RuntimeAttachDiagnostics.ToArray();

            if (snapshot.Length == 0)
                return "[CgfAnimDiag] No animation attach diagnostics captured.";

            maxEntries = Mathf.Max(1, maxEntries);

            var modelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fpSet = new HashSet<string>(StringComparer.Ordinal);
            var layoutSet = new HashSet<string>(StringComparer.Ordinal);
            var animSet = new HashSet<string>(StringComparer.Ordinal);
            int cafHits = 0;
            int cafMisses = 0;
            int clipHits = 0;
            int clipMisses = 0;
            int setHits = 0;
            int setMisses = 0;
            int semanticClipHits = 0;
            int semanticClipMisses = 0;
            int clipBuiltFromSemCache = 0;
            int clipBuiltFromSemFresh = 0;
            int missingTracks = 0;

            for (int i = 0; i < snapshot.Length; i++)
            {
                var entry = snapshot[i];
                modelSet.Add(entry.ModelVirtualPath ?? string.Empty);
                fpSet.Add(entry.AnimationFingerprint ?? "none");
                layoutSet.Add(entry.PathLayoutHash ?? "none");
                animSet.Add(entry.AnimationSetHash ?? "none");
                cafHits += entry.CafHitDelta;
                cafMisses += entry.CafMissDelta;
                clipHits += entry.ClipHitDelta;
                clipMisses += entry.ClipMissDelta;
                setHits += entry.AnimationSetHitDelta;
                setMisses += entry.AnimationSetMissDelta;
                semanticClipHits += entry.SemanticClipHitDelta;
                semanticClipMisses += entry.SemanticClipMissDelta;
                clipBuiltFromSemCache += entry.ClipBuiltFromSemanticCache;
                clipBuiltFromSemFresh += entry.ClipBuiltFromSemanticFresh;
                missingTracks += entry.MissingControllerTrackCount;
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
                var entry = snapshot[i];
                sb.Append("[CgfAnimDiag][Model] path='").Append(entry.ModelVirtualPath).Append('\'')
                    .Append(", sources=").Append(entry.SourceCount)
                    .Append(", existing=").Append(entry.ExistingSourceCount)
                    .Append(", imported=").Append(entry.ImportedClipCount)
                    .Append(", cafHit/miss=").Append(entry.CafHitDelta).Append('/').Append(entry.CafMissDelta)
                    .Append(", clipHit/miss=").Append(entry.ClipHitDelta).Append('/').Append(entry.ClipMissDelta)
                    .Append(", setHit/miss=").Append(entry.AnimationSetHitDelta).Append('/').Append(entry.AnimationSetMissDelta)
                    .Append(", semClipHit/miss=").Append(entry.SemanticClipHitDelta).Append('/').Append(entry.SemanticClipMissDelta)
                    .Append(", clipBuild(semHit/semMiss)=").Append(entry.ClipBuiltFromSemanticCache).Append('/').Append(entry.ClipBuiltFromSemanticFresh)
                    .Append(", missingTracks=").Append(entry.MissingControllerTrackCount)
                    .Append(", animFp=").Append(entry.AnimationFingerprint)
                    .Append(", layout=").Append(entry.PathLayoutHash)
                    .Append(", set=").Append(entry.AnimationSetHash)
                    .Append(", fpSource=").Append(entry.UsedRigDefinitionFingerprint ? "rigDef" : "derived")
                    .AppendLine();
            }

            return sb.ToString();
        }

        public readonly struct AnimSourceEntry
        {
            public readonly string Alias;
            public readonly string VirtualPath;

            public AnimSourceEntry(string alias, string virtualPath)
            {
                Alias = alias;
                VirtualPath = virtualPath;
            }
        }

        public List<CgfRuntimeAnimationClip> TryAttachAnimations(
            GameObject go,
            CgfFile parsedFile,
            CgfRigDefinition rigDefinition,
            string modelVirtualPath,
            float importScale,
            out string warningMessage)
        {
            warningMessage = null;

            if (go == null)
                return null;

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr == null || smr.bones == null || smr.bones.Length == 0)
                return null;

            bool hasRigSnapshotControllers = rigDefinition != null &&
                rigDefinition.IsValid &&
                rigDefinition.ControllerIdsByBoneIndex != null &&
                rigDefinition.ControllerIdsByBoneIndex.Length > 0;
            bool hasParsedBoneAnim = parsedFile?.BoneNames?.Names != null &&
                parsedFile.BoneNames.Names.Length > 0 &&
                parsedFile.BoneAnim?.Bones != null &&
                parsedFile.BoneAnim.Bones.Length > 0;
            if (!hasRigSnapshotControllers && !hasParsedBoneAnim)
                return null;

            var controllerToPath = BuildControllerPathMap(go.transform, smr.bones, parsedFile, rigDefinition);
            if (controllerToPath.Count == 0)
                return null;
            string controllerMapKey = BuildControllerMapKey(controllerToPath);
            string pathLayoutHash = BuildPathLayoutHash(controllerToPath);
            string animationFingerprint = BuildAnimationFingerprintForDiagnostics(controllerToPath, rigDefinition, out bool usedRigDefinitionFingerprint);

            var sources = CollectAnimationSources(modelVirtualPath);
            if (sources.Count == 0)
                return null;

            var imported = new List<CgfRuntimeAnimationClip>(sources.Count);
            var missingControllerIds = new Dictionary<uint, int>();
            var animationSetComponents = new List<KeyValuePair<string, string>>(sources.Count);
            string modelLayoutKey = BuildAnimationSetModelLayoutKey(
                modelVirtualPath,
                animationFingerprint,
                pathLayoutHash,
                importScale);
            string animationSetHash = "none";
            int clipsWithMissingControllers = 0;
            int missingControllerTrackCount = 0;
            int existingSourceCount = 0;
            int clipBuiltFromSemanticCache = 0;
            int clipBuiltFromSemanticFresh = 0;
            bool usedAnimationSetCache = false;
            GetCacheCounters(
                out int cafHitBefore,
                out int cafMissBefore,
                out int clipHitBefore,
                out int clipMissBefore,
                out int setHitBefore,
                out int setMissBefore,
                out int semClipHitBefore,
                out int semClipMissBefore);

            if (TryGetAnimationSetKeyForModelLayout(modelLayoutKey, out string cachedAnimationSetKey) &&
                TryGetCachedAnimationSet(cachedAnimationSetKey, out var cachedAnimationSet))
            {
                usedAnimationSetCache = true;
                imported = CloneCachedAnimationSet(cachedAnimationSet);
                existingSourceCount = cachedAnimationSet.ExistingSourceCount;
                missingControllerTrackCount = cachedAnimationSet.MissingControllerTrackCount;
                animationSetHash = ExtractAnimationSetHashFromCacheKey(cachedAnimationSetKey);
            }
            else
            {
                for (int i = 0; i < sources.Count; i++)
                {
                    var source = sources[i];
                    try
                    {
                        if (!FcFileSystem.Exists(source.VirtualPath))
                        {
                            animationSetComponents.Add(new KeyValuePair<string, string>(source.Alias, "<missing>"));
                            continue;
                        }

                        existingSourceCount++;

                        var caf = GetOrParseCaf(source.VirtualPath, out var cafContentHash);
                        animationSetComponents.Add(new KeyValuePair<string, string>(source.Alias, cafContentHash));
                        bool shouldLoop = ShouldTreatClipAsLoop(source.Alias, caf, controllerToPath, importScale);
                        string loopPolicyKey = BuildLoopPolicyKey(source.Alias, shouldLoop);
                        string compatibilityKey = BuildCompatibilityCacheKey(animationFingerprint, controllerMapKey);
                        string semanticClipCacheKey = BuildSemanticClipCacheKey(
                            cafContentHash,
                            source.Alias,
                            importScale,
                            loopPolicyKey);
                        string clipCacheKey = BuildClipCacheKey(
                            cafContentHash,
                            source.Alias,
                            importScale,
                            compatibilityKey,
                            pathLayoutHash,
                            loopPolicyKey);

                        if (!TryGetCachedClip(clipCacheKey, out var clip))
                        {
                            bool usedSemanticCacheForBuild = TryGetCachedSemanticClip(semanticClipCacheKey, out var semanticClipData);
                            if (!usedSemanticCacheForBuild)
                            {
                                semanticClipData = BuildSemanticClipData(
                                    source.Alias,
                                    caf,
                                    importScale,
                                    shouldLoop);
                                if (semanticClipData != null)
                                    StoreCachedSemanticClip(semanticClipCacheKey, semanticClipData);
                            }

                            clip = BuildAnimationClipFromSemanticData(
                                source.Alias,
                                semanticClipData,
                                controllerToPath,
                                missingControllerIds,
                                ref missingControllerTrackCount,
                                ref clipsWithMissingControllers);

                            if (clip != null)
                            {
                                if (usedSemanticCacheForBuild)
                                    clipBuiltFromSemanticCache++;
                                else
                                    clipBuiltFromSemanticFresh++;
                                StoreCachedClip(clipCacheKey, clip);
                            }
                        }

                        if (clip == null)
                            continue;

                        imported.Add(new CgfRuntimeAnimationClip
                        {
                            Alias = source.Alias,
                            SourceVirtualPath = source.VirtualPath,
                            Clip = clip
                        });
                    }
                    catch (Exception e)
                    {
                        animationSetComponents.Add(new KeyValuePair<string, string>(source.Alias, "<error>"));
                        Debug.LogWarning($"[CgfImporter] Failed to import animation '{source.VirtualPath}': {e.Message}");
                    }
                }

                animationSetHash = BuildAnimationSetHash(animationSetComponents);
                string animationSetCacheKey = BuildAnimationSetCacheKey(
                    animationFingerprint,
                    pathLayoutHash,
                    animationSetHash,
                    importScale);
                StoreCachedAnimationSet(
                    animationSetCacheKey,
                    imported,
                    existingSourceCount,
                    missingControllerTrackCount);
                StoreAnimationSetKeyForModelLayout(modelLayoutKey, animationSetCacheKey);
            }

            GetCacheCounters(
                out int cafHitAfter,
                out int cafMissAfter,
                out int clipHitAfter,
                out int clipMissAfter,
                out int setHitAfter,
                out int setMissAfter,
                out int semClipHitAfter,
                out int semClipMissAfter);
            int cafHitDelta = Mathf.Max(0, cafHitAfter - cafHitBefore);
            int cafMissDelta = Mathf.Max(0, cafMissAfter - cafMissBefore);
            int clipHitDelta = Mathf.Max(0, clipHitAfter - clipHitBefore);
            int clipMissDelta = Mathf.Max(0, clipMissAfter - clipMissBefore);
            int setHitDelta = Mathf.Max(0, setHitAfter - setHitBefore);
            int setMissDelta = Mathf.Max(0, setMissAfter - setMissBefore);
            int semClipHitDelta = Mathf.Max(0, semClipHitAfter - semClipHitBefore);
            int semClipMissDelta = Mathf.Max(0, semClipMissAfter - semClipMissBefore);
            var diagnostics = new RuntimeAttachDiagnosticsEntry(
                modelVirtualPath: modelVirtualPath,
                animationFingerprint: animationFingerprint,
                pathLayoutHash: pathLayoutHash,
                animationSetHash: animationSetHash,
                sourceCount: sources.Count,
                existingSourceCount: existingSourceCount,
                importedClipCount: imported.Count,
                cafHitDelta: cafHitDelta,
                cafMissDelta: cafMissDelta,
                clipHitDelta: clipHitDelta,
                clipMissDelta: clipMissDelta,
                animationSetHitDelta: setHitDelta,
                animationSetMissDelta: setMissDelta,
                semanticClipHitDelta: semClipHitDelta,
                semanticClipMissDelta: semClipMissDelta,
                clipBuiltFromSemanticCache: clipBuiltFromSemanticCache,
                clipBuiltFromSemanticFresh: clipBuiltFromSemanticFresh,
                missingControllerTrackCount: missingControllerTrackCount,
                usedRigDefinitionFingerprint: usedRigDefinitionFingerprint);
            StoreRuntimeAttachDiagnostics(diagnostics);

            if (EnableCompatibilityDiagnosticsLogging)
            {
                Debug.Log(
                    $"[CgfAnimDiag] model='{modelVirtualPath}', sources={sources.Count}, existing={existingSourceCount}, imported={imported.Count}, " +
                    $"cafHit/miss={cafHitDelta}/{cafMissDelta}, clipHit/miss={clipHitDelta}/{clipMissDelta}, " +
                    $"setHit/miss={setHitDelta}/{setMissDelta}, usedSetCache={(usedAnimationSetCache ? 1 : 0)}, " +
                    $"semClipHit/miss={semClipHitDelta}/{semClipMissDelta}, " +
                    $"clipBuild(semHit/semMiss)={clipBuiltFromSemanticCache}/{clipBuiltFromSemanticFresh}, " +
                    $"missingTracks={missingControllerTrackCount}, animFp={animationFingerprint}, layout={pathLayoutHash}, set={animationSetHash}, " +
                    $"fpSource={(usedRigDefinitionFingerprint ? "rigDef" : "derived")}.");
            }

            if (!usedAnimationSetCache && missingControllerTrackCount > 0)
            {
                warningMessage =
                    $"[CgfImporter] Animation import skipped {missingControllerTrackCount} controller track(s) " +
                    $"across {clipsWithMissingControllers} clip(s) because they were not mapped to skeleton bones. " +
                    $"Unique controller ids: {FormatControllerIdSummary(missingControllerIds)}.";
            }

            if (imported.Count == 0)
                return null;

            var anim = go.GetComponent<Animation>();
            if (anim == null)
                anim = go.AddComponent<Animation>();

            AnimationClip defaultClip = null;
            for (int i = 0; i < imported.Count; i++)
            {
                var item = imported[i];
                if (anim.GetClip(item.Alias) != null)
                    anim.RemoveClip(item.Alias);
                anim.AddClip(item.Clip, item.Alias);

                if (defaultClip == null ||
                    string.Equals(item.Alias, "default", StringComparison.OrdinalIgnoreCase))
                    defaultClip = item.Clip;
            }

            anim.clip = defaultClip ?? imported[0].Clip;
            anim.playAutomatically = false;

            return imported;
        }

        static Dictionary<uint, string> BuildControllerPathMap(
            Transform root,
            Transform[] bones,
            CgfFile parsedFile,
            CgfRigDefinition rigDefinition)
        {
            var pathByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < bones.Length; i++)
            {
                var bone = bones[i];
                if (bone == null || pathByName.ContainsKey(bone.name))
                    continue;
                pathByName[bone.name] = GetTransformPath(root, bone);
            }

            var map = new Dictionary<uint, string>();
            if (rigDefinition != null &&
                rigDefinition.IsValid &&
                rigDefinition.ControllerIdsByBoneIndex != null &&
                rigDefinition.BoneNames != null &&
                rigDefinition.BoneNames.Length == rigDefinition.ControllerIdsByBoneIndex.Length)
            {
                for (int i = 0; i < rigDefinition.BoneNames.Length; i++)
                {
                    uint controllerId = rigDefinition.ControllerIdsByBoneIndex[i];
                    if (controllerId == 0)
                        continue;
                    string boneName = rigDefinition.BoneNames[i];
                    if (string.IsNullOrEmpty(boneName))
                        continue;
                    if (!pathByName.TryGetValue(boneName, out var path))
                        continue;
                    map[controllerId] = path;
                }

                if (map.Count > 0)
                    return map;
            }

            var boneNames = parsedFile?.BoneNames?.Names;
            var entities = parsedFile?.BoneAnim?.Bones;
            if (boneNames == null || entities == null)
                return map;

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (entity.BoneID < 0 || entity.BoneID >= boneNames.Length)
                    continue;

                string boneName = boneNames[entity.BoneID];
                if (string.IsNullOrEmpty(boneName))
                    continue;
                if (!pathByName.TryGetValue(boneName, out var path))
                    continue;

                map[entity.ControllerID] = path;
            }

            return map;
        }

        static string GetTransformPath(Transform root, Transform target)
        {
            if (target == null || target == root)
                return string.Empty;

            var segments = new List<string>(8);
            var current = target;
            while (current != null && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        static AnimationClip BuildAnimationClip(
            string alias,
            CafFile caf,
            Dictionary<uint, string> controllerToPath,
            float importScale,
            bool shouldLoop,
            Dictionary<uint, int> missingControllerIds,
            ref int missingControllerTrackCount,
            ref int clipsWithMissingControllers)
        {
            if (caf == null || caf.Tracks == null || caf.Tracks.Count == 0)
                return null;

            var clip = new AnimationClip
            {
                name = alias,
                legacy = true,
                wrapMode = shouldLoop ? WrapMode.Loop : WrapMode.Once
            };

            int baseTick = caf.GlobalStartTick;
            int curveCount = 0;
            int missingControllers = 0;
            for (int i = 0; i < caf.Tracks.Count; i++)
            {
                var track = caf.Tracks[i];
                if (track == null || track.Ticks == null || track.Ticks.Length == 0)
                    continue;
                if (!controllerToPath.TryGetValue(track.ControllerID, out var path))
                {
                    missingControllers++;
                    missingControllerTrackCount++;
                    if (!missingControllerIds.ContainsKey(track.ControllerID))
                        missingControllerIds[track.ControllerID] = 0;
                    missingControllerIds[track.ControllerID]++;
                    continue;
                }

                AddPositionCurves(clip, path, track, caf.SecsPerTick, baseTick, importScale);
                AddRotationCurves(clip, path, track, caf.SecsPerTick, baseTick);
                curveCount++;
            }

            if (curveCount == 0)
                return null;

            if (missingControllers > 0)
                clipsWithMissingControllers++;

            clip.EnsureQuaternionContinuity();
            clip.wrapMode = shouldLoop ? WrapMode.Loop : WrapMode.Once;
            return clip;
        }

        static SemanticClipData BuildSemanticClipData(
            string alias,
            CafFile caf,
            float importScale,
            bool shouldLoop)
        {
            if (caf == null || caf.Tracks == null || caf.Tracks.Count == 0)
                return null;

            int baseTick = caf.GlobalStartTick;
            var semanticTracks = new List<SemanticClipTrack>(caf.Tracks.Count);
            for (int i = 0; i < caf.Tracks.Count; i++)
            {
                var track = caf.Tracks[i];
                if (track == null || track.Ticks == null || track.Ticks.Length == 0)
                    continue;

                int keyCount = 0;
                if (track.Ticks != null && track.Positions != null && track.Rotations != null)
                    keyCount = Mathf.Min(track.Ticks.Length, track.Positions.Length, track.Rotations.Length);
                if (keyCount <= 0)
                    continue;

                var times = new float[keyCount];
                var positions = new Vector3[keyCount];
                var rotations = new Quaternion[keyCount];
                for (int k = 0; k < keyCount; k++)
                {
                    float t = Mathf.Max(0f, (track.Ticks[k] - baseTick) * caf.SecsPerTick);
                    times[k] = t;
                    positions[k] = CryTransformConversion.PositionInImporterSpace(track.Positions[k], importScale);
                    rotations[k] = CryTransformConversion.LocalRotationInImporterSpace(track.Rotations[k]);
                }

                semanticTracks.Add(new SemanticClipTrack
                {
                    ControllerID = track.ControllerID,
                    Times = times,
                    Positions = positions,
                    Rotations = rotations
                });
            }

            if (semanticTracks.Count == 0)
                return null;

            return new SemanticClipData
            {
                Alias = alias,
                ShouldLoop = shouldLoop,
                Tracks = semanticTracks.ToArray()
            };
        }

        static AnimationClip BuildAnimationClipFromSemanticData(
            string alias,
            SemanticClipData semanticClipData,
            Dictionary<uint, string> controllerToPath,
            Dictionary<uint, int> missingControllerIds,
            ref int missingControllerTrackCount,
            ref int clipsWithMissingControllers)
        {
            if (semanticClipData?.Tracks == null || semanticClipData.Tracks.Length == 0)
                return null;

            var clip = new AnimationClip
            {
                name = alias,
                legacy = true,
                wrapMode = semanticClipData.ShouldLoop ? WrapMode.Loop : WrapMode.Once
            };

            int curveCount = 0;
            int missingControllers = 0;
            for (int i = 0; i < semanticClipData.Tracks.Length; i++)
            {
                var track = semanticClipData.Tracks[i];
                if (track == null || track.Times == null || track.Times.Length == 0)
                    continue;

                if (!controllerToPath.TryGetValue(track.ControllerID, out var path))
                {
                    missingControllers++;
                    missingControllerTrackCount++;
                    if (!missingControllerIds.ContainsKey(track.ControllerID))
                        missingControllerIds[track.ControllerID] = 0;
                    missingControllerIds[track.ControllerID]++;
                    continue;
                }

                AddPositionCurves(clip, path, track.Times, track.Positions);
                AddRotationCurves(clip, path, track.Times, track.Rotations);
                curveCount++;
            }

            if (curveCount == 0)
                return null;

            if (missingControllers > 0)
                clipsWithMissingControllers++;

            clip.EnsureQuaternionContinuity();
            clip.wrapMode = semanticClipData.ShouldLoop ? WrapMode.Loop : WrapMode.Once;
            return clip;
        }

        static bool ShouldTreatClipAsLoop(
            string alias,
            CafFile caf,
            Dictionary<uint, string> controllerToPath,
            float importScale)
        {
            if (!string.IsNullOrEmpty(alias))
            {
                if (alias.IndexOf("loop", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (HasOneShotAliasHint(alias))
                    return false;
            }

            if (caf?.Tracks == null || caf.Tracks.Count == 0)
                return false;

            int considered = 0;
            int loopLike = 0;
            for (int i = 0; i < caf.Tracks.Count; i++)
            {
                var track = caf.Tracks[i];
                if (track?.Ticks == null || track.Positions == null || track.Rotations == null)
                    continue;
                if (track.Ticks.Length < 2 || track.Positions.Length < 2 || track.Rotations.Length < 2)
                    continue;

                if (controllerToPath != null && controllerToPath.Count > 0)
                {
                    if (!controllerToPath.TryGetValue(track.ControllerID, out var path))
                        continue;
                    if (string.IsNullOrEmpty(path) || path.Equals("Bip01", StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                considered++;
                if (IsTrackLoopLike(track, importScale))
                    loopLike++;
            }

            if (considered == 0)
                return HasIdleLikeAliasHint(alias);

            float ratio = (float)loopLike / considered;
            if (HasIdleLikeAliasHint(alias) && ratio >= 0.4f)
                return true;

            if (considered < 3)
                return loopLike == considered;

            return ratio >= 0.72f;
        }

        static bool IsTrackLoopLike(CafControllerTrack track, float importScale)
        {
            int last = Mathf.Min(track.Ticks.Length, track.Positions.Length, track.Rotations.Length) - 1;
            if (last <= 0)
                return false;

            Vector3 p0 = track.Positions[0];
            Vector3 p1 = track.Positions[last];
            float posDelta = (p1 - p0).magnitude * importScale;

            Vector3 min = p0;
            Vector3 max = p0;
            for (int i = 1; i <= last; i++)
            {
                Vector3 p = track.Positions[i];
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            float posRange = (max - min).magnitude * importScale;
            float posTolerance = posRange < 0.001f
                ? 0.004f
                : Mathf.Max(0.008f, posRange * 0.2f);
            bool posLoopLike = posDelta <= posTolerance;

            Quaternion r0 = track.Rotations[0];
            Quaternion r1 = track.Rotations[last];
            if (Quaternion.Dot(r0, r1) < 0f)
                r1 = new Quaternion(-r1.x, -r1.y, -r1.z, -r1.w);
            float rotDelta = Quaternion.Angle(r0, r1);

            float rotRange = 0f;
            for (int i = 1; i <= last; i++)
                rotRange = Mathf.Max(rotRange, Quaternion.Angle(r0, track.Rotations[i]));

            float rotTolerance = rotRange < 4f
                ? 4f
                : Mathf.Max(7f, rotRange * 0.25f);
            bool rotLoopLike = rotDelta <= rotTolerance;

            return posLoopLike && rotLoopLike;
        }

        static bool HasIdleLikeAliasHint(string alias)
        {
            if (string.IsNullOrEmpty(alias))
                return false;

            return alias.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   alias.IndexOf("walk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   alias.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   alias.IndexOf("sidle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   alias.IndexOf("rotate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   alias.IndexOf("swim", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool HasOneShotAliasHint(string alias)
        {
            if (string.IsNullOrEmpty(alias))
                return false;

            string lower = alias.ToLowerInvariant();
            if (lower.Contains("jump") ||
                lower.Contains("reload") ||
                lower.Contains("pain") ||
                lower.Contains("death") ||
                lower.Contains("grenade") ||
                lower.Contains("throw"))
            {
                return true;
            }

            var tokens = lower.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string t = tokens[i];
                if (t == "hit" || t == "in" || t == "out" || t == "start" || t == "end")
                    return true;
            }

            return false;
        }

        static string FormatControllerIdSummary(Dictionary<uint, int> ids)
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

        static void AddPositionCurves(AnimationClip clip, string path, CafControllerTrack track, float secsPerTick, int baseTick, float importScale)
        {
            var cx = new AnimationCurve();
            var cy = new AnimationCurve();
            var cz = new AnimationCurve();

            for (int i = 0; i < track.Ticks.Length; i++)
            {
                float t = Mathf.Max(0f, (track.Ticks[i] - baseTick) * secsPerTick);
                var p = CryTransformConversion.PositionInImporterSpace(track.Positions[i], importScale);
                cx.AddKey(new Keyframe(t, p.x));
                cy.AddKey(new Keyframe(t, p.y));
                cz.AddKey(new Keyframe(t, p.z));
            }

            clip.SetCurve(path, typeof(Transform), "localPosition.x", cx);
            clip.SetCurve(path, typeof(Transform), "localPosition.y", cy);
            clip.SetCurve(path, typeof(Transform), "localPosition.z", cz);
        }

        static void AddPositionCurves(AnimationClip clip, string path, float[] times, Vector3[] positions)
        {
            if (times == null || positions == null)
                return;

            int count = Mathf.Min(times.Length, positions.Length);
            if (count <= 0)
                return;

            var cx = new AnimationCurve();
            var cy = new AnimationCurve();
            var cz = new AnimationCurve();
            for (int i = 0; i < count; i++)
            {
                float t = Mathf.Max(0f, times[i]);
                var p = positions[i];
                cx.AddKey(new Keyframe(t, p.x));
                cy.AddKey(new Keyframe(t, p.y));
                cz.AddKey(new Keyframe(t, p.z));
            }

            clip.SetCurve(path, typeof(Transform), "localPosition.x", cx);
            clip.SetCurve(path, typeof(Transform), "localPosition.y", cy);
            clip.SetCurve(path, typeof(Transform), "localPosition.z", cz);
        }

        static void AddRotationCurves(AnimationClip clip, string path, CafControllerTrack track, float secsPerTick, int baseTick)
        {
            var cx = new AnimationCurve();
            var cy = new AnimationCurve();
            var cz = new AnimationCurve();
            var cw = new AnimationCurve();

            for (int i = 0; i < track.Ticks.Length; i++)
            {
                float t = Mathf.Max(0f, (track.Ticks[i] - baseTick) * secsPerTick);
                var q = CryTransformConversion.LocalRotationInImporterSpace(track.Rotations[i]);
                cx.AddKey(new Keyframe(t, q.x));
                cy.AddKey(new Keyframe(t, q.y));
                cz.AddKey(new Keyframe(t, q.z));
                cw.AddKey(new Keyframe(t, q.w));
            }

            clip.SetCurve(path, typeof(Transform), "localRotation.x", cx);
            clip.SetCurve(path, typeof(Transform), "localRotation.y", cy);
            clip.SetCurve(path, typeof(Transform), "localRotation.z", cz);
            clip.SetCurve(path, typeof(Transform), "localRotation.w", cw);
        }

        static void AddRotationCurves(AnimationClip clip, string path, float[] times, Quaternion[] rotations)
        {
            if (times == null || rotations == null)
                return;

            int count = Mathf.Min(times.Length, rotations.Length);
            if (count <= 0)
                return;

            var cx = new AnimationCurve();
            var cy = new AnimationCurve();
            var cz = new AnimationCurve();
            var cw = new AnimationCurve();
            for (int i = 0; i < count; i++)
            {
                float t = Mathf.Max(0f, times[i]);
                var q = rotations[i];
                cx.AddKey(new Keyframe(t, q.x));
                cy.AddKey(new Keyframe(t, q.y));
                cz.AddKey(new Keyframe(t, q.z));
                cw.AddKey(new Keyframe(t, q.w));
            }

            clip.SetCurve(path, typeof(Transform), "localRotation.x", cx);
            clip.SetCurve(path, typeof(Transform), "localRotation.y", cy);
            clip.SetCurve(path, typeof(Transform), "localRotation.z", cz);
            clip.SetCurve(path, typeof(Transform), "localRotation.w", cw);
        }

        static List<AnimSourceEntry> CollectAnimationSources(string modelVirtualPath)
        {
            var result = new List<AnimSourceEntry>();
            if (string.IsNullOrEmpty(modelVirtualPath))
                return result;

            string modelNoExt = RemoveExtension(modelVirtualPath);
            string modelDir = GetVirtualDirectory(modelNoExt);
            string calPath = modelNoExt + ".cal";

            if (FcFileSystem.Exists(calPath))
            {
                try
                {
                    byte[] calBytes = FcFileSystem.ReadAllBytes(calPath);
                    string calText = System.Text.Encoding.UTF8.GetString(calBytes);
                    ParseCalEntries(calText, modelDir, result);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CgfImporter] Failed to parse CAL '{calPath}': {e.Message}");
                }
            }

            if (result.Count > 0)
                return DeduplicateAnimationSources(result);

            string baseNamePrefix = Path.GetFileName(modelNoExt).ToLowerInvariant() + "_";
            string directory = modelDir;
            foreach (var path in FcFileSystem.GetEntries(directory))
            {
                if (!path.EndsWith(".caf", StringComparison.OrdinalIgnoreCase))
                    continue;

                string fileName = Path.GetFileName(path).ToLowerInvariant();
                if (!fileName.StartsWith(baseNamePrefix, StringComparison.Ordinal))
                    continue;

                string file = Path.GetFileNameWithoutExtension(path);
                string alias = file.Length > baseNamePrefix.Length
                    ? file.Substring(baseNamePrefix.Length)
                    : file;
                result.Add(new AnimSourceEntry(alias, path.Replace('\\', '/')));
            }

            result.Sort((a, b) => string.Compare(a.Alias, b.Alias, StringComparison.OrdinalIgnoreCase));
            return DeduplicateAnimationSources(result);
        }

        static List<AnimSourceEntry> DeduplicateAnimationSources(List<AnimSourceEntry> input)
        {
            var unique = new List<AnimSourceEntry>(input.Count);
            var usedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < input.Count; i++)
            {
                var entry = input[i];
                if (string.IsNullOrWhiteSpace(entry.Alias) || string.IsNullOrWhiteSpace(entry.VirtualPath))
                    continue;
                if (usedAliases.Contains(entry.Alias))
                    continue;

                usedAliases.Add(entry.Alias);
                unique.Add(entry);
            }

            return unique;
        }

        static void ParseCalEntries(string calText, string modelDir, List<AnimSourceEntry> output)
        {
            if (string.IsNullOrEmpty(calText))
                return;

            string animDir = GetDefaultAnimDirectory(modelDir);
            var lines = calText.Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i]?.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0 || eq >= line.Length - 1)
                    continue;

                string left = line.Substring(0, eq).Trim();
                string right = line.Substring(eq + 1).Trim();
                if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
                    continue;

                if (right[0] == '?')
                    continue;

                right = right.Replace('\\', '/').TrimStart('/', '\\');

                if (left.StartsWith("$", StringComparison.Ordinal))
                {
                    string directive = left.Substring(1);
                    if (directive.Equals("AnimationDir", StringComparison.OrdinalIgnoreCase) ||
                        directive.Equals("AnimDir", StringComparison.OrdinalIgnoreCase) ||
                        directive.Equals("AnimationDirectory", StringComparison.OrdinalIgnoreCase) ||
                        directive.Equals("AnimDirectory", StringComparison.OrdinalIgnoreCase))
                    {
                        animDir = $"{modelDir}/{right}".Replace('\\', '/').TrimEnd('/');
                    }

                    continue;
                }

                string resolved = $"{animDir}/{right}".Replace('\\', '/');
                output.Add(new AnimSourceEntry(left, resolved));
            }
        }

        static string GetVirtualDirectory(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath))
                return string.Empty;

            int slash = virtualPath.LastIndexOf('/');
            if (slash <= 0)
                return string.Empty;
            return virtualPath.Substring(0, slash);
        }

        static string GetDefaultAnimDirectory(string modelDir)
        {
            string dir = modelDir.Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(dir))
                return "animations";

            int cut = dir.LastIndexOf('/');
            if (cut < 0)
                return "animations";
            string oneUp = dir.Substring(0, cut);
            cut = oneUp.LastIndexOf('/');
            if (cut < 0)
                return "animations";
            string twoUp = oneUp.Substring(0, cut);
            return $"{twoUp}/animations";
        }

        static string RemoveExtension(string virtualPath)
        {
            int ext = virtualPath.LastIndexOf('.');
            return ext > 0 ? virtualPath.Substring(0, ext) : virtualPath;
        }

        static string BuildControllerMapKey(Dictionary<uint, string> controllerToPath)
        {
            if (controllerToPath == null || controllerToPath.Count == 0)
                return "<empty>";

            var ordered = controllerToPath
                .OrderBy(kv => kv.Key)
                .ToArray();

            var sb = new StringBuilder(ordered.Length * 24);
            for (int i = 0; i < ordered.Length; i++)
            {
                sb.Append(ordered[i].Key);
                sb.Append('=');
                sb.Append(ordered[i].Value ?? string.Empty);
                sb.Append(';');
            }

            return sb.ToString();
        }

        static string BuildAnimationFingerprintForDiagnostics(
            Dictionary<uint, string> controllerToPath,
            CgfRigDefinition rigDefinition,
            out bool usedRigDefinitionFingerprint)
        {
            if (rigDefinition != null &&
                rigDefinition.IsValid &&
                !string.IsNullOrEmpty(rigDefinition.AnimationFingerprint))
            {
                usedRigDefinitionFingerprint = true;
                return rigDefinition.AnimationFingerprint;
            }

            usedRigDefinitionFingerprint = false;
            if (controllerToPath == null || controllerToPath.Count == 0)
                return "none";

            var ordered = controllerToPath
                .OrderBy(kv => kv.Key)
                .ToArray();

            var sb = new StringBuilder(ordered.Length * 32);
            sb.Append("anim-diag-v1|");
            for (int i = 0; i < ordered.Length; i++)
            {
                sb.Append(ordered[i].Key.ToString("X8"));
                sb.Append('=');
                string path = ordered[i].Value ?? string.Empty;
                string leaf = path;
                int slash = path.LastIndexOf('/');
                if (slash >= 0 && slash + 1 < path.Length)
                    leaf = path.Substring(slash + 1);
                sb.Append(leaf.ToLowerInvariant());
                sb.Append(';');
            }

            return Hash128.Compute(sb.ToString()).ToString();
        }

        static string BuildPathLayoutHash(Dictionary<uint, string> controllerToPath)
        {
            if (controllerToPath == null || controllerToPath.Count == 0)
                return "none";

            var ordered = controllerToPath
                .OrderBy(kv => kv.Key)
                .ToArray();

            var sb = new StringBuilder(ordered.Length * 48);
            sb.Append("layout-v1|");
            for (int i = 0; i < ordered.Length; i++)
            {
                sb.Append(ordered[i].Key.ToString("X8"));
                sb.Append('=');
                sb.Append((ordered[i].Value ?? string.Empty).ToLowerInvariant());
                sb.Append(';');
            }

            return Hash128.Compute(sb.ToString()).ToString();
        }

        static string BuildAnimationSetHash(List<KeyValuePair<string, string>> animationSetComponents)
        {
            if (animationSetComponents == null || animationSetComponents.Count == 0)
                return "none";

            var ordered = animationSetComponents
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(kv => kv.Value, StringComparer.Ordinal)
                .ToArray();
            if (ordered.Length == 0)
                return "none";

            var sb = new StringBuilder(ordered.Length * 40);
            sb.Append("set-v1|");
            for (int i = 0; i < ordered.Length; i++)
            {
                sb.Append(ordered[i].Key.ToLowerInvariant());
                sb.Append('=');
                sb.Append(ordered[i].Value ?? string.Empty);
                sb.Append(';');
            }

            return Hash128.Compute(sb.ToString()).ToString();
        }

        internal static string BuildCompatibilityCacheKey(string animationFingerprint, string controllerMapKey)
        {
            if (!string.IsNullOrWhiteSpace(animationFingerprint) &&
                !string.Equals(animationFingerprint, "none", StringComparison.OrdinalIgnoreCase))
            {
                return "fp:" + animationFingerprint;
            }

            return "map:" + (controllerMapKey ?? "<empty>");
        }

        internal static string BuildLoopPolicyKey(string alias, bool shouldLoop)
        {
            string aliasKey = string.IsNullOrWhiteSpace(alias) ? "<empty>" : alias.ToLowerInvariant();
            return $"{LoopPolicyVersion}|alias:{aliasKey}|loop:{(shouldLoop ? 1 : 0)}";
        }

        internal static string BuildClipCacheKey(
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

        internal static string BuildAnimationSetModelLayoutKey(
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

        internal static string BuildAnimationSetCacheKey(
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

        internal static string BuildSemanticClipCacheKey(
            string cafContentHash,
            string alias,
            float importScale,
            string loopPolicyKey)
        {
            string aliasKey = string.IsNullOrWhiteSpace(alias) ? "<empty>" : alias.ToLowerInvariant();
            string loopKey = string.IsNullOrWhiteSpace(loopPolicyKey) ? "<none>" : loopPolicyKey;
            return $"v:{SemanticClipVersion}|caf:{cafContentHash}|alias:{aliasKey}|scale:{importScale:R}|loop:{loopKey}";
        }

        static string ExtractAnimationSetHashFromCacheKey(string cacheKey)
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

        static bool TryGetCachedSemanticClip(string key, out SemanticClipData data)
        {
            data = null;
            if (string.IsNullOrEmpty(key))
                return false;

            lock (CacheSync)
            {
                if (!SemanticClipByCacheKey.TryGetValue(key, out var cached) || cached?.Data == null)
                {
                    s_semanticClipMissCount++;
                    return false;
                }

                s_semanticClipHitCount++;
                cached.LastAccessTick = ++s_cacheTick;
                data = cached.Data;
                return true;
            }
        }

        static void StoreCachedSemanticClip(string key, SemanticClipData data)
        {
            if (string.IsNullOrEmpty(key) || data == null)
                return;

            lock (CacheSync)
            {
                SemanticClipByCacheKey[key] = new CachedSemanticClipEntry
                {
                    Data = data,
                    LastAccessTick = ++s_cacheTick
                };
                EvictOldestSemanticClipEntriesUnsafe();
            }
        }

        static CafFile GetOrParseCaf(string virtualPath, out string contentHash)
        {
            byte[] bytes = FcFileSystem.ReadAllBytes(virtualPath);
            string sourceBytesHash = ComputeBytesHash(bytes);

            lock (CacheSync)
            {
                if (CafByVirtualPath.TryGetValue(virtualPath, out var cached) &&
                    string.Equals(cached.SourceBytesHash, sourceBytesHash, StringComparison.Ordinal))
                {
                    s_cafPathHitCount++;
                    cached.LastAccessTick = ++s_cacheTick;
                    contentHash = cached.ContentHash;
                    return cached.Caf;
                }
                s_cafPathMissCount++;

                if (ContentHashBySourceBytesHash.TryGetValue(sourceBytesHash, out var knownContentHash) &&
                    !string.IsNullOrEmpty(knownContentHash) &&
                    CafByContentHash.TryGetValue(knownContentHash, out var cachedByContent) &&
                    cachedByContent?.Caf != null)
                {
                    s_cafSemanticHitCount++;
                    cachedByContent.LastAccessTick = ++s_cacheTick;
                    contentHash = knownContentHash;
                    CafByVirtualPath[virtualPath] = new CachedCafEntry
                    {
                        Caf = cachedByContent.Caf,
                        SourceBytesHash = sourceBytesHash,
                        ContentHash = contentHash,
                        LastAccessTick = s_cacheTick
                    };
                    EvictOldestCafEntriesUnsafe();
                    return cachedByContent.Caf;
                }
            }

            var parsed = CafParser.Parse(bytes);
            contentHash = ComputeCafSemanticHash(parsed);

            lock (CacheSync)
            {
                if (CafByContentHash.TryGetValue(contentHash, out var existingByContent) &&
                    existingByContent?.Caf != null)
                {
                    s_cafSemanticHitCount++;
                    existingByContent.LastAccessTick = ++s_cacheTick;
                    ContentHashBySourceBytesHash[sourceBytesHash] = contentHash;
                    CafByVirtualPath[virtualPath] = new CachedCafEntry
                    {
                        Caf = existingByContent.Caf,
                        SourceBytesHash = sourceBytesHash,
                        ContentHash = contentHash,
                        LastAccessTick = s_cacheTick
                    };
                    EvictOldestCafEntriesUnsafe();
                    return existingByContent.Caf;
                }

                s_cafSemanticMissCount++;
                CafByContentHash[contentHash] = new CachedSemanticCafEntry
                {
                    Caf = parsed,
                    LastAccessTick = ++s_cacheTick
                };
                ContentHashBySourceBytesHash[sourceBytesHash] = contentHash;
                CafByVirtualPath[virtualPath] = new CachedCafEntry
                {
                    Caf = parsed,
                    SourceBytesHash = sourceBytesHash,
                    ContentHash = contentHash,
                    LastAccessTick = s_cacheTick
                };
                EvictOldestCafEntriesUnsafe();
            }

            return parsed;
        }

        static bool TryGetCachedClip(string key, out AnimationClip clip)
        {
            clip = null;
            lock (CacheSync)
            {
                if (!ClipByCacheKey.TryGetValue(key, out var cached))
                {
                    s_clipMissCount++;
                    return false;
                }

                s_clipHitCount++;
                cached.LastAccessTick = ++s_cacheTick;
                clip = cached.Clip;
                return clip != null;
            }
        }

        static void GetCacheCounters(
            out int cafHit,
            out int cafMiss,
            out int clipHit,
            out int clipMiss,
            out int setHit,
            out int setMiss,
            out int semClipHit,
            out int semClipMiss)
        {
            lock (CacheSync)
            {
                cafHit = s_cafPathHitCount + s_cafSemanticHitCount;
                cafMiss = s_cafSemanticMissCount;
                clipHit = s_clipHitCount;
                clipMiss = s_clipMissCount;
                setHit = s_animationSetHitCount;
                setMiss = s_animationSetMissCount;
                semClipHit = s_semanticClipHitCount;
                semClipMiss = s_semanticClipMissCount;
            }
        }

        static bool TryGetAnimationSetKeyForModelLayout(string modelLayoutKey, out string animationSetCacheKey)
        {
            animationSetCacheKey = null;
            if (string.IsNullOrEmpty(modelLayoutKey))
                return false;

            lock (CacheSync)
                return AnimationSetKeyByModelLayout.TryGetValue(modelLayoutKey, out animationSetCacheKey);
        }

        static void StoreAnimationSetKeyForModelLayout(string modelLayoutKey, string animationSetCacheKey)
        {
            if (string.IsNullOrEmpty(modelLayoutKey) || string.IsNullOrEmpty(animationSetCacheKey))
                return;

            lock (CacheSync)
            {
                AnimationSetKeyByModelLayout[modelLayoutKey] = animationSetCacheKey;
                while (AnimationSetKeyByModelLayout.Count > MaxCachedAnimationSetModelLinks)
                {
                    string keyToRemove = null;
                    foreach (var kv in AnimationSetKeyByModelLayout)
                    {
                        keyToRemove = kv.Key;
                        break;
                    }

                    if (keyToRemove == null)
                        break;
                    AnimationSetKeyByModelLayout.Remove(keyToRemove);
                }
            }
        }

        static bool TryGetCachedAnimationSet(string key, out CachedAnimationSetEntry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(key))
                return false;

            lock (CacheSync)
            {
                if (!AnimationSetByCacheKey.TryGetValue(key, out var cached))
                {
                    s_animationSetMissCount++;
                    return false;
                }

                if (cached?.Clips == null || cached.Clips.Length == 0 || HasInvalidClipReference(cached.Clips))
                {
                    AnimationSetByCacheKey.Remove(key);
                    s_animationSetMissCount++;
                    return false;
                }

                s_animationSetHitCount++;
                cached.LastAccessTick = ++s_cacheTick;
                entry = cached;
                return true;
            }
        }

        static void StoreCachedAnimationSet(
            string key,
            List<CgfRuntimeAnimationClip> clips,
            int existingSourceCount,
            int missingControllerTrackCount)
        {
            if (string.IsNullOrEmpty(key) || clips == null || clips.Count == 0)
                return;

            var snapshot = new CgfRuntimeAnimationClip[clips.Count];
            for (int i = 0; i < clips.Count; i++)
            {
                var src = clips[i];
                if (src == null || src.Clip == null)
                    continue;

                snapshot[i] = new CgfRuntimeAnimationClip
                {
                    Alias = src.Alias,
                    SourceVirtualPath = src.SourceVirtualPath,
                    Clip = src.Clip
                };
            }

            lock (CacheSync)
            {
                AnimationSetByCacheKey[key] = new CachedAnimationSetEntry
                {
                    Clips = snapshot,
                    ExistingSourceCount = existingSourceCount,
                    MissingControllerTrackCount = missingControllerTrackCount,
                    LastAccessTick = ++s_cacheTick
                };
                EvictOldestAnimationSetEntriesUnsafe();
            }
        }

        static List<CgfRuntimeAnimationClip> CloneCachedAnimationSet(CachedAnimationSetEntry entry)
        {
            var list = new List<CgfRuntimeAnimationClip>(entry?.Clips?.Length ?? 0);
            if (entry?.Clips == null)
                return list;

            for (int i = 0; i < entry.Clips.Length; i++)
            {
                var src = entry.Clips[i];
                if (src?.Clip == null)
                    continue;

                list.Add(new CgfRuntimeAnimationClip
                {
                    Alias = src.Alias,
                    SourceVirtualPath = src.SourceVirtualPath,
                    Clip = src.Clip
                });
            }

            return list;
        }

        static bool HasInvalidClipReference(CgfRuntimeAnimationClip[] clips)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == null || clips[i].Clip == null)
                    return true;
            }

            return false;
        }

        static void StoreRuntimeAttachDiagnostics(RuntimeAttachDiagnosticsEntry entry)
        {
            lock (CacheSync)
            {
                RuntimeAttachDiagnostics.Add(entry);
                int overflow = RuntimeAttachDiagnostics.Count - MaxRuntimeAttachDiagnosticsEntries;
                if (overflow > 0)
                    RuntimeAttachDiagnostics.RemoveRange(0, overflow);
            }
        }

        static void StoreCachedClip(string key, AnimationClip clip)
        {
            if (clip == null)
                return;

            lock (CacheSync)
            {
                ClipByCacheKey[key] = new CachedClipEntry
                {
                    Clip = clip,
                    LastAccessTick = ++s_cacheTick
                };
                EvictOldestClipEntriesUnsafe();
            }
        }

        static void EvictOldestCafEntriesUnsafe()
        {
            while (CafByVirtualPath.Count > MaxCachedCafPathEntries)
            {
                string oldestKey = null;
                long oldestTick = long.MaxValue;
                foreach (var kv in CafByVirtualPath)
                {
                    if (kv.Value.LastAccessTick < oldestTick)
                    {
                        oldestTick = kv.Value.LastAccessTick;
                        oldestKey = kv.Key;
                    }
                }

                if (oldestKey == null)
                    break;

                CafByVirtualPath.Remove(oldestKey);
            }

            while (CafByContentHash.Count > MaxCachedCafEntries)
            {
                string oldestKey = null;
                long oldestTick = long.MaxValue;
                foreach (var kv in CafByContentHash)
                {
                    if (kv.Value.LastAccessTick < oldestTick)
                    {
                        oldestTick = kv.Value.LastAccessTick;
                        oldestKey = kv.Key;
                    }
                }

                if (oldestKey == null)
                    break;

                CafByContentHash.Remove(oldestKey);
            }
        }

        static void EvictOldestAnimationSetEntriesUnsafe()
        {
            while (AnimationSetByCacheKey.Count > MaxCachedAnimationSetEntries)
            {
                string oldestKey = null;
                long oldestTick = long.MaxValue;
                foreach (var kv in AnimationSetByCacheKey)
                {
                    if (kv.Value.LastAccessTick < oldestTick)
                    {
                        oldestTick = kv.Value.LastAccessTick;
                        oldestKey = kv.Key;
                    }
                }

                if (oldestKey == null)
                    break;

                AnimationSetByCacheKey.Remove(oldestKey);
            }
        }

        static void EvictOldestSemanticClipEntriesUnsafe()
        {
            while (SemanticClipByCacheKey.Count > MaxCachedSemanticClipEntries)
            {
                string oldestKey = null;
                long oldestTick = long.MaxValue;
                foreach (var kv in SemanticClipByCacheKey)
                {
                    if (kv.Value.LastAccessTick < oldestTick)
                    {
                        oldestTick = kv.Value.LastAccessTick;
                        oldestKey = kv.Key;
                    }
                }

                if (oldestKey == null)
                    break;

                SemanticClipByCacheKey.Remove(oldestKey);
            }
        }

        static void EvictOldestClipEntriesUnsafe()
        {
            while (ClipByCacheKey.Count > MaxCachedClipEntries)
            {
                string oldestKey = null;
                long oldestTick = long.MaxValue;
                foreach (var kv in ClipByCacheKey)
                {
                    if (kv.Value.LastAccessTick < oldestTick)
                    {
                        oldestTick = kv.Value.LastAccessTick;
                        oldestKey = kv.Key;
                    }
                }

                if (oldestKey == null)
                    break;

                if (ClipByCacheKey.TryGetValue(oldestKey, out var entry) && entry?.Clip != null)
                    DestroyUnityObject(entry.Clip);
                ClipByCacheKey.Remove(oldestKey);
            }
        }

        static string ComputeCafSemanticHash(CafFile caf)
        {
            if (caf == null)
                return "0";

            // FNV-1a 64-bit over normalized CAF content. This lets us reuse clips
            // even when byte-level CAF packaging differs but track data is the same.
            unchecked
            {
                const ulong fnvOffset = 14695981039346656037UL;
                ulong hash = fnvOffset;

                HashInt(ref hash, Quantize(caf.SecsPerTick, 1000000f));
                HashInt(ref hash, caf.GlobalStartTick);
                HashInt(ref hash, caf.GlobalEndTick);
                int trackCount = caf.Tracks != null ? caf.Tracks.Count : 0;
                HashInt(ref hash, trackCount);

                if (trackCount <= 0)
                {
                    return hash.ToString("X16");
                }

                var orderedTracks = caf.Tracks
                    .Where(t => t != null)
                    .OrderBy(t => t.ControllerID)
                    .ToArray();
                HashInt(ref hash, orderedTracks.Length);

                for (int ti = 0; ti < orderedTracks.Length; ti++)
                {
                    var track = orderedTracks[ti];
                    HashInt(ref hash, unchecked((int)track.ControllerID));

                    int keyCount = 0;
                    if (track.Ticks != null && track.Positions != null && track.Rotations != null)
                        keyCount = Mathf.Min(track.Ticks.Length, track.Positions.Length, track.Rotations.Length);
                    HashInt(ref hash, keyCount);

                    for (int k = 0; k < keyCount; k++)
                    {
                        HashInt(ref hash, track.Ticks[k]);

                        var p = track.Positions[k];
                        HashInt(ref hash, Quantize(p.x, 100000f));
                        HashInt(ref hash, Quantize(p.y, 100000f));
                        HashInt(ref hash, Quantize(p.z, 100000f));

                        var r = track.Rotations[k];
                        HashInt(ref hash, Quantize(r.x, 100000f));
                        HashInt(ref hash, Quantize(r.y, 100000f));
                        HashInt(ref hash, Quantize(r.z, 100000f));
                        HashInt(ref hash, Quantize(r.w, 100000f));
                    }
                }

                return hash.ToString("X16");

                static int Quantize(float value, float scale)
                {
                    return Mathf.RoundToInt(value * scale);
                }

                static void HashInt(ref ulong currentHash, int value)
                {
                    const ulong localPrime = 1099511628211UL;
                    for (int b = 0; b < 4; b++)
                    {
                        byte part = (byte)((value >> (8 * b)) & 0xFF);
                        currentHash ^= part;
                        currentHash *= localPrime;
                    }
                }
            }
        }

        static string ComputeBytesHash(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "0";

            unchecked
            {
                const ulong fnvOffset = 14695981039346656037UL;
                const ulong fnvPrime = 1099511628211UL;
                ulong hash = fnvOffset;
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= fnvPrime;
                }
                return hash.ToString("X16");
            }
        }

        static void DestroyUnityObject(UnityEngine.Object obj)
        {
            if (obj == null)
                return;

            if (Application.isPlaying)
                UnityEngine.Object.Destroy(obj);
            else
                UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
