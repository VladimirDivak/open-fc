using System.Collections.Generic;
using NUnit.Framework;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Services;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcLevelGeometryPreloadPlannerTests
    {
        [Test]
        public void BuildVegetationPlan_DedupesBaseAndLodRequests()
        {
            var supplement = new FcLevelSupplementData
            {
                VegetationTypes = new[]
                {
                    new FcLevelSupplementData.VegetationTypeDesc { Index = 1, FileName = "objects/forest/tree.cgf" },
                    new FcLevelSupplementData.VegetationTypeDesc { Index = 2, FileName = "objects/forest/bush.cgf" },
                },
                VegetationInstances = new[]
                {
                    new FcLevelSupplementData.VegetationInstanceDesc { Type = 1 },
                    new FcLevelSupplementData.VegetationInstanceDesc { Type = 1 },
                    new FcLevelSupplementData.VegetationInstanceDesc { Type = 2 },
                    new FcLevelSupplementData.VegetationInstanceDesc { Type = 77 },
                }
            };

            var planner = new FcLevelGeometryPreloadPlanner(path =>
            {
                if (path == "objects/forest/tree.cgf")
                    return new[] { "objects/forest/tree_lod1.cgf", "objects/forest/tree_lod2.cgf" };
                if (path == "objects/forest/bush.cgf")
                    return new[] { "objects/forest/bush_lod1.cgf" };
                return System.Array.Empty<string>();
            });

            var plan = planner.BuildVegetationPlan(supplement, sceneVegetationFallback: null);

            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.UniqueBaseCount, Is.EqualTo(2));
            Assert.That(plan.UniqueLodCount, Is.EqualTo(3));
            Assert.That(plan.UniqueRequestCount, Is.EqualTo(5));
            Assert.That(plan.TryGetBaseRequestByVirtualPath("objects/forest/tree.cgf", out var treeBase), Is.True);
            Assert.That(plan.TryGetLodModelKeys(treeBase.ModelCacheKey, out var treeLodKeys), Is.True);
            Assert.That(treeLodKeys.Count, Is.EqualTo(2));
        }

        [Test]
        public void BuildVegetationPlan_UsesSceneFallback_WhenSupplementIsMissing()
        {
            var sceneRoot = new UnityEngine.GameObject("veg_fallback_root");
            var a = sceneRoot.AddComponent<OpenFarCry.Level.Entities.FcVegetationInstance>();
            var b = new UnityEngine.GameObject("veg_fallback_b").AddComponent<OpenFarCry.Level.Entities.FcVegetationInstance>();
            b.transform.SetParent(sceneRoot.transform, worldPositionStays: true);

            SetSerializedPath(a, "Objects/Fallback/Tree.cgf");
            SetSerializedPath(b, "objects/fallback/tree.cgf");

            var planner = new FcLevelGeometryPreloadPlanner(_ => System.Array.Empty<string>());
            var plan = planner.BuildVegetationPlan(
                supplement: null,
                sceneVegetationFallback: new[] { a, b });

            Assert.That(plan.UniqueBaseCount, Is.EqualTo(1));
            Assert.That(plan.UniqueLodCount, Is.EqualTo(0));
            Assert.That(plan.TryGetBaseRequestByVirtualPath("objects/fallback/tree.cgf", out var baseReq), Is.True);
            Assert.That(baseReq.SourceKind, Is.EqualTo(FcLevelGeometrySourceKind.Vegetation));

            UnityEngine.Object.DestroyImmediate(sceneRoot);
        }

        [Test]
        public void BuildBrushPlan_DedupesBrushListAndSceneFallback()
        {
            var brushes = new List<FcBrushDesc>
            {
                new FcBrushDesc { VirtualPath = "objects/rocks/r1.cgf" },
                new FcBrushDesc { VirtualPath = "objects/rocks/r1.cgf" },
                new FcBrushDesc { VirtualPath = "objects/rocks/r2.cgf" },
            };

            var sceneRoot = new UnityEngine.GameObject("brush_fallback_root");
            var a = sceneRoot.AddComponent<OpenFarCry.Level.Entities.FcBrushInstance>();
            var b = new UnityEngine.GameObject("brush_fallback_b").AddComponent<OpenFarCry.Level.Entities.FcBrushInstance>();
            b.transform.SetParent(sceneRoot.transform, worldPositionStays: true);

            SetSerializedPath(a, "objects/rocks/r2.cgf");
            SetSerializedPath(b, "objects/rocks/r3.cgf");

            var planner = new FcLevelGeometryPreloadPlanner(path =>
            {
                if (path == "objects/rocks/r1.cgf")
                    return new[] { "objects/rocks/r1_lod1.cgf" };
                if (path == "objects/rocks/r3.cgf")
                    return new[] { "objects/rocks/r3_lod1.cgf", "objects/rocks/r3_lod2.cgf" };
                return System.Array.Empty<string>();
            });

            var plan = planner.BuildBrushPlan(brushes, new[] { a, b });

            Assert.That(plan.UniqueBaseCount, Is.EqualTo(3));
            Assert.That(plan.UniqueLodCount, Is.EqualTo(3));
            Assert.That(plan.UniqueRequestCount, Is.EqualTo(6));
            Assert.That(plan.TryGetBaseRequestByVirtualPath("objects/rocks/r3.cgf", out var r3Base), Is.True);
            Assert.That(r3Base.SourceKind, Is.EqualTo(FcLevelGeometrySourceKind.Brush));
            Assert.That(plan.TryGetLodModelKeys(r3Base.ModelCacheKey, out var r3LodKeys), Is.True);
            Assert.That(r3LodKeys.Count, Is.EqualTo(2));

            UnityEngine.Object.DestroyImmediate(sceneRoot);
        }

        static void SetSerializedPath(OpenFarCry.Level.Entities.FcVegetationInstance instance, string path)
        {
            var so = new UnityEditor.SerializedObject(instance);
            so.FindProperty("_virtualPath").stringValue = path;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetSerializedPath(OpenFarCry.Level.Entities.FcBrushInstance instance, string path)
        {
            var so = new UnityEditor.SerializedObject(instance);
            so.FindProperty("_virtualPath").stringValue = path;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
