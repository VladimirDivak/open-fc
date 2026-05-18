using System;
using System.IO;
using System.IO.Compression;

namespace OpenFarCry.Level.Data
{
    // Decodes a Far Cry layermask_<name>.editor_datac blob.
    //
    // Layout: 4-byte little-endian uncompressed size, followed by a zlib stream
    // (2-byte header + raw DEFLATE + 4-byte Adler-32 trailer). The inflated data
    // is one byte of alpha weight (0..255) per texel, row-major, square.
    public static class FcTerrainLayerMaskDecoder
    {
        public static bool TryDecode(byte[] blob, out byte[] mask, out int resolution)
        {
            mask = null;
            resolution = 0;
            if (blob == null || blob.Length < 7)
                return false;

            int size = blob[0] | (blob[1] << 8) | (blob[2] << 16) | (blob[3] << 24);
            if (size <= 0)
                return false;

            // Mask data is res*res, or res*(res+1) when the editor appends one
            // padding row. res is the usable square side and the row stride.
            int res = (int)Math.Round(Math.Sqrt(size));
            if ((long)res * res != size && (long)res * (res + 1) != size)
                return false;

            // zlib header: CMF (blob[4]) + FLG (blob[5]); FDICT adds 4 dictionary bytes.
            int deflateStart = 4 + 2;
            bool fdict = (blob[5] & 0x20) != 0;
            if (fdict)
                deflateStart += 4;
            if (deflateStart >= blob.Length)
                return false;

            try
            {
                var output = new byte[size];
                using var input = new MemoryStream(blob, deflateStart, blob.Length - deflateStart, writable: false);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                int read = 0;
                while (read < size)
                {
                    int n = deflate.Read(output, read, size - read);
                    if (n <= 0)
                        break;
                    read += n;
                }
                if (read != size)
                    return false;

                mask = output;
                resolution = res;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
