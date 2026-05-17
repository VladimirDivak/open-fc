using System;
using NUnit.Framework;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Services;
using UnityEngine;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcLevelMaterialOverrideServiceTests
    {
        // HasResolvableOverride — checks MaterialDesc lookup, no Material created.

        [Test]
        public void HasResolvableOverride_ResolvesByNameAndFullName()
        {
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

                Assert.That(service.HasResolvableOverride("LeafMaterial", -1), Is.True);
                Assert.That(service.HasResolvableOverride("group/leafmaterial", -1), Is.True);
                Assert.That(service.HasResolvableOverride("Group/LeafMaterial", -1), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void HasResolvableOverride_ReturnsFalse_ForUnknownOverride()
        {
            var go = new GameObject("override_service_test_unknown");
            try
            {
                var service = go.AddComponent<FcLevelMaterialOverrideService>();
                service.Configure("test_level", new FcLevelSupplementData
                {
                    Materials = Array.Empty<FcLevelSupplementData.MaterialDesc>()
                });

                Assert.That(service.HasResolvableOverride("missing_material", -1), Is.False);
                Assert.That(service.HasResolvableOverride(string.Empty, -1), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void HasResolvableOverride_FallsBackToMaterialId_WhenNameIsMissing()
        {
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

                Assert.That(service.HasResolvableOverride(string.Empty, 1), Is.True);
                Assert.That(service.HasResolvableOverride(string.Empty, -1), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // TryApplyBrushOverrideToRendererSlots — without VFS textures, override returns false
        // and leaves slots unchanged. Tests verify targeting/diagnostics logic.

        [Test]
        public void TryApplyBrushOverrideToRendererSlots_NoTextures_ReturnsFalse_SlotsUnchanged()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null)
                Assert.Ignore("URP Lit shader not available in this test context.");

            var go = new GameObject("override_service_test_no_textures");
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

                Assert.That(applied, Is.False, "No textures in override — nothing to apply.");
                Assert.That(slots[0], Is.SameAs(slot0), "Slot 0 must not be replaced without textures.");
                Assert.That(slots[1], Is.SameAs(slot1), "Slot 1 must not be replaced without textures.");

                // Override is still registered — HasResolvableOverride must return true.
                Assert.That(service.HasResolvableOverride("group/root", -1), Is.True);
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
        public void TryApplyBrushOverrideToRendererSlots_ReturnsPerSlotDiagnostics_WithTargetingInfo()
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

                // submeshMaterialId=0 targets slot0, slot1 is not targeted
                service.TryApplyBrushOverrideToRendererSlots(
                    overrideName: "group/root",
                    requestedMaterialId: 0,
                    scopeId: "",
                    slots: slots,
                    submeshMaterialIds: new[] { 0, 1 },
                    out var diagnostics);

                Assert.That(diagnostics, Is.Not.Null);
                Assert.That(diagnostics.Length, Is.EqualTo(2));
                Assert.That(diagnostics[0].Targeted, Is.True, "Slot 0 has submeshMatId=0 == requestedMaterialId=0");
                Assert.That(diagnostics[1].Targeted, Is.False, "Slot 1 has submeshMatId=1 != requestedMaterialId=0");
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
    }
}
