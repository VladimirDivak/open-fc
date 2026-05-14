using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace OpenFarCry.Importer.Texture
{
    // Shared math — called from both jobs; Burst inlines at compile time.
    static class DdsBlockHelpers
    {
        public static byte Bc4UNormPixel(NativeArray<byte> s, int blockOffset, int pixelIdx)
        {
            byte a0 = s[blockOffset];
            byte a1 = s[blockOffset + 1];
            int idx = (int)((Bits48(s, blockOffset + 2) >> (pixelIdx * 3)) & 7);
            return PaletteUNorm(a0, a1, idx);
        }

        public static byte Bc4SNormPixel(NativeArray<byte> s, int blockOffset, int pixelIdx)
        {
            int s0 = (sbyte)s[blockOffset];
            int s1 = (sbyte)s[blockOffset + 1];
            int idx = (int)((Bits48(s, blockOffset + 2) >> (pixelIdx * 3)) & 7);
            int v = PaletteSNorm(s0, s1, idx);
            v = v < -128 ? -128 : v > 127 ? 127 : v;
            return (byte)(v + 128);
        }

        static byte PaletteUNorm(byte a0, byte a1, int idx)
        {
            if (idx == 0) return a0;
            if (idx == 1) return a1;
            if (a0 > a1) return (byte)(((8 - idx) * a0 + (idx - 1) * a1) / 7);
            if (idx < 6)  return (byte)(((6 - idx) * a0 + (idx - 1) * a1) / 5);
            return idx == 6 ? (byte)0 : (byte)255;
        }

        static int PaletteSNorm(int s0, int s1, int idx)
        {
            if (idx == 0) return s0;
            if (idx == 1) return s1;
            if (s0 > s1) return ((8 - idx) * s0 + (idx - 1) * s1) / 7;
            if (idx < 6)  return ((6 - idx) * s0 + (idx - 1) * s1) / 5;
            return idx == 6 ? -128 : 127;
        }

        static ulong Bits48(NativeArray<byte> s, int o)
        {
            return (ulong)s[o]
                | ((ulong)s[o + 1] << 8)
                | ((ulong)s[o + 2] << 16)
                | ((ulong)s[o + 3] << 24)
                | ((ulong)s[o + 4] << 32)
                | ((ulong)s[o + 5] << 40);
        }
    }

    // One invocation per 4×4 block; blocks are independent so no data races.
    [BurstCompile]
    internal struct Bc4DecompressJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Source;   // slice starting at mip0
        public int BlocksX;
        public int Width;
        public int Height;
        public bool SignedNormalized;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<Color32> Output;

        public void Execute(int blockIdx)
        {
            int bx = blockIdx % BlocksX;
            int by = blockIdx / BlocksX;
            int src = blockIdx * 8;  // BC4: 8 bytes/block
            int startX = bx * 4;
            int startY = by * 4;

            for (int py = 0; py < 4; py++)
            {
                int y = startY + py;
                if (y >= Height) continue;
                for (int px = 0; px < 4; px++)
                {
                    int x = startX + px;
                    if (x >= Width) continue;
                    int pi = py * 4 + px;
                    byte v = SignedNormalized
                        ? DdsBlockHelpers.Bc4SNormPixel(Source, src, pi)
                        : DdsBlockHelpers.Bc4UNormPixel(Source, src, pi);
                    Output[y * Width + x] = new Color32(v, v, v, 255);
                }
            }
        }
    }

    // BC5 = two independent BC4 channels (R=normal X, G=normal Y).
    [BurstCompile]
    internal struct Bc5DecompressJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Source;   // slice starting at mip0
        public int BlocksX;
        public int Width;
        public int Height;
        public bool SignedNormalized;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<Color32> Output;

        public void Execute(int blockIdx)
        {
            int bx = blockIdx % BlocksX;
            int by = blockIdx / BlocksX;
            int src = blockIdx * 16;  // BC5: 16 bytes/block (two BC4 sub-blocks)
            int startX = bx * 4;
            int startY = by * 4;

            for (int py = 0; py < 4; py++)
            {
                int y = startY + py;
                if (y >= Height) continue;
                for (int px = 0; px < 4; px++)
                {
                    int x = startX + px;
                    if (x >= Width) continue;
                    int pi = py * 4 + px;
                    byte r = SignedNormalized
                        ? DdsBlockHelpers.Bc4SNormPixel(Source, src,     pi)
                        : DdsBlockHelpers.Bc4UNormPixel(Source, src,     pi);
                    byte g = SignedNormalized
                        ? DdsBlockHelpers.Bc4SNormPixel(Source, src + 8, pi)
                        : DdsBlockHelpers.Bc4UNormPixel(Source, src + 8, pi);
                    Output[y * Width + x] = new Color32(r, g, 0, 255);
                }
            }
        }
    }
}
