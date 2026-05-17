using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace OpenFarCry.Importer.Texture
{
    // Shared math — called from both jobs; Burst inlines at compile time.
    static class DdsBlockHelpers
    {
        public static void BuildBc1Palette(ushort c0, ushort c1, out float4 p0, out float4 p1, out float4 p2, out float4 p3, bool forceFourColor = false)
        {
            p0 = Rgb565ToFloat4(c0);
            p1 = Rgb565ToFloat4(c1);

            if (c0 > c1 || forceFourColor)
            {
                p2 = math.lerp(p0, p1, 1.0f / 3.0f);
                p3 = math.lerp(p0, p1, 2.0f / 3.0f);
            }
            else
            {
                p2 = math.lerp(p0, p1, 0.5f);
                p3 = new float4(0, 0, 0, 0);
            }
        }

        public static float4 Rgb565ToFloat4(ushort c)
        {
            int r5 = (c >> 11) & 0x1F;
            int g6 = (c >> 5) & 0x3F;
            int b5 = c & 0x1F;
            return new float4(r5 * 255f / 31f, g6 * 255f / 63f, b5 * 255f / 31f, 255f);
        }

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

        public static byte PaletteUNorm(byte a0, byte a1, int idx)
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

        public static ulong Bits48(NativeArray<byte> s, int o)
        {
            return (ulong)s[o]
                | ((ulong)s[o + 1] << 8)
                | ((ulong)s[o + 2] << 16)
                | ((ulong)s[o + 3] << 24)
                | ((ulong)s[o + 4] << 32)
                | ((ulong)s[o + 5] << 40);
        }

        public static ushort ReadU16(NativeArray<byte> s, int o)
        {
            return (ushort)(s[o] | (s[o + 1] << 8));
        }

        public static uint ReadU32(NativeArray<byte> s, int o)
        {
            return (uint)(s[o] | (s[o + 1] << 8) | (s[o + 2] << 16) | (s[o + 3] << 24));
        }

        public static ulong ReadU64(NativeArray<byte> s, int o)
        {
            return (ulong)s[o] | ((ulong)s[o + 1] << 8) | ((ulong)s[o + 2] << 16) | ((ulong)s[o + 3] << 24) |
                   ((ulong)s[o + 4] << 32) | ((ulong)s[o + 5] << 40) | ((ulong)s[o + 6] << 48) | ((ulong)s[o + 7] << 56);
        }
    }

    [BurstCompile]
    internal struct Bc1DecompressJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Source;
        public int BlocksX, Width, Height;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<Color32> Output;

        public void Execute(int blockIdx)
        {
            int bx = blockIdx % BlocksX;
            int by = blockIdx / BlocksX;
            int src = blockIdx * 8;
            int startX = bx * 4, startY = by * 4;

            ushort c0 = DdsBlockHelpers.ReadU16(Source, src);
            ushort c1 = DdsBlockHelpers.ReadU16(Source, src + 2);
            uint indices = DdsBlockHelpers.ReadU32(Source, src + 4);

            DdsBlockHelpers.BuildBc1Palette(c0, c1, out var p0, out var p1, out var p2, out var p3);

            for (int py = 0; py < 4; py++)
            {
                int y = startY + py;
                if (y >= Height) continue;
                for (int px = 0; px < 4; px++)
                {
                    int x = startX + px;
                    if (x >= Width) continue;
                    int i = py * 4 + px;
                    int idx = (int)((indices >> (i * 2)) & 3);
                    float4 c = idx == 0 ? p0 : (idx == 1 ? p1 : (idx == 2 ? p2 : p3));
                    Output[y * Width + x] = new Color32((byte)c.x, (byte)c.y, (byte)c.z, (byte)c.w);
                }
            }
        }
    }

    [BurstCompile]
    internal struct Bc2DecompressJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Source;
        public int BlocksX, Width, Height;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<Color32> Output;

        public void Execute(int blockIdx)
        {
            int bx = blockIdx % BlocksX;
            int by = blockIdx / BlocksX;
            int src = blockIdx * 16;
            int startX = bx * 4, startY = by * 4;

            ulong alphaBits = DdsBlockHelpers.ReadU64(Source, src);
            ushort c0 = DdsBlockHelpers.ReadU16(Source, src + 8);
            ushort c1 = DdsBlockHelpers.ReadU16(Source, src + 10);
            uint indices = DdsBlockHelpers.ReadU32(Source, src + 12);

            DdsBlockHelpers.BuildBc1Palette(c0, c1, out var p0, out var p1, out var p2, out var p3, true);

            for (int py = 0; py < 4; py++)
            {
                int y = startY + py;
                if (y >= Height) continue;
                for (int px = 0; px < 4; px++)
                {
                    int x = startX + px;
                    if (x >= Width) continue;
                    int i = py * 4 + px;
                    int idx = (int)((indices >> (i * 2)) & 3);
                    float4 c = idx == 0 ? p0 : (idx == 1 ? p1 : (idx == 2 ? p2 : p3));
                    byte alpha = (byte)(((alphaBits >> (i * 4)) & 0xF) * 17);
                    Output[y * Width + x] = new Color32((byte)c.x, (byte)c.y, (byte)c.z, alpha);
                }
            }
        }
    }

    [BurstCompile]
    internal struct Bc3DecompressJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> Source;
        public int BlocksX, Width, Height;

        [WriteOnly, NativeDisableParallelForRestriction]
        public NativeArray<Color32> Output;

        public void Execute(int blockIdx)
        {
            int bx = blockIdx % BlocksX;
            int by = blockIdx / BlocksX;
            int src = blockIdx * 16;
            int startX = bx * 4, startY = by * 4;

            byte a0 = Source[src];
            byte a1 = Source[src + 1];
            ulong alphaIndices = DdsBlockHelpers.Bits48(Source, src + 2);
            ushort c0 = DdsBlockHelpers.ReadU16(Source, src + 8);
            ushort c1 = DdsBlockHelpers.ReadU16(Source, src + 10);
            uint indices = DdsBlockHelpers.ReadU32(Source, src + 12);

            DdsBlockHelpers.BuildBc1Palette(c0, c1, out var p0, out var p1, out var p2, out var p3, true);

            for (int py = 0; py < 4; py++)
            {
                int y = startY + py;
                if (y >= Height) continue;
                for (int px = 0; px < 4; px++)
                {
                    int x = startX + px;
                    if (x >= Width) continue;
                    int i = py * 4 + px;
                    int idx = (int)((indices >> (i * 2)) & 3);
                    int aIdx = (int)((alphaIndices >> (i * 3)) & 7);
                    float4 c = idx == 0 ? p0 : (idx == 1 ? p1 : (idx == 2 ? p2 : p3));
                    byte alpha = DdsBlockHelpers.PaletteUNorm(a0, a1, aIdx);
                    Output[y * Width + x] = new Color32((byte)c.x, (byte)c.y, (byte)c.z, alpha);
                }
            }
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
