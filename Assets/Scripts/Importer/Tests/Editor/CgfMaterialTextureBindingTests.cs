using NUnit.Framework;
using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Importer.Tests.Editor
{
    public sealed class CgfMaterialTextureBindingTests
    {
        [Test]
        public void MaterialBuilder_AssignsBaseMap_WhenResolvedTextureProvided()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var chunk = new CgfMaterialChunk
            {
                Name = "test_mat",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255)
            };

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            var resolved = new CgfResolvedMaterialTextures(
                diffuseTextureName: "objects/props/crate_d.tga",
                normalTextureName: null,
                specularTextureName: null,
                opacityTextureName: null,
                baseMapVirtualPath: "objects/props/crate_d.tga",
                normalMapVirtualPath: null,
                specularMapVirtualPath: null,
                opacityMapVirtualPath: null,
                baseMap: tex,
                normalMap: null,
                specularMap: null,
                opacityMap: null);

            var mat = CgfMaterialBuilder.Build(chunk, resolved);
            Assert.That(mat, Is.Not.Null);
            Assert.That(mat.GetTexture("_BaseMap"), Is.SameAs(tex));

            Object.DestroyImmediate(mat);
            Object.DestroyImmediate(tex);
        }

        [Test]
        public void MaterialBuilder_AssignsNormalMap_WhenResolvedTextureProvided()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var chunk = new CgfMaterialChunk
            {
                Name = "test_mat_normal",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255)
            };

            var normalTex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            var resolved = new CgfResolvedMaterialTextures(
                diffuseTextureName: null,
                normalTextureName: "objects/props/crate_ddn.dds",
                specularTextureName: null,
                opacityTextureName: null,
                baseMapVirtualPath: null,
                normalMapVirtualPath: "objects/props/crate_ddn.dds",
                specularMapVirtualPath: null,
                opacityMapVirtualPath: null,
                baseMap: null,
                normalMap: normalTex,
                specularMap: null,
                opacityMap: null);

            var mat = CgfMaterialBuilder.Build(chunk, resolved);
            Assert.That(mat, Is.Not.Null);
            Assert.That(mat.GetTexture("_BumpMap"), Is.SameAs(normalTex));

            Object.DestroyImmediate(mat);
            Object.DestroyImmediate(normalTex);
        }

        [Test]
        public void MaterialBuilder_LeavesBaseMapNull_WhenTextureMissing()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var chunk = new CgfMaterialChunk
            {
                Name = "test_mat_no_tex",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(200, 200, 200, 255)
            };

            var mat = CgfMaterialBuilder.Build(chunk);
            Assert.That(mat, Is.Not.Null);
            Assert.That(mat.GetTexture("_BaseMap"), Is.Null);

            Object.DestroyImmediate(mat);
        }

        [Test]
        public void MaterialImportService_ReusesMaterial_ForSameTextureSet()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var cache = new CgfMaterialRuntimeCache();
            var service = new CgfMaterialImportService(cache: cache);

            var chunk = new CgfMaterialChunk
            {
                ChunkID = 100,
                TableIndex = 0,
                Name = "same_key_mat",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255),
                DiffuseTextureName = "textures/same/path.texture" // unsupported ext; keeps texture set stable without IO
            };

            var parsed = new CgfFile
            {
                SourceVirtualPath = "objects/props/test_model.cgf",
                SelectedMeshChunkID = 10
            };
            parsed.NodeChunks.Add(new CgfNodeChunk
            {
                ObjectID = 10,
                MatID = 100
            });
            parsed.MaterialByChunkID[100] = chunk;
            parsed.MaterialChunks.Add(chunk);

            var mesh = new Mesh { subMeshCount = 1 };

            var matsA = service.ResolveSubmeshMaterials(parsed, mesh);
            var matsB = service.ResolveSubmeshMaterials(parsed, mesh);

            Assert.That(matsA, Is.Not.Null);
            Assert.That(matsB, Is.Not.Null);
            Assert.That(matsA.Length, Is.EqualTo(1));
            Assert.That(matsB.Length, Is.EqualTo(1));
            Assert.That(matsA[0], Is.SameAs(matsB[0]));
            Assert.That(cache.Count, Is.EqualTo(1));

            service.ClearCache();
            Object.DestroyImmediate(mesh);
        }
    }
}
