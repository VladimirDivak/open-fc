using NUnit.Framework;
using OpenFarCry.Level.Services;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcLevelLoadReportTests
    {
        [Test]
        public void EntityCounters_AccumulateByStateAndCache()
        {
            var report = new FcLevelLoadReport
            {
                LevelName = "test_level",
                MissionName = "test_mission"
            };

            report.RecordEntityQueued();
            report.RecordEntityQueued();
            report.RecordEntityRetried();
            report.RecordEntityStarted(queueWaitMs: 4.0, activeCount: 1);
            report.RecordEntityStarted(queueWaitMs: 7.0, activeCount: 3);
            report.RecordEntityCacheResult(cacheHit: true);
            report.RecordEntityCacheResult(cacheHit: false);
            report.RecordEntityCompleted("Mercenary", "objects/merc.cgf", EntityLoadState.Applied, loadMs: 12.5);
            report.RecordEntityCompleted("Mercenary", "objects/merc_b.cgf", EntityLoadState.Failed, loadMs: 0);
            report.RecordEntityCompleted("Mercenary", "objects/merc_c.cgf", EntityLoadState.Cancelled, loadMs: 0);

            Assert.That(report.EntitiesQueued, Is.EqualTo(2));
            Assert.That(report.EntitiesRetried, Is.EqualTo(1));
            Assert.That(report.EntityConcurrencyPeak, Is.EqualTo(3));
            Assert.That(report.EntityQueueWaitTotalMs, Is.EqualTo(11.0).Within(0.001));
            Assert.That(report.EntityQueueWaitSlowestMs, Is.EqualTo(7.0).Within(0.001));
            Assert.That(report.EntitiesApplied, Is.EqualTo(1));
            Assert.That(report.EntitiesFailed, Is.EqualTo(1));
            Assert.That(report.EntitiesCancelled, Is.EqualTo(1));
            Assert.That(report.EntityLoadTotalMs, Is.EqualTo(12.5).Within(0.001));
            Assert.That(report.EntityLoadSlowestMs, Is.EqualTo(12.5).Within(0.001));
            Assert.That(report.EntityCacheHits, Is.EqualTo(1));
            Assert.That(report.EntityCacheMisses, Is.EqualTo(1));
        }

        [Test]
        public void AnimationCounters_AccumulateClipAndCacheStats()
        {
            var report = new FcLevelLoadReport
            {
                LevelName = "test_level",
                MissionName = "test_mission"
            };

            report.RecordAnimationAttach(
                clipCount: 3,
                attachMs: 9.0,
                animationSetHitDelta: 1,
                animationSetMissDelta: 2,
                clipHitDelta: 4,
                clipMissDelta: 5,
                cafHitDelta: 6,
                cafMissDelta: 7);
            report.RecordAnimationAttach(
                clipCount: 2,
                attachMs: 12.0,
                animationSetHitDelta: 0,
                animationSetMissDelta: 1,
                clipHitDelta: 2,
                clipMissDelta: 0,
                cafHitDelta: 3,
                cafMissDelta: 1);

            Assert.That(report.AnimationAttachCount, Is.EqualTo(2));
            Assert.That(report.AnimationClipTotal, Is.EqualTo(5));
            Assert.That(report.AnimationAttachTotalMs, Is.EqualTo(21.0).Within(0.001));
            Assert.That(report.AnimationAttachSlowestMs, Is.EqualTo(12.0).Within(0.001));
            Assert.That(report.AnimationSetCacheHits, Is.EqualTo(1));
            Assert.That(report.AnimationSetCacheMisses, Is.EqualTo(3));
            Assert.That(report.AnimationClipCacheHits, Is.EqualTo(6));
            Assert.That(report.AnimationClipCacheMisses, Is.EqualTo(5));
            Assert.That(report.AnimationCafHits, Is.EqualTo(9));
            Assert.That(report.AnimationCafMisses, Is.EqualTo(8));
        }

        [Test]
        public void RuntimeReportRegistry_Release_RemovesScopeReport()
        {
            const string scope = "report_scope_release_test";
            FcLevelRuntimeReportRegistry.Release(scope);
            var first = FcLevelRuntimeReportRegistry.GetOrCreate(scope);
            first.RecordEntityQueued();
            Assert.That(first.EntitiesQueued, Is.EqualTo(1));

            FcLevelRuntimeReportRegistry.Release(scope);

            var second = FcLevelRuntimeReportRegistry.GetOrCreate(scope);
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.EntitiesQueued, Is.EqualTo(0));

            FcLevelRuntimeReportRegistry.Release(scope);
        }

        [Test]
        public void GeometryPreloadCounters_StoreLatestSnapshot()
        {
            var report = new FcLevelLoadReport();

            report.RecordGeometryPreloadStats(
                uniqueBaseRequestCount: 11,
                uniqueLodRequestCount: 7,
                cachedModelReuseCount: 3,
                uniqueTexturePreloadCount: 42);

            Assert.That(report.GeometryUniqueBaseRequestCount, Is.EqualTo(11));
            Assert.That(report.GeometryUniqueLodRequestCount, Is.EqualTo(7));
            Assert.That(report.GeometryCachedModelReuseCount, Is.EqualTo(3));
            Assert.That(report.GeometryUniqueTexturePreloadCount, Is.EqualTo(42));

            report.RecordGeometryPreloadStats(
                uniqueBaseRequestCount: -1,
                uniqueLodRequestCount: -1,
                cachedModelReuseCount: -1,
                uniqueTexturePreloadCount: -1);

            Assert.That(report.GeometryUniqueBaseRequestCount, Is.EqualTo(0));
            Assert.That(report.GeometryUniqueLodRequestCount, Is.EqualTo(0));
            Assert.That(report.GeometryCachedModelReuseCount, Is.EqualTo(0));
            Assert.That(report.GeometryUniqueTexturePreloadCount, Is.EqualTo(0));
        }
    }
}
