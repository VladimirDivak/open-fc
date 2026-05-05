using System;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    [Serializable]
    public struct CgfRigSnapshot
    {
        public string RigFingerprint;
        public string AnimationFingerprint;
        public string SourceVirtualPath;
        public string BindPoseHash;
        public string[] BoneNames;
        public int[] ParentIndices;
        public int[] BoneIndexToId;
        public int[] BoneIdToIndex;
        public uint[] ControllerIdsByBoneIndex;
        public Matrix4x4[] BindPoses;

        public bool IsValid =>
            !string.IsNullOrEmpty(RigFingerprint) &&
            BoneNames != null &&
            ParentIndices != null &&
            BoneIndexToId != null &&
            BindPoses != null &&
            BoneNames.Length > 0 &&
            BoneNames.Length == ParentIndices.Length &&
            BoneNames.Length == BoneIndexToId.Length &&
            BoneNames.Length == BindPoses.Length;
    }

    public class CgfRigDefinition : ScriptableObject
    {
        [SerializeField] string rigFingerprint;
        [SerializeField] string animationFingerprint;
        [SerializeField] string sourceVirtualPath;
        [SerializeField] string bindPoseHash;
        [SerializeField] string[] boneNames;
        [SerializeField] int[] parentIndices;
        [SerializeField] int[] boneIndexToId;
        [SerializeField] int[] boneIdToIndex;
        [SerializeField] uint[] controllerIdsByBoneIndex;
        [SerializeField] Matrix4x4[] bindPoses;

        public string RigFingerprint => rigFingerprint;
        public string AnimationFingerprint => animationFingerprint;
        public string SourceVirtualPath => sourceVirtualPath;
        public string BindPoseHash => bindPoseHash;
        public string[] BoneNames => boneNames;
        public int[] ParentIndices => parentIndices;
        public int[] BoneIndexToId => boneIndexToId;
        public int[] BoneIdToIndex => boneIdToIndex;
        public uint[] ControllerIdsByBoneIndex => controllerIdsByBoneIndex;
        public Matrix4x4[] BindPoses => bindPoses;

        public bool IsValid =>
            !string.IsNullOrEmpty(rigFingerprint) &&
            boneNames != null &&
            parentIndices != null &&
            boneIndexToId != null &&
            bindPoses != null &&
            boneNames.Length > 0 &&
            boneNames.Length == parentIndices.Length &&
            boneNames.Length == boneIndexToId.Length &&
            boneNames.Length == bindPoses.Length;

        public void ApplySnapshot(in CgfRigSnapshot snapshot)
        {
            if (!snapshot.IsValid)
                throw new ArgumentException("Rig snapshot is invalid.", nameof(snapshot));

            rigFingerprint = snapshot.RigFingerprint;
            animationFingerprint = snapshot.AnimationFingerprint ?? string.Empty;
            sourceVirtualPath = snapshot.SourceVirtualPath ?? string.Empty;
            bindPoseHash = snapshot.BindPoseHash ?? string.Empty;
            boneNames = CloneArray(snapshot.BoneNames);
            parentIndices = CloneArray(snapshot.ParentIndices);
            boneIndexToId = CloneArray(snapshot.BoneIndexToId);
            boneIdToIndex = CloneArray(snapshot.BoneIdToIndex);
            controllerIdsByBoneIndex = CloneArray(snapshot.ControllerIdsByBoneIndex);
            bindPoses = CloneArray(snapshot.BindPoses);
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
