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
                glossTextureName: null,
                baseMapVirtualPath: "objects/props/crate_d.tga",
                normalMapVirtualPath: null,
                specularMapVirtualPath: null,
                opacityMapVirtualPath: null,
                glossMapVirtualPath: null,
                baseMap: tex,
                normalMap: null,
                specularMap: null,
                opacityMap: null,
                glossMap: null);

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
                glossTextureName: null,
                baseMapVirtualPath: null,
                normalMapVirtualPath: "objects/props/crate_ddn.dds",
                specularMapVirtualPath: null,
                opacityMapVirtualPath: null,
                glossMapVirtualPath: null,
                baseMap: null,
                normalMap: normalTex,
                specularMap: null,
                opacityMap: null,
                glossMap: null);

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
        public void MaterialBuilder_AppliesBaseMap_WhenTextureResolvesAfterMaterialCreation()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var chunk = new CgfMaterialChunk
            {
                Name = "test_mat_late_tex",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255)
            };

            var mat = CgfMaterialBuilder.Build(chunk);
            Assert.That(mat.GetTexture("_BaseMap"), Is.Null);

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
            var resolved = new CgfResolvedMaterialTextures(
                diffuseTextureName: "objects/indoor/boxes/barrel/barrel_oil_02.dds",
                normalTextureName: null,
                specularTextureName: null,
                opacityTextureName: null,
                glossTextureName: null,
                baseMapVirtualPath: "objects/indoor/boxes/barrel/barrel_oil_02.dds",
                normalMapVirtualPath: null,
                specularMapVirtualPath: null,
                opacityMapVirtualPath: null,
                glossMapVirtualPath: null,
                baseMap: tex,
                normalMap: null,
                specularMap: null,
                opacityMap: null,
                glossMap: null);

            CgfMaterialBuilder.ApplyResolvedTextures(mat, resolved);

            Assert.That(mat.GetTexture("_BaseMap"), Is.SameAs(tex));

            Object.DestroyImmediate(mat);
            Object.DestroyImmediate(tex);
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

        [Test]
        public void MaterialImportService_UsesTableIndexMatIds_ForMultipleStandardMaterials()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var cache = new CgfMaterialRuntimeCache();
            var service = new CgfMaterialImportService(cache: cache);

            var noDraw = new CgfMaterialChunk
            {
                ChunkID = 2,
                TableIndex = 0,
                Name = "barrellhull(nodraw)/mat_metal_plate_nd",
                ShaderName = "nodraw",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(51, 141, 45, 255)
            };
            var visible = new CgfMaterialChunk
            {
                ChunkID = 5,
                TableIndex = 1,
                Name = "s_barrel1(TemplBumpSpec_hp_GlossAlpha)/mat_default",
                ShaderName = "templbumpspec_hp_glossalpha",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255)
            };

            var parsed = new CgfFile
            {
                SourceVirtualPath = "objects/indoor/boxes/barrel/barrel_oil_02.cgf",
                SelectedMeshChunkID = 3
            };
            parsed.NodeChunks.Add(new CgfNodeChunk { ObjectID = 3, MatID = 2 });
            parsed.NodeChunks.Add(new CgfNodeChunk { ObjectID = 6, MatID = 5 });
            parsed.MaterialChunks.Add(noDraw);
            parsed.MaterialChunks.Add(visible);
            parsed.MaterialByChunkID[2] = noDraw;
            parsed.MaterialByChunkID[5] = visible;

            var mesh = new Mesh { subMeshCount = 2 };
            var mats = service.ResolveSubmeshMaterials(parsed, mesh, new[] { 0, 1 });

            Assert.That(mats, Is.Not.Null);
            Assert.That(mats.Length, Is.EqualTo(2));
            Assert.That(mats[0].name, Does.Contain("barrellhull"));
            Assert.That(mats[1].name, Does.Contain("s_barrel1"));
            Assert.That(mats[0], Is.Not.SameAs(mats[1]));

            service.ClearCache();
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void MaterialImportService_UsesFirstMultiChild_WhenLodFaceMatIdIsNegative()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var cache = new CgfMaterialRuntimeCache();
            var service = new CgfMaterialImportService(cache: cache);

            var root = new CgfMaterialChunk
            {
                ChunkID = 2,
                TableIndex = 0,
                Name = "Material #12",
                MtlType = CgfMtlType.Multi,
                ChildCount = 2
            };
            var visible = new CgfMaterialChunk
            {
                ChunkID = 3,
                TableIndex = 1,
                Name = "rock_colourfull(templmodelcommon)/mat_rock",
                ShaderName = "templmodelcommon",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255)
            };
            var noDraw = new CgfMaterialChunk
            {
                ChunkID = 4,
                TableIndex = 2,
                Name = "rock_colourfull_proxy(nodraw)/mat_rock",
                ShaderName = "nodraw",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(51, 141, 45, 255)
            };

            var parsed = new CgfFile
            {
                SourceVirtualPath = "objects/natural/coastal_objects/cst_rock_colourfull_b_lod1.cgf",
                SelectedMeshChunkID = 5
            };
            parsed.NodeChunks.Add(new CgfNodeChunk { ObjectID = 5, MatID = 2 });
            parsed.MaterialChunks.Add(root);
            parsed.MaterialChunks.Add(visible);
            parsed.MaterialChunks.Add(noDraw);
            parsed.MaterialByChunkID[2] = root;
            parsed.MaterialByChunkID[3] = visible;
            parsed.MaterialByChunkID[4] = noDraw;
            parsed.MaterialChildrenByParentChunkID[2] = new System.Collections.Generic.List<CgfMaterialChunk>
            {
                visible,
                noDraw
            };

            var mesh = new Mesh { subMeshCount = 1 };
            var mats = service.ResolveSubmeshMaterials(parsed, mesh, new[] { -1 });

            Assert.That(mats, Is.Not.Null);
            Assert.That(mats.Length, Is.EqualTo(1));
            Assert.That(mats[0].name, Does.Contain("rock_colourfull"));
            Assert.That(mats[0].name, Does.Not.Contain("fallback"));

            service.ClearCache();
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void MaterialBuilder_UsesAlphaBlendState_ForTemplAlphaBlend()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var chunk = new CgfMaterialChunk
            {
                Name = "glass_alpha",
                ShaderName = "templalphablend",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255),
                Opacity = 0.25f
            };

            var mat = CgfMaterialBuilder.Build(chunk);

            Assert.That(mat.GetFloat("_Surface"), Is.EqualTo(1f));
            Assert.That(mat.GetFloat("_ZWrite"), Is.EqualTo(0f));
            Assert.That(mat.GetFloat("_SrcBlend"), Is.EqualTo((float)UnityEngine.Rendering.BlendMode.SrcAlpha));
            Assert.That(mat.GetFloat("_DstBlend"), Is.EqualTo((float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            Assert.That(mat.GetColor("_BaseColor").a, Is.EqualTo(0.25f).Within(0.001f));

            Object.DestroyImmediate(mat);
        }

        [Test]
        public void MaterialBuilder_UsesGlassPreset_ForTemplGlassCm()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var chunk = new CgfMaterialChunk
            {
                Name = "glass_cm",
                ShaderName = "templglasscm",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255),
                Opacity = 0.4f
            };

            var mat = CgfMaterialBuilder.Build(chunk);

            Assert.That(mat.GetFloat("_Surface"), Is.EqualTo(1f));
            Assert.That(mat.GetFloat("_ZWrite"), Is.EqualTo(0f));
            Assert.That(mat.GetFloat("_Smoothness"), Is.EqualTo(1f));
            Assert.That(mat.GetColor("_BaseColor").a, Is.EqualTo(0.4f).Within(0.001f));

            if (mat.HasProperty("_EnvironmentReflections"))
                Assert.That(mat.GetFloat("_EnvironmentReflections"), Is.EqualTo(1f));
            if (mat.HasProperty("_SpecularHighlights"))
                Assert.That(mat.GetFloat("_SpecularHighlights"), Is.EqualTo(1f));

            Object.DestroyImmediate(mat);
        }

        [Test]
        public void MaterialBuilder_UsesGlassFallbackAlpha_WhenOpacityMissing()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var chunk = new CgfMaterialChunk
            {
                Name = "glass_cm_fallback_alpha",
                ShaderName = "templglasscm",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 0),
                Opacity = 0f
            };

            var mat = CgfMaterialBuilder.Build(chunk);
            Assert.That(mat.GetColor("_BaseColor").a, Is.EqualTo(0.35f).Within(0.001f));
            Object.DestroyImmediate(mat);
        }

        [Test]
        public void MaterialBuilder_UsesModulateState_ForDecalModulate()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var chunk = new CgfMaterialChunk
            {
                Name = "decal_mod",
                ShaderName = "templdecalmodulate",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255)
            };

            var mat = CgfMaterialBuilder.Build(chunk);

            Assert.That(mat.GetFloat("_SrcBlend"), Is.EqualTo((float)UnityEngine.Rendering.BlendMode.DstColor));
            Assert.That(mat.GetFloat("_DstBlend"), Is.EqualTo((float)UnityEngine.Rendering.BlendMode.Zero));

            Object.DestroyImmediate(mat);
        }

        [Test]
        public void MaterialBuilder_EnablesSpecularWorkflow_ForBumpSpecShaders()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var chunk = new CgfMaterialChunk
            {
                Name = "spec_mat",
                ShaderName = "templbumpspec_hp",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255),
                SpecularColor = new Color32(200, 200, 200, 255),
                SpecLevel = 0.8f,
                SpecShininess = 0.49f
            };

            var mat = CgfMaterialBuilder.Build(chunk);
            if (mat.HasProperty("_WorkflowMode"))
                Assert.That(mat.GetFloat("_WorkflowMode"), Is.EqualTo(0f));
            Assert.That(mat.GetFloat("_Smoothness"), Is.EqualTo(Mathf.Sqrt(0.49f)).Within(0.001f));

            Object.DestroyImmediate(mat);
        }

        [Test]
        public void MaterialBuilder_UsesCutoutAndTwoSided_ForPlantShadersWithoutAlphaTest()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var chunk = new CgfMaterialChunk
            {
                Name = "plant_mat",
                ShaderName = "templplants1",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255),
                AlphaTest = 0f
            };

            var mat = CgfMaterialBuilder.Build(chunk);

            Assert.That(mat.GetFloat("_AlphaClip"), Is.EqualTo(1f));
            Assert.That(mat.GetFloat("_Cutoff"), Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(mat.GetFloat("_Cull"), Is.EqualTo((float)UnityEngine.Rendering.CullMode.Off));

            Object.DestroyImmediate(mat);
        }

        [Test]
        public void MaterialBuilder_UsesEmissionMask_ForGlowShader_WhenBaseMapReadable()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var chunk = new CgfMaterialChunk
            {
                Name = "glow_decal",
                ShaderName = "templdecalglowselfillum",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255)
            };

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: false);
            tex.SetPixels32(new[]
            {
                new Color32(10, 20, 30, 0),
                new Color32(10, 20, 30, 64),
                new Color32(10, 20, 30, 128),
                new Color32(10, 20, 30, 255),
            });
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            var resolved = new CgfResolvedMaterialTextures(
                diffuseTextureName: "objects/test/glow_d.dds",
                normalTextureName: null,
                specularTextureName: null,
                opacityTextureName: null,
                glossTextureName: null,
                baseMapVirtualPath: "objects/test/glow_d.dds",
                normalMapVirtualPath: null,
                specularMapVirtualPath: null,
                opacityMapVirtualPath: null,
                glossMapVirtualPath: null,
                baseMap: tex,
                normalMap: null,
                specularMap: null,
                opacityMap: null,
                glossMap: null);

            var mat = CgfMaterialBuilder.Build(chunk, resolved);
            var emission = mat.GetTexture("_EmissionMap");
            Assert.That(emission, Is.Not.Null);
            Assert.That(emission, Is.Not.SameAs(tex));

            Object.DestroyImmediate(mat);
            Object.DestroyImmediate(tex);
            if (emission != null)
                Object.DestroyImmediate(emission);
        }

        [Test]
        public void GameObjectBuilder_AddsUvScrollRuntime_ForTextureShiftShader()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var chunk = new CgfMaterialChunk
            {
                ChunkID = 10,
                TableIndex = 0,
                Name = "water_shift",
                ShaderName = "templtextureshiftt05",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255)
            };

            var parsed = new CgfFile
            {
                SourceVirtualPath = "objects/water/test.cgf",
                SelectedMeshChunkID = 1
            };
            parsed.NodeChunks.Add(new CgfNodeChunk { ObjectID = 1, MatID = 10 });
            parsed.MaterialChunks.Add(chunk);
            parsed.MaterialByChunkID[10] = chunk;

            var mesh = new Mesh { subMeshCount = 1 };
            var buildResult = new BuildResult
            {
                Mesh = mesh,
                HasSkeleton = false,
                SubmeshMaterialIds = new[] { 0 },
                NodeLocalOffset = Vector3.zero
            };

            var builder = new CgfGameObjectBuilder();
            var output = builder.Build(new CgfGameObjectBuilder.BuildRequest(
                result: buildResult,
                parsedFile: parsed,
                rigDefinition: null,
                name: "uvscroll_test",
                materialService: new CgfMaterialImportService(new CgfMaterialRuntimeCache()),
                textureScopeId: null));

            Assert.That(output.Root.GetComponent<CgfUvScrollRuntime>(), Is.Not.Null);

            Object.DestroyImmediate(output.Root);
            Object.DestroyImmediate(mesh);
        }
    }
}
