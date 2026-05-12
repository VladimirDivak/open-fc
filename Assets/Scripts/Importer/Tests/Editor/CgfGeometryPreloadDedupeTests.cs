using System.Collections.Generic;
using NUnit.Framework;
using OpenFarCry.Importer.Cgf;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OpenFarCry.Importer.Tests.Editor
{
    public sealed class CgfGeometryPreloadDedupeTests
    {
        [Test]
        public void BuildUniquePreloadRequests_DedupesByModelCacheKey_AndKeepsFirstOrder()
        {
            var requests = new List<CgfRuntimeImportRequest>
            {
                new CgfRuntimeImportRequest(
                    virtualPath: "Objects/Props/Rock.cgf",
                    importSkeleton: false,
                    importScale: 0.01f),
                new CgfRuntimeImportRequest(
                    virtualPath: "objects/props/rock.cgf",
                    importSkeleton: false,
                    importScale: 0.01f),
                new CgfRuntimeImportRequest(
                    virtualPath: "objects/props/rock.cgf",
                    selectedMeshChunkId: 5,
                    importSkeleton: false,
                    importScale: 0.01f),
                new CgfRuntimeImportRequest(
                    virtualPath: "objects/props/rock.cgf",
                    selectedMeshChunkId: 5,
                    importSkeleton: false,
                    importScale: 0.02f),
                null
            };

            var unique = CgfRuntimeImportService.BuildUniquePreloadRequests(requests);

            Assert.That(unique.Count, Is.EqualTo(3));
            Assert.That(unique[0].Request, Is.SameAs(requests[0]));
            Assert.That(unique[1].Request, Is.SameAs(requests[2]));
            Assert.That(unique[2].Request, Is.SameAs(requests[3]));
            Assert.That(unique[0].ModelCacheKey, Is.Not.EqualTo(unique[1].ModelCacheKey));
            Assert.That(unique[1].ModelCacheKey, Is.Not.EqualTo(unique[2].ModelCacheKey));
        }

        [Test]
        public void CollectUniqueTexturePreloadRequests_DedupesByVirtualPathAndLinearFlag()
        {
            var service = new CgfMaterialImportService(cache: new CgfMaterialRuntimeCache());
            var meshA = new Mesh { subMeshCount = 1 };
            var meshB = new Mesh { subMeshCount = 1 };

            var chunkA = new CgfMaterialChunk
            {
                ChunkID = 10,
                Name = "mat_a",
                MtlType = CgfMtlType.Standard,
                DiffuseTextureName = "terrain/rock/rock_d.dds",
                NormalTextureName = "terrain/rock/rock_ddn.dds",
                SpecularTextureName = "terrain/rock/rock_ddn.dds",
                OpacityTextureName = "terrain/rock/rock_ddn.dds"
            };
            var chunkB = new CgfMaterialChunk
            {
                ChunkID = 20,
                Name = "mat_b",
                MtlType = CgfMtlType.Standard,
                DiffuseTextureName = "terrain/rock/rock_ddn.dds",
                NormalTextureName = "terrain/rock/rock_ddn.dds"
            };

            var parsedA = BuildParsedWithSingleMaterial(
                sourcePath: "objects/props/rock_a.cgf",
                meshChunkId: 1,
                materialChunkId: 10,
                chunk: chunkA);
            var parsedB = BuildParsedWithSingleMaterial(
                sourcePath: "objects/props/rock_b.cgf",
                meshChunkId: 2,
                materialChunkId: 20,
                chunk: chunkB);

            var resultA = CgfRuntimeImportResult.Completed(
                virtualPath: parsedA.SourceVirtualPath,
                parsedFile: parsedA,
                buildResult: new BuildResult { Mesh = meshA, SubmeshMaterialIds = new[] { 0 } },
                usedRuntimeMemoryCache: false,
                parsedCacheKey: "a|parsed",
                modelCacheKey: "a|model");
            var resultB = CgfRuntimeImportResult.Completed(
                virtualPath: parsedB.SourceVirtualPath,
                parsedFile: parsedB,
                buildResult: new BuildResult { Mesh = meshB, SubmeshMaterialIds = new[] { 0 } },
                usedRuntimeMemoryCache: false,
                parsedCacheKey: "b|parsed",
                modelCacheKey: "b|model");

            var requests = service.CollectUniqueTexturePreloadRequests(new[] { resultA, resultB });

            Assert.That(requests.Count, Is.EqualTo(3));
            Assert.That(requests[0].VirtualPath, Is.EqualTo("terrain/rock/rock_d.dds"));
            Assert.That(requests[0].LinearColorSpace, Is.False);
            Assert.That(requests[1].VirtualPath, Is.EqualTo("terrain/rock/rock_ddn.dds"));
            Assert.That(requests[1].LinearColorSpace, Is.True);
            Assert.That(requests[2].VirtualPath, Is.EqualTo("terrain/rock/rock_ddn.dds"));
            Assert.That(requests[2].LinearColorSpace, Is.False);

            service.ClearCache();
            Object.DestroyImmediate(meshA);
            Object.DestroyImmediate(meshB);
        }

        static CgfFile BuildParsedWithSingleMaterial(
            string sourcePath,
            int meshChunkId,
            int materialChunkId,
            CgfMaterialChunk chunk)
        {
            var parsed = new CgfFile
            {
                SourceVirtualPath = sourcePath,
                SelectedMeshChunkID = meshChunkId
            };

            parsed.NodeChunks.Add(new CgfNodeChunk
            {
                ObjectID = meshChunkId,
                MatID = materialChunkId
            });
            parsed.MaterialChunks.Add(chunk);
            parsed.MaterialByChunkID[materialChunkId] = chunk;

            return parsed;
        }
    }
}
