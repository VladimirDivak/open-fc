using NUnit.Framework;
using OpenFarCry.Importer.Texture;
using UnityEngine;

namespace OpenFarCry.Importer.Tests.Editor
{
    public sealed class DdsRuntimeDecoderTests
    {
        const uint DdsMagic = 0x20534444; // "DDS "
        const uint PfFourCC = 0x4;
        const uint PfRgb = 0x40;
        const uint FourCC_DXT1 = 0x31545844; // "DXT1"
        const uint FourCC_DX10 = 0x30315844; // "DX10"
        const uint Dxgi_BC5_UNORM = 83;
        const uint D3d10ResourceDimensionTexture2D = 3;
        const uint FourCC_LegacyA8L8 = 51; // non-standard: D3DFMT_A8L8 in FourCC

        [Test]
        public void DecodeDxt1_ParsesBlockAndReturnsFormatTag()
        {
            byte[] dds = BuildDxt1Solid4x4();
            var options = new TextureRuntimeImportOptions(
                useRuntimeMemoryCache: true,
                markNonReadable: false,
                linearColorSpace: false,
                generateMipmaps: false);

            bool ok = DdsRuntimeDecoder.TryDecode(
                dds,
                options,
                out var texture,
                out var hasAlphaChannel,
                out var hasTransparentPixels,
                out var formatTag,
                out var error);

            Assert.That(ok, Is.True, $"decode error: {error}");
            Assert.That(texture, Is.Not.Null);
            Assert.That(formatTag, Is.EqualTo("dxt1"));
            Assert.That(hasAlphaChannel, Is.False);
            Assert.That(hasTransparentPixels, Is.False);
            Assert.That(texture.width, Is.EqualTo(4));
            Assert.That(texture.height, Is.EqualTo(4));

            var pixels = texture.GetPixels32();
            Assert.That(pixels.Length, Is.EqualTo(16));
            Assert.That(pixels[0].r, Is.GreaterThan(200));
            Assert.That(pixels[0].g, Is.LessThan(20));
            Assert.That(pixels[0].b, Is.LessThan(20));

            Object.DestroyImmediate(texture);
        }

        [Test]
        public void LoadNativeDxt1_UsesCompressedUnityTexture()
        {
            byte[] dds = BuildDxt1Solid4x4();
            var options = new TextureRuntimeImportOptions(
                useRuntimeMemoryCache: true,
                markNonReadable: false,
                linearColorSpace: false,
                generateMipmaps: false);

            bool ok = DdsRuntimeDecoder.TryLoadNative(
                dds,
                options,
                out var texture,
                out var hasAlphaChannel,
                out var formatTag,
                out var error);

            Assert.That(ok, Is.True, $"native load error: {error}");
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.format, Is.EqualTo(TextureFormat.DXT1));
            Assert.That(formatTag, Is.EqualTo("dxt1"));
            Assert.That(hasAlphaChannel, Is.False);

            Object.DestroyImmediate(texture);
        }

        [Test]
        public void LoadNativeDxt5_UsesCompressedUnityTexture()
        {
            byte[] dds = BuildDxt5Solid4x4();
            var options = new TextureRuntimeImportOptions(
                useRuntimeMemoryCache: true,
                markNonReadable: false,
                linearColorSpace: false,
                generateMipmaps: false);

            bool ok = DdsRuntimeDecoder.TryLoadNative(
                dds,
                options,
                out var texture,
                out var hasAlphaChannel,
                out var formatTag,
                out var error);

            Assert.That(ok, Is.True, $"native load error: {error}");
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.format, Is.EqualTo(TextureFormat.DXT5));
            Assert.That(formatTag, Is.EqualTo("dxt5"));
            Assert.That(hasAlphaChannel, Is.True);

            Object.DestroyImmediate(texture);
        }

        [Test]
        public void DecodeDx10Bc5_ParsesTwoChannelBlock()
        {
            byte[] dds = BuildDx10Bc5Solid4x4(red: 200, green: 96);
            var options = new TextureRuntimeImportOptions(
                useRuntimeMemoryCache: true,
                markNonReadable: false,
                linearColorSpace: false,
                generateMipmaps: false);

            bool ok = DdsRuntimeDecoder.TryDecode(
                dds,
                options,
                out var texture,
                out var hasAlphaChannel,
                out var hasTransparentPixels,
                out var formatTag,
                out var error);

            Assert.That(ok, Is.True, $"decode error: {error}");
            Assert.That(texture, Is.Not.Null);
            Assert.That(formatTag, Is.EqualTo("dx10_bc5"));
            Assert.That(hasAlphaChannel, Is.False);
            Assert.That(hasTransparentPixels, Is.False);

            var pixels = texture.GetPixels32();
            Assert.That(pixels[0].r, Is.EqualTo(200).Within(1));
            Assert.That(pixels[0].g, Is.EqualTo(96).Within(1));
            Assert.That(pixels[0].b, Is.EqualTo(0).Within(1));
            Assert.That(pixels[0].a, Is.EqualTo(255));

            Object.DestroyImmediate(texture);
        }

        [Test]
        public void DecodeUncompressedRgba8_GeneratesMipmaps_WhenEnabled()
        {
            byte[] dds = BuildUncompressedRgba8Solid4x4();
            var options = new TextureRuntimeImportOptions(
                useRuntimeMemoryCache: true,
                markNonReadable: false,
                linearColorSpace: false,
                generateMipmaps: true);

            bool ok = DdsRuntimeDecoder.TryDecode(
                dds,
                options,
                out var texture,
                out _,
                out _,
                out var formatTag,
                out var error);

            Assert.That(ok, Is.True, $"decode error: {error}");
            Assert.That(texture, Is.Not.Null);
            Assert.That(formatTag, Is.EqualTo("maskedrgb_32"));
            Assert.That(texture.mipmapCount, Is.GreaterThan(1));

            Object.DestroyImmediate(texture);
        }

        [Test]
        public void DecodeLegacyD3dFormatA8L8_ReadsLuminanceAndAlpha()
        {
            byte[] dds = BuildLegacyA8L8Solid4x4(luminance: 64, alpha: 200);
            var options = new TextureRuntimeImportOptions(
                useRuntimeMemoryCache: true,
                markNonReadable: false,
                linearColorSpace: false,
                generateMipmaps: false);

            bool ok = DdsRuntimeDecoder.TryDecode(
                dds,
                options,
                out var texture,
                out var hasAlphaChannel,
                out var hasTransparentPixels,
                out var formatTag,
                out var error);

            Assert.That(ok, Is.True, $"decode error: {error}");
            Assert.That(texture, Is.Not.Null);
            Assert.That(formatTag, Is.EqualTo("legacy_d3dfmt_a8l8"));
            Assert.That(hasAlphaChannel, Is.True);
            Assert.That(hasTransparentPixels, Is.True);

            var p = texture.GetPixels32()[0];
            Assert.That(p.r, Is.EqualTo(64).Within(1));
            Assert.That(p.g, Is.EqualTo(64).Within(1));
            Assert.That(p.b, Is.EqualTo(64).Within(1));
            Assert.That(p.a, Is.EqualTo(200).Within(1));

            Object.DestroyImmediate(texture);
        }

        static byte[] BuildDxt1Solid4x4()
        {
            // 4x4, single DXT1 block (8 bytes). Use color0=red, color1=green, indices=0 => solid red.
            var bytes = new byte[128 + 8];
            WriteLegacyHeader(bytes, width: 4, height: 4, pfFlags: PfFourCC, fourCC: FourCC_DXT1, rgbBitCount: 0, rMask: 0, gMask: 0, bMask: 0, aMask: 0);

            int o = 128;
            WriteU16(bytes, o + 0, 0xF800); // red 565
            WriteU16(bytes, o + 2, 0x07E0); // green 565
            WriteU32(bytes, o + 4, 0u);     // all indices = 0
            return bytes;
        }

        static byte[] BuildDx10Bc5Solid4x4(byte red, byte green)
        {
            // 4x4, single BC5 block (16 bytes): two BC4 blocks, constant values by using index=0 everywhere.
            var bytes = new byte[148 + 16];
            WriteLegacyHeader(bytes, width: 4, height: 4, pfFlags: PfFourCC, fourCC: FourCC_DX10, rgbBitCount: 0, rMask: 0, gMask: 0, bMask: 0, aMask: 0);

            // DDS_HEADER_DXT10 at 128
            WriteU32(bytes, 128 + 0, Dxgi_BC5_UNORM);
            WriteU32(bytes, 128 + 4, D3d10ResourceDimensionTexture2D);
            WriteU32(bytes, 128 + 8, 0u);  // miscFlag
            WriteU32(bytes, 128 + 12, 1u); // arraySize
            WriteU32(bytes, 128 + 16, 0u); // miscFlags2

            int o = 148;
            // Red BC4 block
            bytes[o + 0] = red;
            bytes[o + 1] = red;
            for (int i = 0; i < 6; i++)
                bytes[o + 2 + i] = 0; // all indices 0
            // Green BC4 block
            bytes[o + 8] = green;
            bytes[o + 9] = green;
            for (int i = 0; i < 6; i++)
                bytes[o + 10 + i] = 0; // all indices 0

            return bytes;
        }

        static byte[] BuildDxt5Solid4x4()
        {
            var bytes = new byte[128 + 16];
            WriteLegacyHeader(bytes, width: 4, height: 4, pfFlags: PfFourCC, fourCC: 0x35545844, rgbBitCount: 0, rMask: 0, gMask: 0, bMask: 0, aMask: 0);

            int o = 128;
            // Alpha block: a0=a1=255, zero indices.
            bytes[o + 0] = 255;
            bytes[o + 1] = 255;
            for (int i = 0; i < 6; i++)
                bytes[o + 2 + i] = 0;

            // Color block: red->green with all indices 0 => solid red.
            WriteU16(bytes, o + 8, 0xF800);
            WriteU16(bytes, o + 10, 0x07E0);
            WriteU32(bytes, o + 12, 0u);
            return bytes;
        }

        static byte[] BuildUncompressedRgba8Solid4x4()
        {
            var bytes = new byte[128 + (4 * 4 * 4)];
            WriteLegacyHeader(
                bytes,
                width: 4,
                height: 4,
                pfFlags: PfRgb,
                fourCC: 0,
                rgbBitCount: 32,
                rMask: 0x000000ff,
                gMask: 0x0000ff00,
                bMask: 0x00ff0000,
                aMask: 0xff000000);

            int o = 128;
            for (int i = 0; i < 16; i++)
            {
                bytes[o + i * 4 + 0] = 30;  // R
                bytes[o + i * 4 + 1] = 60;  // G
                bytes[o + i * 4 + 2] = 90;  // B
                bytes[o + i * 4 + 3] = 255; // A
            }

            return bytes;
        }

        static byte[] BuildLegacyA8L8Solid4x4(byte luminance, byte alpha)
        {
            var bytes = new byte[128 + (4 * 4 * 2)];
            WriteLegacyHeader(
                bytes,
                width: 4,
                height: 4,
                pfFlags: PfFourCC, // non-standard legacy encoding: FourCC holds D3DFORMAT enum value
                fourCC: FourCC_LegacyA8L8,
                rgbBitCount: 0,
                rMask: 0,
                gMask: 0,
                bMask: 0,
                aMask: 0);

            int o = 128;
            for (int i = 0; i < 16; i++)
            {
                // A8L8: high byte alpha, low byte luminance
                bytes[o + i * 2 + 0] = luminance;
                bytes[o + i * 2 + 1] = alpha;
            }

            return bytes;
        }

        static void WriteLegacyHeader(
            byte[] bytes,
            int width,
            int height,
            uint pfFlags,
            uint fourCC,
            uint rgbBitCount,
            uint rMask,
            uint gMask,
            uint bMask,
            uint aMask)
        {
            WriteU32(bytes, 0, DdsMagic);
            WriteU32(bytes, 4, 124u); // DDS_HEADER size
            WriteU32(bytes, 12, (uint)height);
            WriteU32(bytes, 16, (uint)width);
            WriteU32(bytes, 76, 32u); // DDS_PIXELFORMAT size
            WriteU32(bytes, 80, pfFlags);
            WriteU32(bytes, 84, fourCC);
            WriteU32(bytes, 88, rgbBitCount);
            WriteU32(bytes, 92, rMask);
            WriteU32(bytes, 96, gMask);
            WriteU32(bytes, 100, bMask);
            WriteU32(bytes, 104, aMask);
        }

        static void WriteU16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset + 0] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        static void WriteU32(byte[] bytes, int offset, uint value)
        {
            bytes[offset + 0] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
            bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
        }
    }
}
