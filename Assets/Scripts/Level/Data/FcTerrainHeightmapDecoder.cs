using System;

namespace OpenFarCry.Level.Data
{
    public static class FcTerrainHeightmapDecoder
    {
        public const int DefaultResolution = 1024;
        public const float MaxWorldHeight = 256f;

        // Decodes little-endian 16-bit height samples from terrain/land_map.h16.
        // Output indexing is row-major: [z * resolution + x].
        public static bool TryDecodeH16(byte[] bytes, int resolution, out ushort[] samples)
        {
            samples = null;
            if (bytes == null || resolution <= 0)
                return false;

            int expectedBytes = resolution * resolution * 2;
            if (bytes.Length != expectedBytes)
                return false;

            samples = new ushort[resolution * resolution];
            int src = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                samples[i] = (ushort)(bytes[src] | (bytes[src + 1] << 8));
                src += 2;
            }

            return true;
        }

        // Extracts surface type IDs (STYPE_BIT_MASK = bits 0-2) from decoded h16 samples.
        // Returns byte[] length = resolution*resolution, row-major [z*resolution+x].
        // Values 0-6 are valid surface types; 7 = STYPE_HOLE (terrain hole).
        // Returns surface type IDs in Unity terrain order [hz*resolution+hx] (Z-major),
        // matching the transpose applied in TryDecodeToUnityHeights.
        public static byte[] DecodeSurfaceTypes(ushort[] samples)
        {
            int resolution = (int)System.Math.Round(System.Math.Sqrt(samples.Length));
            var ids = new byte[samples.Length];
            for (int hz = 0; hz < resolution; hz++)
                for (int hx = 0; hx < resolution; hx++)
                    ids[hz * resolution + hx] = (byte)(samples[hx * resolution + hz] & 0x07);
            return ids;
        }

        // Converts h16 bytes to normalized float[,] for TerrainData.SetHeights.
        // Output indexing: [hz, hx] (Unity convention). heightmapSize = resolution + 1 (must be 2^n+1).
        // Normalized: (raw & 0xFFE0) / 65536f  ->  world height = normalized * MaxWorldHeight.
        public static bool TryDecodeToUnityHeights(byte[] bytes, int resolution, out float[,] heights)
        {
            heights = null;
            if (!TryDecodeH16(bytes, resolution, out var samples))
                return false;
            return TryDecodeToUnityHeights(samples, resolution, out heights);
        }

        // Overload from pre-decoded samples; avoids re-parsing when samples are already available.
        public static bool TryDecodeToUnityHeights(ushort[] samples, int resolution, out float[,] heights)
        {
            heights = null;
            if (samples == null || resolution <= 0)
                return false;

            int hmSize = resolution + 1; // e.g. 1024 -> 1025 (valid Unity 2^n+1)
            heights = new float[hmSize, hmSize];
            for (int hz = 0; hz < resolution; hz++)
            {
                for (int hx = 0; hx < resolution; hx++)
                {
                    // Border (hx==0 or hz==0) returns 0 from SampleCryHeightRaw; replicate here.
                    if (hx == 0 || hz == 0)
                        continue;
                    if ((uint)(hx * resolution + hz) >= (uint)samples.Length)
                        continue;
                    ushort raw = samples[hx * resolution + hz];
                    heights[hz, hx] = (raw & 0xFFE0) / 65536f;
                }
            }
            return true;
        }
    }
}
