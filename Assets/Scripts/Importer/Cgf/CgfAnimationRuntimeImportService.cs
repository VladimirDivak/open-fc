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
        public readonly struct RuntimeCacheStats
        {
            public readonly int CafEntryCount;
            public readonly int CafPathEntryCount;
            public readonly int CafSourceHashEntryCount;
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
            public readonly int AnimationSetModelLinkCount;
            public readonly int AnimationSetHitCount;
            public readonly int AnimationSetMissCount;
            public readonly int SemanticClipEntryCount;
            public readonly int SemanticClipHitCount;
            public readonly int SemanticClipMissCount;

            public RuntimeCacheStats(
                int cafEntryCount,
                int cafPathEntryCount,
                int cafSourceHashEntryCount,
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
                int animationSetModelLinkCount,
                int animationSetHitCount,
                int animationSetMissCount,
                int semanticClipEntryCount,
                int semanticClipHitCount,
                int semanticClipMissCount)
            {
                CafEntryCount = cafEntryCount;
                CafPathEntryCount = cafPathEntryCount;
                CafSourceHashEntryCount = cafSourceHashEntryCount;
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
                AnimationSetModelLinkCount = animationSetModelLinkCount;
                AnimationSetHitCount = animationSetHitCount;
                AnimationSetMissCount = animationSetMissCount;
                SemanticClipEntryCount = semanticClipEntryCount;
                SemanticClipHitCount = semanticClipHitCount;
                SemanticClipMissCount = semanticClipMissCount;
            }
        }

        public static RuntimeCacheStats GetRuntimeCacheStats()
        {
            var caf = CafLoader.GetStats();
            var cache = CgfAnimationSetCache.GetStats();
            return new RuntimeCacheStats(
                cafEntryCount: caf.SemanticEntryCount,
                cafPathEntryCount: caf.PathEntryCount,
                cafSourceHashEntryCount: caf.SourceHashEntryCount,
                clipEntryCount: cache.ClipEntryCount,
                cafHitCount: caf.TotalHitCount,
                cafMissCount: caf.SemanticMissCount,
                cafPathHitCount: caf.PathHitCount,
                cafPathMissCount: caf.PathMissCount,
                cafSemanticHitCount: caf.SemanticHitCount,
                cafSemanticMissCount: caf.SemanticMissCount,
                clipHitCount: cache.ClipHitCount,
                clipMissCount: cache.ClipMissCount,
                animationSetEntryCount: cache.AnimationSetEntryCount,
                animationSetModelLinkCount: cache.AnimationSetModelLinkCount,
                animationSetHitCount: cache.AnimationSetHitCount,
                animationSetMissCount: cache.AnimationSetMissCount,
                semanticClipEntryCount: cache.SemanticClipEntryCount,
                semanticClipHitCount: cache.SemanticClipHitCount,
                semanticClipMissCount: cache.SemanticClipMissCount);
        }

        public static void ClearRuntimeCache()
        {
            CafLoader.Clear();
            CgfAnimationSetCache.Clear();
            CgfAnimationDiagnostics.Clear();
        }

        public static void ClearRuntimeAttachDiagnostics() =>
            CgfAnimationDiagnostics.Clear();

        public static string BuildRuntimeAttachDiagnosticsReport(int maxEntries = 32) =>
            CgfAnimationDiagnostics.BuildReport(maxEntries);

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

            if (!HasAnimationData(parsedFile, rigDefinition))
                return null;

            var controllerToPath = BuildControllerPathMap(go.transform, smr.bones, parsedFile, rigDefinition);
            if (controllerToPath.Count == 0)
                return null;

            string controllerMapKey = BuildControllerMapKey(controllerToPath);
            string pathLayoutHash = BuildPathLayoutHash(controllerToPath);
            string animationFingerprint = BuildAnimationFingerprintForDiagnostics(
                controllerToPath, rigDefinition, out bool usedRigDefinitionFingerprint);

            var sources = CollectAnimationSources(modelVirtualPath);
            if (sources.Count == 0)
                return null;

            string modelLayoutKey = CgfCacheKeys.BuildAnimationSetModelLayoutKey(
                modelVirtualPath, animationFingerprint, pathLayoutHash, importScale);

            // The diagnostics scope snapshots cache stats now and turns them into
            // hit/miss deltas in Record() — keeping that bookkeeping out of here.
            var diagnostics = new CgfAnimAttachDiagnosticsScope();
            var set = BuildOrFetchAnimationSet(
                sources, controllerToPath, controllerMapKey,
                pathLayoutHash, animationFingerprint, modelLayoutKey, importScale);
            diagnostics.Record(
                modelVirtualPath, animationFingerprint, pathLayoutHash,
                sources.Count, usedRigDefinitionFingerprint, set);

            if (!set.UsedAnimationSetCache && set.MissingControllerTrackCount > 0)
            {
                warningMessage =
                    $"[CgfImporter] Animation import skipped {set.MissingControllerTrackCount} controller track(s) " +
                    $"across {set.ClipsWithMissingControllers} clip(s) because they were not mapped to skeleton bones. " +
                    $"Unique controller ids: {CgfAnimationDiagnostics.FormatControllerIdSummary(set.MissingControllerIds)}.";
            }

            if (set.Imported.Count == 0)
                return null;

            ApplyAnimationsToGameObject(go, set.Imported);
            return set.Imported;
        }

        static bool HasAnimationData(CgfFile parsedFile, CgfRigDefinition rigDefinition)
        {
            bool hasRigSnapshotControllers = rigDefinition != null &&
                rigDefinition.IsValid &&
                rigDefinition.ControllerIdsByBoneIndex != null &&
                rigDefinition.ControllerIdsByBoneIndex.Length > 0;
            bool hasParsedBoneAnim = parsedFile?.BoneNames?.Names != null &&
                parsedFile.BoneNames.Names.Length > 0 &&
                parsedFile.BoneAnim?.Bones != null &&
                parsedFile.BoneAnim.Bones.Length > 0;
            return hasRigSnapshotControllers || hasParsedBoneAnim;
        }

        // Result of resolving the full animation set for one model — the imported
        // clips plus the counters the diagnostics scope reports.
        sealed class AnimationSetResult
        {
            public List<CgfRuntimeAnimationClip> Imported;
            public Dictionary<uint, int> MissingControllerIds;
            public int ExistingSourceCount;
            public int MissingControllerTrackCount;
            public int ClipsWithMissingControllers;
            public int ClipBuiltFromSemanticCache;
            public int ClipBuiltFromSemanticFresh;
            public string AnimationSetHash;
            public bool UsedAnimationSetCache;
        }

        // Returns the cached animation set for this model layout, or builds it
        // from the CAF sources and caches it.
        static AnimationSetResult BuildOrFetchAnimationSet(
            List<AnimSourceEntry> sources,
            Dictionary<uint, string> controllerToPath,
            string controllerMapKey,
            string pathLayoutHash,
            string animationFingerprint,
            string modelLayoutKey,
            float importScale)
        {
            var result = new AnimationSetResult
            {
                Imported = new List<CgfRuntimeAnimationClip>(sources.Count),
                MissingControllerIds = new Dictionary<uint, int>(),
                AnimationSetHash = "none",
            };

            bool hasCachedSetKeyForLayout =
                CgfAnimationSetCache.TryGetAnimationSetKeyForModelLayout(modelLayoutKey, out string cachedAnimationSetKey);
            if (hasCachedSetKeyForLayout &&
                CgfAnimationSetCache.TryGetCachedAnimationSet(cachedAnimationSetKey, out var cachedAnimationSet))
            {
                result.UsedAnimationSetCache = true;
                result.Imported = CgfAnimationSetCache.CloneCachedAnimationSet(cachedAnimationSet);
                result.ExistingSourceCount = cachedAnimationSet.ExistingSourceCount;
                result.MissingControllerTrackCount = cachedAnimationSet.MissingControllerTrackCount;
                result.AnimationSetHash = CgfCacheKeys.ExtractAnimationSetHashFromCacheKey(cachedAnimationSetKey);
                return result;
            }

            if (hasCachedSetKeyForLayout)
                CgfAnimationSetCache.InvalidateAnimationSetKeyForModelLayout(modelLayoutKey, cachedAnimationSetKey);

            var animationSetComponents = new List<KeyValuePair<string, string>>(sources.Count);
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

                    result.ExistingSourceCount++;

                    var caf = CafLoader.GetOrParse(source.VirtualPath, out var cafContentHash);
                    animationSetComponents.Add(new KeyValuePair<string, string>(source.Alias, cafContentHash));
                    bool shouldLoop = CgfClipBuilder.ShouldTreatClipAsLoop(source.Alias, caf, controllerToPath, importScale);
                    string loopPolicyKey = CgfCacheKeys.BuildLoopPolicyKey(source.Alias, shouldLoop);
                    string compatibilityKey = CgfCacheKeys.BuildCompatibilityCacheKey(animationFingerprint, controllerMapKey);
                    string semanticClipCacheKey = CgfCacheKeys.BuildSemanticClipCacheKey(
                        cafContentHash,
                        source.Alias,
                        importScale,
                        loopPolicyKey);
                    string clipCacheKey = CgfCacheKeys.BuildClipCacheKey(
                        cafContentHash,
                        source.Alias,
                        importScale,
                        compatibilityKey,
                        pathLayoutHash,
                        loopPolicyKey);

                    if (!CgfAnimationSetCache.TryGetCachedClip(clipCacheKey, out var clip))
                    {
                        bool usedSemanticCacheForBuild = CgfAnimationSetCache.TryGetCachedSemanticClip(semanticClipCacheKey, out var semanticClipData);
                        if (!usedSemanticCacheForBuild)
                        {
                            semanticClipData = CgfClipBuilder.BuildSemanticClipData(
                                source.Alias,
                                caf,
                                importScale,
                                shouldLoop);
                            if (semanticClipData != null)
                                CgfAnimationSetCache.StoreCachedSemanticClip(semanticClipCacheKey, semanticClipData);
                        }

                        int missingTrackCount = result.MissingControllerTrackCount;
                        int clipsWithMissing = result.ClipsWithMissingControllers;
                        clip = CgfClipBuilder.BuildFromSemanticData(
                            source.Alias,
                            semanticClipData,
                            controllerToPath,
                            result.MissingControllerIds,
                            ref missingTrackCount,
                            ref clipsWithMissing);
                        result.MissingControllerTrackCount = missingTrackCount;
                        result.ClipsWithMissingControllers = clipsWithMissing;

                        if (clip != null)
                        {
                            if (usedSemanticCacheForBuild)
                                result.ClipBuiltFromSemanticCache++;
                            else
                                result.ClipBuiltFromSemanticFresh++;
                            CgfAnimationSetCache.StoreCachedClip(clipCacheKey, clip);
                        }
                    }

                    if (clip == null)
                        continue;

                    result.Imported.Add(new CgfRuntimeAnimationClip
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

            result.AnimationSetHash = BuildAnimationSetHash(animationSetComponents);
            string animationSetCacheKey = CgfCacheKeys.BuildAnimationSetCacheKey(
                animationFingerprint,
                pathLayoutHash,
                result.AnimationSetHash,
                importScale);
            CgfAnimationSetCache.StoreCachedAnimationSet(
                animationSetCacheKey,
                result.Imported,
                result.ExistingSourceCount,
                result.MissingControllerTrackCount);
            CgfAnimationSetCache.StoreAnimationSetKeyForModelLayout(modelLayoutKey, animationSetCacheKey);
            return result;
        }

        static void ApplyAnimationsToGameObject(GameObject go, List<CgfRuntimeAnimationClip> imported)
        {
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
            anim.playAutomatically = true;

            // playAutomatically only auto-plays at the Animation component's first
            // enable. This component is added at runtime to an already-active object
            // and 'clip' is assigned afterwards, so play it explicitly.
            if (anim.clip != null)
                anim.Play();
        }

        // Brackets one TryAttachAnimations call: snapshots CAF/clip/set cache
        // stats on construction and, in Record(), converts them to hit/miss
        // deltas and stores the diagnostics entry.
        sealed class CgfAnimAttachDiagnosticsScope
        {
            readonly int _cafHit;
            readonly int _cafMiss;
            readonly int _clipHit;
            readonly int _clipMiss;
            readonly int _setHit;
            readonly int _setMiss;
            readonly int _semClipHit;
            readonly int _semClipMiss;

            public CgfAnimAttachDiagnosticsScope()
            {
                var caf = CafLoader.GetStats();
                var cache = CgfAnimationSetCache.GetStats();
                _cafHit = caf.TotalHitCount;
                _cafMiss = caf.SemanticMissCount;
                _clipHit = cache.ClipHitCount;
                _clipMiss = cache.ClipMissCount;
                _setHit = cache.AnimationSetHitCount;
                _setMiss = cache.AnimationSetMissCount;
                _semClipHit = cache.SemanticClipHitCount;
                _semClipMiss = cache.SemanticClipMissCount;
            }

            public void Record(
                string modelVirtualPath,
                string animationFingerprint,
                string pathLayoutHash,
                int sourceCount,
                bool usedRigDefinitionFingerprint,
                AnimationSetResult set)
            {
                var caf = CafLoader.GetStats();
                var cache = CgfAnimationSetCache.GetStats();
                int cafHitDelta = Mathf.Max(0, caf.TotalHitCount - _cafHit);
                int cafMissDelta = Mathf.Max(0, caf.SemanticMissCount - _cafMiss);
                int clipHitDelta = Mathf.Max(0, cache.ClipHitCount - _clipHit);
                int clipMissDelta = Mathf.Max(0, cache.ClipMissCount - _clipMiss);
                int setHitDelta = Mathf.Max(0, cache.AnimationSetHitCount - _setHit);
                int setMissDelta = Mathf.Max(0, cache.AnimationSetMissCount - _setMiss);
                int semClipHitDelta = Mathf.Max(0, cache.SemanticClipHitCount - _semClipHit);
                int semClipMissDelta = Mathf.Max(0, cache.SemanticClipMissCount - _semClipMiss);

                var diagnostics = new RuntimeAttachDiagnosticsEntry(
                    modelVirtualPath: modelVirtualPath,
                    animationFingerprint: animationFingerprint,
                    pathLayoutHash: pathLayoutHash,
                    animationSetHash: set.AnimationSetHash,
                    sourceCount: sourceCount,
                    existingSourceCount: set.ExistingSourceCount,
                    importedClipCount: set.Imported.Count,
                    cafHitDelta: cafHitDelta,
                    cafMissDelta: cafMissDelta,
                    clipHitDelta: clipHitDelta,
                    clipMissDelta: clipMissDelta,
                    animationSetHitDelta: setHitDelta,
                    animationSetMissDelta: setMissDelta,
                    semanticClipHitDelta: semClipHitDelta,
                    semanticClipMissDelta: semClipMissDelta,
                    clipBuiltFromSemanticCache: set.ClipBuiltFromSemanticCache,
                    clipBuiltFromSemanticFresh: set.ClipBuiltFromSemanticFresh,
                    missingControllerTrackCount: set.MissingControllerTrackCount,
                    usedRigDefinitionFingerprint: usedRigDefinitionFingerprint);
                CgfAnimationDiagnostics.Store(diagnostics);

                if (CgfAnimationDiagnostics.EnableCompatibilityDiagnosticsLogging)
                {
                    Debug.Log(
                        $"[CgfAnimDiag] model='{modelVirtualPath}', sources={sourceCount}, existing={set.ExistingSourceCount}, imported={set.Imported.Count}, " +
                        $"cafHit/miss={cafHitDelta}/{cafMissDelta}, clipHit/miss={clipHitDelta}/{clipMissDelta}, " +
                        $"setHit/miss={setHitDelta}/{setMissDelta}, usedSetCache={(set.UsedAnimationSetCache ? 1 : 0)}, " +
                        $"semClipHit/miss={semClipHitDelta}/{semClipMissDelta}, " +
                        $"clipBuild(semHit/semMiss)={set.ClipBuiltFromSemanticCache}/{set.ClipBuiltFromSemanticFresh}, " +
                        $"missingTracks={set.MissingControllerTrackCount}, animFp={animationFingerprint}, layout={pathLayoutHash}, set={set.AnimationSetHash}, " +
                        $"fpSource={(usedRigDefinitionFingerprint ? "rigDef" : "derived")}.");
                }
            }
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
                    var calEntries = CalParser.Parse(calText, modelDir);
                    for (int i = 0; i < calEntries.Count; i++)
                    {
                        var entry = calEntries[i];
                        result.Add(new AnimSourceEntry(entry.Alias, entry.VirtualPath));
                    }
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

        static string GetVirtualDirectory(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath))
                return string.Empty;

            int slash = virtualPath.LastIndexOf('/');
            if (slash <= 0)
                return string.Empty;
            return virtualPath.Substring(0, slash);
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

    }
}
