using NUnit.Framework;
using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Importer.Tests.Editor
{
    public sealed class CgfRuntimeAssetCacheTests
    {
        [Test]
        public void ParsedEntry_StoreRetainReleaseTrim_RemovesEntry()
        {
            var cache = new CgfRuntimeAssetCache();
            var parsed = new CgfFile();
            const string key = "objects/props/rock.cgf|parsed";

            cache.StoreParsed(key, parsed, scopeId: "level_01");
            bool retained = cache.TryRetainParsed(key, "level_01", out var retainedParsed);

            Assert.That(retained, Is.True);
            Assert.That(retainedParsed, Is.SameAs(parsed));
            Assert.That(cache.ParsedEntryCount, Is.EqualTo(1));

            cache.ReleaseParsed(key);
            cache.ReleaseParsed(key);
            int removed = cache.TrimUnused();

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(cache.ParsedEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void ParsedEntry_ReleaseLevelScope_DropsScopedReferences()
        {
            var cache = new CgfRuntimeAssetCache();
            const string key = "objects/npc/soldier.cgf|parsed";
            cache.StoreParsed(key, new CgfFile(), scopeId: "level_02");

            cache.ReleaseLevelScope("level_02");
            int removed = cache.TrimUnused();

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(cache.ParsedEntryCount, Is.EqualTo(0));
        }

        [Test]
        public void ModelEntry_StoreRetainReleaseTrim_RemovesEntryAndKeepsStatsConsistent()
        {
            var cache = new CgfRuntimeAssetCache();
            const string modelKey = "objects/vehicle/jeep.cgf|mesh:-1|skel:1|scale:0.01";
            const string parsedKey = "objects/vehicle/jeep.cgf|parsed";

            var buildResult = new BuildResult
            {
                Mesh = new Mesh()
            };
            var artifact = new CgfRuntimeAssetCache.RuntimeModelArtifact(
                parsedFile: new CgfFile(),
                buildResult: buildResult,
                parsedCacheKey: parsedKey);

            cache.StoreModel(modelKey, artifact, scopeId: "level_03");
            bool retained = cache.TryRetainModel(modelKey, "level_03", out var retainedArtifact);

            Assert.That(retained, Is.True);
            Assert.That(retainedArtifact.BuildResult, Is.SameAs(buildResult));
            Assert.That(cache.ModelEntryCount, Is.EqualTo(1));

            cache.ReleaseModel(modelKey);
            cache.ReleaseModel(modelKey);
            int removed = cache.TrimUnused();

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(cache.ModelEntryCount, Is.EqualTo(0));

            var stats = cache.GetStats();
            Assert.That(stats.ModelEntryCount, Is.EqualTo(0));
            Assert.That(stats.ModelTotalRefCount, Is.EqualTo(0));
        }
    }
}
