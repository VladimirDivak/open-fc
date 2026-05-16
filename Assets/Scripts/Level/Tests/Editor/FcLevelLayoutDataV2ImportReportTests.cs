using System.Collections.Generic;
using NUnit.Framework;
using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Tests.Editor
{
    public sealed class FcLevelLayoutDataV2ImportReportTests
    {
        [Test]
        public void ImportReport_BrushMaterialOverrideDiagnostics_TracksMatchModesAndMissingBuckets()
        {
            var mission = new FcMissionDesc
            {
                LevelName = "test_level",
                MissionName = "test_mission",
                Environment = new FcLevelEnvironmentDesc()
            };

            var brushes = new List<FcBrushDesc>
            {
                new FcBrushDesc { Id = 1, VirtualPath = "objects/a.cgf", MaterialOverride = "Leaf", MaterialId = -1, Matrix = new float[12] },
                new FcBrushDesc { Id = 2, VirtualPath = "objects/b.cgf", MaterialOverride = "group/stone", MaterialId = -1, Matrix = new float[12] },
                new FcBrushDesc { Id = 3, VirtualPath = "objects/c.cgf", MaterialOverride = "unknown/path", MaterialId = 1, Matrix = new float[12] },
                new FcBrushDesc { Id = 4, VirtualPath = "objects/d.cgf", MaterialOverride = "missing", MaterialId = -1, Matrix = new float[12] },
                new FcBrushDesc { Id = 5, VirtualPath = "objects/e.cgf", MaterialOverride = null, MaterialId = 0, Matrix = new float[12] },
                new FcBrushDesc { Id = 6, VirtualPath = "objects/f.cgf", MaterialOverride = null, MaterialId = -1, Matrix = new float[12] },
            };

            var supplement = new FcLevelSupplementData
            {
                Materials = new[]
                {
                    new FcLevelSupplementData.MaterialDesc
                    {
                        Name = "Leaf",
                        FullName = "group/leaf",
                    },
                    new FcLevelSupplementData.MaterialDesc
                    {
                        Name = "Stone",
                        FullName = "group/stone",
                    },
                },
                ParserIssues = new FcLevelSupplementData.ParserIssue[0]
            };

            var data = FcLevelLayoutDataV2.FromMission(mission, brushes, supplement);
            try
            {
                var report = data.ImportReport;
                Assert.That(report.BrushMaterialOverrideCount, Is.EqualTo(5));
                Assert.That(report.BrushMaterialOverrideMatchedCount, Is.EqualTo(4));
                Assert.That(report.BrushMaterialOverrideMissingCount, Is.EqualTo(1));
                Assert.That(report.BrushMaterialOverrideMatchedByNameCount, Is.EqualTo(1));
                Assert.That(report.BrushMaterialOverrideMatchedByFullNameCount, Is.EqualTo(1));
                Assert.That(report.BrushMaterialOverrideMatchedByMaterialIdCount, Is.EqualTo(2));
                Assert.That(report.BrushMaterialOverrideMissingWithMaterialIdCount, Is.EqualTo(0));
                Assert.That(report.BrushMaterialSlotDiagnosticCount, Is.EqualTo(0));
                Assert.That(report.BrushMaterialSlotAppliedCount, Is.EqualTo(0));
                Assert.That(report.BrushMaterialSlotTargetedMissCount, Is.EqualTo(0));
                Assert.That(report.BrushMaterialSlotUnresolvedCount, Is.EqualTo(0));
                Assert.That(report.BrushMaterialInstanceDiagnosticCount, Is.EqualTo(0));
                Assert.That(report.BrushMaterialInstanceOverrideAppliedCount, Is.EqualTo(0));
                Assert.That(report.BrushMaterialInstanceFallbackCount, Is.EqualTo(0));
                Assert.That(report.BrushMaterialInstanceTargetedMissCount, Is.EqualTo(0));
                Assert.That(report.BrushMaterialInstanceUnresolvedCount, Is.EqualTo(0));

                Assert.That(report.BrushMaterialOverrideMissingCounts, Is.Not.Null);
                Assert.That(report.BrushMaterialOverrideMissingCounts.Length, Is.EqualTo(1));
                Assert.That(report.BrushMaterialOverrideMissingCounts[0].Key, Is.EqualTo("missing"));
                Assert.That(report.BrushMaterialOverrideMissingCounts[0].Count, Is.EqualTo(1));
                Assert.That(report.BrushMaterialSlotOutcomeCounts, Is.Not.Null);
                Assert.That(report.BrushMaterialSlotOutcomeCounts.Length, Is.EqualTo(0));
                Assert.That(report.BrushMaterialSlotUnresolvedSamples, Is.Not.Null);
                Assert.That(report.BrushMaterialSlotUnresolvedSamples.Length, Is.EqualTo(0));
                Assert.That(report.BrushMaterialResolutionSourceCounts, Is.Not.Null);
                Assert.That(report.BrushMaterialResolutionSourceCounts.Length, Is.EqualTo(0));
                Assert.That(report.BrushMaterialShaderFamilyCounts, Is.Not.Null);
                Assert.That(report.BrushMaterialShaderFamilyCounts.Length, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(data);
            }
        }
    }
}
