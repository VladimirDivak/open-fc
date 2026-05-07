using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public static class CgfSkeletonBuilder
    {
        public static Transform[] CreateBoneTransforms(
            CgfFile parsedFile,
            BuildResult result,
            CgfRigDefinition rigDefinition,
            Transform root)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (result.BoneNames == null || result.BoneNames.Length == 0)
                return Array.Empty<Transform>();

            string[] boneNames = result.BoneNames;
            Matrix4x4[] bindPoses = result.BindPoses;

            var transforms = new Transform[boneNames.Length];
            for (int i = 0; i < boneNames.Length; i++)
            {
                var boneGo = new GameObject(boneNames[i]);
                boneGo.transform.SetParent(root, worldPositionStays: false);
                transforms[i] = boneGo.transform;
            }

            bool hierarchyBuilt = TryBuildHierarchyFromBoneAnim(parsedFile, result, transforms);
            if (!hierarchyBuilt && rigDefinition != null && rigDefinition.IsValid)
                hierarchyBuilt = TryBuildHierarchyFromRigDefinition(rigDefinition, transforms);
            if (!hierarchyBuilt)
                TryBuildHierarchyFromNodes(parsedFile, boneNames, transforms);

            var worldBoneMatrices = new Matrix4x4[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                Matrix4x4 bind = (bindPoses != null && i < bindPoses.Length) ? bindPoses[i] : Matrix4x4.identity;
                worldBoneMatrices[i] = bind.inverse;
            }

            for (int i = 0; i < transforms.Length; i++)
            {
                Matrix4x4 local = worldBoneMatrices[i];
                var parent = transforms[i].parent;
                if (parent != null && parent != root)
                {
                    int parentIdx = Array.IndexOf(transforms, parent);
                    if (parentIdx >= 0)
                        local = worldBoneMatrices[parentIdx].inverse * worldBoneMatrices[i];
                }

                ApplyLocalMatrix(transforms[i], local);
            }

            return transforms;
        }

        public static Dictionary<uint, int> BuildRuntimeBoneIndexByControllerId(CgfFile parsedFile, BuildResult result)
        {
            var map = new Dictionary<uint, int>();

            var bones = parsedFile?.BoneAnim?.Bones;
            if (bones == null || bones.Length == 0)
                return map;

            int[] boneIdToIndex = result?.BoneIdToIndex;
            int boneCount = result?.BoneNames?.Length ?? 0;
            if (boneCount <= 0)
                return map;

            for (int i = 0; i < bones.Length; i++)
            {
                var entity = bones[i];
                int runtimeIndex = ResolveRuntimeBoneIndex(entity, boneIdToIndex, null, boneCount);
                if (runtimeIndex < 0 || runtimeIndex >= boneCount)
                    continue;

                if (!map.ContainsKey(entity.ControllerID))
                    map[entity.ControllerID] = runtimeIndex;
            }

            return map;
        }

        public static int ResolveRuntimeBoneIndex(
            CgfBoneEntity entity,
            int[] boneIdToIndex,
            Dictionary<uint, int> runtimeBoneByController,
            int boneCount)
        {
            if (runtimeBoneByController != null &&
                runtimeBoneByController.TryGetValue(entity.ControllerID, out int byController))
            {
                if (byController >= 0 && byController < boneCount)
                    return byController;
            }

            int boneId = entity.BoneID;
            if (boneIdToIndex != null && boneId >= 0 && boneId < boneIdToIndex.Length)
            {
                int byBoneIdMap = boneIdToIndex[boneId];
                if (byBoneIdMap >= 0 && byBoneIdMap < boneCount)
                    return byBoneIdMap;
            }

            return boneId >= 0 && boneId < boneCount ? boneId : -1;
        }

        static bool TryBuildHierarchyFromRigDefinition(CgfRigDefinition rigDefinition, Transform[] transforms)
        {
            var parentIndices = rigDefinition?.ParentIndices;
            if (parentIndices == null || parentIndices.Length != transforms.Length)
                return false;

            bool anyParentAssigned = false;
            for (int i = 0; i < transforms.Length; i++)
            {
                int parentIndex = parentIndices[i];
                if (parentIndex < 0 || parentIndex >= transforms.Length || parentIndex == i)
                    continue;

                var child = transforms[i];
                var parent = transforms[parentIndex];
                if (child == null || parent == null || child == parent)
                    continue;

                child.SetParent(parent, worldPositionStays: false);
                anyParentAssigned = true;
            }

            return anyParentAssigned;
        }

        static bool TryBuildHierarchyFromBoneAnim(CgfFile parsedFile, BuildResult result, Transform[] transforms)
        {
            var bones = parsedFile?.BoneAnim?.Bones;
            if (bones == null || bones.Length == 0)
                return false;

            int boneCount = transforms.Length;
            int[] boneIdToIndex = result?.BoneIdToIndex;
            var runtimeBoneByController = BuildRuntimeBoneIndexByControllerId(parsedFile, result);

            var runtimeByBoneId = new Dictionary<int, int>();
            var entityByRuntime = new Dictionary<int, CgfBoneEntity>();
            for (int i = 0; i < bones.Length; i++)
            {
                var entity = bones[i];
                int runtimeIndex = ResolveRuntimeBoneIndex(entity, boneIdToIndex, runtimeBoneByController, boneCount);
                if (runtimeIndex < 0 || runtimeIndex >= boneCount)
                    continue;

                if (!entityByRuntime.ContainsKey(runtimeIndex))
                    entityByRuntime[runtimeIndex] = entity;
                runtimeByBoneId[entity.BoneID] = runtimeIndex;
            }

            bool anyParentAssigned = false;
            foreach (var kv in entityByRuntime)
            {
                int childIndex = kv.Key;
                var entity = kv.Value;
                int parentIndex = -1;

                if (runtimeByBoneId.TryGetValue(entity.ParentID, out int byParentBoneId))
                {
                    parentIndex = byParentBoneId;
                }
                else if (boneIdToIndex != null &&
                         entity.ParentID >= 0 &&
                         entity.ParentID < boneIdToIndex.Length)
                {
                    parentIndex = boneIdToIndex[entity.ParentID];
                }

                if (parentIndex < 0 || parentIndex >= boneCount || parentIndex == childIndex)
                    continue;

                var child = transforms[childIndex];
                var parent = transforms[parentIndex];
                if (child == null || parent == null || child == parent)
                    continue;

                child.SetParent(parent, worldPositionStays: false);
                anyParentAssigned = true;
            }

            return anyParentAssigned;
        }

        static void TryBuildHierarchyFromNodes(CgfFile parsedFile, string[] boneNames, Transform[] transforms)
        {
            if (parsedFile == null || parsedFile.NodeChunks == null || parsedFile.NodeChunks.Count == 0)
                return;

            var indexByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var nodeByName = new Dictionary<string, CgfNodeChunk>(StringComparer.OrdinalIgnoreCase);
            var nodeByChunkId = new Dictionary<int, CgfNodeChunk>();

            for (int i = 0; i < boneNames.Length; i++)
                indexByName[boneNames[i]] = i;

            for (int i = 0; i < parsedFile.NodeChunks.Count; i++)
            {
                var node = parsedFile.NodeChunks[i];
                nodeByName[node.Name] = node;
                nodeByChunkId[node.ChunkID] = node;
            }

            for (int i = 0; i < boneNames.Length; i++)
            {
                if (!nodeByName.TryGetValue(boneNames[i], out var node))
                    continue;
                if (node.ParentID < 0)
                    continue;
                if (!nodeByChunkId.TryGetValue(node.ParentID, out var parentNode))
                    continue;
                if (!indexByName.TryGetValue(parentNode.Name, out int parentIdx))
                    continue;

                var child = transforms[i];
                var parent = transforms[parentIdx];
                if (child != null && parent != null && child != parent)
                    child.SetParent(parent, worldPositionStays: false);
            }
        }

        static void ApplyLocalMatrix(Transform t, Matrix4x4 m)
        {
            var pos = new Vector3(m.m03, m.m13, m.m23);

            var x = new Vector3(m.m00, m.m10, m.m20);
            var y = new Vector3(m.m01, m.m11, m.m21);
            var z = new Vector3(m.m02, m.m12, m.m22);

            float sx = x.magnitude;
            float sy = y.magnitude;
            float sz = z.magnitude;
            if (sx < 1e-8f || sy < 1e-8f || sz < 1e-8f)
            {
                t.localPosition = pos;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
                return;
            }

            var rx = x / sx;
            var ry = y / sy;
            var rz = z / sz;

            if (Vector3.Dot(Vector3.Cross(rx, ry), rz) < 0f)
                rx = -rx;

            var rot = Quaternion.LookRotation(rz, ry);
            t.localPosition = pos;
            t.localRotation = rot.normalized;
            t.localScale = Vector3.one;
        }
    }
}
