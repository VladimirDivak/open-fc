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
            string loopKey = CgfAnimationSetCache.BuildLoopPolicyKey(alias, shouldLoop: true);

            string keyA = CgfAnimationSetCache.BuildClipCacheKey(
                cafHash, alias, scale, compatibility, "layout-A", loopKey);
            string keyB = CgfAnimationSetCache.BuildClipCacheKey(
                cafHash, alias, scale, compatibility, "layout-B", loopKey);

            Assert.That(keyA, Is.Not.EqualTo(keyB));
        }

        [Test]
        public void SemanticClipCacheKey_IsStableAcrossModelLayout()
        {
            // Semantic key excludes layout — the same CAF/alias/scale/loop
            // must produce the same key regardless of which model layout is loading it.
            // This is verified by showing semantic keys are equal while full clip keys
            // (which embed layout) are not.
            const string cafHash = "CAF_HASH_A";
            const string alias = "walk";
            const float scale = 0.01f;
            const string compat = "fp:anim-fp";
            string loopKey = CgfAnimationSetCache.BuildLoopPolicyKey(alias, shouldLoop: true);

            string semLayoutA = CgfAnimationSetCache.BuildSemanticClipCacheKey(cafHash, alias, scale, loopKey);
            string semLayoutB = CgfAnimationSetCache.BuildSemanticClipCacheKey(cafHash, alias, scale, loopKey);

            string clipKeyA = CgfAnimationSetCache.BuildClipCacheKey(cafHash, alias, scale, compat, "layout-A", loopKey);
            string clipKeyB = CgfAnimationSetCache.BuildClipCacheKey(cafHash, alias, scale, compat, "layout-B", loopKey);

            Assert.That(semLayoutA, Is.EqualTo(semLayoutB),
                "Semantic key must be identical regardless of model layout");
            Assert.That(clipKeyA, Is.Not.EqualTo(clipKeyB),
                "Full clip key must differ when layout differs");
        }

        [Test]
        public void AnimationSetModelLayoutKey_DiffersByModelOrLayout()
        {
            const string animFp = "anim-fp";
            const float scale = 0.01f;

            string keyA = CgfAnimationSetCache.BuildAnimationSetModelLayoutKey(
                "objects/characters/mercenaries/merc_cover/merc_cover.cgf",
                animFp,
                "layout-A",
                scale);
            string keyB = CgfAnimationSetCache.BuildAnimationSetModelLayoutKey(
                "objects/characters/mercenaries/merc_tshirt/merc_tshirt.cgf",
                animFp,
                "layout-A",
                scale);
            string keyC = CgfAnimationSetCache.BuildAnimationSetModelLayoutKey(
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
            string keyA = CgfAnimationSetCache.BuildAnimationSetCacheKey(
                animationFingerprint: "anim-fp",
                pathLayoutHash: "layout-A",
                animationSetHash: "set-A",
                importScale: scale);
            string keyB = CgfAnimationSetCache.BuildAnimationSetCacheKey(
                animationFingerprint: "anim-fp",
                pathLayoutHash: "layout-A",
                animationSetHash: "set-A",
                importScale: scale);
            string keyC = CgfAnimationSetCache.BuildAnimationSetCacheKey(
                animationFingerprint: "anim-fp",
                pathLayoutHash: "layout-B",
                animationSetHash: "set-A",
                importScale: scale);
            string keyD = CgfAnimationSetCache.BuildAnimationSetCacheKey(
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
