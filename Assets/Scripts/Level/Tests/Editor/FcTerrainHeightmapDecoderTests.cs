using NUnit.Framework;
using OpenFarCry.Level.Data;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcTerrainHeightmapDecoderTests
    {
        [Test]
        public void TryDecodeH16_ReturnsFalse_ForInvalidByteLength()
        {
            var bytes = new byte[7];
            bool ok = FcTerrainHeightmapDecoder.TryDecodeH16(bytes, 2, out var samples);

            Assert.That(ok, Is.False);
            Assert.That(samples, Is.Null);
        }

        [Test]
        public void TryDecodeH16_DecodesLittleEndianRowMajor()
        {
            // resolution=2 => 4 samples => 8 bytes
            var bytes = new byte[]
            {
                0x01, 0x00, // 1
                0xFF, 0x00, // 255
                0x34, 0x12, // 0x1234
                0x78, 0x56, // 0x5678
            };

            bool ok = FcTerrainHeightmapDecoder.TryDecodeH16(bytes, 2, out var samples);

            Assert.That(ok, Is.True);
            Assert.That(samples, Is.Not.Null);
            Assert.That(samples.Length, Is.EqualTo(4));
            Assert.That(samples[0], Is.EqualTo(1));
            Assert.That(samples[1], Is.EqualTo(255));
            Assert.That(samples[2], Is.EqualTo(0x1234));
            Assert.That(samples[3], Is.EqualTo(0x5678));
        }
    }
}
