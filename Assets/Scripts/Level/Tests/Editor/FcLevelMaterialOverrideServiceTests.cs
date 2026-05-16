using System;
using NUnit.Framework;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Services;
using UnityEngine;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcLevelMaterialOverrideServiceTests
    {
        [Test]
        public void TryResolveBrushOverrideMaterial_ResolvesByNameAndFullName()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var go = new GameObject("override_service_test");
            try
            {
                var service = go.AddComponent<FcLevelMaterialOverrideService>();
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

                bool byName = service.TryResolveBrushOverrideMaterial(
                    overrideName: "LeafMaterial",
                    materialId: -1,
                    scopeId: "",
                    out var matByName);
                bool byFullName = service.TryResolveBrushOverrideMaterial(
                    overrideName: "group/leafmaterial",
                    materialId: -1,
                    scopeId: "",
                    out var matByFullName);

                Assert.That(byName, Is.True);
                Assert.That(byFullName, Is.True);
                Assert.That(matByName, Is.Not.Null);
                Assert.That(matByFullName, Is.Not.Null);
                Assert.That(matByName.GetFloat("_AlphaClip"), Is.EqualTo(1f));
                Assert.That(matByFullName.GetFloat("_AlphaClip"), Is.EqualTo(1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TryResolveBrushOverrideMaterial_ReturnsFalse_ForUnknownOverride()
        {
            var go = new GameObject("override_service_test_unknown");
            try
            {
                var service = go.AddComponent<FcLevelMaterialOverrideService>();
                service.Configure("test_level", new FcLevelSupplementData
                {
                    Materials = new FcLevelSupplementData.MaterialDesc[0]
                });

                bool found = service.TryResolveBrushOverrideMaterial(
                    overrideName: "missing_material",
                    materialId: -1,
                    scopeId: "",
                    out var mat);

                Assert.That(found, Is.False);
                Assert.That(mat, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TryResolveBrushOverrideMaterial_FallsBackToMaterialId_WhenNameIsMissing()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var go = new GameObject("override_service_test_material_id");
            try
            {
                var service = go.AddComponent<FcLevelMaterialOverrideService>();
                service.Configure("test_level", new FcLevelSupplementData
                {
                    Materials = new[]
                    {
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "Mat0",
                            FullName = "Set/Mat0",
                            Shader = "templmodelcommon",
                            TextureRefs = null
                        },
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "Mat1",
                            FullName = "Set/Mat1",
                            Shader = "templplants1",
                            TextureRefs = null
                        }
                    }
                });

                bool found = service.TryResolveBrushOverrideMaterial(
                    overrideName: string.Empty,
                    materialId: 1,
                    scopeId: "",
                    out var mat);

                Assert.That(found, Is.True);
                Assert.That(mat, Is.Not.Null);
                Assert.That(mat.GetFloat("_AlphaClip"), Is.EqualTo(1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TryApplyBrushOverrideToRendererSlots_UsesSubmaterialChildrenBySubmeshMatId()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var go = new GameObject("override_service_test_submaterials");
            Material slot0 = null;
            Material slot1 = null;
            try
            {
                var service = go.AddComponent<FcLevelMaterialOverrideService>();
                service.Configure("test_level", new FcLevelSupplementData
                {
                    Materials = new[]
                    {
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "Root",
                            FullName = "Group/Root",
                            ParentName = string.Empty,
                            Shader = "templmodelcommon",
                            AlphaTest = 0f,
                            TextureRefs = null
                        },
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "Child0",
                            FullName = "Group/Root/Child0",
                            ParentName = "Group/Root",
                            Shader = "templmodelcommon",
                            AlphaTest = 0f,
                            TextureRefs = null
                        },
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "Child1",
                            FullName = "Group/Root/Child1",
                            ParentName = "Group/Root",
                            Shader = "templplants1",
                            AlphaTest = 1f,
                            TextureRefs = null
                        }
                    }
                });

                slot0 = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "slot0" };
                slot1 = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "slot1" };
                var slots = new[] { slot0, slot1 };

                bool applied = service.TryApplyBrushOverrideToRendererSlots(
                    overrideName: "group/root",
                    requestedMaterialId: -1,
                    scopeId: "",
                    slots: slots,
                    submeshMaterialIds: new[] { 0, 1 });

                Assert.That(applied, Is.True);
                Assert.That(slots[0], Is.Not.Null);
                Assert.That(slots[1], Is.Not.Null);
                Assert.That(slots[0], Is.Not.SameAs(slot0));
                Assert.That(slots[1], Is.Not.SameAs(slot1));
                Assert.That(slots[0].name, Does.Contain("Group/Root/Child0"));
                Assert.That(slots[1].name, Does.Contain("Group/Root/Child1"));
                Assert.That(slots[0].GetFloat("_AlphaClip"), Is.EqualTo(0f));
                Assert.That(slots[1].GetFloat("_AlphaClip"), Is.EqualTo(1f));
            }
            finally
            {
                if (slot0 != null)
                    UnityEngine.Object.DestroyImmediate(slot0);
                if (slot1 != null)
                    UnityEngine.Object.DestroyImmediate(slot1);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TryApplyBrushOverrideToRendererSlots_FallsBackToSourceMaterialName_WhenMatIdMappingMisses()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var go = new GameObject("override_service_test_submaterials_name_fallback");
            Material slot0 = null;
            Material slot1 = null;
            try
            {
                var service = go.AddComponent<FcLevelMaterialOverrideService>();
                service.Configure("test_level", new FcLevelSupplementData
                {
                    Materials = new[]
                    {
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "Root",
                            FullName = "Group/Root",
                            ParentName = string.Empty,
                            Shader = "templmodelcommon",
                            AlphaTest = 0f,
                            TextureRefs = null
                        },
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "Child0",
                            FullName = "Group/Root/Child0",
                            ParentName = "Group/Root",
                            Shader = "templmodelcommon",
                            AlphaTest = 0f,
                            TextureRefs = null
                        },
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "Child1",
                            FullName = "Group/Root/Child1",
                            ParentName = "Group/Root",
                            Shader = "templplants1",
                            AlphaTest = 1f,
                            TextureRefs = null
                        }
                    }
                });

                slot0 = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Group/Root/Child1" };
                slot1 = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Group/Root/Child0" };
                var slots = new[] { slot0, slot1 };

                bool applied = service.TryApplyBrushOverrideToRendererSlots(
                    overrideName: "group/root",
                    requestedMaterialId: -1,
                    scopeId: "",
                    slots: slots,
                    submeshMaterialIds: new[] { 99, 99 });

                Assert.That(applied, Is.True);
                Assert.That(slots[0].name, Does.Contain("Group/Root/Child1"));
                Assert.That(slots[1].name, Does.Contain("Group/Root/Child0"));
                Assert.That(slots[0].GetFloat("_AlphaClip"), Is.EqualTo(1f));
                Assert.That(slots[1].GetFloat("_AlphaClip"), Is.EqualTo(0f));
            }
            finally
            {
                if (slot0 != null)
                    UnityEngine.Object.DestroyImmediate(slot0);
                if (slot1 != null)
                    UnityEngine.Object.DestroyImmediate(slot1);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TryApplyBrushOverrideToRendererSlots_ReturnsPerSlotDiagnostics()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var go = new GameObject("override_service_test_diagnostics");
            Material slot0 = null;
            Material slot1 = null;
            try
            {
                var service = go.AddComponent<FcLevelMaterialOverrideService>();
                service.Configure("test_level", new FcLevelSupplementData
                {
                    Materials = new[]
                    {
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "Root",
                            FullName = "Group/Root",
                            ParentName = string.Empty,
                            Shader = "templmodelcommon",
                            AlphaTest = 0f,
                            TextureRefs = null
                        },
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "Child0",
                            FullName = "Group/Root/Child0",
                            ParentName = "Group/Root",
                            Shader = "templmodelcommon",
                            AlphaTest = 0f,
                            TextureRefs = null
                        }
                    }
                });

                slot0 = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "slot0" };
                slot1 = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "slot1" };
                var slots = new[] { slot0, slot1 };

                bool applied = service.TryApplyBrushOverrideToRendererSlots(
                    overrideName: "group/root",
                    requestedMaterialId: 0,
                    scopeId: "",
                    slots: slots,
                    submeshMaterialIds: new[] { 0, 1 },
                    out var diagnostics);

                Assert.That(applied, Is.True);
                Assert.That(diagnostics, Is.Not.Null);
                Assert.That(diagnostics.Length, Is.EqualTo(2));
                Assert.That(diagnostics[0].Targeted, Is.True);
                Assert.That(diagnostics[0].Applied, Is.True);
                Assert.That(diagnostics[0].Outcome, Does.Contain("level-"));
                Assert.That(diagnostics[1].Targeted, Is.False);
                Assert.That(diagnostics[1].Applied, Is.False);
                Assert.That(diagnostics[1].Outcome, Is.EqualTo("not-targeted"));
            }
            finally
            {
                if (slot0 != null)
                    UnityEngine.Object.DestroyImmediate(slot0);
                if (slot1 != null)
                    UnityEngine.Object.DestroyImmediate(slot1);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TryResolveBrushMaterialMetadata_ResolvesSurfaceType_FromMaterialName()
        {
            var go = new GameObject("override_service_test_surface_by_name");
            try
            {
                var service = go.AddComponent<FcLevelMaterialOverrideService>();
                service.Configure("test_level", new FcLevelSupplementData
                {
                    Materials = new[]
                    {
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "RockMat",
                            FullName = "Set/RockMat",
                            Shader = "templmodelcommon",
                            TextureRefs = null
                        }
                    },
                    SurfaceTypes = new[]
                    {
                        new FcLevelSupplementData.SurfaceTypeDesc
                        {
                            Id = 4,
                            Name = "rock",
                            Material = "Set/RockMat",
                            DetailObject = "detail/rock"
                        }
                    }
                });

                bool found = service.TryResolveBrushMaterialMetadata(
                    overrideName: "set/rockmat",
                    materialId: -1,
                    out var metadata);

                Assert.That(found, Is.True);
                Assert.That(metadata.HasMaterial, Is.True);
                Assert.That(metadata.MaterialFullName, Is.EqualTo("Set/RockMat"));
                Assert.That(metadata.HasSurfaceType, Is.True);
                Assert.That(metadata.SurfaceTypeId, Is.EqualTo(4));
                Assert.That(metadata.SurfaceTypeName, Is.EqualTo("rock"));
                Assert.That(metadata.SurfaceTypeDetailObject, Is.EqualTo("detail/rock"));
                Assert.That(metadata.ResolutionSource, Is.EqualTo("override-name"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TryResolveBrushMaterialMetadata_FallsBackToMaterialIdAndSurfaceType()
        {
            var go = new GameObject("override_service_test_surface_by_material_id");
            try
            {
                var service = go.AddComponent<FcLevelMaterialOverrideService>();
                service.Configure("test_level", new FcLevelSupplementData
                {
                    Materials = new[]
                    {
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "Mat0",
                            FullName = "Set/Mat0",
                            Shader = "templmodelcommon",
                            TextureRefs = null
                        },
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "Mat1",
                            FullName = "Set/Mat1",
                            Shader = "templplants1",
                            TextureRefs = null
                        }
                    },
                    SurfaceTypes = new[]
                    {
                        new FcLevelSupplementData.SurfaceTypeDesc
                        {
                            Id = 8,
                            Name = "leaf",
                            Material = "Mat1",
                            DetailObject = "detail/leaf"
                        }
                    }
                });

                bool found = service.TryResolveBrushMaterialMetadata(
                    overrideName: string.Empty,
                    materialId: 1,
                    out var metadata);

                Assert.That(found, Is.True);
                Assert.That(metadata.HasMaterial, Is.True);
                Assert.That(metadata.MaterialName, Is.EqualTo("Mat1"));
                Assert.That(metadata.HasSurfaceType, Is.True);
                Assert.That(metadata.SurfaceTypeId, Is.EqualTo(8));
                Assert.That(metadata.SurfaceTypeName, Is.EqualTo("leaf"));
                Assert.That(metadata.ResolutionSource, Is.EqualTo("material-id"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TryResolveBrushMaterialMetadata_ResolvesDirectSurface_WhenMaterialMissing()
        {
            var go = new GameObject("override_service_test_direct_surface");
            try
            {
                var service = go.AddComponent<FcLevelMaterialOverrideService>();
                service.Configure("test_level", new FcLevelSupplementData
                {
                    Materials = Array.Empty<FcLevelSupplementData.MaterialDesc>(),
                    SurfaceTypes = new[]
                    {
                        new FcLevelSupplementData.SurfaceTypeDesc
                        {
                            Id = 3,
                            Name = "mud",
                            Material = "Ground/Mud",
                            DetailObject = "detail/mud"
                        }
                    }
                });

                bool found = service.TryResolveBrushMaterialMetadata(
                    overrideName: "ground/mud",
                    materialId: -1,
                    out var metadata);

                Assert.That(found, Is.True);
                Assert.That(metadata.HasMaterial, Is.False);
                Assert.That(metadata.HasSurfaceType, Is.True);
                Assert.That(metadata.SurfaceTypeId, Is.EqualTo(3));
                Assert.That(metadata.SurfaceTypeName, Is.EqualTo("mud"));
                Assert.That(metadata.ResolutionSource, Is.EqualTo("override-surface"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TryApplyBrushOverrideToRendererSlots_ResolvesByReversePath_WhenChildFullnameHasLongerPrefix()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var go = new GameObject("override_service_test_reverse_path");
            Material slot0 = null;
            try
            {
                var service = go.AddComponent<FcLevelMaterialOverrideService>();
                service.Configure("test_level", new FcLevelSupplementData
                {
                    Materials = new[]
                    {
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "Root",
                            FullName = "objects/indoor/Root",
                            ParentName = string.Empty,
                            Shader = "templmodelcommon",
                            AlphaTest = 0f,
                            TextureRefs = null
                        },
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "metal_part",
                            FullName = "objects/indoor/Root/metal_part",
                            ParentName = "objects/indoor/Root",
                            Shader = "templmodelcommon",
                            AlphaTest = 0f,
                            TextureRefs = null
                        }
                    }
                });

                // Slot material name is the short leaf name; child fullname has path prefix.
                slot0 = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "metal_part" };
                var slots = new[] { slot0 };

                bool applied = service.TryApplyBrushOverrideToRendererSlots(
                    overrideName: "objects/indoor/Root",
                    requestedMaterialId: -1,
                    scopeId: "",
                    slots: slots,
                    submeshMaterialIds: new[] { 99 });

                Assert.That(applied, Is.True);
                Assert.That(slots[0].name, Does.Contain("metal_part"));
                Assert.That(slots[0], Is.Not.SameAs(slot0));
            }
            finally
            {
                if (slot0 != null)
                    UnityEngine.Object.DestroyImmediate(slot0);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TryResolveBrushOverrideMaterial_DecalShader_BuildsAsMaterialWithAlphaClipOff()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var go = new GameObject("override_service_test_decal_opaque");
            try
            {
                var service = go.AddComponent<FcLevelMaterialOverrideService>();
                service.Configure("test_level", new FcLevelSupplementData
                {
                    Materials = new[]
                    {
                        new FcLevelSupplementData.MaterialDesc
                        {
                            Name = "WallDecal",
                            FullName = "Group/WallDecal",
                            Shader = "Decal",
                            AlphaTest = 0.5f,
                            TextureRefs = null
                        }
                    }
                });

                bool found = service.TryResolveBrushOverrideMaterial(
                    overrideName: "Group/WallDecal",
                    materialId: -1,
                    scopeId: "",
                    out var mat);

                Assert.That(found, Is.True);
                Assert.That(mat, Is.Not.Null);
                Assert.That(mat.GetFloat("_AlphaClip"), Is.EqualTo(0f),
                    "Decal shader must suppress AlphaTest → opaque mode.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
