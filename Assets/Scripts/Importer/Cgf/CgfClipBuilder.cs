using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    // Converts CafFile track data into Unity AnimationClip objects.
    // Stateless — no cache state, no Unity object ownership.
    internal static class CgfClipBuilder
    {
        // Converts CafFile tracks to normalized, scale-applied semantic clip data.
        // Result can be stored in semantic clip cache and reused across rigs with
        // different bone path layouts (path binding happens in BuildFromSemanticData).
        internal static SemanticClipData BuildSemanticClipData(
            string alias,
            CafFile caf,
            float importScale,
            bool shouldLoop)
        {
            if (caf == null || caf.Tracks == null || caf.Tracks.Count == 0)
                return null;

            int baseTick = caf.GlobalStartTick;
            var semanticTracks = new List<SemanticClipTrack>(caf.Tracks.Count);
            
            var jobHandles = new NativeList<JobHandle>(caf.Tracks.Count, Allocator.Temp);
            var results = new List<(CafControllerTrack track, NativeArray<float3> pos, NativeArray<quaternion> rot)>();

            foreach (var track in caf.Tracks)
            {
                if (track == null || !track.Ticks.IsCreated || track.Ticks.Length == 0)
                    continue;

                int keyCount = 0;
                if (track.Ticks.IsCreated && track.Positions.IsCreated && track.Rotations.IsCreated)
                    keyCount = Mathf.Min(track.Ticks.Length, track.Positions.Length, track.Rotations.Length);

                if (keyCount == 0) continue;

                var outPositions = new NativeArray<float3>(keyCount, Allocator.TempJob);
                var outRotations = new NativeArray<quaternion>(keyCount, Allocator.TempJob);

                var job = new CafTrackNormalizationJob
                {
                    RawPositions = track.Positions.Reinterpret<float3>(UnsafeUtility.SizeOf<Vector3>()),
                    RawRotations = track.Rotations.Reinterpret<quaternion>(UnsafeUtility.SizeOf<Quaternion>()),
                    OutPositions = outPositions,
                    OutRotations = outRotations,
                    ImportScale  = importScale
                };

                jobHandles.Add(job.Schedule(keyCount, 64));
                results.Add((track, outPositions, outRotations));
            }

            JobHandle.CompleteAll(jobHandles);

            foreach (var res in results)
            {
                var track = res.track;
                int keyCount = res.pos.Length;
                
                var times = new float[keyCount];
                var positions = new Vector3[keyCount];
                var rotations = new Quaternion[keyCount];

                for (int k = 0; k < keyCount; k++)
                {
                    times[k] = Mathf.Max(0f, (track.Ticks[k] - baseTick) * caf.SecsPerTick);
                }

                // Copy from NativeArray to managed arrays
                res.pos.Reinterpret<Vector3>(UnsafeUtility.SizeOf<float3>()).CopyTo(positions);
                res.rot.Reinterpret<Quaternion>(UnsafeUtility.SizeOf<quaternion>()).CopyTo(rotations);

                semanticTracks.Add(new SemanticClipTrack
                {
                    ControllerID = track.ControllerID,
                    Times = times,
                    Positions = positions,
                    Rotations = rotations
                });

                res.pos.Dispose();
                res.rot.Dispose();
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

        // Builds a Unity AnimationClip by binding pre-normalized semantic clip data
        // to the given controllerToPath map. Missing controller IDs are recorded in
        // missingControllerIds and counted in missingControllerTrackCount.
        internal static AnimationClip BuildFromSemanticData(
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

        internal static bool ShouldTreatClipAsLoop(
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
                if (track == null || !track.Ticks.IsCreated || !track.Positions.IsCreated || !track.Rotations.IsCreated)
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
    }
}
