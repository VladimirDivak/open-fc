using NUnit.Framework;
using OpenFarCry.Importer.Cgf;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Tests.Editor
{
    public sealed class CgfAnimationSetCacheTests
    {
        [SetUp]
        public void SetUp()
        {
            CgfAnimationSetCache.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            CgfAnimationSetCache.Clear();
        }

        [Test]
        public void InvalidateAnimationSetKeyForModelLayout_RemovesMapping_WhenExpectedKeyMatches()
        {
            const string layoutKey = "model:objects/merc.cgf|fp:fp1|layout:l1|scale:0.01";
            const string setKey = "v:animset-v1|fp:fp1|layout:l1|set:s1|scale:0.01|clip:clip-v2";
            SeedAnimationSet(setKey);

            CgfAnimationSetCache.StoreAnimationSetKeyForModelLayout(layoutKey, setKey);
            Assert.That(CgfAnimationSetCache.TryGetAnimationSetKeyForModelLayout(layoutKey, out var before), Is.True);
            Assert.That(before, Is.EqualTo(setKey));

            CgfAnimationSetCache.InvalidateAnimationSetKeyForModelLayout(layoutKey, setKey);

            Assert.That(CgfAnimationSetCache.TryGetAnimationSetKeyForModelLayout(layoutKey, out _), Is.False);
        }

        [Test]
        public void InvalidateAnimationSetKeyForModelLayout_LeavesMapping_WhenExpectedKeyDiffers()
        {
            const string layoutKey = "model:objects/merc.cgf|fp:fp1|layout:l1|scale:0.01";
            const string setKey = "v:animset-v1|fp:fp1|layout:l1|set:s1|scale:0.01|clip:clip-v2";
            const string wrongSetKey = "v:animset-v1|fp:fp1|layout:l1|set:other|scale:0.01|clip:clip-v2";
            SeedAnimationSet(setKey);

            CgfAnimationSetCache.StoreAnimationSetKeyForModelLayout(layoutKey, setKey);

            CgfAnimationSetCache.InvalidateAnimationSetKeyForModelLayout(layoutKey, wrongSetKey);

            Assert.That(CgfAnimationSetCache.TryGetAnimationSetKeyForModelLayout(layoutKey, out var still), Is.True);
            Assert.That(still, Is.EqualTo(setKey));
        }

        [Test]
        public void InvalidateAnimationSetKeyForModelLayout_RemovesMapping_WhenExpectedKeyNotProvided()
        {
            const string layoutKey = "model:objects/merc.cgf|fp:fp1|layout:l1|scale:0.01";
            const string setKey = "v:animset-v1|fp:fp1|layout:l1|set:s1|scale:0.01|clip:clip-v2";
            SeedAnimationSet(setKey);

            CgfAnimationSetCache.StoreAnimationSetKeyForModelLayout(layoutKey, setKey);
            CgfAnimationSetCache.InvalidateAnimationSetKeyForModelLayout(layoutKey);

            Assert.That(CgfAnimationSetCache.TryGetAnimationSetKeyForModelLayout(layoutKey, out _), Is.False);
        }

        [Test]
        public void TryGetAnimationSetKeyForModelLayout_ReturnsFalseAndPrunes_WhenSetEntryIsMissing()
        {
            const string layoutKey = "model:objects/merc.cgf|fp:fp1|layout:l1|scale:0.01";
            const string setKey = "v:animset-v1|fp:fp1|layout:l1|set:s1|scale:0.01|clip:clip-v2";

            CgfAnimationSetCache.StoreAnimationSetKeyForModelLayout(layoutKey, setKey);

            Assert.That(CgfAnimationSetCache.TryGetAnimationSetKeyForModelLayout(layoutKey, out _), Is.False);
            Assert.That(CgfAnimationSetCache.TryGetAnimationSetKeyForModelLayout(layoutKey, out _), Is.False);
        }

        [Test]
        public void TryGetAnimationSetKeyForModelLayout_ReturnsTrue_WhenSetEntryExists()
        {
            const string layoutKey = "model:objects/merc.cgf|fp:fp1|layout:l1|scale:0.01";
            const string setKey = "v:animset-v1|fp:fp1|layout:l1|set:s1|scale:0.01|clip:clip-v2";
            SeedAnimationSet(setKey);
            CgfAnimationSetCache.StoreAnimationSetKeyForModelLayout(layoutKey, setKey);

            Assert.That(CgfAnimationSetCache.TryGetAnimationSetKeyForModelLayout(layoutKey, out var resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(setKey));
        }

        // ── Cache pressure tests ──────────────────────────────────────────────

        [Test]
        public void AnimationSet_EntryCountStaysAtOrBelowMax_WhenStoreExceedsCapacity()
        {
            const int max = 256; // MaxAnimationSetEntries
            for (int i = 0; i < max + 10; i++)
                StoreUniqueAnimationSet(i);

            Assert.That(CgfAnimationSetCache.GetStats().AnimationSetEntryCount, Is.LessThanOrEqualTo(max));
        }

        [Test]
        public void AnimationSet_OldestEntryEvicted_WhenCapacityExceeded()
        {
            const int max = 256;
            const string firstKey = "v:animset-v1|fp:fp0|layout:l0|set:s0|scale:0.01|clip:clip-v2";
            SeedAnimationSet(firstKey);

            for (int i = 1; i <= max; i++)
                StoreUniqueAnimationSet(i);

            Assert.That(CgfAnimationSetCache.TryGetCachedAnimationSet(firstKey, out _), Is.False,
                "Oldest animation set should have been evicted");
        }

        [Test]
        public void ModelLayoutLinks_StaleLinksRemovedAfterAnimSetEviction()
        {
            const int max = 256;
            for (int i = 0; i < max; i++)
            {
                string setKey = MakeSetKey(i);
                SeedAnimationSet(setKey);
                CgfAnimationSetCache.StoreAnimationSetKeyForModelLayout($"model-layout-{i}", setKey);
            }

            // Overflow animation sets — oldest sets (and their links) become stale
            for (int i = max; i < max + 20; i++)
                StoreUniqueAnimationSet(i);

            Assert.That(CgfAnimationSetCache.GetStats().AnimationSetModelLinkCount, Is.LessThan(max),
                "Links pointing to evicted animation sets should be pruned");
        }

        [Test]
        public void ModelLayoutLinks_CountStaysAtOrBelowMax_WhenOverflow()
        {
            const int maxLinks = 2048; // MaxAnimationSetModelLinks
            const string setKey = "v:animset-v1|fp:fp1|layout:l1|set:s1|scale:0.01|clip:clip-v2";
            SeedAnimationSet(setKey);

            for (int i = 0; i < maxLinks + 20; i++)
                CgfAnimationSetCache.StoreAnimationSetKeyForModelLayout($"model-layout-{i}", setKey);

            Assert.That(CgfAnimationSetCache.GetStats().AnimationSetModelLinkCount, Is.LessThanOrEqualTo(maxLinks));
        }

        [Test]
        public void SemanticClip_EntryCountStaysAtOrBelowMax_WhenStoreExceedsCapacity()
        {
            const int max = 4096; // MaxSemanticClipEntries
            for (int i = 0; i < max + 10; i++)
            {
                var data = new SemanticClipData { Alias = "idle", ShouldLoop = false, Tracks = new SemanticClipTrack[0] };
                CgfAnimationSetCache.StoreCachedSemanticClip($"sem-key-{i}", data);
            }

            Assert.That(CgfAnimationSetCache.GetStats().SemanticClipEntryCount, Is.LessThanOrEqualTo(max));
        }

        [Test]
        public void Clip_EntryCountStaysAtOrBelowMax_WhenStoreExceedsCapacity()
        {
            const int max = 4096; // MaxClipEntries
            for (int i = 0; i < max + 10; i++)
                CgfAnimationSetCache.StoreCachedClip($"clip-key-{i}", new AnimationClip());

            Assert.That(CgfAnimationSetCache.GetStats().ClipEntryCount, Is.LessThanOrEqualTo(max));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static string MakeSetKey(int i) =>
            $"v:animset-v1|fp:fp{i}|layout:l{i}|set:s{i}|scale:0.01|clip:clip-v2";

        static void StoreUniqueAnimationSet(int i)
        {
            SeedAnimationSet(MakeSetKey(i));
        }

        static void SeedAnimationSet(string setKey)
        {
            var clip = new AnimationClip();
            var clips = new List<CgfRuntimeAnimationClip>
            {
                new CgfRuntimeAnimationClip
                {
                    Alias = "default",
                    SourceVirtualPath = "animations/merc_default.caf",
                    Clip = clip
                }
            };

            CgfAnimationSetCache.StoreCachedAnimationSet(
                setKey,
                clips,
                existingSourceCount: 1,
                missingControllerTrackCount: 0);
        }
    }
}
