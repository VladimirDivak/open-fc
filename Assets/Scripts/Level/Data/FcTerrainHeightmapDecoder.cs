using System;

namespace OpenFarCry.Level.Data
{
    public static class FcTerrainHeightmapDecoder
    {
        public const int DefaultResolution = 1024;

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
    }
}
