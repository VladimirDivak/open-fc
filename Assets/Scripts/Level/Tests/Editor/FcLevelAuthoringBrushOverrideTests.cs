using System;
using System.Reflection;
using NUnit.Framework;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Entities;
using OpenFarCry.Level.Services;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcLevelAuthoringBrushOverrideTests
    {
        [Test]
        public void ApplyBrushOverrideMaterialToCachedInstance_AppliesOverrideAndMetadata()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var serviceGo = new GameObject("override_service");
            var brushGo = new GameObject("brush_owner");
            var cached = GameObject.CreatePrimitive(PrimitiveType.Cube);

            try
            {
                var service = serviceGo.AddComponent<FcLevelMaterialOverrideService>();
                service.Configure("test_level", new FcLevelSupplementData
                {
                    Materials = new[]
                    {
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "LeafMaterial",
                            FullName = "Group/LeafMaterial",
                            Shader = "templplants1",
                            TextureRefs = null
                        }
                    },
                    SurfaceTypes = new[]
                    {
                        new FcLevelSupplementData.SurfaceTypeDesc
                        {
                            Id = 12,
                            Name = "leaf",
                            Material = "Group/LeafMaterial",
                            DetailObject = "detail/leaf"
                        }
                    }
                });

                var brush = brushGo.AddComponent<FcBrushInstance>();
                var brushSo = new SerializedObject(brush);
                brushSo.FindProperty("_materialOverride").stringValue = "group/leafmaterial";
                brushSo.FindProperty("_materialId").intValue = -1;
                brushSo.ApplyModifiedPropertiesWithoutUndo();

                var renderer = cached.GetComponent<Renderer>();
                Assert.That(renderer, Is.Not.Null);
                var before = renderer.sharedMaterial;
                Assert.That(before, Is.Not.Null);

                InvokeApplyBrushOverrideMaterialToCachedInstance(
                    brush,
                    cached,
                    service,
                    "test_level");

                var after = renderer.sharedMaterial;
                Assert.That(after, Is.Not.Null);
                Assert.That(after, Is.Not.SameAs(before), "Material should be replaced by resolved override.");
                Assert.That(after.GetFloat("_AlphaClip"), Is.EqualTo(1f));

                var rootMeta = cached.GetComponent<FcLevelMaterialMetadata>();
                Assert.That(rootMeta, Is.Not.Null);
                Assert.That(rootMeta.MaterialFullName, Is.EqualTo("Group/LeafMaterial"));
                Assert.That(rootMeta.HasSurfaceType, Is.True);
                Assert.That(rootMeta.SurfaceTypeId, Is.EqualTo(12));
                Assert.That(rootMeta.ResolutionSource, Is.EqualTo("override-name"));
                Assert.That(rootMeta.SlotResolutionDiagnostics, Is.Not.Null);
                Assert.That(rootMeta.SlotResolutionDiagnostics.Length, Is.GreaterThan(0));
                Assert.That(rootMeta.SlotResolutionDiagnostics[0], Does.Contain("applied=True").IgnoreCase);

                var colliderMeta = cached.GetComponent<Collider>().GetComponent<FcLevelMaterialMetadata>();
                Assert.That(colliderMeta, Is.Not.Null);
                Assert.That(colliderMeta.SurfaceTypeName, Is.EqualTo("leaf"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cached);
                UnityEngine.Object.DestroyImmediate(brushGo);
                UnityEngine.Object.DestroyImmediate(serviceGo);
            }
        }

        [Test]
        public void ApplyBrushOverrideMaterialToCachedInstance_TargetsMatchingSubmeshMaterialId()
        {
            var litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var serviceGo = new GameObject("override_service_submesh");
            var brushGo = new GameObject("brush_owner_submesh");
            var cached = new GameObject("cached_submesh");
            var testMesh = new Mesh { name = "submesh_test_mesh" };
            testMesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(1f, 1f, 0f),
            };
            testMesh.normals = new[]
            {
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
                Vector3.forward,
            };
            testMesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            };
            testMesh.subMeshCount = 2;
            testMesh.SetTriangles(new[] { 0, 2, 1 }, 0);
            testMesh.SetTriangles(new[] { 1, 2, 3 }, 1);
            testMesh.RecalculateBounds();

            cached.AddComponent<MeshFilter>().sharedMesh = testMesh;
            cached.AddComponent<MeshRenderer>();
            cached.AddComponent<BoxCollider>();

            Material original0 = null;
            Material original1 = null;

            try
            {
                var service = serviceGo.AddComponent<FcLevelMaterialOverrideService>();
                service.Configure("test_level", new FcLevelSupplementData
                {
                    Materials = new[]
                    {
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "LeafMaterial",
                            FullName = "Group/LeafMaterial",
                            Shader = "templplants1",
                            TextureRefs = null
                        }
                    }
                });

                var brush = brushGo.AddComponent<FcBrushInstance>();
                var brushSo = new SerializedObject(brush);
                brushSo.FindProperty("_materialOverride").stringValue = "group/leafmaterial";
                brushSo.FindProperty("_materialId").intValue = 7;
                brushSo.ApplyModifiedPropertiesWithoutUndo();

                var renderer = cached.GetComponent<Renderer>();
                Assert.That(renderer, Is.Not.Null);

                original0 = new Material(litShader) { name = "original0" };
                original1 = new Material(litShader) { name = "original1" };
                renderer.sharedMaterials = new[] { original0, original1 };

                var cacheMeta = cached.AddComponent<FcCachedGeometryMetadata>();
                cacheMeta.SetMetadata(cacheFormatVersion: 3, brushRuntimeParity: true, submeshMaterialIds: new[] { 3, 7 });

                InvokeApplyBrushOverrideMaterialToCachedInstance(
                    brush,
                    cached,
                    service,
                    "test_level");

                var after = renderer.sharedMaterials;
                Assert.That(after, Has.Length.EqualTo(2));
                Assert.That(after[0], Is.SameAs(original0), "Non-matching slot should keep embedded material.");
                Assert.That(after[1], Is.Not.Null);
                Assert.That(after[1], Is.Not.SameAs(original1), "Matching submesh MatID slot should be overridden.");
                Assert.That(after[1].GetFloat("_AlphaClip"), Is.EqualTo(1f));

                var rootMeta = cached.GetComponent<FcLevelMaterialMetadata>();
                Assert.That(rootMeta, Is.Not.Null);
                Assert.That(rootMeta.SlotResolutionDiagnostics, Is.Not.Null);
                Assert.That(rootMeta.SlotResolutionDiagnostics.Length, Is.EqualTo(2));
                Assert.That(rootMeta.SlotResolutionDiagnostics[0], Does.Contain("applied=False").IgnoreCase);
                Assert.That(rootMeta.SlotResolutionDiagnostics[1], Does.Contain("applied=True").IgnoreCase);
            }
            finally
            {
                if (original0 != null)
                    UnityEngine.Object.DestroyImmediate(original0);
                if (original1 != null)
                    UnityEngine.Object.DestroyImmediate(original1);
                UnityEngine.Object.DestroyImmediate(testMesh);
                UnityEngine.Object.DestroyImmediate(cached);
                UnityEngine.Object.DestroyImmediate(brushGo);
                UnityEngine.Object.DestroyImmediate(serviceGo);
            }
        }

        static void InvokeApplyBrushOverrideMaterialToCachedInstance(
            FcBrushInstance brush,
            GameObject cachedChild,
            FcLevelMaterialOverrideService materialOverrideService,
            string scopeId)
        {
            var type = Type.GetType("OpenFarCry.Level.Editor.FcLevelBuilderWindow, OpenFarCry.Level.Editor");
            Assert.That(type, Is.Not.Null, "Failed to resolve FcLevelBuilderWindow type.");

            var method = type.GetMethod(
                "ApplyBrushOverrideMaterialToCachedInstance",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "Failed to resolve ApplyBrushOverrideMaterialToCachedInstance method.");

            method.Invoke(null, new object[] { brush, cachedChild, materialOverrideService, scopeId, null });
        }
    }
}
