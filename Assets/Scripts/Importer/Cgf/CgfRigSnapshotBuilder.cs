using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public static class CgfRigSnapshotBuilder
    {
        const int BindPoseQuantization = 10000;
        const string FingerprintVersion = "v1";

        public static bool TryBuild(
            CgfFile parsedFile,
            BuildResult result,
            string sourceVirtualPath,
            out CgfRigSnapshot snapshot)
        {
            snapshot = default;
            if (result == null || !result.HasSkeleton)
                return false;
            if (result.BoneNames == null || result.BoneNames.Length == 0)
                return false;
            if (result.BindPoses == null || result.BindPoses.Length != result.BoneNames.Length)
                return false;

            int boneCount = result.BoneNames.Length;
            var parentIndices = BuildParentIndices(parsedFile, result, boneCount);
            var controllerIds = BuildControllerIdsByBoneIndex(parsedFile, result, boneCount);
            string bindPoseHash = BuildBindPoseHash(result.BindPoses);
            string animationFingerprint = BuildAnimationFingerprintFromRigData(result.BoneNames, controllerIds);
            string fingerprint = BuildFingerprint(
                result.BoneNames,
                parentIndices,
                result.BoneIndexToId,
                controllerIds,
                bindPoseHash);

            snapshot = new CgfRigSnapshot
            {
                RigFingerprint = fingerprint,
                AnimationFingerprint = animationFingerprint,
                SourceVirtualPath = sourceVirtualPath ?? string.Empty,
                BindPoseHash = bindPoseHash,
                BoneNames = CloneArray(result.BoneNames),
                ParentIndices = parentIndices,
                BoneIndexToId = CloneArray(result.BoneIndexToId),
                BoneIdToIndex = CloneArray(result.BoneIdToIndex),
                ControllerIdsByBoneIndex = controllerIds,
                BindPoses = CloneArray(result.BindPoses)
            };

            return snapshot.IsValid;
        }

        public static string BuildAnimationFingerprintFromRigData(string[] boneNames, uint[] controllerIdsByBoneIndex)
        {
            if (boneNames == null || controllerIdsByBoneIndex == null || boneNames.Length != controllerIdsByBoneIndex.Length)
                return string.Empty;

            var pairs = new List<(uint controllerId, string boneName)>(boneNames.Length);
            for (int i = 0; i < boneNames.Length; i++)
            {
                uint controllerId = controllerIdsByBoneIndex[i];
                if (controllerId == 0u)
                    continue;

                string boneName = boneNames[i];
                if (string.IsNullOrWhiteSpace(boneName))
                    continue;

                pairs.Add((controllerId, boneName.ToLowerInvariant()));
            }

            if (pairs.Count == 0)
                return string.Empty;

            pairs.Sort((a, b) =>
            {
                int byController = a.controllerId.CompareTo(b.controllerId);
                if (byController != 0)
                    return byController;
                return string.CompareOrdinal(a.boneName, b.boneName);
            });

            var sb = new StringBuilder(1024);
            sb.Append("anim-v1|");
            for (int i = 0; i < pairs.Count; i++)
            {
                sb.Append(pairs[i].controllerId.ToString("X8"));
                sb.Append('=');
                sb.Append(pairs[i].boneName);
                sb.Append(';');
            }

            return Hash128.Compute(sb.ToString()).ToString();
        }

        static int[] BuildParentIndices(CgfFile parsedFile, BuildResult result, int boneCount)
        {
            var parentIndices = new int[boneCount];
            for (int i = 0; i < boneCount; i++)
                parentIndices[i] = -1;

            if (TryBuildParentIndicesFromBoneAnim(parsedFile, result, parentIndices))
                return parentIndices;

            TryBuildParentIndicesFromNodes(parsedFile, result.BoneNames, parentIndices);
            return parentIndices;
        }

        static bool TryBuildParentIndicesFromBoneAnim(CgfFile parsedFile, BuildResult result, int[] parentIndices)
        {
            var entities = parsedFile?.BoneAnim?.Bones;
            if (entities == null || entities.Length == 0)
                return false;
            if (result.BoneIndexToId == null || result.BoneIdToIndex == null)
                return false;

            var byBoneId = new Dictionary<int, CgfBoneEntity>(entities.Length);
            for (int i = 0; i < entities.Length; i++)
                byBoneId[entities[i].BoneID] = entities[i];

            bool anyAssigned = false;
            for (int runtimeIndex = 0; runtimeIndex < parentIndices.Length; runtimeIndex++)
            {
                int boneId = runtimeIndex < result.BoneIndexToId.Length
                    ? result.BoneIndexToId[runtimeIndex]
                    : runtimeIndex;
                if (!byBoneId.TryGetValue(boneId, out var entity))
                    continue;

                int parentBoneId = entity.ParentID;
                if (parentBoneId < 0 || parentBoneId >= result.BoneIdToIndex.Length)
                    continue;

                int parentRuntimeIndex = result.BoneIdToIndex[parentBoneId];
                if (parentRuntimeIndex < 0 || parentRuntimeIndex >= parentIndices.Length || parentRuntimeIndex == runtimeIndex)
                    continue;

                parentIndices[runtimeIndex] = parentRuntimeIndex;
                anyAssigned = true;
            }

            return anyAssigned;
        }

        static void TryBuildParentIndicesFromNodes(CgfFile parsedFile, string[] boneNames, int[] parentIndices)
        {
            if (parsedFile?.NodeChunks == null || parsedFile.NodeChunks.Count == 0)
                return;

            var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < boneNames.Length; i++)
                indexByName[boneNames[i]] = i;

            var nodeByName = new Dictionary<string, CgfNodeChunk>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < parsedFile.NodeChunks.Count; i++)
            {
                var node = parsedFile.NodeChunks[i];
                nodeByName[node.Name] = node;
            }

            var nodeByChunkId = parsedFile.NodeByChunkID;
            for (int i = 0; i < boneNames.Length; i++)
            {
                if (!nodeByName.TryGetValue(boneNames[i], out var node))
                    continue;
                if (node.ParentID < 0)
                    continue;
                if (nodeByChunkId == null || !nodeByChunkId.TryGetValue(node.ParentID, out var parentNode))
                    continue;
                if (!indexByName.TryGetValue(parentNode.Name, out int parentIndex))
                    continue;
                if (parentIndex == i)
                    continue;
                parentIndices[i] = parentIndex;
            }
        }

        static uint[] BuildControllerIdsByBoneIndex(CgfFile parsedFile, BuildResult result, int boneCount)
        {
            var controllerIds = new uint[boneCount];
            var entities = parsedFile?.BoneAnim?.Bones;
            if (entities == null || entities.Length == 0)
                return controllerIds;
            if (result.BoneIndexToId == null)
                return controllerIds;

            var byBoneId = new Dictionary<int, CgfBoneEntity>(entities.Length);
            for (int i = 0; i < entities.Length; i++)
                byBoneId[entities[i].BoneID] = entities[i];

            for (int runtimeIndex = 0; runtimeIndex < boneCount; runtimeIndex++)
            {
                int boneId = runtimeIndex < result.BoneIndexToId.Length
                    ? result.BoneIndexToId[runtimeIndex]
                    : runtimeIndex;
                if (!byBoneId.TryGetValue(boneId, out var entity))
                    continue;
                controllerIds[runtimeIndex] = entity.ControllerID;
            }

            return controllerIds;
        }

        static string BuildFingerprint(
            string[] boneNames,
            int[] parentIndices,
            int[] boneIndexToId,
            uint[] controllerIdsByBoneIndex,
            string bindPoseHash)
        {
            var sb = new StringBuilder(2048);
            sb.Append(FingerprintVersion).Append('|');
            sb.Append(bindPoseHash).Append('|');

            for (int i = 0; i < boneNames.Length; i++)
            {
                string name = boneNames[i] ?? string.Empty;
                int parent = i < parentIndices.Length ? parentIndices[i] : -1;
                int boneId = (boneIndexToId != null && i < boneIndexToId.Length) ? boneIndexToId[i] : i;
                uint controllerId = (controllerIdsByBoneIndex != null && i < controllerIdsByBoneIndex.Length)
                    ? controllerIdsByBoneIndex[i]
                    : 0u;

                sb.Append(name.ToLowerInvariant()).Append('|');
                sb.Append(parent).Append('|');
                sb.Append(boneId).Append('|');
                sb.Append(controllerId.ToString("X8")).Append(';');
            }

            return Hash128.Compute(sb.ToString()).ToString();
        }

        static string BuildBindPoseHash(Matrix4x4[] bindPoses)
        {
            var sb = new StringBuilder(bindPoses.Length * 48);
            for (int i = 0; i < bindPoses.Length; i++)
            {
                AppendMatrix(sb, bindPoses[i]);
                sb.Append(';');
            }
            return Hash128.Compute(sb.ToString()).ToString();
        }

        static void AppendMatrix(StringBuilder sb, Matrix4x4 m)
        {
            AppendQuantized(sb, m.m00); AppendQuantized(sb, m.m01); AppendQuantized(sb, m.m02); AppendQuantized(sb, m.m03);
            AppendQuantized(sb, m.m10); AppendQuantized(sb, m.m11); AppendQuantized(sb, m.m12); AppendQuantized(sb, m.m13);
            AppendQuantized(sb, m.m20); AppendQuantized(sb, m.m21); AppendQuantized(sb, m.m22); AppendQuantized(sb, m.m23);
            AppendQuantized(sb, m.m30); AppendQuantized(sb, m.m31); AppendQuantized(sb, m.m32); AppendQuantized(sb, m.m33);
        }

        static void AppendQuantized(StringBuilder sb, float value)
        {
            int q = Mathf.RoundToInt(value * BindPoseQuantization);
            sb.Append(q).Append(',');
        }

        static T[] CloneArray<T>(T[] source)
        {
            if (source == null)
                return null;

            var clone = new T[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }
    }
}
