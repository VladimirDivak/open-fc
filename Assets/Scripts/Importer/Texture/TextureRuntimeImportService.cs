using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenFarCry.FileSystem;
using UnityEngine;

namespace OpenFarCry.Importer.Texture
{
    public sealed class TextureRuntimeImportService
    {
        const int MaxUniqueWarningLogs = 32;

        public readonly struct LoadedTextureInfo
        {
            public readonly string NormalizedVirtualPath;
            public readonly Texture2D Texture;
            public readonly bool HasAlphaChannel;
            public readonly bool HasTransparentPixels;

            public LoadedTextureInfo(
                string normalizedVirtualPath,
                Texture2D texture,
                bool hasAlphaChannel,
                bool hasTransparentPixels)
            {
                NormalizedVirtualPath = normalizedVirtualPath;
                Texture = texture;
                HasAlphaChannel = hasAlphaChannel;
                HasTransparentPixels = hasTransparentPixels;
            }
        }

        public readonly struct RuntimeDiagnostics
        {
            public readonly TextureRuntimeScopedCache.Stats CacheStats;
            public readonly int SuccessfulLoadCount;
            public readonly int UnsupportedPathCount;
            public readonly int ReadFailureCount;
            public readonly int EmptyPayloadCount;
            public readonly int DecodeFailureCount;
            public readonly int DdsDecodeSuccessCount;
            public readonly int DdsDecodeFailureCount;
            public readonly int MissingPathWarningCount;
            public readonly int MissingPathWarningSuppressedCount;
            public readonly int DecodeWarningCount;
            public readonly int DecodeWarningSuppressedCount;
            public readonly string DdsFormatSuccessReport;
            public readonly string DdsFormatFailureReport;

            public RuntimeDiagnostics(
                TextureRuntimeScopedCache.Stats cacheStats,
                int successfulLoadCount,
                int unsupportedPathCount,
                int readFailureCount,
                int emptyPayloadCount,
                int decodeFailureCount,
                int ddsDecodeSuccessCount,
                int ddsDecodeFailureCount,
                int missingPathWarningCount,
                int missingPathWarningSuppressedCount,
                int decodeWarningCount,
                int decodeWarningSuppressedCount,
                string ddsFormatSuccessReport,
                string ddsFormatFailureReport)
            {
                CacheStats = cacheStats;
                SuccessfulLoadCount = successfulLoadCount;
                UnsupportedPathCount = unsupportedPathCount;
                ReadFailureCount = readFailureCount;
                EmptyPayloadCount = emptyPayloadCount;
                DecodeFailureCount = decodeFailureCount;
                DdsDecodeSuccessCount = ddsDecodeSuccessCount;
                DdsDecodeFailureCount = ddsDecodeFailureCount;
                MissingPathWarningCount = missingPathWarningCount;
                MissingPathWarningSuppressedCount = missingPathWarningSuppressedCount;
                DecodeWarningCount = decodeWarningCount;
                DecodeWarningSuppressedCount = decodeWarningSuppressedCount;
                DdsFormatSuccessReport = ddsFormatSuccessReport;
                DdsFormatFailureReport = ddsFormatFailureReport;
            }
        }

        readonly IResourceImportService _resourceService;
        readonly TextureRuntimeScopedCache _runtimeCache;
        readonly TextureRuntimeImportOptions _defaultOptions;
        readonly Dictionary<string, int> _ddsFormatSuccessCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> _ddsFormatFailureCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _warnedMissingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _warnedDecodePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Guards every field above plus the plain counters below: TryLoadWithInfoAsync's
        // continuations are not guaranteed to resume on the main thread, so the fan-out
        // preload in CgfMaterialImportService can touch these from multiple pool threads
        // concurrently.
        readonly object _diagnosticsLock = new object();
        int _successfulLoadCount;
        int _unsupportedPathCount;
        int _readFailureCount;
        int _emptyPayloadCount;
        int _decodeFailureCount;
        int _ddsDecodeSuccessCount;
        int _ddsDecodeFailureCount;
        int _missingPathWarningCount;
        int _missingPathWarningSuppressedCount;
        int _decodeWarningCount;
        int _decodeWarningSuppressedCount;

        public TextureRuntimeImportService(
            IResourceImportService resourceService = null,
            TextureRuntimeScopedCache runtimeCache = null,
            TextureRuntimeImportOptions defaultOptions = default)
        {
            _resourceService = resourceService ?? TextureResourceImportService.Instance;
            _runtimeCache = runtimeCache ?? new TextureRuntimeScopedCache();
            _defaultOptions = defaultOptions.IsConfigured
                ? defaultOptions
                : TextureRuntimeImportOptions.Default;
        }

        public TextureRuntimeScopedCache RuntimeCache => _runtimeCache;

        public Texture2D Load(string virtualPath, TextureRuntimeImportOptions options = default)
        {
            TryLoad(virtualPath, out var texture, scopeId: null, options: options);
            return texture;
        }

        public Texture2D Load(string virtualPath, string scopeId, TextureRuntimeImportOptions options = default)
        {
            TryLoad(virtualPath, out var texture, scopeId, options);
            return texture;
        }

        public bool TryLoad(string virtualPath, out Texture2D texture, TextureRuntimeImportOptions options = default)
        {
            return TryLoad(virtualPath, out texture, scopeId: null, options: options);
        }

        public bool TryLoad(
            string virtualPath,
            out Texture2D texture,
            string scopeId,
            TextureRuntimeImportOptions options = default)
        {
            if (TryLoadWithInfo(virtualPath, out var loadedInfo, scopeId, options))
            {
                texture = loadedInfo.Texture;
                return true;
            }

            texture = null;
            return false;
        }

        public bool TryLoadWithInfo(
            string virtualPath,
            out LoadedTextureInfo loadedInfo,
            TextureRuntimeImportOptions options = default)
        {
            return TryLoadWithInfo(virtualPath, out loadedInfo, scopeId: null, options: options);
        }

        public bool TryLoadWithInfo(
            string virtualPath,
            out LoadedTextureInfo loadedInfo,
            string scopeId,
            TextureRuntimeImportOptions options = default)
        {
            loadedInfo = default;
            Texture2D texture = null;

            if (string.IsNullOrWhiteSpace(virtualPath))
                return false;

            if (!_resourceService.IsSupportedVirtualPath(virtualPath))
            {
                lock (_diagnosticsLock) { _unsupportedPathCount++; }
                return false;
            }

            string normalizedVirtualPath = ImportAssetPaths.NormalizeVirtualPath(virtualPath);
            var resolvedOptions = ResolveOptions(options);
            string cacheKey = BuildRuntimeCacheKey(normalizedVirtualPath, resolvedOptions);

            if (resolvedOptions.UseRuntimeMemoryCache &&
                _runtimeCache.TryRetain(cacheKey, scopeId, out _, out loadedInfo))
            {
                return true;
            }

            byte[] bytes;
            try
            {
                bytes = _resourceService.LoadRuntimeResourceBytes(normalizedVirtualPath);
            }
            catch (Exception e)
            {
                lock (_diagnosticsLock) { _readFailureCount++; }
                LogMissingPath(normalizedVirtualPath, e.Message);
                return false;
            }

            if (bytes == null || bytes.Length == 0)
            {
                lock (_diagnosticsLock) { _emptyPayloadCount++; }
                LogMissingPath(normalizedVirtualPath, "empty payload");
                return false;
            }

            if (!TryDecodeTexture(
                    normalizedVirtualPath,
                    bytes,
                    resolvedOptions,
                    out texture,
                    out bool hasAlphaChannel,
                    out bool hasTransparentPixels,
                    out string ddsFormatTag,
                    out string decodeError,
                    out bool usedNativeDdsPath))
            {
                lock (_diagnosticsLock)
                {
                    _decodeFailureCount++;
                    if (Path.GetExtension(normalizedVirtualPath).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                    {
                        _ddsDecodeFailureCount++;
                        IncrementFormatCount(_ddsFormatFailureCounts, ddsFormatTag);
                    }
                }
                LogDecodeFailure(normalizedVirtualPath, decodeError);
                return false;
            }

            // Stamp the decoded texture with its source virtual path so material/diagnostic
            // tooling can identify which file a bound texture came from.
            if (texture != null && string.IsNullOrEmpty(texture.name))
                texture.name = normalizedVirtualPath;

            loadedInfo = new LoadedTextureInfo(
                normalizedVirtualPath: normalizedVirtualPath,
                texture: texture,
                hasAlphaChannel: hasAlphaChannel,
                hasTransparentPixels: hasTransparentPixels);

            // Store returns the canonical cached info: if a concurrent load already
            // cached this path, adopt that texture so every caller shares one instance.
            if (resolvedOptions.UseRuntimeMemoryCache && texture != null)
            {
                loadedInfo = _runtimeCache.Store(cacheKey, scopeId, texture, loadedInfo);
                texture = loadedInfo.Texture;
            }

            lock (_diagnosticsLock)
            {
                if (Path.GetExtension(normalizedVirtualPath).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                {
                    _ddsDecodeSuccessCount++;
                    string successTag = usedNativeDdsPath ? $"{ddsFormatTag}|native" : ddsFormatTag;
                    IncrementFormatCount(_ddsFormatSuccessCounts, successTag);
                }

                _successfulLoadCount++;
            }
            return texture != null;
        }

        public void ClearRuntimeCache()
        {
            _runtimeCache.Clear();
            lock (_diagnosticsLock)
            {
                _ddsFormatSuccessCounts.Clear();
                _ddsFormatFailureCounts.Clear();
                _warnedMissingPaths.Clear();
                _warnedDecodePaths.Clear();
                _successfulLoadCount = 0;
                _unsupportedPathCount = 0;
                _readFailureCount = 0;
                _emptyPayloadCount = 0;
                _decodeFailureCount = 0;
                _ddsDecodeSuccessCount = 0;
                _ddsDecodeFailureCount = 0;
                _missingPathWarningCount = 0;
                _missingPathWarningSuppressedCount = 0;
                _decodeWarningCount = 0;
                _decodeWarningSuppressedCount = 0;
            }
        }

        public void ReleaseLevelScope(string scopeId)
        {
            _runtimeCache.ReleaseLevelScope(scopeId);
        }

        public int TrimUnused()
        {
            return _runtimeCache.TrimUnused();
        }

        public TextureRuntimeScopedCache.Stats GetStats()
        {
            return _runtimeCache.GetStats();
        }

        public async UniTask<(bool Success, LoadedTextureInfo Info)> TryLoadWithInfoAsync(
            string virtualPath,
            string scopeId,
            CancellationToken cancellationToken = default,
            TextureRuntimeImportOptions options = default)
        {
            if (string.IsNullOrWhiteSpace(virtualPath))
                return (false, default);

            if (!_resourceService.IsSupportedVirtualPath(virtualPath))
            {
                lock (_diagnosticsLock) { _unsupportedPathCount++; }
                return (false, default);
            }

            string normalizedVirtualPath = ImportAssetPaths.NormalizeVirtualPath(virtualPath);
            var resolvedOptions = ResolveOptions(options);
            string cacheKey = BuildRuntimeCacheKey(normalizedVirtualPath, resolvedOptions);

            if (resolvedOptions.UseRuntimeMemoryCache &&
                _runtimeCache.TryRetain(cacheKey, scopeId, out _, out var cachedInfo))
            {
                return (true, cachedInfo);
            }

            byte[] bytes;
            try
            {
                bytes = await FcFileSystem.ReadAllBytesAsync(normalizedVirtualPath, cancellationToken);
            }
            catch (Exception e)
            {
                lock (_diagnosticsLock) { _readFailureCount++; }
                LogMissingPath(normalizedVirtualPath, e.Message);
                return (false, default);
            }

            if (bytes == null || bytes.Length == 0)
            {
                lock (_diagnosticsLock) { _emptyPayloadCount++; }
                LogMissingPath(normalizedVirtualPath, "empty payload");
                return (false, default);
            }

            var decodeResult = await TryDecodeTextureAsync(
                normalizedVirtualPath,
                bytes,
                resolvedOptions,
                cancellationToken);

            if (!decodeResult.Success)
            {
                lock (_diagnosticsLock)
                {
                    _decodeFailureCount++;
                    if (Path.GetExtension(normalizedVirtualPath).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                    {
                        _ddsDecodeFailureCount++;
                        IncrementFormatCount(_ddsFormatFailureCounts, decodeResult.DdsFormatTag);
                    }
                }

                LogDecodeFailure(normalizedVirtualPath, decodeResult.DecodeError);
                return (false, default);
            }

            var texture = decodeResult.Texture;
            bool hasAlphaChannel = decodeResult.HasAlphaChannel;
            bool hasTransparentPixels = decodeResult.HasTransparentPixels;
            string ddsFormatTag = decodeResult.DdsFormatTag;
            bool usedNativeDdsPath = decodeResult.UsedNativeDdsPath;

            // Stamp the decoded texture with its source virtual path so material/diagnostic
            // tooling can identify which file a bound texture came from.
            if (texture != null && string.IsNullOrEmpty(texture.name))
                texture.name = normalizedVirtualPath;

            var loadedInfo = new LoadedTextureInfo(
                normalizedVirtualPath: normalizedVirtualPath,
                texture: texture,
                hasAlphaChannel: hasAlphaChannel,
                hasTransparentPixels: hasTransparentPixels);

            // Store returns the canonical cached info: if a concurrent load already
            // cached this path, adopt that texture so every caller shares one instance.
            if (resolvedOptions.UseRuntimeMemoryCache && texture != null)
                loadedInfo = _runtimeCache.Store(cacheKey, scopeId, texture, loadedInfo);

            lock (_diagnosticsLock)
            {
                if (Path.GetExtension(normalizedVirtualPath).Equals(".dds", StringComparison.OrdinalIgnoreCase))
                {
                    _ddsDecodeSuccessCount++;
                    string successTag = usedNativeDdsPath ? $"{ddsFormatTag}|native" : ddsFormatTag;
                    IncrementFormatCount(_ddsFormatSuccessCounts, successTag);
                }

                _successfulLoadCount++;
            }
            return (true, loadedInfo);
        }

        async UniTask<(bool Success, Texture2D Texture, bool HasAlphaChannel, bool HasTransparentPixels, string DdsFormatTag, string DecodeError, bool UsedNativeDdsPath)> TryDecodeTextureAsync(
            string normalizedVirtualPath,
            byte[] bytes,
            TextureRuntimeImportOptions options,
            CancellationToken cancellationToken)
        {
            string ext = Path.GetExtension(normalizedVirtualPath)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
                return (false, null, false, false, "-", "missing extension", false);

            switch (ext)
            {
                case ".dds":
                    if (DdsRuntimeDecoder.TryLoadNative(
                            bytes,
                            options,
                            out var nativeTexture,
                            out bool nativeHasAlpha,
                            out string nativeTag,
                            out string nativeError))
                    {
                        return (true, nativeTexture, nativeHasAlpha, false, nativeTag, null, true);
                    }

                    await UniTask.SwitchToThreadPool();
                    cancellationToken.ThrowIfCancellationRequested();
                    bool ddsDecoded = DdsRuntimeDecoder.TryDecodeToRgba(
                        bytes,
                        out int ddsWidth,
                        out int ddsHeight,
                        out var ddsPixels,
                        out bool ddsHasAlpha,
                        out bool ddsHasTransparent,
                        out string ddsTag,
                        out string ddsError);
                    await UniTask.SwitchToMainThread(cancellationToken);
                    if (!ddsDecoded)
                    {
                        string error = string.IsNullOrEmpty(ddsError) ? nativeError : ddsError;
                        return (false, null, false, false, ddsTag, error, false);
                    }

                    return (
                        true,
                        CreateTexture(ddsWidth, ddsHeight, ddsPixels, options),
                        ddsHasAlpha,
                        ddsHasTransparent,
                        ddsTag,
                        null,
                        false);

                case ".bmp":
                    await UniTask.SwitchToThreadPool();
                    cancellationToken.ThrowIfCancellationRequested();
                    bool bmpDecoded = TryDecodeBmpPixels(
                        bytes,
                        out int bmpWidth,
                        out int bmpHeight,
                        out var bmpPixels,
                        out bool bmpHasAlpha,
                        out bool bmpHasTransparent);
                    await UniTask.SwitchToMainThread(cancellationToken);
                    if (!bmpDecoded)
                        return (false, null, false, false, "-", "BMP decode failed", false);

                    return (
                        true,
                        CreateTexture(bmpWidth, bmpHeight, bmpPixels, options),
                        bmpHasAlpha,
                        bmpHasTransparent,
                        "-",
                        null,
                        false);

                case ".tga":
                    await UniTask.SwitchToThreadPool();
                    cancellationToken.ThrowIfCancellationRequested();
                    bool tgaDecoded = TryDecodeTgaPixels(
                        bytes,
                        out int tgaWidth,
                        out int tgaHeight,
                        out var tgaPixels,
                        out bool tgaHasAlpha,
                        out bool tgaHasTransparent);
                    await UniTask.SwitchToMainThread(cancellationToken);
                    if (!tgaDecoded)
                        return (false, null, false, false, "-", "TGA decode failed", false);

                    return (
                        true,
                        CreateTexture(tgaWidth, tgaHeight, tgaPixels, options),
                        tgaHasAlpha,
                        tgaHasTransparent,
                        "-",
                        null,
                        false);

                case ".jpg":
                case ".jpeg":
                    if (TryDecodeJpeg(bytes, options, out var jpegTexture, out bool jpgAlpha, out bool jpgTransparent))
                        return (true, jpegTexture, jpgAlpha, jpgTransparent, "-", null, false);
                    return (false, null, false, false, "-", "JPEG decode failed", false);

                default:
                    return (false, null, false, false, "-", $"unsupported extension '{ext}'", false);
            }
        }

        public RuntimeDiagnostics GetRuntimeDiagnostics()
        {
            var cacheStats = _runtimeCache.GetStats();
            lock (_diagnosticsLock)
            {
                return new RuntimeDiagnostics(
                    cacheStats: cacheStats,
                    successfulLoadCount: _successfulLoadCount,
                    unsupportedPathCount: _unsupportedPathCount,
                    readFailureCount: _readFailureCount,
                    emptyPayloadCount: _emptyPayloadCount,
                    decodeFailureCount: _decodeFailureCount,
                    ddsDecodeSuccessCount: _ddsDecodeSuccessCount,
                    ddsDecodeFailureCount: _ddsDecodeFailureCount,
                    missingPathWarningCount: _missingPathWarningCount,
                    missingPathWarningSuppressedCount: _missingPathWarningSuppressedCount,
                    decodeWarningCount: _decodeWarningCount,
                    decodeWarningSuppressedCount: _decodeWarningSuppressedCount,
                    ddsFormatSuccessReport: BuildFormatReport(_ddsFormatSuccessCounts),
                    ddsFormatFailureReport: BuildFormatReport(_ddsFormatFailureCounts));
            }
        }

        public string BuildRuntimeDebugReport()
        {
            var diag = GetRuntimeDiagnostics();
            return
                $"TextureRuntimeCache: entries={diag.CacheStats.EntryCount}, hit/miss={diag.CacheStats.HitCount}/{diag.CacheStats.MissCount}. " +
                $"Loads: ok={diag.SuccessfulLoadCount}, unsupported={diag.UnsupportedPathCount}, readFail={diag.ReadFailureCount}, empty={diag.EmptyPayloadCount}, decodeFail={diag.DecodeFailureCount}. " +
                $"DDS: ok={diag.DdsDecodeSuccessCount}, fail={diag.DdsDecodeFailureCount}, successByFormat=[{diag.DdsFormatSuccessReport}], failByFormat=[{diag.DdsFormatFailureReport}]. " +
                $"Warnings: missing={diag.MissingPathWarningCount}(suppressed={diag.MissingPathWarningSuppressedCount}), " +
                $"decode={diag.DecodeWarningCount}(suppressed={diag.DecodeWarningSuppressedCount}).";
        }

        TextureRuntimeImportOptions ResolveOptions(TextureRuntimeImportOptions options)
        {
            return options.IsConfigured ? options : _defaultOptions;
        }

        static string BuildRuntimeCacheKey(string normalizedVirtualPath, TextureRuntimeImportOptions options)
        {
            return
                $"{normalizedVirtualPath}|lin:{(options.LinearColorSpace ? 1 : 0)}|mip:{(options.GenerateMipmaps ? 1 : 0)}|nr:{(options.MarkNonReadable ? 1 : 0)}";
        }

        static bool TryDecodeTexture(
            string normalizedVirtualPath,
            byte[] bytes,
            TextureRuntimeImportOptions options,
            out Texture2D texture,
            out bool hasAlphaChannel,
            out bool hasTransparentPixels,
            out string ddsFormatTag,
            out string decodeError,
            out bool usedNativeDdsPath)
        {
            texture = null;
            hasAlphaChannel = false;
            hasTransparentPixels = false;
            ddsFormatTag = "-";
            decodeError = "unknown decode error";
            usedNativeDdsPath = false;

            string ext = Path.GetExtension(normalizedVirtualPath);
            if (string.IsNullOrEmpty(ext))
            {
                decodeError = "missing extension";
                return false;
            }

            switch (ext.ToLowerInvariant())
            {
                case ".dds":
                    if (DdsRuntimeDecoder.TryLoadNative(
                            bytes,
                            options,
                            out texture,
                            out hasAlphaChannel,
                            out ddsFormatTag,
                            out decodeError))
                    {
                        usedNativeDdsPath = true;
                        hasTransparentPixels = false;
                        return true;
                    }

                    return DdsRuntimeDecoder.TryDecode(
                        bytes,
                        options,
                        out texture,
                        out hasAlphaChannel,
                        out hasTransparentPixels,
                        out ddsFormatTag,
                        out decodeError);
                case ".bmp":
                    if (TryDecodeBmp(bytes, options, out texture, out hasAlphaChannel, out hasTransparentPixels))
                        return true;
                    decodeError = "BMP decode failed";
                    return false;
                case ".tga":
                    if (TryDecodeTga(bytes, options, out texture, out hasAlphaChannel, out hasTransparentPixels))
                        return true;
                    decodeError = "TGA decode failed";
                    return false;
                case ".jpg":
                case ".jpeg":
                    if (TryDecodeJpeg(bytes, options, out texture, out hasAlphaChannel, out hasTransparentPixels))
                        return true;
                    decodeError = "JPEG decode failed";
                    return false;
                default:
                    decodeError = $"unsupported extension '{ext}'";
                    return false;
            }
        }

        static bool TryDecodeJpeg(
            byte[] bytes,
            TextureRuntimeImportOptions options,
            out Texture2D texture,
            out bool hasAlphaChannel,
            out bool hasTransparentPixels)
        {
            texture = new Texture2D(2, 2, TextureFormat.RGB24, mipChain: options.GenerateMipmaps, linear: options.LinearColorSpace);
            hasAlphaChannel = false;
            hasTransparentPixels = false;

            try
            {
                bool ok = ImageConversion.LoadImage(texture, bytes, markNonReadable: options.MarkNonReadable);
                if (!ok)
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(texture);
                    else
                        UnityEngine.Object.DestroyImmediate(texture);
                    texture = null;
                    return false;
                }

                return true;
            }
            catch
            {
                if (texture != null)
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(texture);
                    else
                        UnityEngine.Object.DestroyImmediate(texture);
                }
                texture = null;
                return false;
            }
        }

        static bool TryDecodeBmp(
            byte[] bytes,
            TextureRuntimeImportOptions options,
            out Texture2D texture,
            out bool hasAlphaChannel,
            out bool hasTransparentPixels)
        {
            if (!TryDecodeBmpPixels(
                    bytes,
                    out int width,
                    out int height,
                    out var pixels,
                    out hasAlphaChannel,
                    out hasTransparentPixels))
            {
                texture = null;
                return false;
            }

            texture = CreateTexture(width, height, pixels, options);
            return texture != null;
        }

        static bool TryDecodeTga(
            byte[] bytes,
            TextureRuntimeImportOptions options,
            out Texture2D texture,
            out bool hasAlphaChannel,
            out bool hasTransparentPixels)
        {
            if (!TryDecodeTgaPixels(
                    bytes,
                    out int width,
                    out int height,
                    out var pixels,
                    out hasAlphaChannel,
                    out hasTransparentPixels))
            {
                texture = null;
                return false;
            }

            texture = CreateTexture(width, height, pixels, options);
            return texture != null;
        }

        static bool TryDecodeBmpPixels(
            byte[] bytes,
            out int width,
            out int height,
            out Color32[] pixels,
            out bool hasAlphaChannel,
            out bool hasTransparentPixels)
        {
            width = 0;
            height = 0;
            pixels = null;
            hasAlphaChannel = false;
            hasTransparentPixels = false;

            if (bytes == null || bytes.Length < 54)
                return false;
            if (bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
                return false;

            int pixelOffset = ReadInt32LE(bytes, 10);
            int dibHeaderSize = ReadInt32LE(bytes, 14);
            width = ReadInt32LE(bytes, 18);
            int rawHeight = ReadInt32LE(bytes, 22);
            ushort planes = ReadUInt16LE(bytes, 26);
            ushort bitsPerPixel = ReadUInt16LE(bytes, 28);
            uint compression = ReadUInt32LE(bytes, 30);

            if (dibHeaderSize < 40 || width <= 0 || rawHeight == 0 || planes != 1)
                return false;
            if (bitsPerPixel != 24 && bitsPerPixel != 32)
                return false;
            if (compression != 0)
                return false;

            height = rawHeight > 0 ? rawHeight : -rawHeight;
            int bytesPerPixel = bitsPerPixel / 8;
            int rowBytes = width * bytesPerPixel;
            int rowStride = ((rowBytes + 3) / 4) * 4;
            int requiredBytes = pixelOffset + rowStride * height;
            if (pixelOffset < 0 || requiredBytes < 0 || requiredBytes > bytes.Length)
                return false;

            pixels = new Color32[width * height];
            bool sourceBottomUp = rawHeight > 0;

            for (int y = 0; y < height; y++)
            {
                int sourceRow = sourceBottomUp ? y : (height - 1 - y);
                int rowStart = pixelOffset + sourceRow * rowStride;
                int destRowStart = y * width;

                for (int x = 0; x < width; x++)
                {
                    int p = rowStart + x * bytesPerPixel;
                    byte b = bytes[p];
                    byte g = bytes[p + 1];
                    byte r = bytes[p + 2];
                    byte a = bytesPerPixel == 4 ? bytes[p + 3] : (byte)255;
                    pixels[destRowStart + x] = new Color32(r, g, b, a);
                }
            }

            hasAlphaChannel = bytesPerPixel == 4;
            hasTransparentPixels = hasAlphaChannel && ContainsTransparentPixels(pixels);
            return true;
        }

        static bool TryDecodeTgaPixels(
            byte[] bytes,
            out int width,
            out int height,
            out Color32[] pixels,
            out bool hasAlphaChannel,
            out bool hasTransparentPixels)
        {
            width = 0;
            height = 0;
            pixels = null;
            hasAlphaChannel = false;
            hasTransparentPixels = false;

            if (bytes == null || bytes.Length < 18)
                return false;

            int idLength = bytes[0];
            int colorMapType = bytes[1];
            int imageType = bytes[2];
            int colorMapLength = ReadUInt16LE(bytes, 5);
            int colorMapEntrySizeBits = bytes[7];
            width = ReadUInt16LE(bytes, 12);
            height = ReadUInt16LE(bytes, 14);
            int bitsPerPixel = bytes[16];
            int descriptor = bytes[17];

            if (width <= 0 || height <= 0)
                return false;
            if (colorMapType != 0 && colorMapType != 1)
                return false;

            bool isRle;
            bool isGrayscale;
            switch (imageType)
            {
                case 2:
                    isRle = false;
                    isGrayscale = false;
                    break;
                case 3:
                    isRle = false;
                    isGrayscale = true;
                    break;
                case 10:
                    isRle = true;
                    isGrayscale = false;
                    break;
                case 11:
                    isRle = true;
                    isGrayscale = true;
                    break;
                default:
                    return false;
            }

            if (isGrayscale && bitsPerPixel != 8)
                return false;
            if (!isGrayscale && bitsPerPixel != 24 && bitsPerPixel != 32)
                return false;

            int pixelSize = bitsPerPixel / 8;
            int colorMapBytes = 0;
            if (colorMapType == 1)
            {
                if (colorMapLength <= 0 || colorMapEntrySizeBits <= 0)
                    return false;

                int colorMapEntrySizeBytes = (colorMapEntrySizeBits + 7) / 8;
                colorMapBytes = colorMapLength * colorMapEntrySizeBytes;
            }

            int cursor = 18 + idLength + colorMapBytes;
            if (cursor < 0 || cursor >= bytes.Length)
                return false;

            int pixelCount = width * height;
            var sourcePixels = new Color32[pixelCount];

            if (isRle)
            {
                if (!DecodeTgaRle(bytes, ref cursor, sourcePixels, pixelSize, isGrayscale))
                    return false;
            }
            else
            {
                int needed = pixelCount * pixelSize;
                if (cursor + needed > bytes.Length)
                    return false;

                for (int i = 0; i < pixelCount; i++)
                    sourcePixels[i] = ReadTgaPixel(bytes, ref cursor, pixelSize, isGrayscale);
            }

            bool sourceTopOrigin = (descriptor & 0x20) != 0;
            bool sourceRightToLeft = (descriptor & 0x10) != 0;
            pixels = new Color32[pixelCount];

            for (int row = 0; row < height; row++)
            {
                int destY = sourceTopOrigin ? (height - 1 - row) : row;
                int sourceRowStart = row * width;
                int destRowStart = destY * width;

                for (int col = 0; col < width; col++)
                {
                    int sourceX = sourceRightToLeft ? (width - 1 - col) : col;
                    pixels[destRowStart + col] = sourcePixels[sourceRowStart + sourceX];
                }
            }

            hasAlphaChannel = !isGrayscale && pixelSize == 4;
            hasTransparentPixels = hasAlphaChannel && ContainsTransparentPixels(pixels);
            return true;
        }

        static bool DecodeTgaRle(byte[] bytes, ref int cursor, Color32[] target, int pixelSize, bool grayscale)
        {
            int total = target.Length;
            int outIndex = 0;

            while (outIndex < total)
            {
                if (cursor >= bytes.Length)
                    return false;

                byte packet = bytes[cursor++];
                int count = (packet & 0x7F) + 1;
                bool isRun = (packet & 0x80) != 0;

                if (isRun)
                {
                    if (!CanRead(bytes, cursor, pixelSize))
                        return false;

                    var color = ReadTgaPixel(bytes, ref cursor, pixelSize, grayscale);
                    for (int i = 0; i < count && outIndex < total; i++)
                        target[outIndex++] = color;
                }
                else
                {
                    for (int i = 0; i < count && outIndex < total; i++)
                    {
                        if (!CanRead(bytes, cursor, pixelSize))
                            return false;
                        target[outIndex++] = ReadTgaPixel(bytes, ref cursor, pixelSize, grayscale);
                    }
                }
            }

            return true;
        }

        static Color32 ReadTgaPixel(byte[] bytes, ref int cursor, int pixelSize, bool grayscale)
        {
            if (grayscale)
            {
                byte v = bytes[cursor++];
                return new Color32(v, v, v, 255);
            }

            byte b = bytes[cursor++];
            byte g = bytes[cursor++];
            byte r = bytes[cursor++];
            byte a = pixelSize == 4 ? bytes[cursor++] : (byte)255;
            return new Color32(r, g, b, a);
        }

        static Texture2D CreateTexture(int width, int height, Color32[] pixels, TextureRuntimeImportOptions options)
        {
            if (pixels == null || pixels.Length != width * height)
                return null;

            var texture = new Texture2D(
                width,
                height,
                TextureFormat.RGBA32,
                mipChain: options.GenerateMipmaps,
                linear: options.LinearColorSpace);
            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: options.GenerateMipmaps, makeNoLongerReadable: options.MarkNonReadable);
            return texture;
        }

        static bool CanRead(byte[] data, int start, int count)
        {
            if (start < 0 || count < 0)
                return false;
            return start <= data.Length - count;
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

        static int ReadInt32LE(byte[] data, int index)
        {
            return unchecked((int)ReadUInt32LE(data, index));
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

        static void IncrementFormatCount(Dictionary<string, int> map, string key)
        {
            if (map == null)
                return;

            string normalized = string.IsNullOrWhiteSpace(key) ? "unknown" : key.Trim().ToLowerInvariant();
            if (map.TryGetValue(normalized, out int value))
            {
                map[normalized] = value + 1;
                return;
            }

            map[normalized] = 1;
        }

        static string BuildFormatReport(Dictionary<string, int> map)
        {
            if (map == null || map.Count == 0)
                return "-";

            var entries = new List<KeyValuePair<string, int>>(map);
            entries.Sort((a, b) =>
            {
                int byCount = b.Value.CompareTo(a.Value);
                if (byCount != 0)
                    return byCount;
                return string.CompareOrdinal(a.Key, b.Key);
            });

            const int max = 8;
            int count = entries.Count < max ? entries.Count : max;
            var parts = new string[count];
            for (int i = 0; i < count; i++)
                parts[i] = $"{entries[i].Key}:{entries[i].Value}";

            if (entries.Count > max)
                return string.Join(", ", parts) + $", ...(+{entries.Count - max})";

            return string.Join(", ", parts);
        }

        void LogMissingPath(string normalizedVirtualPath, string reason)
        {
            if (string.IsNullOrEmpty(normalizedVirtualPath))
                return;

            lock (_diagnosticsLock)
            {
                if (!_warnedMissingPaths.Add(normalizedVirtualPath))
                    return;

                if (_missingPathWarningCount < MaxUniqueWarningLogs)
                {
                    _missingPathWarningCount++;
                    Debug.LogWarning($"[TextureRuntime] Missing texture '{normalizedVirtualPath}': {reason}");
                    return;
                }

                _missingPathWarningSuppressedCount++;
                if (_missingPathWarningSuppressedCount == 1)
                {
                    Debug.LogWarning(
                        $"[TextureRuntime] Suppressing additional missing-texture warnings after {MaxUniqueWarningLogs} unique paths.");
                }
            }
        }

        void LogDecodeFailure(string normalizedVirtualPath, string reason)
        {
            if (string.IsNullOrEmpty(normalizedVirtualPath))
                return;

            lock (_diagnosticsLock)
            {
                if (!_warnedDecodePaths.Add(normalizedVirtualPath))
                    return;

                if (_decodeWarningCount < MaxUniqueWarningLogs)
                {
                    _decodeWarningCount++;
                    Debug.LogWarning($"[TextureRuntime] Failed to decode '{normalizedVirtualPath}': {reason}");
                    return;
                }

                _decodeWarningSuppressedCount++;
                if (_decodeWarningSuppressedCount == 1)
                {
                    Debug.LogWarning(
                        $"[TextureRuntime] Suppressing additional decode-failure warnings after {MaxUniqueWarningLogs} unique paths.");
                }
            }
        }
    }
}
