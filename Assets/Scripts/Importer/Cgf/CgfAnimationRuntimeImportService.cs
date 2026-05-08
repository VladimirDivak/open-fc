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
            public readonly int ClipEntryCount;
            public readonly int CafHitCount;
            public readonly int CafMissCount;
            public readonly int ClipHitCount;
            public readonly int ClipMissCount;

            public RuntimeCacheStats(
                int cafEntryCount,
                int clipEntryCount,
                int cafHitCount,
                int cafMissCount,
                int clipHitCount,
                int clipMissCount)
            {
                CafEntryCount = cafEntryCount;
                ClipEntryCount = clipEntryCount;
                CafHitCount = cafHitCount;
                CafMissCount = cafMissCount;
                ClipHitCount = clipHitCount;
                ClipMissCount = clipMissCount;
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

        static readonly object CacheSync = new object();
        static readonly Dictionary<string, CachedCafEntry> CafByVirtualPath =
            new Dictionary<string, CachedCafEntry>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, CachedClipEntry> ClipByCacheKey =
            new Dictionary<string, CachedClipEntry>(StringComparer.Ordinal);

        static long s_cacheTick;
        static int s_cafHitCount;
        static int s_cafMissCount;
        static int s_clipHitCount;
        static int s_clipMissCount;

        const int MaxCachedCafEntries = 256;
        const int MaxCachedClipEntries = 512;

        public static RuntimeCacheStats GetRuntimeCacheStats()
        {
            lock (CacheSync)
            {
                return new RuntimeCacheStats(
                    cafEntryCount: CafByVirtualPath.Count,
                    clipEntryCount: ClipByCacheKey.Count,
                    cafHitCount: s_cafHitCount,
                    cafMissCount: s_cafMissCount,
                    clipHitCount: s_clipHitCount,
                    clipMissCount: s_clipMissCount);
            }
        }

        public static void ClearRuntimeCache()
        {
            lock (CacheSync)
            {
                CafByVirtualPath.Clear();
                foreach (var entry in ClipByCacheKey.Values)
                    if (entry?.Clip != null)
                        DestroyUnityObject(entry.Clip);
                ClipByCacheKey.Clear();
                s_cacheTick = 0;
                s_cafHitCount = 0;
                s_cafMissCount = 0;
                s_clipHitCount = 0;
                s_clipMissCount = 0;
            }
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

            var sources = CollectAnimationSources(modelVirtualPath);
            if (sources.Count == 0)
                return null;

            var imported = new List<CgfRuntimeAnimationClip>(sources.Count);
            var missingControllerIds = new Dictionary<uint, int>();
            int clipsWithMissingControllers = 0;
            int missingControllerTrackCount = 0;

            for (int i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                try
                {
                    if (!FcFileSystem.Exists(source.VirtualPath))
                        continue;

                    var caf = GetOrParseCaf(source.VirtualPath, out var cafContentHash);
                    string clipCacheKey = BuildClipCacheKey(
                        cafContentHash,
                        source.Alias,
                        importScale,
                        controllerMapKey);

                    if (!TryGetCachedClip(clipCacheKey, out var clip))
                    {
                        clip = BuildAnimationClip(
                            source.Alias,
                            caf,
                            controllerToPath,
                            importScale,
                            missingControllerIds,
                            ref missingControllerTrackCount,
                            ref clipsWithMissingControllers);

                        if (clip != null)
                            StoreCachedClip(clipCacheKey, clip);
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
                    Debug.LogWarning($"[CgfImporter] Failed to import animation '{source.VirtualPath}': {e.Message}");
                }
            }

            if (missingControllerTrackCount > 0)
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

            anim.playAutomatically = false;
            anim.clip = defaultClip ?? imported[0].Clip;

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
            Dictionary<uint, int> missingControllerIds,
            ref int missingControllerTrackCount,
            ref int clipsWithMissingControllers)
        {
            if (caf == null || caf.Tracks == null || caf.Tracks.Count == 0)
                return null;

            bool shouldLoop = ShouldTreatClipAsLoop(alias, caf, controllerToPath, importScale);
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

        static string BuildClipCacheKey(string cafContentHash, string alias, float importScale, string controllerMapKey)
        {
            return
                $"caf:{cafContentHash}|alias:{alias}|scale:{importScale:R}|map:{controllerMapKey}";
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
                    s_cafHitCount++;
                    cached.LastAccessTick = ++s_cacheTick;
                    contentHash = cached.ContentHash;
                    return cached.Caf;
                }
                s_cafMissCount++;
            }

            var parsed = CafParser.Parse(bytes);
            contentHash = ComputeCafSemanticHash(parsed);

            lock (CacheSync)
            {
                CafByVirtualPath[virtualPath] = new CachedCafEntry
                {
                    Caf = parsed,
                    SourceBytesHash = sourceBytesHash,
                    ContentHash = contentHash,
                    LastAccessTick = ++s_cacheTick
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
            while (CafByVirtualPath.Count > MaxCachedCafEntries)
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
