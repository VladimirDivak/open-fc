using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    internal sealed class CachedAnimationSetEntry
    {
        public CgfRuntimeAnimationClip[] Clips;
        public int ExistingSourceCount;
        public int MissingControllerTrackCount;
        public long LastAccessTick;
    }

    internal sealed class SemanticClipTrack
    {
        public uint ControllerID;
        public float[] Times;
        public Vector3[] Positions;
        public Quaternion[] Rotations;
    }

    internal sealed class SemanticClipData
    {
        public string Alias;
        public bool ShouldLoop;
        public SemanticClipTrack[] Tracks;
    }

    internal sealed class CachedSemanticClipEntry
    {
        public SemanticClipData Data;
        public long LastAccessTick;
    }
}
