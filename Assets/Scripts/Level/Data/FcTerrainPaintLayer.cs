namespace OpenFarCry.Level.Data
{
    // One Far Cry terrain paint layer parsed from a level .cry editor archive.
    // Layers composite in paint order: index 0 is the bottom layer (raw copy),
    // each subsequent layer blends over the accumulator by its mask weight.
    public sealed class FcTerrainPaintLayer
    {
        public string Name;
        public string SurfaceType;

        // Base texture pixels, raw RGBA32, row-major, TextureWidth x TextureHeight.
        // Source is the layer_<name>.editor_data entry inside the .cry archive.
        public byte[] TextureRgba;
        public int TextureWidth;
        public int TextureHeight;

        // When true, the per-texel weight is generated procedurally from the
        // heightmap altitude/slope filters below; Mask is null.
        // When false, the hand-painted Mask is used instead.
        public bool AutoGenMask;

        // Autogen altitude filter, in meters (inclusive).
        public int AltStart;
        public int AltEnd;

        // Autogen slope filter, 0..255 (inclusive).
        public int MinSlope;
        public int MaxSlope;

        // Applies a 3x3 box blur to the autogen mask, softening hard edges.
        public bool Smooth;

        // Hand-painted weight mask, 1 byte alpha (0..255) per texel, row-major,
        // MaskResolution x MaskResolution. Null for autogen layers.
        public byte[] Mask;
        public int MaskResolution;
    }
}
