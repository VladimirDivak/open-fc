namespace OpenFarCry.Level.Data
{
    public sealed class FcBrushDesc
    {
        public int     Id;
        public string  VirtualPath;      // normalized CGF virtual path
        public string  MaterialOverride; // null if no per-brush material override
        public int     MaterialId;       // raw material index from brush.lst (-1 if none/invalid)
        public float[] Matrix;           // 12 floats, row-major: m00..m03, m10..m13, m20..m23
        public bool    NoPhysics;        // SExportedBrushGeom.NO_PHYSICS flag (0x02)
        public int     Flags;            // brush instance flags from brush.lst
        public int     MergeId;          // brush merge group id from brush.lst
        public byte    LodRatio;
        public byte    ViewDistRatio;
    }
}
