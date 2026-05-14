using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;

namespace OpenFarCry.Importer.Texture
{
    internal static class DdsRuntimeDecoder
    {
        const uint DdsMagic = 0x20534444; // "DDS "

        const uint PfFourCC = 0x4;
        const uint PfAlpha = 0x2;
        const uint PfRgb = 0x40;
        const uint PfLuminance = 0x20000;

        const uint FourCC_DXT1 = 0x31545844; // "DXT1"
        const uint FourCC_DXT2 = 0x32545844; // "DXT2"
        const uint FourCC_DXT3 = 0x33545844; // "DXT3"
        const uint FourCC_DXT4 = 0x34545844; // "DXT4"
        const uint FourCC_DXT5 = 0x35545844; // "DXT5"
        const uint FourCC_DX10 = 0x30315844; // "DX10"
        const uint FourCC_RXGB = 0x42475852; // "RXGB"
        const uint FourCC_ATI1 = 0x31495441; // "ATI1"
        const uint FourCC_ATI2 = 0x32495441; // "ATI2"

        const uint D3dFmt_A8R8G8B8 = 21;
        const uint D3dFmt_X8R8G8B8 = 22;
        const uint D3dFmt_R5G6B5 = 23;
        const uint D3dFmt_A1R5G5B5 = 25;
        const uint D3dFmt_A4R4G4B4 = 26;
        const uint D3dFmt_A8 = 28;
        const uint D3dFmt_A8B8G8R8 = 32;
        const uint D3dFmt_L8 = 50;
        const uint D3dFmt_A8L8 = 51;
        const uint D3dFmt_A4L4 = 52;

        const uint Dxgi_BC1_UNORM = 71;
        const uint Dxgi_BC1_UNORM_SRGB = 72;
        const uint Dxgi_BC2_UNORM = 74;
        const uint Dxgi_BC2_UNORM_SRGB = 75;
        const uint Dxgi_BC3_UNORM = 77;
        const uint Dxgi_BC3_UNORM_SRGB = 78;
        const uint Dxgi_BC4_UNORM = 80;
        const uint Dxgi_BC4_SNORM = 81;
        const uint Dxgi_BC5_UNORM = 83;
        const uint Dxgi_BC5_SNORM = 84;
        const uint Dxgi_R8G8B8A8_UNORM = 28;
        const uint Dxgi_R8G8B8A8_UNORM_SRGB = 29;
        const uint Dxgi_B8G8R8A8_UNORM = 87;
        const uint Dxgi_B8G8R8A8_UNORM_SRGB = 91;

        const uint D3d10ResourceDimensionTexture2D = 3;

        enum DdsFormat
        {
            Unknown = 0,
            Dxt1,
            Dxt3,
            Dxt5,
            Bc4,
            Bc5,
            MaskedRgb,
            Luminance8,
            LuminanceAlpha,
            Alpha8
        }

        struct DdsHeaderInfo
        {
            public DdsFormat Format;
            public int Width;
            public int Height;
            public int DataOffset;
            public int MipCount;
            public int BitsPerPixel;
            public uint RMask;
            public uint GMask;
            public uint BMask;
            public uint AMask;
            public bool SignedNormalized;
        }

        public static bool TryLoadNative(
            byte[] bytes,
            TextureRuntimeImportOptions options,
            out Texture2D texture,
            out bool hasAlphaChannel,
            out string formatTag,
            out string error)
        {
            texture = null;
            hasAlphaChannel = false;
            formatTag = "unknown";
            error = null;

            if (!TryParseHeader(bytes, out var header, out formatTag, out error))
                return false;

            TextureFormat unityFormat;
            switch (header.Format)
            {
                case DdsFormat.Dxt1:
                    unityFormat = TextureFormat.DXT1;
                    hasAlphaChannel = false;
                    break;
                case DdsFormat.Dxt5:
                    unityFormat = TextureFormat.DXT5;
                    hasAlphaChannel = true;
                    break;
                default:
                    error = $"DDS format '{formatTag}' does not support native load";
                    return false;
            }

            bool hasEmbeddedMipChain = header.MipCount > 1;
            bool useEmbeddedMipChain = hasEmbeddedMipChain && options.GenerateMipmaps;
            int payloadBytes = GetCompressedPayloadLength(
                header.Format,
                header.Width,
                header.Height,
                useEmbeddedMipChain ? header.MipCount : 1);
            if (payloadBytes <= 0)
            {
                error = "invalid DDS compressed payload size";
                return false;
            }

            if (!CanRead(bytes, header.DataOffset, payloadBytes))
            {
                error = $"DDS compressed payload is truncated (need {payloadBytes} bytes)";
                return false;
            }

            try
            {
                texture = new Texture2D(
                    header.Width,
                    header.Height,
                    unityFormat,
                    mipChain: useEmbeddedMipChain,
                    linear: options.LinearColorSpace);

                var payload = new byte[payloadBytes];
                Buffer.BlockCopy(bytes, header.DataOffset, payload, 0, payloadBytes);
                texture.LoadRawTextureData(payload);
                texture.Apply(
                    updateMipmaps: options.GenerateMipmaps && useEmbeddedMipChain,
                    makeNoLongerReadable: options.MarkNonReadable);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                if (texture != null)
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(texture);
                    else
                        UnityEngine.Object.DestroyImmediate(texture);
                    texture = null;
                }

                return false;
            }
        }

        public static bool TryDecode(
            byte[] bytes,
            TextureRuntimeImportOptions options,
            out Texture2D texture,
            out bool hasAlphaChannel,
            out bool hasTransparentPixels,
            out string formatTag,
            out string error)
        {
            texture = null;
            hasAlphaChannel = false;
            hasTransparentPixels = false;
            formatTag = "unknown";
            error = null;

            if (!TryDecodeToRgba(
                    bytes,
                    out int width,
                    out int height,
                    out var pixels,
                    out hasAlphaChannel,
                    out hasTransparentPixels,
                    out formatTag,
                    out error))
            {
                return false;
            }

            try
            {
                texture = new Texture2D(
                    width,
                    height,
                    TextureFormat.RGBA32,
                    mipChain: options.GenerateMipmaps,
                    linear: options.LinearColorSpace);
                texture.SetPixels32(pixels);
                texture.Apply(updateMipmaps: options.GenerateMipmaps, makeNoLongerReadable: options.MarkNonReadable);
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                if (texture != null)
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(texture);
                    else
                        UnityEngine.Object.DestroyImmediate(texture);
                    texture = null;
                }

                return false;
            }
        }

        public static bool TryDecodeToRgba(
            byte[] bytes,
            out int width,
            out int height,
            out Color32[] pixels,
            out bool hasAlphaChannel,
            out bool hasTransparentPixels,
            out string formatTag,
            out string error)
        {
            width = 0;
            height = 0;
            pixels = null;
            hasAlphaChannel = false;
            hasTransparentPixels = false;
            formatTag = "unknown";
            error = null;

            if (!TryParseHeader(bytes, out var header, out formatTag, out error))
                return false;

            width = header.Width;
            height = header.Height;

            switch (header.Format)
            {
                case DdsFormat.Dxt1:
                    if (!TryDecodeBc1(bytes, header.DataOffset, header.Width, header.Height, out pixels))
                    {
                        error = "failed to decode BC1/DXT1 payload";
                        return false;
                    }
                    hasAlphaChannel = ContainsTransparentPixels(pixels);
                    break;
                case DdsFormat.Dxt3:
                    if (!TryDecodeBc2(bytes, header.DataOffset, header.Width, header.Height, out pixels))
                    {
                        error = "failed to decode BC2/DXT3 payload";
                        return false;
                    }
                    hasAlphaChannel = true;
                    break;
                case DdsFormat.Dxt5:
                    if (!TryDecodeBc3(bytes, header.DataOffset, header.Width, header.Height, out pixels))
                    {
                        error = "failed to decode BC3/DXT5 payload";
                        return false;
                    }
                    hasAlphaChannel = true;
                    break;
                case DdsFormat.Bc4:
                    if (!TryDecodeBc4(
                            bytes,
                            header.DataOffset,
                            header.Width,
                            header.Height,
                            header.SignedNormalized,
                            out pixels))
                    {
                        error = "failed to decode BC4 payload";
                        return false;
                    }
                    hasAlphaChannel = false;
                    break;
                case DdsFormat.Bc5:
                    if (!TryDecodeBc5(
                            bytes,
                            header.DataOffset,
                            header.Width,
                            header.Height,
                            header.SignedNormalized,
                            out pixels))
                    {
                        error = "failed to decode BC5 payload";
                        return false;
                    }
                    hasAlphaChannel = false;
                    break;
                case DdsFormat.MaskedRgb:
                    if (!TryDecodeMaskedRgb(bytes, header, out pixels))
                    {
                        error = "failed to decode uncompressed masked RGB payload";
                        return false;
                    }
                    hasAlphaChannel = header.AMask != 0;
                    break;
                case DdsFormat.Luminance8:
                    if (!TryDecodeLuminance8(bytes, header.DataOffset, header.Width, header.Height, out pixels))
                    {
                        error = "failed to decode L8 payload";
                        return false;
                    }
                    hasAlphaChannel = false;
                    break;
                case DdsFormat.LuminanceAlpha:
                    if (!TryDecodeLuminanceAlpha(bytes, header, out pixels))
                    {
                        error = "failed to decode luminance+alpha payload";
                        return false;
                    }
                    hasAlphaChannel = true;
                    break;
                case DdsFormat.Alpha8:
                    if (!TryDecodeAlpha8(bytes, header.DataOffset, header.Width, header.Height, out pixels))
                    {
                        error = "failed to decode A8 payload";
                        return false;
                    }
                    hasAlphaChannel = true;
                    break;
                default:
                    error = "unsupported DDS format";
                    return false;
            }

            // UV V is flipped in mesh import (CgfMeshBuilder), so keep decoded texture rows as-is.
            hasTransparentPixels = hasAlphaChannel && ContainsTransparentPixels(pixels);
            return true;
        }

        public static bool TryReadFormatTag(byte[] bytes, out string formatTag, out string error)
        {
            if (!TryParseHeader(bytes, out _, out formatTag, out error))
                return false;

            return true;
        }

        static bool TryParseHeader(byte[] bytes, out DdsHeaderInfo header, out string formatTag, out string error)
        {
            header = default;
            formatTag = "unknown";
            error = null;

            if (bytes == null || bytes.Length < 128)
            {
                error = "DDS payload is too small";
                return false;
            }

            if (ReadUInt32LE(bytes, 0) != DdsMagic)
            {
                error = "missing DDS magic";
                return false;
            }

            int headerSize = ReadInt32LE(bytes, 4);
            if (headerSize != 124)
            {
                error = $"unexpected DDS header size: {headerSize}";
                return false;
            }

            int height = ReadInt32LE(bytes, 12);
            int width = ReadInt32LE(bytes, 16);
            int depth = ReadInt32LE(bytes, 24);
            int mipCount = ReadInt32LE(bytes, 28);
            int pfSize = ReadInt32LE(bytes, 76);
            uint pfFlags = ReadUInt32LE(bytes, 80);
            uint fourCC = ReadUInt32LE(bytes, 84);
            uint rgbBitCount = ReadUInt32LE(bytes, 88);
            uint rMask = ReadUInt32LE(bytes, 92);
            uint gMask = ReadUInt32LE(bytes, 96);
            uint bMask = ReadUInt32LE(bytes, 100);
            uint aMask = ReadUInt32LE(bytes, 104);
            uint caps2 = ReadUInt32LE(bytes, 112);

            if (pfSize != 32)
            {
                error = $"unexpected DDS pixel format size: {pfSize}";
                return false;
            }

            if (width <= 0 || height <= 0)
            {
                error = $"invalid DDS size: {width}x{height}";
                return false;
            }

            if (depth > 1)
            {
                error = "3D DDS textures are not supported";
                return false;
            }

            if (caps2 != 0)
            {
                // Cubemap/volume flags live in caps2. MVP supports only plain 2D textures.
                error = "DDS cubemap/volume textures are not supported";
                return false;
            }

            DdsFormat format = DdsFormat.Unknown;
            bool signedNormalized = false;
            int dataOffset = 128;
            uint resolvedBitCount = rgbBitCount;
            uint resolvedRMask = rMask;
            uint resolvedGMask = gMask;
            uint resolvedBMask = bMask;
            uint resolvedAMask = aMask;
            if ((pfFlags & PfFourCC) != 0)
            {
                switch (fourCC)
                {
                    case FourCC_DXT1:
                        format = DdsFormat.Dxt1;
                        formatTag = "dxt1";
                        break;
                    case FourCC_DXT2:
                    case FourCC_DXT3:
                        format = DdsFormat.Dxt3;
                        formatTag = "dxt3";
                        break;
                    case FourCC_DXT4:
                    case FourCC_DXT5:
                        format = DdsFormat.Dxt5;
                        formatTag = "dxt5";
                        break;
                    case FourCC_RXGB:
                        // RXGB is a DXT5-like block encoding; decode as DXT5 for MVP.
                        format = DdsFormat.Dxt5;
                        formatTag = "rxgb_as_dxt5";
                        break;
                    case FourCC_ATI1:
                        format = DdsFormat.Bc4;
                        formatTag = "ati1_bc4";
                        break;
                    case FourCC_ATI2:
                        format = DdsFormat.Bc5;
                        formatTag = "ati2_bc5";
                        break;
                    case FourCC_DX10:
                        if (!TryParseDx10Format(
                                bytes,
                                out format,
                                out formatTag,
                                out signedNormalized,
                                out uint dx10DxgiFormat,
                                out error))
                            return false;
                        dataOffset = 148;
                        if (dx10DxgiFormat == Dxgi_R8G8B8A8_UNORM || dx10DxgiFormat == Dxgi_R8G8B8A8_UNORM_SRGB)
                        {
                            resolvedBitCount = 32;
                            resolvedRMask = 0x000000ff;
                            resolvedGMask = 0x0000ff00;
                            resolvedBMask = 0x00ff0000;
                            resolvedAMask = 0xff000000;
                        }
                        else if (dx10DxgiFormat == Dxgi_B8G8R8A8_UNORM || dx10DxgiFormat == Dxgi_B8G8R8A8_UNORM_SRGB)
                        {
                            resolvedBitCount = 32;
                            resolvedRMask = 0x00ff0000;
                            resolvedGMask = 0x0000ff00;
                            resolvedBMask = 0x000000ff;
                            resolvedAMask = 0xff000000;
                        }
                        break;
                    case 0:
                        // Some files mark FOURCC flag but keep value 0 for uncompressed pixel formats.
                        formatTag = "fourcc0_uncompressed";
                        break;
                    default:
                        if (!TryMapLegacyD3dFormat(
                                fourCC,
                                out format,
                                out formatTag,
                                out resolvedBitCount,
                                out resolvedRMask,
                                out resolvedGMask,
                                out resolvedBMask,
                                out resolvedAMask))
                        {
                            formatTag = $"fourcc_0x{fourCC:X8}";
                            error = $"unsupported DDS FourCC: 0x{fourCC:X8}";
                            return false;
                        }
                        break;
                }
            }

            if (format == DdsFormat.Unknown && (pfFlags & PfRgb) != 0)
            {
                if (rgbBitCount == 16 || rgbBitCount == 24 || rgbBitCount == 32)
                {
                    format = DdsFormat.MaskedRgb;
                    formatTag = $"maskedrgb_{rgbBitCount}";
                }
            }

            if (format == DdsFormat.Unknown && (pfFlags & PfLuminance) != 0)
            {
                if (rgbBitCount == 8)
                {
                    format = DdsFormat.Luminance8;
                    formatTag = "l8";
                }
                else if (rgbBitCount == 16 && aMask != 0 && rMask != 0)
                {
                    format = DdsFormat.LuminanceAlpha;
                    formatTag = "la16";
                }
                else if (rgbBitCount == 8 && aMask != 0 && rMask != 0)
                {
                    format = DdsFormat.LuminanceAlpha;
                    formatTag = "la8";
                }
            }

            if (format == DdsFormat.Unknown && (pfFlags & PfAlpha) != 0)
            {
                if (rgbBitCount == 8)
                {
                    format = DdsFormat.Alpha8;
                    formatTag = "a8";
                }
            }

            if (format == DdsFormat.Unknown)
            {
                error = $"unsupported DDS pixel format (flags=0x{pfFlags:X8}, bitCount={rgbBitCount}, fourCC=0x{fourCC:X8})";
                return false;
            }

            header = new DdsHeaderInfo
            {
                Format = format,
                Width = width,
                Height = height,
                DataOffset = dataOffset,
                MipCount = Math.Max(1, mipCount),
                BitsPerPixel = (int)resolvedBitCount,
                RMask = resolvedRMask,
                GMask = resolvedGMask,
                BMask = resolvedBMask,
                AMask = resolvedAMask,
                SignedNormalized = signedNormalized
            };
            return true;
        }

        static bool TryParseDx10Format(
            byte[] bytes,
            out DdsFormat format,
            out string formatTag,
            out bool signedNormalized,
            out uint dxgiFormatValue,
            out string error)
        {
            format = DdsFormat.Unknown;
            formatTag = "dx10_unknown";
            signedNormalized = false;
            dxgiFormatValue = 0;
            error = null;

            if (!CanRead(bytes, 128, 20))
            {
                error = "DDS DX10 header is truncated";
                return false;
            }

            uint dxgiFormat = ReadUInt32LE(bytes, 128);
            dxgiFormatValue = dxgiFormat;
            uint resourceDimension = ReadUInt32LE(bytes, 132);
            uint miscFlag = ReadUInt32LE(bytes, 136);
            uint arraySize = ReadUInt32LE(bytes, 140);

            if (resourceDimension != D3d10ResourceDimensionTexture2D)
            {
                formatTag = $"dx10_dim_{resourceDimension}";
                error = $"unsupported DX10 resource dimension: {resourceDimension}";
                return false;
            }

            if (arraySize != 1)
            {
                formatTag = $"dx10_array_{arraySize}";
                error = $"DX10 texture arrays are not supported (arraySize={arraySize})";
                return false;
            }

            if ((miscFlag & 0x4u) != 0u)
            {
                formatTag = "dx10_cubemap";
                error = "DX10 cubemap textures are not supported";
                return false;
            }

            switch (dxgiFormat)
            {
                case Dxgi_BC1_UNORM:
                case Dxgi_BC1_UNORM_SRGB:
                    format = DdsFormat.Dxt1;
                    formatTag = dxgiFormat == Dxgi_BC1_UNORM ? "dx10_bc1" : "dx10_bc1_srgb";
                    return true;
                case Dxgi_BC2_UNORM:
                case Dxgi_BC2_UNORM_SRGB:
                    format = DdsFormat.Dxt3;
                    formatTag = dxgiFormat == Dxgi_BC2_UNORM ? "dx10_bc2" : "dx10_bc2_srgb";
                    return true;
                case Dxgi_BC3_UNORM:
                case Dxgi_BC3_UNORM_SRGB:
                    format = DdsFormat.Dxt5;
                    formatTag = dxgiFormat == Dxgi_BC3_UNORM ? "dx10_bc3" : "dx10_bc3_srgb";
                    return true;
                case Dxgi_BC4_UNORM:
                    format = DdsFormat.Bc4;
                    formatTag = "dx10_bc4";
                    return true;
                case Dxgi_BC4_SNORM:
                    format = DdsFormat.Bc4;
                    formatTag = "dx10_bc4_snorm";
                    signedNormalized = true;
                    return true;
                case Dxgi_BC5_UNORM:
                    format = DdsFormat.Bc5;
                    formatTag = "dx10_bc5";
                    return true;
                case Dxgi_BC5_SNORM:
                    format = DdsFormat.Bc5;
                    formatTag = "dx10_bc5_snorm";
                    signedNormalized = true;
                    return true;
                case Dxgi_R8G8B8A8_UNORM:
                case Dxgi_R8G8B8A8_UNORM_SRGB:
                    format = DdsFormat.MaskedRgb;
                    formatTag = dxgiFormat == Dxgi_R8G8B8A8_UNORM ? "dx10_rgba8" : "dx10_rgba8_srgb";
                    return true;
                case Dxgi_B8G8R8A8_UNORM:
                case Dxgi_B8G8R8A8_UNORM_SRGB:
                    format = DdsFormat.MaskedRgb;
                    formatTag = dxgiFormat == Dxgi_B8G8R8A8_UNORM ? "dx10_bgra8" : "dx10_bgra8_srgb";
                    return true;
                default:
                    formatTag = $"dx10_dxgi_{dxgiFormat}";
                    error = $"unsupported DX10 DXGI format: {dxgiFormat}";
                    return false;
            }
        }

        static bool TryDecodeMaskedRgb(byte[] bytes, DdsHeaderInfo header, out Color32[] pixels)
        {
            pixels = null;
            int width = header.Width;
            int height = header.Height;
            int bitsPerPixel = header.BitsPerPixel;
            int bytesPerPixel = bitsPerPixel / 8;
            if (bytesPerPixel <= 0 || bitsPerPixel % 8 != 0)
                return false;

            int pixelCount = width * height;
            int required = pixelCount * bytesPerPixel;
            int offset = header.DataOffset;
            if (!CanRead(bytes, offset, required))
                return false;

            pixels = new Color32[pixelCount];
            int src = offset;
            for (int i = 0; i < pixelCount; i++)
            {
                uint value = ReadPackedPixel(bytes, src, bytesPerPixel);
                src += bytesPerPixel;

                byte r = ExpandMaskedChannel(value, header.RMask);
                byte g = ExpandMaskedChannel(value, header.GMask);
                byte b = ExpandMaskedChannel(value, header.BMask);
                byte a = header.AMask != 0 ? ExpandMaskedChannel(value, header.AMask) : (byte)255;
                pixels[i] = new Color32(r, g, b, a);
            }

            return true;
        }

        static bool TryDecodeLuminance8(byte[] bytes, int offset, int width, int height, out Color32[] pixels)
        {
            pixels = null;
            int pixelCount = width * height;
            int required = pixelCount;
            if (!CanRead(bytes, offset, required))
                return false;

            pixels = new Color32[pixelCount];
            int src = offset;
            for (int i = 0; i < pixelCount; i++)
            {
                byte l = bytes[src++];
                pixels[i] = new Color32(l, l, l, 255);
            }

            return true;
        }

        static bool TryDecodeLuminanceAlpha(byte[] bytes, DdsHeaderInfo header, out Color32[] pixels)
        {
            pixels = null;
            int width = header.Width;
            int height = header.Height;
            int bitsPerPixel = header.BitsPerPixel;
            int bytesPerPixel = bitsPerPixel / 8;
            if (bytesPerPixel <= 0 || bitsPerPixel % 8 != 0)
                return false;

            int pixelCount = width * height;
            int required = pixelCount * bytesPerPixel;
            int offset = header.DataOffset;
            if (!CanRead(bytes, offset, required))
                return false;

            pixels = new Color32[pixelCount];
            int src = offset;
            for (int i = 0; i < pixelCount; i++)
            {
                uint value = ReadPackedPixel(bytes, src, bytesPerPixel);
                src += bytesPerPixel;

                byte l = ExpandMaskedChannel(value, header.RMask);
                byte a = ExpandMaskedChannel(value, header.AMask);
                pixels[i] = new Color32(l, l, l, a);
            }

            return true;
        }

        static bool TryDecodeAlpha8(byte[] bytes, int offset, int width, int height, out Color32[] pixels)
        {
            pixels = null;
            int pixelCount = width * height;
            int required = pixelCount;
            if (!CanRead(bytes, offset, required))
                return false;

            pixels = new Color32[pixelCount];
            int src = offset;
            for (int i = 0; i < pixelCount; i++)
            {
                byte a = bytes[src++];
                pixels[i] = new Color32(255, 255, 255, a);
            }

            return true;
        }

        static bool TryDecodeBc1(byte[] bytes, int offset, int width, int height, out Color32[] pixels)
        {
            pixels = new Color32[width * height];
            int blocksX = (width + 3) / 4;
            int blocksY = (height + 3) / 4;
            int blockSize = 8;
            int required = blocksX * blocksY * blockSize;
            if (!CanRead(bytes, offset, required))
                return false;

            int src = offset;
            var palette = new Color32[4];
            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    ushort c0 = ReadUInt16LE(bytes, src + 0);
                    ushort c1 = ReadUInt16LE(bytes, src + 2);
                    uint indices = ReadUInt32LE(bytes, src + 4);
                    src += blockSize;

                    BuildBc1Palette(c0, c1, palette, forceFourColor: false);
                    WriteColorBlock(pixels, width, height, bx, by, palette, indices);
                }
            }

            return true;
        }

        static bool TryDecodeBc2(byte[] bytes, int offset, int width, int height, out Color32[] pixels)
        {
            pixels = new Color32[width * height];
            int blocksX = (width + 3) / 4;
            int blocksY = (height + 3) / 4;
            int blockSize = 16;
            int required = blocksX * blocksY * blockSize;
            if (!CanRead(bytes, offset, required))
                return false;

            int src = offset;
            var palette = new Color32[4];
            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    ulong alphaBits = ReadUInt64LE(bytes, src + 0);
                    ushort c0 = ReadUInt16LE(bytes, src + 8);
                    ushort c1 = ReadUInt16LE(bytes, src + 10);
                    uint colorIndices = ReadUInt32LE(bytes, src + 12);
                    src += blockSize;

                    BuildBc1Palette(c0, c1, palette, forceFourColor: true);
                    WriteColorBlockWithExplicitAlpha(
                        pixels,
                        width,
                        height,
                        bx,
                        by,
                        palette,
                        colorIndices,
                        alphaBits);
                }
            }

            return true;
        }

        static bool TryDecodeBc3(byte[] bytes, int offset, int width, int height, out Color32[] pixels)
        {
            pixels = new Color32[width * height];
            int blocksX = (width + 3) / 4;
            int blocksY = (height + 3) / 4;
            int blockSize = 16;
            int required = blocksX * blocksY * blockSize;
            if (!CanRead(bytes, offset, required))
                return false;

            int src = offset;
            var alphaPalette = new byte[8];
            var palette = new Color32[4];
            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    byte a0 = bytes[src + 0];
                    byte a1 = bytes[src + 1];
                    ulong alphaIndexBits =
                        (ulong)bytes[src + 2] |
                        ((ulong)bytes[src + 3] << 8) |
                        ((ulong)bytes[src + 4] << 16) |
                        ((ulong)bytes[src + 5] << 24) |
                        ((ulong)bytes[src + 6] << 32) |
                        ((ulong)bytes[src + 7] << 40);
                    ushort c0 = ReadUInt16LE(bytes, src + 8);
                    ushort c1 = ReadUInt16LE(bytes, src + 10);
                    uint colorIndices = ReadUInt32LE(bytes, src + 12);
                    src += blockSize;

                    BuildBc3AlphaPalette(a0, a1, alphaPalette);
                    BuildBc1Palette(c0, c1, palette, forceFourColor: true);
                    WriteColorBlockWithInterpolatedAlpha(
                        pixels,
                        width,
                        height,
                        bx,
                        by,
                        palette,
                        colorIndices,
                        alphaPalette,
                        alphaIndexBits);
                }
            }

            return true;
        }

        static unsafe bool TryDecodeBc4(
            byte[] bytes,
            int offset,
            int width,
            int height,
            bool signedNormalized,
            out Color32[] pixels)
        {
            pixels = null;
            int blocksX = (width + 3) / 4;
            int blocksY = (height + 3) / 4;
            int numBlocks = blocksX * blocksY;
            int sliceLen = numBlocks * 8;
            if (!CanRead(bytes, offset, sliceLen))
                return false;

            var source = new NativeArray<byte>(sliceLen, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            fixed (byte* src = bytes)
                UnsafeUtility.MemCpy(source.GetUnsafePtr(), src + offset, sliceLen);

            var output = new NativeArray<Color32>(width * height, Allocator.TempJob);

            new Bc4DecompressJob
            {
                Source           = source,
                BlocksX          = blocksX,
                Width            = width,
                Height           = height,
                SignedNormalized = signedNormalized,
                Output           = output,
            }.Schedule(numBlocks, 32).Complete();

            pixels = output.ToArray();
            source.Dispose();
            output.Dispose();
            return true;
        }

        static unsafe bool TryDecodeBc5(
            byte[] bytes,
            int offset,
            int width,
            int height,
            bool signedNormalized,
            out Color32[] pixels)
        {
            pixels = null;
            int blocksX = (width + 3) / 4;
            int blocksY = (height + 3) / 4;
            int numBlocks = blocksX * blocksY;
            int sliceLen = numBlocks * 16;
            if (!CanRead(bytes, offset, sliceLen))
                return false;

            var source = new NativeArray<byte>(sliceLen, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            fixed (byte* src = bytes)
                UnsafeUtility.MemCpy(source.GetUnsafePtr(), src + offset, sliceLen);

            var output = new NativeArray<Color32>(width * height, Allocator.TempJob);

            new Bc5DecompressJob
            {
                Source           = source,
                BlocksX          = blocksX,
                Width            = width,
                Height           = height,
                SignedNormalized = signedNormalized,
                Output           = output,
            }.Schedule(numBlocks, 32).Complete();

            pixels = output.ToArray();
            source.Dispose();
            output.Dispose();
            return true;
        }

        static void WriteColorBlock(
            Color32[] pixels,
            int width,
            int height,
            int blockX,
            int blockY,
            Color32[] palette,
            uint indices)
        {
            int startX = blockX * 4;
            int startY = blockY * 4;
            for (int py = 0; py < 4; py++)
            {
                int y = startY + py;
                if (y >= height)
                    continue;

                for (int px = 0; px < 4; px++)
                {
                    int x = startX + px;
                    if (x >= width)
                        continue;

                    int p = py * 4 + px;
                    int colorIndex = (int)((indices >> (p * 2)) & 0x3);
                    pixels[y * width + x] = palette[colorIndex];
                }
            }
        }

        static void WriteColorBlockWithExplicitAlpha(
            Color32[] pixels,
            int width,
            int height,
            int blockX,
            int blockY,
            Color32[] palette,
            uint colorIndices,
            ulong alphaBits)
        {
            int startX = blockX * 4;
            int startY = blockY * 4;
            for (int py = 0; py < 4; py++)
            {
                int y = startY + py;
                if (y >= height)
                    continue;

                for (int px = 0; px < 4; px++)
                {
                    int x = startX + px;
                    if (x >= width)
                        continue;

                    int p = py * 4 + px;
                    int colorIndex = (int)((colorIndices >> (p * 2)) & 0x3);
                    byte alpha = (byte)(((alphaBits >> (p * 4)) & 0xF) * 17);

                    var c = palette[colorIndex];
                    c.a = alpha;
                    pixels[y * width + x] = c;
                }
            }
        }

        static void WriteColorBlockWithInterpolatedAlpha(
            Color32[] pixels,
            int width,
            int height,
            int blockX,
            int blockY,
            Color32[] palette,
            uint colorIndices,
            byte[] alphaPalette,
            ulong alphaIndexBits)
        {
            int startX = blockX * 4;
            int startY = blockY * 4;
            for (int py = 0; py < 4; py++)
            {
                int y = startY + py;
                if (y >= height)
                    continue;

                for (int px = 0; px < 4; px++)
                {
                    int x = startX + px;
                    if (x >= width)
                        continue;

                    int p = py * 4 + px;
                    int colorIndex = (int)((colorIndices >> (p * 2)) & 0x3);
                    int alphaIndex = (int)((alphaIndexBits >> (p * 3)) & 0x7);
                    byte alpha = alphaPalette[alphaIndex];

                    var c = palette[colorIndex];
                    c.a = alpha;
                    pixels[y * width + x] = c;
                }
            }
        }

        static void DecodeBc4UNormBlock(byte[] bytes, int offset, byte[] outValues)
        {
            byte a0 = bytes[offset + 0];
            byte a1 = bytes[offset + 1];
            ulong bits =
                (ulong)bytes[offset + 2] |
                ((ulong)bytes[offset + 3] << 8) |
                ((ulong)bytes[offset + 4] << 16) |
                ((ulong)bytes[offset + 5] << 24) |
                ((ulong)bytes[offset + 6] << 32) |
                ((ulong)bytes[offset + 7] << 40);

            var palette = new byte[8];
            BuildBc3AlphaPalette(a0, a1, palette);
            for (int i = 0; i < 16; i++)
            {
                int idx = (int)((bits >> (i * 3)) & 0x7);
                outValues[i] = palette[idx];
            }
        }

        static void DecodeBc4SNormBlock(byte[] bytes, int offset, byte[] outValues)
        {
            int s0 = (sbyte)bytes[offset + 0];
            int s1 = (sbyte)bytes[offset + 1];
            ulong bits =
                (ulong)bytes[offset + 2] |
                ((ulong)bytes[offset + 3] << 8) |
                ((ulong)bytes[offset + 4] << 16) |
                ((ulong)bytes[offset + 5] << 24) |
                ((ulong)bytes[offset + 6] << 32) |
                ((ulong)bytes[offset + 7] << 40);

            var palette = new int[8];
            BuildBc4SNormPalette(s0, s1, palette);
            for (int i = 0; i < 16; i++)
            {
                int idx = (int)((bits >> (i * 3)) & 0x7);
                outValues[i] = SignedNormToByte(palette[idx]);
            }
        }

        static void BuildBc4SNormPalette(int s0, int s1, int[] palette)
        {
            palette[0] = s0;
            palette[1] = s1;
            if (s0 > s1)
            {
                palette[2] = (6 * s0 + 1 * s1) / 7;
                palette[3] = (5 * s0 + 2 * s1) / 7;
                palette[4] = (4 * s0 + 3 * s1) / 7;
                palette[5] = (3 * s0 + 4 * s1) / 7;
                palette[6] = (2 * s0 + 5 * s1) / 7;
                palette[7] = (1 * s0 + 6 * s1) / 7;
            }
            else
            {
                palette[2] = (4 * s0 + 1 * s1) / 5;
                palette[3] = (3 * s0 + 2 * s1) / 5;
                palette[4] = (2 * s0 + 3 * s1) / 5;
                palette[5] = (1 * s0 + 4 * s1) / 5;
                palette[6] = -128;
                palette[7] = 127;
            }
        }

        static byte SignedNormToByte(int value)
        {
            if (value < -128)
                value = -128;
            if (value > 127)
                value = 127;
            return (byte)(value + 128);
        }

        static void WriteSingleChannelBlock(
            Color32[] pixels,
            int width,
            int height,
            int blockX,
            int blockY,
            byte[] channelValues,
            int channel)
        {
            int startX = blockX * 4;
            int startY = blockY * 4;
            for (int py = 0; py < 4; py++)
            {
                int y = startY + py;
                if (y >= height)
                    continue;

                for (int px = 0; px < 4; px++)
                {
                    int x = startX + px;
                    if (x >= width)
                        continue;

                    int i = py * 4 + px;
                    byte v = channelValues[i];
                    var c = new Color32(v, v, v, 255);
                    if (channel == 1)
                        c = new Color32(0, v, 0, 255);
                    pixels[y * width + x] = c;
                }
            }
        }

        static void WriteRgBlock(
            Color32[] pixels,
            int width,
            int height,
            int blockX,
            int blockY,
            byte[] reds,
            byte[] greens)
        {
            int startX = blockX * 4;
            int startY = blockY * 4;
            for (int py = 0; py < 4; py++)
            {
                int y = startY + py;
                if (y >= height)
                    continue;

                for (int px = 0; px < 4; px++)
                {
                    int x = startX + px;
                    if (x >= width)
                        continue;

                    int i = py * 4 + px;
                    pixels[y * width + x] = new Color32(reds[i], greens[i], 0, 255);
                }
            }
        }

        static void BuildBc1Palette(ushort c0, ushort c1, Color32[] palette, bool forceFourColor)
        {
            palette[0] = Rgb565ToColor(c0, 255);
            palette[1] = Rgb565ToColor(c1, 255);

            if (c0 > c1 || forceFourColor)
            {
                palette[2] = LerpColor(palette[0], palette[1], 2, 1, 3);
                palette[3] = LerpColor(palette[0], palette[1], 1, 2, 3);
            }
            else
            {
                palette[2] = LerpColor(palette[0], palette[1], 1, 1, 2);
                palette[3] = new Color32(0, 0, 0, 0);
            }
        }

        static void BuildBc3AlphaPalette(byte a0, byte a1, byte[] palette)
        {
            palette[0] = a0;
            palette[1] = a1;
            if (a0 > a1)
            {
                palette[2] = (byte)((6 * a0 + 1 * a1) / 7);
                palette[3] = (byte)((5 * a0 + 2 * a1) / 7);
                palette[4] = (byte)((4 * a0 + 3 * a1) / 7);
                palette[5] = (byte)((3 * a0 + 4 * a1) / 7);
                palette[6] = (byte)((2 * a0 + 5 * a1) / 7);
                palette[7] = (byte)((1 * a0 + 6 * a1) / 7);
            }
            else
            {
                palette[2] = (byte)((4 * a0 + 1 * a1) / 5);
                palette[3] = (byte)((3 * a0 + 2 * a1) / 5);
                palette[4] = (byte)((2 * a0 + 3 * a1) / 5);
                palette[5] = (byte)((1 * a0 + 4 * a1) / 5);
                palette[6] = 0;
                palette[7] = 255;
            }
        }

        static Color32 LerpColor(Color32 a, Color32 b, int wa, int wb, int div)
        {
            return new Color32(
                (byte)((a.r * wa + b.r * wb) / div),
                (byte)((a.g * wa + b.g * wb) / div),
                (byte)((a.b * wa + b.b * wb) / div),
                255);
        }

        static Color32 Rgb565ToColor(ushort c, byte alpha)
        {
            int r5 = (c >> 11) & 0x1F;
            int g6 = (c >> 5) & 0x3F;
            int b5 = c & 0x1F;

            byte r = (byte)((r5 * 255 + 15) / 31);
            byte g = (byte)((g6 * 255 + 31) / 63);
            byte b = (byte)((b5 * 255 + 15) / 31);
            return new Color32(r, g, b, alpha);
        }

        static uint ReadPackedPixel(byte[] bytes, int offset, int bytesPerPixel)
        {
            switch (bytesPerPixel)
            {
                case 2:
                    return ReadUInt16LE(bytes, offset);
                case 3:
                    return (uint)(
                        bytes[offset] |
                        (bytes[offset + 1] << 8) |
                        (bytes[offset + 2] << 16));
                case 4:
                    return ReadUInt32LE(bytes, offset);
                default:
                    return 0u;
            }
        }

        static byte ExpandMaskedChannel(uint value, uint mask)
        {
            if (mask == 0)
                return 0;

            int shift = CountTrailingZeroBits(mask);
            int bits = CountSetBits(mask >> shift);
            if (bits <= 0)
                return 0;

            uint max = (1u << bits) - 1u;
            uint raw = (value & mask) >> shift;
            return (byte)((raw * 255u + (max / 2u)) / max);
        }

        static int CountTrailingZeroBits(uint value)
        {
            int count = 0;
            while ((value & 1u) == 0u && count < 32)
            {
                value >>= 1;
                count++;
            }

            return count;
        }

        static int CountSetBits(uint value)
        {
            int count = 0;
            while (value != 0u)
            {
                count += (int)(value & 1u);
                value >>= 1;
            }

            return count;
        }

        static bool ContainsTransparentPixels(Color32[] pixels)
        {
            if (pixels == null)
                return false;

            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].a < 255)
                    return true;

            return false;
        }

        static bool CanRead(byte[] data, int start, int count)
        {
            if (data == null || start < 0 || count < 0)
                return false;
            return start <= data.Length - count;
        }

        static int GetCompressedPayloadLength(DdsFormat format, int width, int height, int mipCount)
        {
            int blockSize;
            switch (format)
            {
                case DdsFormat.Dxt1:
                    blockSize = 8;
                    break;
                case DdsFormat.Dxt5:
                    blockSize = 16;
                    break;
                default:
                    return -1;
            }

            int total = 0;
            int w = Math.Max(1, width);
            int h = Math.Max(1, height);
            int mips = Math.Max(1, mipCount);

            for (int i = 0; i < mips; i++)
            {
                int blocksX = (w + 3) / 4;
                int blocksY = (h + 3) / 4;
                total += blocksX * blocksY * blockSize;
                w = Math.Max(1, w >> 1);
                h = Math.Max(1, h >> 1);
            }

            return total;
        }

        static bool TryMapLegacyD3dFormat(
            uint fourCC,
            out DdsFormat format,
            out string formatTag,
            out uint bitCount,
            out uint rMask,
            out uint gMask,
            out uint bMask,
            out uint aMask)
        {
            format = DdsFormat.Unknown;
            formatTag = "legacy_unknown";
            bitCount = 0;
            rMask = 0;
            gMask = 0;
            bMask = 0;
            aMask = 0;

            switch (fourCC)
            {
                case D3dFmt_A8R8G8B8:
                    format = DdsFormat.MaskedRgb;
                    formatTag = "legacy_d3dfmt_a8r8g8b8";
                    bitCount = 32;
                    rMask = 0x00ff0000;
                    gMask = 0x0000ff00;
                    bMask = 0x000000ff;
                    aMask = 0xff000000;
                    return true;
                case D3dFmt_X8R8G8B8:
                    format = DdsFormat.MaskedRgb;
                    formatTag = "legacy_d3dfmt_x8r8g8b8";
                    bitCount = 32;
                    rMask = 0x00ff0000;
                    gMask = 0x0000ff00;
                    bMask = 0x000000ff;
                    aMask = 0;
                    return true;
                case D3dFmt_A8B8G8R8:
                    format = DdsFormat.MaskedRgb;
                    formatTag = "legacy_d3dfmt_a8b8g8r8";
                    bitCount = 32;
                    rMask = 0x000000ff;
                    gMask = 0x0000ff00;
                    bMask = 0x00ff0000;
                    aMask = 0xff000000;
                    return true;
                case D3dFmt_R5G6B5:
                    format = DdsFormat.MaskedRgb;
                    formatTag = "legacy_d3dfmt_r5g6b5";
                    bitCount = 16;
                    rMask = 0xF800;
                    gMask = 0x07E0;
                    bMask = 0x001F;
                    aMask = 0;
                    return true;
                case D3dFmt_A1R5G5B5:
                    format = DdsFormat.MaskedRgb;
                    formatTag = "legacy_d3dfmt_a1r5g5b5";
                    bitCount = 16;
                    rMask = 0x7C00;
                    gMask = 0x03E0;
                    bMask = 0x001F;
                    aMask = 0x8000;
                    return true;
                case D3dFmt_A4R4G4B4:
                    format = DdsFormat.MaskedRgb;
                    formatTag = "legacy_d3dfmt_a4r4g4b4";
                    bitCount = 16;
                    rMask = 0x0F00;
                    gMask = 0x00F0;
                    bMask = 0x000F;
                    aMask = 0xF000;
                    return true;
                case D3dFmt_A8:
                    format = DdsFormat.Alpha8;
                    formatTag = "legacy_d3dfmt_a8";
                    bitCount = 8;
                    return true;
                case D3dFmt_L8:
                    format = DdsFormat.Luminance8;
                    formatTag = "legacy_d3dfmt_l8";
                    bitCount = 8;
                    rMask = 0xFF;
                    return true;
                case D3dFmt_A8L8:
                    format = DdsFormat.LuminanceAlpha;
                    formatTag = "legacy_d3dfmt_a8l8";
                    bitCount = 16;
                    rMask = 0x00FF;
                    aMask = 0xFF00;
                    return true;
                case D3dFmt_A4L4:
                    format = DdsFormat.LuminanceAlpha;
                    formatTag = "legacy_d3dfmt_a4l4";
                    bitCount = 8;
                    rMask = 0x0F;
                    aMask = 0xF0;
                    return true;
                default:
                    return false;
            }
        }

        static ushort ReadUInt16LE(byte[] data, int index)
        {
            return (ushort)(data[index] | (data[index + 1] << 8));
        }

        static uint ReadUInt32LE(byte[] data, int index)
        {
            return (uint)(
                data[index] |
                (data[index + 1] << 8) |
                (data[index + 2] << 16) |
                (data[index + 3] << 24));
        }

        static ulong ReadUInt64LE(byte[] data, int index)
        {
            return
                (ulong)data[index] |
                ((ulong)data[index + 1] << 8) |
                ((ulong)data[index + 2] << 16) |
                ((ulong)data[index + 3] << 24) |
                ((ulong)data[index + 4] << 32) |
                ((ulong)data[index + 5] << 40) |
                ((ulong)data[index + 6] << 48) |
                ((ulong)data[index + 7] << 56);
        }

        static int ReadInt32LE(byte[] data, int index)
        {
            return unchecked((int)ReadUInt32LE(data, index));
        }
    }
}
