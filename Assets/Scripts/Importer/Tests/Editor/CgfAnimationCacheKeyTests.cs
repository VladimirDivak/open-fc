using NUnit.Framework;
using OpenFarCry.Importer.Cgf;

namespace OpenFarCry.Importer.Tests.Editor
{
    public sealed class CgfAnimationCacheKeyTests
    {
        [Test]
        public void ClipCacheKey_DiffersByLayoutHash_WhenOtherPartsEqual()
        {
            const string cafHash = "CAF_HASH_A";
            const string alias = "idle";
            const float scale = 0.01f;
            const string compatibility = "fp:anim-fp";
            string loopKey = CgfAnimationRuntimeImportService.BuildLoopPolicyKey(alias, shouldLoop: true);

            string keyA = CgfAnimationRuntimeImportService.BuildClipCacheKey(
                cafHash, alias, scale, compatibility, "layout-A", loopKey);
            string keyB = CgfAnimationRuntimeImportService.BuildClipCacheKey(
                cafHash, alias, scale, compatibility, "layout-B", loopKey);

            Assert.That(keyA, Is.Not.EqualTo(keyB));
        }

        [Test]
        public void SemanticClipCacheKey_IsStableAcrossModelLayout()
        {
            const string cafHash = "CAF_HASH_A";
            const string alias = "walk";
            const float scale = 0.01f;
            string loopKey = CgfAnimationRuntimeImportService.BuildLoopPolicyKey(alias, shouldLoop: true);

            string semA = CgfAnimationRuntimeImportService.BuildSemanticClipCacheKey(cafHash, alias, scale, loopKey);
            string semB = CgfAnimationRuntimeImportService.BuildSemanticClipCacheKey(cafHash, alias, scale, loopKey);

            Assert.That(semA, Is.EqualTo(semB));
        }

        [Test]
        public void AnimationSetModelLayoutKey_DiffersByModelOrLayout()
        {
            const string animFp = "anim-fp";
            const float scale = 0.01f;

            string keyA = CgfAnimationRuntimeImportService.BuildAnimationSetModelLayoutKey(
                "objects/characters/mercenaries/merc_cover/merc_cover.cgf",
                animFp,
                "layout-A",
                scale);
            string keyB = CgfAnimationRuntimeImportService.BuildAnimationSetModelLayoutKey(
                "objects/characters/mercenaries/merc_tshirt/merc_tshirt.cgf",
                animFp,
                "layout-A",
                scale);
            string keyC = CgfAnimationRuntimeImportService.BuildAnimationSetModelLayoutKey(
                "objects/characters/mercenaries/merc_cover/merc_cover.cgf",
                animFp,
                "layout-B",
                scale);

            Assert.That(keyA, Is.Not.EqualTo(keyB));
            Assert.That(keyA, Is.Not.EqualTo(keyC));
        }

        [Test]
        public void AnimationSetCacheKey_UsesAnimFingerprintLayoutAndSet()
        {
            const float scale = 0.01f;
            string keyA = CgfAnimationRuntimeImportService.BuildAnimationSetCacheKey(
                animationFingerprint: "anim-fp",
                pathLayoutHash: "layout-A",
                animationSetHash: "set-A",
                importScale: scale);
            string keyB = CgfAnimationRuntimeImportService.BuildAnimationSetCacheKey(
                animationFingerprint: "anim-fp",
                pathLayoutHash: "layout-A",
                animationSetHash: "set-A",
                importScale: scale);
            string keyC = CgfAnimationRuntimeImportService.BuildAnimationSetCacheKey(
                animationFingerprint: "anim-fp",
                pathLayoutHash: "layout-B",
                animationSetHash: "set-A",
                importScale: scale);
            string keyD = CgfAnimationRuntimeImportService.BuildAnimationSetCacheKey(
                animationFingerprint: "anim-fp",
                pathLayoutHash: "layout-A",
                animationSetHash: "set-B",
                importScale: scale);

            Assert.That(keyA, Is.EqualTo(keyB));
            Assert.That(keyA, Is.Not.EqualTo(keyC));
            Assert.That(keyA, Is.Not.EqualTo(keyD));
        }
    }
}
