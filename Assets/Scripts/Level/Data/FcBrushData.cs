namespace OpenFarCry.Level.Data
{
    public sealed class FcBrushDesc
    {
        public int     Id;
        public string  VirtualPath;      // normalized CGF virtual path
        public string  MaterialOverride; // null if no per-brush material override
        public float[] Matrix;           // 12 floats, row-major: m00..m03, m10..m13, m20..m23
        public bool    NoPhysics;        // SExportedBrushGeom.NO_PHYSICS flag (0x02)
        public byte    LodRatio;
    }
}
