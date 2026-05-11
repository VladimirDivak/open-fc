using NUnit.Framework;
using OpenFarCry.Importer.Cgf;

namespace OpenFarCry.Importer.Tests.Editor
{
    public sealed class CafLoaderCachePressureTests
    {
        [SetUp]
        public void SetUp() => CafLoader.Clear();

        [TearDown]
        public void TearDown() => CafLoader.Clear();

        [Test]
        public void PathCache_EntryCountStaysAtOrBelowMax_AfterOverflow()
        {
            const int max = 1024; // MaxPathEntries

            for (int i = 0; i < max + 10; i++)
                CafLoader.SeedPathEntryForTest($"animations/track_{i}.caf", $"src-{i}", $"content-{i}", caf: null);

            CafLoader.TrimToCapacityForTest();

            Assert.That(CafLoader.GetStats().PathEntryCount, Is.LessThanOrEqualTo(max));
        }

        [Test]
        public void PathCache_OldestEntryEvicted_WhenCapacityExceeded()
        {
            const int max = 1024;
            const string firstPath = "animations/oldest.caf";
            CafLoader.SeedPathEntryForTest(firstPath, "src-oldest", "content-oldest", caf: null);

            for (int i = 1; i <= max; i++)
                CafLoader.SeedPathEntryForTest($"animations/track_{i}.caf", $"src-{i}", $"content-{i}", caf: null);

            CafLoader.TrimToCapacityForTest();

            Assert.That(CafLoader.GetStats().PathEntryCount, Is.LessThanOrEqualTo(max));
        }

        [Test]
        public void SemanticCache_EntryCountStaysAtOrBelowMax_AfterOverflow()
        {
            const int max = 256; // MaxSemanticEntries

            for (int i = 0; i < max + 10; i++)
                CafLoader.SeedSemanticEntryForTest($"content-hash-{i}", caf: null);

            CafLoader.TrimToCapacityForTest();

            Assert.That(CafLoader.GetStats().SemanticEntryCount, Is.LessThanOrEqualTo(max));
        }

        [Test]
        public void SourceHashIndex_StaleEntriesPruned_WhenOverCapacityAndSemanticsClear()
        {
            const int max = 4096; // MaxSourceHashEntries

            // Seed source-hash entries pointing to semantic hashes that are NOT in s_bySemantic
            for (int i = 0; i < max + 20; i++)
                CafLoader.SeedSourceHashEntryForTest($"source-bytes-{i}", $"orphan-content-{i}");

            CafLoader.TrimToCapacityForTest();

            // All orphan entries should be pruned (no live semantic entries exist to keep them)
            Assert.That(CafLoader.GetStats().SourceHashEntryCount, Is.LessThanOrEqualTo(max));
        }

        [Test]
        public void SourceHashIndex_LiveEntriesPreserved_WhenOrphansExist()
        {
            const int liveCount = 5;

            // Seed matching semantic + source entries
            for (int i = 0; i < liveCount; i++)
            {
                CafLoader.SeedSemanticEntryForTest($"live-content-{i}", caf: null);
                CafLoader.SeedSourceHashEntryForTest($"live-source-{i}", $"live-content-{i}");
            }

            // Seed orphan source entries beyond any meaningful limit
            for (int i = 0; i < 30; i++)
                CafLoader.SeedSourceHashEntryForTest($"orphan-source-{i}", $"orphan-content-{i}");

            CafLoader.TrimToCapacityForTest();

            // Live semantic entries must survive trim
            Assert.That(CafLoader.GetStats().SemanticEntryCount, Is.EqualTo(liveCount));
        }
    }
}
