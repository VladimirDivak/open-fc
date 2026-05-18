using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using OpenFarCry.Level.Data;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcTerrainLayerMaskDecoderTests
    {
        // Wraps raw payload as a Far Cry .editor_datac blob:
        // 4-byte LE size + zlib stream (0x78 0x9C header + DEFLATE + 4-byte trailer).
        static byte[] MakeBlob(byte[] payload)
        {
            byte[] deflated;
            using (var ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    ds.Write(payload, 0, payload.Length);
                deflated = ms.ToArray();
            }

            using var outMs = new MemoryStream();
            int size = payload.Length;
            outMs.Write(new[] { (byte)size, (byte)(size >> 8), (byte)(size >> 16), (byte)(size >> 24) }, 0, 4);
            outMs.WriteByte(0x78);            // zlib CMF
            outMs.WriteByte(0x9C);            // zlib FLG, FDICT = 0
            outMs.Write(deflated, 0, deflated.Length);
            outMs.Write(new byte[4], 0, 4);   // Adler-32 trailer (ignored by decoder)
            return outMs.ToArray();
        }

        [Test]
        public void TryDecode_RoundTrips_SquarePayload()
        {
            var payload = new byte[16];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = (byte)(i * 17);

            bool ok = FcTerrainLayerMaskDecoder.TryDecode(MakeBlob(payload), out var mask, out int resolution);

            Assert.IsTrue(ok);
            Assert.AreEqual(4, resolution);
            Assert.AreEqual(payload, mask);
        }

        [Test]
        public void TryDecode_Accepts_PaddingRow()
        {
            // 20 == 4 * 5 == res * (res + 1): one padding row, usable side 4.
            var payload = new byte[20];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = (byte)i;

            bool ok = FcTerrainLayerMaskDecoder.TryDecode(MakeBlob(payload), out var mask, out int resolution);

            Assert.IsTrue(ok);
            Assert.AreEqual(4, resolution);
            Assert.AreEqual(payload, mask);
        }

        [Test]
        public void TryDecode_ReturnsFalse_ForNonSquareSize()
        {
            var payload = new byte[15]; // 15 is not a perfect square
            bool ok = FcTerrainLayerMaskDecoder.TryDecode(MakeBlob(payload), out var mask, out _);

            Assert.IsFalse(ok);
            Assert.IsNull(mask);
        }

        [Test]
        public void TryDecode_ReturnsFalse_ForTooShortBlob()
        {
            bool ok = FcTerrainLayerMaskDecoder.TryDecode(new byte[3], out var mask, out _);

            Assert.IsFalse(ok);
            Assert.IsNull(mask);
        }
    }
}
