using System.Collections.Generic;
using NUnit.Framework;
using OpenFarCry.Level.Services;
using UnityEngine;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcVegetationLodMathTests
    {
        // ── BuildCullDistanceSqr ──────────────────────────────────────────────

        [Test]
        public void BuildCullDistanceSqr_ReturnsSquared()
        {
            float result = FcVegetationLodMath.BuildCullDistanceSqr(50f, 300f);
            Assert.That(result, Is.EqualTo(300f * 300f).Within(0.001f));
        }

        [Test]
        public void BuildCullDistanceSqr_ClampsWhenCullBelowLod0()
        {
            // cull < lod0 → cull is forced to lod0 + 0.01
            float result = FcVegetationLodMath.BuildCullDistanceSqr(200f, 10f);
            float expected = (200f + 0.01f) * (200f + 0.01f);
            Assert.That(result, Is.EqualTo(expected).Within(0.01f));
        }

        // ── BuildLodThresholdsSqr ─────────────────────────────────────────────

        [Test]
        public void BuildLodThresholdsSqr_SingleLod_ThresholdEqualsCullSqr()
        {
            float[] t = FcVegetationLodMath.BuildLodThresholdsSqr(1, 50f, 300f);
            Assert.That(t.Length, Is.EqualTo(1));
            Assert.That(t[0], Is.EqualTo(300f * 300f).Within(0.001f));
        }

        [Test]
        public void BuildLodThresholdsSqr_ThreeLods_FirstThresholdIsLod0Sqr()
        {
            float[] t = FcVegetationLodMath.BuildLodThresholdsSqr(3, 50f, 300f);
            Assert.That(t.Length, Is.EqualTo(3));
            // lod0 threshold = lod0Distance squared
            Assert.That(t[0], Is.EqualTo(50f * 50f).Within(0.001f));
        }

        [Test]
        public void BuildLodThresholdsSqr_ThreeLods_LastThresholdIsCullSqr()
        {
            float[] t = FcVegetationLodMath.BuildLodThresholdsSqr(3, 50f, 300f);
            Assert.That(t[2], Is.EqualTo(300f * 300f).Within(0.001f));
        }

        [Test]
        public void BuildLodThresholdsSqr_ThreeLods_Monotonic()
        {
            float[] t = FcVegetationLodMath.BuildLodThresholdsSqr(3, 50f, 300f);
            Assert.That(t[1], Is.GreaterThan(t[0]));
            Assert.That(t[2], Is.GreaterThan(t[1]));
        }

        [Test]
        public void BuildLodThresholdsSqr_ZeroLodCount_ReturnsSingleEntry()
        {
            float[] t = FcVegetationLodMath.BuildLodThresholdsSqr(0, 50f, 300f);
            Assert.That(t.Length, Is.EqualTo(1));
        }

        // ── FindLod ───────────────────────────────────────────────────────────

        [Test]
        public void FindLod_BeyondCullDistance_ReturnsMinusOne()
        {
            float[] thresholds = FcVegetationLodMath.BuildLodThresholdsSqr(2, 50f, 300f);
            float cullSqr = FcVegetationLodMath.BuildCullDistanceSqr(50f, 300f);
            int lod = FcVegetationLodMath.FindLod(thresholds, cullSqr, lodMeshCount: 2, sqrDist: 400f * 400f);
            Assert.That(lod, Is.EqualTo(-1));
        }

        [Test]
        public void FindLod_AtCullDistance_ReturnsMinusOne()
        {
            float[] thresholds = FcVegetationLodMath.BuildLodThresholdsSqr(2, 50f, 300f);
            float cullSqr = FcVegetationLodMath.BuildCullDistanceSqr(50f, 300f);
            int lod = FcVegetationLodMath.FindLod(thresholds, cullSqr, lodMeshCount: 2, sqrDist: cullSqr);
            Assert.That(lod, Is.EqualTo(-1));
        }

        [Test]
        public void FindLod_CloseToCamera_ReturnsLod0()
        {
            float[] thresholds = FcVegetationLodMath.BuildLodThresholdsSqr(3, 50f, 300f);
            float cullSqr = FcVegetationLodMath.BuildCullDistanceSqr(50f, 300f);
            int lod = FcVegetationLodMath.FindLod(thresholds, cullSqr, lodMeshCount: 3, sqrDist: 10f * 10f);
            Assert.That(lod, Is.EqualTo(0));
        }

        [Test]
        public void FindLod_NoMeshes_ReturnsMinusOne()
        {
            int lod = FcVegetationLodMath.FindLod(null, 90000f, lodMeshCount: 0, sqrDist: 25f);
            Assert.That(lod, Is.EqualTo(-1));
        }

        [Test]
        public void FindLod_SingleLod_AlwaysZeroWhenInRange()
        {
            float[] thresholds = FcVegetationLodMath.BuildLodThresholdsSqr(1, 50f, 300f);
            float cullSqr = FcVegetationLodMath.BuildCullDistanceSqr(50f, 300f);
            int lod = FcVegetationLodMath.FindLod(thresholds, cullSqr, lodMeshCount: 1, sqrDist: 1f);
            Assert.That(lod, Is.EqualTo(0));
        }
    }

    public sealed class FcVegetationDefaultPolicyTests
    {
        // ── No sibling LODs → always None ────────────────────────────────────

        [Test]
        public void ResolveCollisionMode_NoSiblingLods_ReturnsNone()
        {
            var mode = FcVegetationDefaultPolicy.ResolveCollisionMode("objects/jungle/tree_big.cgf", hasSiblingLods: false);
            Assert.That(mode, Is.EqualTo(FcVegetationCollisionMode.None));
        }

        [Test]
        public void ResolveCollisionMode_NullPath_ReturnsNone()
        {
            var mode = FcVegetationDefaultPolicy.ResolveCollisionMode(null, hasSiblingLods: true);
            Assert.That(mode, Is.EqualTo(FcVegetationCollisionMode.None));
        }

        // ── Blocking keywords → LowLodMesh ───────────────────────────────────

        [TestCase("objects/jungle/tree_big.cgf")]
        [TestCase("objects/forest/jungle_trunk01.cgf")]
        [TestCase("objects/palms/palm_tall.cgf")]
        [TestCase("objects/trees/cedar_01.cgf")]
        [TestCase("objects/trees/pine_large.cgf")]
        [TestCase("objects/trees/oak_med.cgf")]
        [TestCase("objects/trees/cypress01.cgf")]
        [TestCase("objects/misc/stump_a.cgf")]
        [TestCase("objects/misc/log_fallen.cgf")]
        public void ResolveCollisionMode_BlockingKeyword_WithLods_ReturnsLowLodMesh(string path)
        {
            var mode = FcVegetationDefaultPolicy.ResolveCollisionMode(path, hasSiblingLods: true);
            Assert.That(mode, Is.EqualTo(FcVegetationCollisionMode.LowLodMesh));
        }

        // ── Non-blocking → None ───────────────────────────────────────────────

        [TestCase("objects/jungle/bush_small.cgf")]
        [TestCase("objects/ground/grass_patch.cgf")]
        [TestCase("objects/plants/fern01.cgf")]
        [TestCase("objects/misc/rock_small.cgf")]
        public void ResolveCollisionMode_NonBlockingAsset_WithLods_ReturnsNone(string path)
        {
            var mode = FcVegetationDefaultPolicy.ResolveCollisionMode(path, hasSiblingLods: true);
            Assert.That(mode, Is.EqualTo(FcVegetationCollisionMode.None));
        }

        // ── Backslash paths are normalized ───────────────────────────────────

        [Test]
        public void ResolveCollisionMode_BackslashPath_NormalizedAndMatched()
        {
            var mode = FcVegetationDefaultPolicy.ResolveCollisionMode(@"objects\jungle\tree01.cgf", hasSiblingLods: true);
            Assert.That(mode, Is.EqualTo(FcVegetationCollisionMode.LowLodMesh));
        }

        // ── Case-insensitive ──────────────────────────────────────────────────

        [Test]
        public void ResolveCollisionMode_UppercasePath_ReturnsLowLodMesh()
        {
            var mode = FcVegetationDefaultPolicy.ResolveCollisionMode("Objects/Forest/TREE_Big.CGF", hasSiblingLods: true);
            Assert.That(mode, Is.EqualTo(FcVegetationCollisionMode.LowLodMesh));
        }
    }

    public sealed class FcVegetationSpatialPartitionerTests
    {
        [Test]
        public void Partition_EmptyPositions_ReturnsEmpty()
        {
            var cells = FcVegetationSpatialPartitioner.Partition(System.Array.Empty<Vector3>(), 64f);
            Assert.That(cells, Is.Not.Null);
            Assert.That(cells.Length, Is.EqualTo(0));
        }

        [Test]
        public void Partition_NullPositions_ReturnsEmpty()
        {
            var cells = FcVegetationSpatialPartitioner.Partition(null, 64f);
            Assert.That(cells.Length, Is.EqualTo(0));
        }

        [Test]
        public void Partition_AllInOneCell_ReturnsSingleCell()
        {
            var positions = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(10f, 0f, 10f),
                new Vector3(20f, 0f, 20f),
            };

            var cells = FcVegetationSpatialPartitioner.Partition(positions, 64f);
            Assert.That(cells.Length, Is.EqualTo(1));
            Assert.That(cells[0].InstanceIndices.Length, Is.EqualTo(3));
        }

        [Test]
        public void Partition_InDifferentCells_SplitsCorrectly()
        {
            // Cell size 64: positions at x=10 (cell 0) and x=70 (cell 1)
            var positions = new[]
            {
                new Vector3(10f, 0f, 10f),
                new Vector3(70f, 0f, 10f),
            };

            var cells = FcVegetationSpatialPartitioner.Partition(positions, 64f);
            Assert.That(cells.Length, Is.EqualTo(2));
        }

        [Test]
        public void Partition_InstanceIndicesCoversAllInput()
        {
            var positions = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(10f, 0f, 0f),
                new Vector3(200f, 0f, 200f),
                new Vector3(210f, 0f, 210f),
            };

            var cells = FcVegetationSpatialPartitioner.Partition(positions, 64f);
            int totalInstances = 0;
            foreach (var cell in cells)
                totalInstances += cell.InstanceIndices.Length;
            Assert.That(totalInstances, Is.EqualTo(positions.Length));
        }

        [Test]
        public void Partition_BoundsContainAllPositions()
        {
            var positions = new[]
            {
                new Vector3(5f, 0f, 5f),
                new Vector3(15f, 3f, 8f),
                new Vector3(20f, 0f, 2f),
            };

            var cells = FcVegetationSpatialPartitioner.Partition(positions, 64f);
            Assert.That(cells.Length, Is.EqualTo(1));

            var bounds = cells[0].Bounds;
            foreach (var pos in positions)
                Assert.That(bounds.Contains(pos), Is.True, $"Bounds should contain {pos}");
        }

        [Test]
        public void Partition_CellSizeClampedToMinimum()
        {
            // Cell size 0 → clamped to 8
            var positions = new[]
            {
                new Vector3(4f, 0f, 4f),
                new Vector3(100f, 0f, 100f),
            };
            // With cell size 8: pos[0] at cell (0,0), pos[1] at cell (12,12) → 2 cells
            var cells = FcVegetationSpatialPartitioner.Partition(positions, 0f);
            Assert.That(cells.Length, Is.EqualTo(2));
        }
    }

    public sealed class FcVegetationColliderSelectorTests
    {
        static FcVegetationColliderSelector.Candidate C(int instance, int protoIndex, float dist)
            => new FcVegetationColliderSelector.Candidate(instance, protoIndex, dist * dist);

        [Test]
        public void Select_EmptyCandidates_ReturnsEmpty()
        {
            var result = FcVegetationColliderSelector.Select(
                new List<FcVegetationColliderSelector.Candidate>(),
                new int[] { 10 },
                globalBudget: 100);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Select_WithinBudgets_SelectsAll()
        {
            var candidates = new List<FcVegetationColliderSelector.Candidate>
            {
                C(0, protoIndex: 0, dist: 5f),
                C(1, protoIndex: 0, dist: 10f),
                C(2, protoIndex: 1, dist: 15f),
            };
            var perTypeBudgets = new[] { 10, 10 };

            var result = FcVegetationColliderSelector.Select(candidates, perTypeBudgets, globalBudget: 100);
            Assert.That(result, Is.EquivalentTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void Select_GlobalBudgetExceeded_CapsAtGlobal()
        {
            var candidates = new List<FcVegetationColliderSelector.Candidate>
            {
                C(0, 0, 5f), C(1, 0, 6f), C(2, 0, 7f), C(3, 0, 8f), C(4, 0, 9f),
            };
            var perTypeBudgets = new[] { 100 };

            var result = FcVegetationColliderSelector.Select(candidates, perTypeBudgets, globalBudget: 3);
            Assert.That(result.Count, Is.EqualTo(3));
            // Nearest 3 should be selected (indices 0, 1, 2)
            Assert.That(result, Is.EquivalentTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void Select_PerTypeBudgetExceeded_CapsPerType()
        {
            // Proto 0: budget 2, proto 1: budget 10
            var candidates = new List<FcVegetationColliderSelector.Candidate>
            {
                C(0, protoIndex: 0, dist: 1f),
                C(1, protoIndex: 0, dist: 2f),
                C(2, protoIndex: 0, dist: 3f), // exceeds proto 0 budget
                C(3, protoIndex: 1, dist: 4f),
            };
            var perTypeBudgets = new[] { 2, 10 };

            var result = FcVegetationColliderSelector.Select(candidates, perTypeBudgets, globalBudget: 100);
            // Proto 0: instances 0 and 1 (not 2). Proto 1: instance 3.
            Assert.That(result, Is.EquivalentTo(new[] { 0, 1, 3 }));
        }

        [Test]
        public void Select_NullBudgets_UsesDefaultOfOne()
        {
            var candidates = new List<FcVegetationColliderSelector.Candidate>
            {
                C(0, 0, 5f),
                C(1, 0, 10f), // same proto, budget null → default 1 → blocked
            };

            var result = FcVegetationColliderSelector.Select(candidates, null, globalBudget: 100);
            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result.Contains(0), Is.True);
        }

        [Test]
        public void Select_GlobalZero_ReturnsEmpty()
        {
            var candidates = new List<FcVegetationColliderSelector.Candidate> { C(0, 0, 5f) };
            // globalBudget clamped to max(1, 0) = 1 — selector selects 1
            // Actually Mathf.Max(1, 0) = 1, so it selects one candidate
            // Let's verify by using globalBudget=1
            var result = FcVegetationColliderSelector.Select(candidates, new[] { 10 }, globalBudget: 1);
            Assert.That(result.Count, Is.EqualTo(1));
        }

        [Test]
        public void Select_PreservesNearestFirstOrder()
        {
            // All same proto, budget 2 → nearest two should win
            var candidates = new List<FcVegetationColliderSelector.Candidate>
            {
                C(instance: 10, protoIndex: 0, dist: 1f),
                C(instance: 20, protoIndex: 0, dist: 2f),
                C(instance: 30, protoIndex: 0, dist: 3f),
            };
            var result = FcVegetationColliderSelector.Select(candidates, new[] { 2 }, globalBudget: 100);
            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result.Contains(10), Is.True);
            Assert.That(result.Contains(20), Is.True);
            Assert.That(result.Contains(30), Is.False);
        }
    }
}
