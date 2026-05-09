using NUnit.Framework;
using OpenFarCry.Importer.Texture;
using UnityEngine;

namespace OpenFarCry.Importer.Tests.Editor
{
    public sealed class TextureRuntimeCacheTests
    {
        [Test]
        public void TryRetain_TracksHitAndMissStats()
        {
            var cache = new TextureRuntimeScopedCache();
            const string key = "objects/props/stone_d.bmp";
            const string scope = "level_01";

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false);
            var info = new TextureRuntimeImportService.LoadedTextureInfo(
                normalizedVirtualPath: key,
                texture: tex,
                hasAlphaChannel: false,
                hasTransparentPixels: false);
            cache.Store(key, scope, tex, info);

            bool hit = cache.TryRetain(key, scope, out var cachedTex, out var cachedInfo);
            bool miss = cache.TryRetain("objects/props/missing_d.bmp", scope, out _, out _);

            Assert.That(hit, Is.True);
            Assert.That(cachedTex, Is.SameAs(tex));
            Assert.That(cachedInfo.Texture, Is.SameAs(tex));
            Assert.That(miss, Is.False);

            var stats = cache.GetStats();
            Assert.That(stats.EntryCount, Is.EqualTo(1));
            Assert.That(stats.HitCount, Is.EqualTo(1));
            Assert.That(stats.MissCount, Is.EqualTo(1));
        }

        [Test]
        public void ReleaseLevelScope_ThenTrim_RemovesScopedEntry()
        {
            var cache = new TextureRuntimeScopedCache();
            const string key = "objects/props/crate_d.tga";
            const string scope = "level_02";

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            var info = new TextureRuntimeImportService.LoadedTextureInfo(
                normalizedVirtualPath: key,
                texture: tex,
                hasAlphaChannel: false,
                hasTransparentPixels: false);
            cache.Store(key, scope, tex, info);

            cache.ReleaseLevelScope(scope);
            int removed = cache.TrimUnused();
            var stats = cache.GetStats();

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(stats.EntryCount, Is.EqualTo(0));
            Assert.That(stats.TotalRefCount, Is.EqualTo(0));
        }
    }
}
