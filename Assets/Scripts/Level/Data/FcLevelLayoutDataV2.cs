using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Level.Data
{
    // V2 layout cache format: keeps full level layout records as ScriptableObject
    // metadata, and is intended to grow as lossless parsers are introduced.
    [CreateAssetMenu(fileName = "FcLevelLayoutDataV2",
        menuName = "OpenFarCry/Level Layout Data V2")]
    public sealed class FcLevelLayoutDataV2 : ScriptableObject
    {
        public const int CurrentVersion = 2;

        [Serializable]
        public struct NameValuePair
        {
            public string Key;
            public string Value;
        }

        [Serializable]
        public struct BrushEntry
        {
            public int Id;
            public string VirtualPath;
            public bool NoPhysics;
            public float[] Matrix; // 12 floats, Cry Matrix34 row-major
            public byte LodRatio;
            public string MaterialOverride;
            public int MaterialId;
            public int Flags;
            public int MergeId;
            public int ViewDistRatio;
        }

        [Serializable]
        public struct EntityEntry
        {
            public int Id;
            public int ParentId;
            public string EntityClass;
            public string Name;
            public string Layer;
            public Vector3 Pos;
            public Vector3 Angles;
            public float Scale;
            public bool HiddenInGame;
            public bool CastShadows;
            public bool SkipOnLowSpec;
            public int ViewDistRatio;
            public string ModelVirtualPath;
            public NameValuePair[] RootAttributes;
            public NameValuePair[] Properties;
            public NameValuePair[] Properties2;
        }

        [Serializable]
        public struct ObjectEntry
        {
            public string Type;
            public string Name;
            public Vector3 Pos;
            public Vector3 Angles;
            public int AreaId;
            public Vector3 AreaBoxDims;
            public Vector3[] ShapePoints;
            public NameValuePair[] Attributes;
        }

        [Serializable]
        public struct LevelObjectEntry
        {
            public string Type;
            public string Name;
            public Vector3 Pos;
            public Vector3 Angles;
            public int AreaId;
            public NameValuePair[] Attributes;
            public Vector3[] ShapePoints;
        }

        [Serializable]
        public struct EnvironmentData
        {
            public Vector3 SunVector;
            public Color SunColor;
            public Color SkyColor;
            public float SunMultiplier;
            public Color AmbientColor;
            public Color FogColor;
            public float FogStart;
            public float FogEnd;
            public float WaterLevel;
        }

        [Serializable]
        public struct ImportReportData
        {
            [Serializable]
            public struct CountEntry
            {
                public string Key;
                public int Count;
            }

            public int EntityCount;
            public int ObjectCount;
            public int BrushCount;
            public int EntityClassUniqueCount;
            public int ObjectTypeUniqueCount;
            public int EntityRootAttributeCount;
            public int ObjectAttributeCount;
            public int EntityUnknownAttributeCount;
            public int ObjectUnknownAttributeCount;
            public int SurfaceTypeCount;
            public int VegetationTypeCount;
            public int MaterialCount;
            public int VegetationInstanceCount;
            public int ParserIssueCount;
            public bool VegetationInstancesHeaderCountDetected;
            public bool VegetationInstancesHeaderCountMatched;
            public int VegetationInstancesTrailingBytes;
            public string VegetationInstancesParseMode;
            public int VegetationInstancesCandidateDataOnlyTrailingBytes;
            public int VegetationInstancesCandidateHeaderTrailingBytes;
            public int VegetationInstanceInvalidScaleCount;
            public int VegetationInstanceZeroBrightnessCount;
            public int MaterialRootCount;
            public int MaterialNestedCount;
            public int MaterialMaxDepth;
            public int MaterialWithTextureRefsCount;
            public int MaterialWithoutNameCount;
            public int MaterialTextureRefCount;
            public int MaterialTextureRefMatchedCount;
            public int MaterialTextureRefMissingCount;
            public int PackageEntryCount;
            public int KnownFilePresentCount;
            public int KnownFileMissingCount;
            public int MaterialLibraryRefCount;
            public int MaterialLibraryMatchedCount;
            public int MaterialLibraryMissingCount;
            public int MaterialLibraryDuplicateRefCount;
            public bool HasLevelDataXml;
            public bool HasMaterialsXml;
            public bool HasObjectsLst;
            public bool HasBrushLst;
            public bool HasTerrainHeightmap;
            public bool HasTerrainCoverLow;
            public int MissionXmlCount;
            public int MusicXmlCount;
            public bool CurrentMissionXmlPresent;
            public int VegetationInstanceMatchedTypeCount;
            public int VegetationInstanceMissingTypeCount;
            public int VegetationTypeMissingFileNameCount;
            public int VegetationTypeDuplicateIndexCount;
            public int VegetationTypeInvalidIndexCount;
            public int BrushMaterialOverrideCount;
            public int BrushMaterialOverrideMatchedCount;
            public int BrushMaterialOverrideMissingCount;
            public int BrushMaterialOverrideMatchedByNameCount;
            public int BrushMaterialOverrideMatchedByFullNameCount;
            public int BrushMaterialOverrideMatchedByMaterialIdCount;
            public int BrushMaterialOverrideMissingWithMaterialIdCount;
            public int BrushMaterialSlotDiagnosticCount;
            public int BrushMaterialSlotAppliedCount;
            public int BrushMaterialSlotTargetedMissCount;
            public int BrushMaterialSlotUnresolvedCount;
            public int BrushMaterialInstanceDiagnosticCount;
            public int BrushMaterialInstanceOverrideAppliedCount;
            public int BrushMaterialInstanceFallbackCount;
            public int BrushMaterialInstanceTargetedMissCount;
            public int BrushMaterialInstanceUnresolvedCount;
            public int EntityModelPathCount;
            public int EntityModelPathMatchedCount;
            public int EntityModelPathMissingCount;
            public int ObjectModelPathCount;
            public int ObjectModelPathMatchedCount;
            public int ObjectModelPathMissingCount;
            public CountEntry[] BrushMaterialOverrideMissingCounts;
            public CountEntry[] BrushMaterialSlotOutcomeCounts;
            public CountEntry[] BrushMaterialSlotUnresolvedSamples;
            public CountEntry[] BrushMaterialResolutionSourceCounts;
            public CountEntry[] BrushMaterialShaderFamilyCounts;
            public CountEntry[] EntityClassCounts;
            public CountEntry[] ObjectTypeCounts;
            public CountEntry[] VegetationInstanceTypeCounts;
            public CountEntry[] ParserIssueSourceCounts;
            public CountEntry[] EntityUnknownAttrByClassCounts;
            public CountEntry[] ObjectUnknownAttrByTypeCounts;
            public CountEntry[] EntityUnknownAttrKeyCounts;
            public CountEntry[] ObjectUnknownAttrKeyCounts;
            public ValidationData Validation;
        }

        [Serializable]
        public struct ValidationData
        {
            public bool HasCriticalValidationErrors;
            public bool MissionXmlPresent;
            public bool LevelDataPresent;
            public bool BrushListPresent;
            public bool ObjectsListPresent;
            public bool VegetationTypeCoverageComplete;
            public bool VegetationHeaderConsistent;
            public bool VegetationTrailingBytesZero;
            public bool MaterialLibraryRefsResolved;
            public bool BrushMaterialOverridesResolved;
            public bool MaterialTextureRefsResolved;
            public int FailedCheckCount;
        }

        public int Version = CurrentVersion;
        public string LevelName;
        public string MissionName;
        public BrushEntry[] Brushes;
        public EntityEntry[] Entities;
        public LevelObjectEntry[] LevelObjects;
        public ObjectEntry[] Objects;
        public EnvironmentData Environment;
        public ImportReportData ImportReport;
        public string[] PackageEntries;
        public FcLevelSupplementData.KnownFileStatus[] KnownFiles;
        public string[] MissionXmlFiles;
        public string[] MusicXmlFiles;
        public string[] MaterialLibraries;
        public int MaterialLibraryRawCount;
        public string[] NetBaiFiles;
        public string[] HideBaiFiles;
        public FcLevelSupplementData.SurfaceTypeDesc[] SurfaceTypes;
        public FcLevelSupplementData.VegetationTypeDesc[] VegetationTypes;
        public FcLevelSupplementData.MaterialDesc[] Materials;
        public FcLevelSupplementData.VegetationInstanceDesc[] VegetationInstances;
        public FcLevelSupplementData.VegetationInstanceParseMeta VegetationInstancesParseMeta;
        public FcLevelSupplementData.ParserIssue[] ParserIssues;

        public static FcLevelLayoutDataV2 FromMission(
            FcMissionDesc mission,
            IReadOnlyList<FcBrushDesc> brushes,
            FcLevelSupplementData supplement = null)
        {
            var data = CreateInstance<FcLevelLayoutDataV2>();
            data.Version = CurrentVersion;
            data.LevelName = mission.LevelName;
            data.MissionName = mission.MissionName;

            data.Brushes = new BrushEntry[brushes?.Count ?? 0];
            for (int i = 0; i < data.Brushes.Length; i++)
            {
                var b = brushes[i];
                data.Brushes[i] = new BrushEntry
                {
                    Id = b.Id,
                    VirtualPath = b.VirtualPath,
                    NoPhysics = b.NoPhysics,
                    Matrix = b.Matrix != null ? (float[])b.Matrix.Clone() : null,
                    LodRatio = b.LodRatio,
                    MaterialOverride = b.MaterialOverride,
                    MaterialId = b.MaterialId,
                    Flags = b.Flags,
                    MergeId = b.MergeId,
                    ViewDistRatio = b.ViewDistRatio,
                };
            }

            data.Entities = new EntityEntry[mission.Entities.Count];
            for (int i = 0; i < data.Entities.Length; i++)
            {
                var e = mission.Entities[i];
                data.Entities[i] = new EntityEntry
                {
                    Id = e.Id,
                    ParentId = e.ParentId,
                    EntityClass = e.EntityClass,
                    Name = e.Name,
                    Layer = e.Layer,
                    Pos = e.Pos,
                    Angles = e.Angles,
                    Scale = e.Scale,
                    HiddenInGame = e.HiddenInGame,
                    CastShadows = e.CastShadows,
                    SkipOnLowSpec = e.SkipOnLowSpec,
                    ViewDistRatio = e.ViewDistRatio,
                    ModelVirtualPath = e.GetModelVirtualPath() ?? string.Empty,
                    RootAttributes = DictPairs(e.RootAttributes),
                    Properties = DictPairs(e.Properties),
                    Properties2 = DictPairs(e.Properties2),
                };
            }

            data.LevelObjects = new LevelObjectEntry[mission.LevelObjects.Count];
            for (int i = 0; i < data.LevelObjects.Length; i++)
            {
                var o = mission.LevelObjects[i];
                data.LevelObjects[i] = new LevelObjectEntry
                {
                    Type = o.Type,
                    Name = o.Name,
                    Pos = o.Pos,
                    Angles = o.Angles,
                    AreaId = o.AreaId,
                    Attributes = DictPairs(o.Attributes),
                    ShapePoints = o.ShapePoints.Count > 0 ? o.ShapePoints.ToArray() : Array.Empty<Vector3>(),
                };
            }

            // Legacy projection retained for current scene builder object path.
            data.Objects = new ObjectEntry[mission.Objects.Count];
            for (int i = 0; i < data.Objects.Length; i++)
            {
                var o = mission.Objects[i];
                data.Objects[i] = new ObjectEntry
                {
                    Type = o.Type,
                    Name = o.Name,
                    Pos = o.Pos,
                    Angles = o.Angles,
                    AreaId = o.AreaId,
                    AreaBoxDims = o.AreaBoxDims,
                    ShapePoints = o.ShapePoints != null ? (Vector3[])o.ShapePoints.Clone() : null,
                    Attributes = DictPairs(o.Attributes),
                };
            }

            if (mission.Environment != null)
            {
                var env = mission.Environment;
                data.Environment = new EnvironmentData
                {
                    SunVector = env.SunVector,
                    SunColor = env.SunColor,
                    SkyColor = env.SkyColor,
                    SunMultiplier = env.SunMultiplier,
                    AmbientColor = env.AmbientColor,
                    FogColor = env.FogColor,
                    FogStart = env.FogStart,
                    FogEnd = env.FogEnd,
                    WaterLevel = env.WaterLevel,
                };
            }

            data.ImportReport = BuildImportReport(data);
            ApplySupplement(data, supplement);
            data.ImportReport = BuildImportReport(data);

            return data;
        }

        public FcMissionDesc ToMission()
        {
            var mission = new FcMissionDesc
            {
                LevelName = LevelName,
                MissionName = MissionName,
            };

            if (Entities != null)
            {
                foreach (var e in Entities)
                {
                    var desc = new FcEntityDesc
                    {
                        Id = e.Id,
                        ParentId = e.ParentId,
                        EntityClass = e.EntityClass,
                        Name = e.Name,
                        Layer = e.Layer,
                        Pos = e.Pos,
                        Angles = e.Angles,
                        Scale = e.Scale,
                        HiddenInGame = e.HiddenInGame,
                        CastShadows = e.CastShadows,
                        SkipOnLowSpec = e.SkipOnLowSpec,
                        ViewDistRatio = e.ViewDistRatio,
                    };
                    RestoreDict(desc.RootAttributes, e.RootAttributes);
                    RestoreDict(desc.Properties, e.Properties);
                    RestoreDict(desc.Properties2, e.Properties2);
                    mission.Entities.Add(desc);
                }
            }

            if (Objects != null && Objects.Length > 0)
            {
                foreach (var o in Objects)
                {
                    var desc = new FcObjectDesc
                    {
                        Type = o.Type,
                        Name = o.Name,
                        Pos = o.Pos,
                        Angles = o.Angles,
                        AreaId = o.AreaId,
                        AreaBoxDims = o.AreaBoxDims,
                        ShapePoints = o.ShapePoints,
                    };
                    RestoreDict(desc.Attributes, o.Attributes);
                    mission.Objects.Add(desc);
                }
            }
            else if (LevelObjects != null)
            {
                foreach (var o in LevelObjects)
                {
                    var desc = new FcObjectDesc
                    {
                        Type = o.Type,
                        Name = o.Name,
                        Pos = o.Pos,
                        Angles = o.Angles,
                        AreaId = o.AreaId,
                        ShapePoints = o.ShapePoints,
                    };
                    RestoreDict(desc.Attributes, o.Attributes);

                    if (desc.Attributes.TryGetValue("Width", out string wText) &&
                        desc.Attributes.TryGetValue("Height", out string hText) &&
                        desc.Attributes.TryGetValue("Length", out string lText))
                    {
                        desc.AreaBoxDims = new Vector3(
                            ParseInvariantFloat(wText, 5f),
                            ParseInvariantFloat(hText, 5f),
                            ParseInvariantFloat(lText, 5f));
                    }

                    mission.Objects.Add(desc);
                }
            }

            if (LevelObjects != null)
            {
                foreach (var o in LevelObjects)
                {
                    var desc = new FcLevelObjectDesc
                    {
                        Type = o.Type,
                        Name = o.Name,
                        Pos = o.Pos,
                        Angles = o.Angles,
                        AreaId = o.AreaId,
                    };
                    RestoreDict(desc.Attributes, o.Attributes);
                    if (o.ShapePoints != null && o.ShapePoints.Length > 0)
                        desc.ShapePoints.AddRange(o.ShapePoints);
                    mission.LevelObjects.Add(desc);
                }
            }

            mission.Environment = new FcLevelEnvironmentDesc
            {
                SunVector = Environment.SunVector,
                SunColor = Environment.SunColor,
                SkyColor = Environment.SkyColor,
                SunMultiplier = Environment.SunMultiplier,
                AmbientColor = Environment.AmbientColor,
                FogColor = Environment.FogColor,
                FogStart = Environment.FogStart,
                FogEnd = Environment.FogEnd,
                WaterLevel = Environment.WaterLevel,
            };

            return mission;
        }

        public IReadOnlyList<FcBrushDesc> ToBrushList()
        {
            if (Brushes == null || Brushes.Length == 0)
                return Array.Empty<FcBrushDesc>();

            var list = new FcBrushDesc[Brushes.Length];
            for (int i = 0; i < Brushes.Length; i++)
            {
                var b = Brushes[i];
                list[i] = new FcBrushDesc
                {
                    Id = b.Id,
                    VirtualPath = b.VirtualPath,
                    NoPhysics = b.NoPhysics,
                    Matrix = b.Matrix != null ? (float[])b.Matrix.Clone() : null,
                    LodRatio = b.LodRatio,
                    MaterialOverride = b.MaterialOverride,
                    MaterialId = b.MaterialId,
                    Flags = b.Flags,
                    MergeId = b.MergeId,
                    ViewDistRatio = (byte)Mathf.Clamp(b.ViewDistRatio, byte.MinValue, byte.MaxValue),
                };
            }
            return list;
        }

        static NameValuePair[] DictPairs(Dictionary<string, string> source)
        {
            if (source == null || source.Count == 0)
                return Array.Empty<NameValuePair>();

            var pairs = new NameValuePair[source.Count];
            int i = 0;
            foreach (var kv in source)
            {
                pairs[i++] = new NameValuePair { Key = kv.Key, Value = kv.Value };
            }
            return pairs;
        }

        static void RestoreDict(Dictionary<string, string> target, NameValuePair[] pairs)
        {
            target.Clear();
            if (pairs == null || pairs.Length == 0) return;

            for (int i = 0; i < pairs.Length; i++)
            {
                var key = pairs[i].Key;
                if (string.IsNullOrEmpty(key))
                    continue;

                target[key] = pairs[i].Value ?? string.Empty;
            }
        }

        static ImportReportData BuildImportReport(FcLevelLayoutDataV2 data)
        {
            int entityAttrCount = 0;
            int objectAttrCount = 0;
            int entityUnknownAttrCount = 0;
            int objectUnknownAttrCount = 0;
            var brushMaterialStats = BuildBrushMaterialOverrideStats(data.Brushes, data.Materials);

            if (data.Entities != null)
            {
                for (int i = 0; i < data.Entities.Length; i++)
                {
                    var attrs = data.Entities[i].RootAttributes;
                    entityAttrCount += attrs?.Length ?? 0;
                    if (attrs == null) continue;
                    for (int j = 0; j < attrs.Length; j++)
                    {
                        if (!KnownEntityAttributes.Contains(attrs[j].Key))
                            entityUnknownAttrCount++;
                    }
                }
            }

            if (data.LevelObjects != null)
            {
                for (int i = 0; i < data.LevelObjects.Length; i++)
                {
                    var attrs = data.LevelObjects[i].Attributes;
                    objectAttrCount += attrs?.Length ?? 0;
                    if (attrs == null) continue;
                    for (int j = 0; j < attrs.Length; j++)
                    {
                        if (!KnownObjectAttributes.Contains(attrs[j].Key))
                            objectUnknownAttrCount++;
                    }
                }
            }
            else if (data.Objects != null)
            {
                for (int i = 0; i < data.Objects.Length; i++)
                {
                    var attrs = data.Objects[i].Attributes;
                    objectAttrCount += attrs?.Length ?? 0;
                    if (attrs == null) continue;
                    for (int j = 0; j < attrs.Length; j++)
                    {
                        if (!KnownObjectAttributes.Contains(attrs[j].Key))
                            objectUnknownAttrCount++;
                    }
                }
            }

            return new ImportReportData
            {
                EntityCount = data.Entities?.Length ?? 0,
                ObjectCount = data.LevelObjects?.Length ?? data.Objects?.Length ?? 0,
                BrushCount = data.Brushes?.Length ?? 0,
                EntityClassUniqueCount = CountUniqueEntityClasses(data.Entities),
                ObjectTypeUniqueCount = CountUniqueObjectTypes(data.LevelObjects, data.Objects),
                EntityRootAttributeCount = entityAttrCount,
                ObjectAttributeCount = objectAttrCount,
                EntityUnknownAttributeCount = entityUnknownAttrCount,
                ObjectUnknownAttributeCount = objectUnknownAttrCount,
                SurfaceTypeCount = data.SurfaceTypes?.Length ?? 0,
                VegetationTypeCount = data.VegetationTypes?.Length ?? 0,
                MaterialCount = data.Materials?.Length ?? 0,
                VegetationInstanceCount = data.VegetationInstances?.Length ?? 0,
                ParserIssueCount = data.ParserIssues?.Length ?? 0,
                VegetationInstancesHeaderCountDetected = data.VegetationInstancesParseMeta.HeaderCountDetected,
                VegetationInstancesHeaderCountMatched = data.VegetationInstancesParseMeta.HeaderCountMatched,
                VegetationInstancesTrailingBytes = data.VegetationInstancesParseMeta.TrailingBytes,
                VegetationInstancesParseMode = data.VegetationInstancesParseMeta.ParseMode ?? string.Empty,
                VegetationInstancesCandidateDataOnlyTrailingBytes =
                    data.VegetationInstancesParseMeta.CandidateDataOnlyTrailingBytes,
                VegetationInstancesCandidateHeaderTrailingBytes =
                    data.VegetationInstancesParseMeta.CandidateHeaderTrailingBytes,
                VegetationInstanceInvalidScaleCount = CountInvalidVegetationScales(data.VegetationInstances),
                VegetationInstanceZeroBrightnessCount = CountZeroVegetationBrightness(data.VegetationInstances),
                MaterialRootCount = CountMaterialRoots(data.Materials),
                MaterialNestedCount = CountMaterialNested(data.Materials),
                MaterialMaxDepth = GetMaterialMaxDepth(data.Materials),
                MaterialWithTextureRefsCount = CountMaterialsWithTextureRefs(data.Materials),
                MaterialWithoutNameCount = CountMaterialsWithoutName(data.Materials),
                MaterialTextureRefCount = CountMaterialTextureRefs(data.Materials),
                MaterialTextureRefMatchedCount = CountMaterialTextureRefMatches(
                    data.Materials, data.PackageEntries, matched: true),
                MaterialTextureRefMissingCount = CountMaterialTextureRefMatches(
                    data.Materials, data.PackageEntries, matched: false),
                PackageEntryCount = data.PackageEntries?.Length ?? 0,
                KnownFilePresentCount = CountKnownFiles(data.KnownFiles, present: true),
                KnownFileMissingCount = CountKnownFiles(data.KnownFiles, present: false),
                MaterialLibraryRefCount = data.MaterialLibraries?.Length ?? 0,
                MaterialLibraryMatchedCount = CountMaterialLibraryMatches(
                    data.MaterialLibraries, data.PackageEntries, matched: true),
                MaterialLibraryMissingCount = CountMaterialLibraryMatches(
                    data.MaterialLibraries, data.PackageEntries, matched: false),
                MaterialLibraryDuplicateRefCount = CountMaterialLibraryDuplicates(
                    data.MaterialLibraryRawCount, data.MaterialLibraries),
                HasLevelDataXml = HasKnownFile(data.KnownFiles, "leveldata.xml"),
                HasMaterialsXml = HasKnownFile(data.KnownFiles, "materials.xml"),
                HasObjectsLst = HasKnownFile(data.KnownFiles, "objects.lst"),
                HasBrushLst = HasKnownFile(data.KnownFiles, "brush.lst"),
                HasTerrainHeightmap = HasKnownFile(data.KnownFiles, "terrain/land_map.h16"),
                HasTerrainCoverLow = HasKnownFile(data.KnownFiles, "terrain/cover_low.dds"),
                MissionXmlCount = data.MissionXmlFiles?.Length ?? 0,
                MusicXmlCount = data.MusicXmlFiles?.Length ?? 0,
                CurrentMissionXmlPresent = HasCurrentMissionXml(data),
                VegetationInstanceMatchedTypeCount = CountVegetationTypeMatches(
                    data.VegetationInstances, data.VegetationTypes, matched: true),
                VegetationInstanceMissingTypeCount = CountVegetationTypeMatches(
                    data.VegetationInstances, data.VegetationTypes, matched: false),
                VegetationTypeMissingFileNameCount = CountVegetationTypesMissingFileName(data.VegetationTypes),
                VegetationTypeDuplicateIndexCount = CountVegetationTypeDuplicateIndexes(data.VegetationTypes),
                VegetationTypeInvalidIndexCount = CountVegetationTypeInvalidIndexes(data.VegetationTypes),
                BrushMaterialOverrideCount = brushMaterialStats.OverrideCount,
                BrushMaterialOverrideMatchedCount = brushMaterialStats.MatchedCount,
                BrushMaterialOverrideMissingCount = brushMaterialStats.MissingCount,
                BrushMaterialOverrideMatchedByNameCount = brushMaterialStats.MatchedByNameCount,
                BrushMaterialOverrideMatchedByFullNameCount = brushMaterialStats.MatchedByFullNameCount,
                BrushMaterialOverrideMatchedByMaterialIdCount = brushMaterialStats.MatchedByMaterialIdCount,
                BrushMaterialOverrideMissingWithMaterialIdCount = brushMaterialStats.MissingWithMaterialIdCount,
                EntityModelPathCount = CountEntityModelPaths(data.Entities),
                EntityModelPathMatchedCount = CountEntityModelPathMatches(
                    data.Entities, data.PackageEntries, matched: true),
                EntityModelPathMissingCount = CountEntityModelPathMatches(
                    data.Entities, data.PackageEntries, matched: false),
                ObjectModelPathCount = CountObjectModelPaths(data.LevelObjects),
                ObjectModelPathMatchedCount = CountObjectModelPathMatches(
                    data.LevelObjects, data.PackageEntries, matched: true),
                ObjectModelPathMissingCount = CountObjectModelPathMatches(
                    data.LevelObjects, data.PackageEntries, matched: false),
                BrushMaterialOverrideMissingCounts = brushMaterialStats.MissingOverrideCounts,
                BrushMaterialSlotOutcomeCounts = Array.Empty<ImportReportData.CountEntry>(),
                BrushMaterialSlotUnresolvedSamples = Array.Empty<ImportReportData.CountEntry>(),
                BrushMaterialResolutionSourceCounts = Array.Empty<ImportReportData.CountEntry>(),
                BrushMaterialShaderFamilyCounts = Array.Empty<ImportReportData.CountEntry>(),
                EntityClassCounts = BuildEntityClassCounts(data.Entities),
                ObjectTypeCounts = BuildObjectTypeCounts(data.LevelObjects, data.Objects),
                VegetationInstanceTypeCounts = BuildVegetationInstanceTypeCounts(data.VegetationInstances),
                ParserIssueSourceCounts = BuildParserIssueSourceCounts(data.ParserIssues),
                EntityUnknownAttrByClassCounts = BuildEntityUnknownAttrByClassCounts(data.Entities),
                ObjectUnknownAttrByTypeCounts = BuildObjectUnknownAttrByTypeCounts(data.LevelObjects, data.Objects),
                EntityUnknownAttrKeyCounts = BuildEntityUnknownAttrKeyCounts(data.Entities),
                ObjectUnknownAttrKeyCounts = BuildObjectUnknownAttrKeyCounts(data.LevelObjects, data.Objects),
                Validation = BuildValidation(data),
            };
        }

        static readonly HashSet<string> KnownEntityAttributes = new HashSet<string>(
            new[]
            {
                "EntityClass", "Name", "Layer", "EntityId", "ParentId", "HiddenInGame",
                "CastShadows", "SkipOnLowSpec", "ViewDistRatio", "Pos", "Angles", "Scale",
            },
            StringComparer.OrdinalIgnoreCase);

        static readonly HashSet<string> KnownObjectAttributes = new HashSet<string>(
            new[]
            {
                "Type", "Name", "Pos", "Angles", "AreaId", "Width", "Height", "Length",
            },
            StringComparer.OrdinalIgnoreCase);

        static ImportReportData.CountEntry[] BuildEntityClassCounts(EntityEntry[] entities)
        {
            if (entities == null || entities.Length == 0)
                return Array.Empty<ImportReportData.CountEntry>();

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entities.Length; i++)
            {
                string key = string.IsNullOrWhiteSpace(entities[i].EntityClass)
                    ? "<empty>"
                    : entities[i].EntityClass;
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            return ToSortedCountEntries(counts);
        }

        static int CountUniqueEntityClasses(EntityEntry[] entities)
        {
            if (entities == null || entities.Length == 0)
                return 0;

            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entities.Length; i++)
            {
                string key = string.IsNullOrWhiteSpace(entities[i].EntityClass)
                    ? "<empty>"
                    : entities[i].EntityClass;
                keys.Add(key);
            }
            return keys.Count;
        }

        static ImportReportData.CountEntry[] BuildObjectTypeCounts(LevelObjectEntry[] levelObjects, ObjectEntry[] objects)
        {
            if ((levelObjects == null || levelObjects.Length == 0) &&
                (objects == null || objects.Length == 0))
            {
                return Array.Empty<ImportReportData.CountEntry>();
            }

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (levelObjects != null && levelObjects.Length > 0)
            {
                for (int i = 0; i < levelObjects.Length; i++)
                {
                    string key = string.IsNullOrWhiteSpace(levelObjects[i].Type)
                        ? "<empty>"
                        : levelObjects[i].Type;
                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
            else
            {
                for (int i = 0; i < objects.Length; i++)
                {
                    string key = string.IsNullOrWhiteSpace(objects[i].Type)
                        ? "<empty>"
                        : objects[i].Type;
                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }

            return ToSortedCountEntries(counts);
        }

        static int CountUniqueObjectTypes(LevelObjectEntry[] levelObjects, ObjectEntry[] objects)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (levelObjects != null && levelObjects.Length > 0)
            {
                for (int i = 0; i < levelObjects.Length; i++)
                {
                    string key = string.IsNullOrWhiteSpace(levelObjects[i].Type)
                        ? "<empty>"
                        : levelObjects[i].Type;
                    keys.Add(key);
                }
                return keys.Count;
            }

            if (objects != null && objects.Length > 0)
            {
                for (int i = 0; i < objects.Length; i++)
                {
                    string key = string.IsNullOrWhiteSpace(objects[i].Type)
                        ? "<empty>"
                        : objects[i].Type;
                    keys.Add(key);
                }
            }

            return keys.Count;
        }

        static ImportReportData.CountEntry[] ToSortedCountEntries(Dictionary<string, int> counts)
        {
            if (counts.Count == 0)
                return Array.Empty<ImportReportData.CountEntry>();

            var keys = new List<string>(counts.Keys);
            keys.Sort(StringComparer.Ordinal);
            var result = new ImportReportData.CountEntry[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                result[i] = new ImportReportData.CountEntry
                {
                    Key = keys[i],
                    Count = counts[keys[i]],
                };
            }
            return result;
        }

        static ImportReportData.CountEntry[] BuildVegetationInstanceTypeCounts(
            FcLevelSupplementData.VegetationInstanceDesc[] instances)
        {
            if (instances == null || instances.Length == 0)
                return Array.Empty<ImportReportData.CountEntry>();

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < instances.Length; i++)
            {
                string key = instances[i].Type.ToString();
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            return ToSortedCountEntries(counts);
        }

        static ImportReportData.CountEntry[] BuildParserIssueSourceCounts(
            FcLevelSupplementData.ParserIssue[] issues)
        {
            if (issues == null || issues.Length == 0)
                return Array.Empty<ImportReportData.CountEntry>();

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < issues.Length; i++)
            {
                string key = string.IsNullOrWhiteSpace(issues[i].Source)
                    ? "<unknown>"
                    : issues[i].Source;
                counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            return ToSortedCountEntries(counts);
        }

        static ImportReportData.CountEntry[] BuildEntityUnknownAttrByClassCounts(EntityEntry[] entities)
        {
            if (entities == null || entities.Length == 0)
                return Array.Empty<ImportReportData.CountEntry>();

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entities.Length; i++)
            {
                string key = string.IsNullOrWhiteSpace(entities[i].EntityClass)
                    ? "<empty>"
                    : entities[i].EntityClass;

                var attrs = entities[i].RootAttributes;
                if (attrs == null || attrs.Length == 0)
                    continue;

                int unknown = 0;
                for (int j = 0; j < attrs.Length; j++)
                {
                    if (!KnownEntityAttributes.Contains(attrs[j].Key))
                        unknown++;
                }

                if (unknown > 0)
                    counts[key] = counts.TryGetValue(key, out int n) ? n + unknown : unknown;
            }

            return ToSortedCountEntries(counts);
        }

        static ImportReportData.CountEntry[] BuildObjectUnknownAttrByTypeCounts(
            LevelObjectEntry[] levelObjects, ObjectEntry[] objects)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (levelObjects != null && levelObjects.Length > 0)
            {
                for (int i = 0; i < levelObjects.Length; i++)
                {
                    string key = string.IsNullOrWhiteSpace(levelObjects[i].Type)
                        ? "<empty>"
                        : levelObjects[i].Type;
                    var attrs = levelObjects[i].Attributes;
                    if (attrs == null || attrs.Length == 0)
                        continue;

                    int unknown = 0;
                    for (int j = 0; j < attrs.Length; j++)
                    {
                        if (!KnownObjectAttributes.Contains(attrs[j].Key))
                            unknown++;
                    }

                    if (unknown > 0)
                        counts[key] = counts.TryGetValue(key, out int n) ? n + unknown : unknown;
                }

                return ToSortedCountEntries(counts);
            }

            if (objects != null)
            {
                for (int i = 0; i < objects.Length; i++)
                {
                    string key = string.IsNullOrWhiteSpace(objects[i].Type)
                        ? "<empty>"
                        : objects[i].Type;
                    var attrs = objects[i].Attributes;
                    if (attrs == null || attrs.Length == 0)
                        continue;

                    int unknown = 0;
                    for (int j = 0; j < attrs.Length; j++)
                    {
                        if (!KnownObjectAttributes.Contains(attrs[j].Key))
                            unknown++;
                    }

                    if (unknown > 0)
                        counts[key] = counts.TryGetValue(key, out int n) ? n + unknown : unknown;
                }
            }

            return ToSortedCountEntries(counts);
        }

        static ImportReportData.CountEntry[] BuildEntityUnknownAttrKeyCounts(EntityEntry[] entities)
        {
            if (entities == null || entities.Length == 0)
                return Array.Empty<ImportReportData.CountEntry>();

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < entities.Length; i++)
            {
                var attrs = entities[i].RootAttributes;
                if (attrs == null || attrs.Length == 0)
                    continue;

                for (int j = 0; j < attrs.Length; j++)
                {
                    string key = attrs[j].Key;
                    if (KnownEntityAttributes.Contains(key))
                        continue;

                    key = string.IsNullOrWhiteSpace(key) ? "<empty>" : key;
                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }

            return ToSortedCountEntries(counts);
        }

        static ImportReportData.CountEntry[] BuildObjectUnknownAttrKeyCounts(
            LevelObjectEntry[] levelObjects, ObjectEntry[] objects)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (levelObjects != null && levelObjects.Length > 0)
            {
                for (int i = 0; i < levelObjects.Length; i++)
                {
                    var attrs = levelObjects[i].Attributes;
                    if (attrs == null || attrs.Length == 0)
                        continue;

                    for (int j = 0; j < attrs.Length; j++)
                    {
                        string key = attrs[j].Key;
                        if (KnownObjectAttributes.Contains(key))
                            continue;

                        key = string.IsNullOrWhiteSpace(key) ? "<empty>" : key;
                        counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                    }
                }
                return ToSortedCountEntries(counts);
            }

            if (objects != null)
            {
                for (int i = 0; i < objects.Length; i++)
                {
                    var attrs = objects[i].Attributes;
                    if (attrs == null || attrs.Length == 0)
                        continue;

                    for (int j = 0; j < attrs.Length; j++)
                    {
                        string key = attrs[j].Key;
                        if (KnownObjectAttributes.Contains(key))
                            continue;

                        key = string.IsNullOrWhiteSpace(key) ? "<empty>" : key;
                        counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                    }
                }
            }

            return ToSortedCountEntries(counts);
        }

        static ValidationData BuildValidation(FcLevelLayoutDataV2 data)
        {
            int vegetationMissingTypeCount = CountVegetationTypeMatches(
                data.VegetationInstances, data.VegetationTypes, matched: false);
            int materialLibraryMissingCount = CountMaterialLibraryMatches(
                data.MaterialLibraries, data.PackageEntries, matched: false);
            int brushMaterialMissingCount = CountBrushMaterialOverrideMatches(
                data.Brushes, data.Materials, matched: false);
            int materialTextureMissingCount = CountMaterialTextureRefMatches(
                data.Materials, data.PackageEntries, matched: false);

            var validation = new ValidationData
            {
                MissionXmlPresent = HasCurrentMissionXml(data),
                LevelDataPresent = HasKnownFile(data.KnownFiles, "leveldata.xml"),
                BrushListPresent = HasKnownFile(data.KnownFiles, "brush.lst"),
                ObjectsListPresent = HasKnownFile(data.KnownFiles, "objects.lst"),
                VegetationTypeCoverageComplete = vegetationMissingTypeCount == 0,
                VegetationHeaderConsistent = !data.VegetationInstancesParseMeta.HeaderCountDetected ||
                                             data.VegetationInstancesParseMeta.HeaderCountMatched,
                VegetationTrailingBytesZero = data.VegetationInstancesParseMeta.TrailingBytes == 0,
                MaterialLibraryRefsResolved = materialLibraryMissingCount == 0,
                BrushMaterialOverridesResolved = brushMaterialMissingCount == 0,
                MaterialTextureRefsResolved = materialTextureMissingCount == 0,
            };

            int failed = 0;
            if (!validation.MissionXmlPresent) failed++;
            if (!validation.LevelDataPresent) failed++;
            if (!validation.BrushListPresent) failed++;
            if (!validation.ObjectsListPresent) failed++;
            if (!validation.VegetationTypeCoverageComplete) failed++;
            if (!validation.VegetationHeaderConsistent) failed++;
            if (!validation.VegetationTrailingBytesZero) failed++;
            if (string.Equals(data.VegetationInstancesParseMeta.ParseMode, "header+data", StringComparison.Ordinal) &&
                data.VegetationInstancesParseMeta.HeaderCountDetected &&
                !data.VegetationInstancesParseMeta.HeaderCountMatched)
            {
                failed++;
            }
            if (!validation.MaterialLibraryRefsResolved) failed++;
            if (!validation.BrushMaterialOverridesResolved) failed++;
            if (!validation.MaterialTextureRefsResolved) failed++;

            validation.FailedCheckCount = failed;
            validation.HasCriticalValidationErrors = failed > 0;
            return validation;
        }

        static int CountKnownFiles(FcLevelSupplementData.KnownFileStatus[] files, bool present)
        {
            if (files == null || files.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < files.Length; i++)
            {
                if (files[i].Present == present)
                    count++;
            }
            return count;
        }

        static bool HasCurrentMissionXml(FcLevelLayoutDataV2 data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.LevelName) || string.IsNullOrWhiteSpace(data.MissionName))
                return false;

            string expected = $"levels/{data.LevelName.ToLowerInvariant()}/mission_{data.MissionName.ToLowerInvariant()}.xml";

            if (data.MissionXmlFiles != null)
            {
                for (int i = 0; i < data.MissionXmlFiles.Length; i++)
                {
                    if (string.Equals(
                            NormalizePath(data.MissionXmlFiles[i]),
                            NormalizePath(expected),
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            if (data.PackageEntries != null)
            {
                for (int i = 0; i < data.PackageEntries.Length; i++)
                {
                    if (string.Equals(
                            NormalizePath(data.PackageEntries[i]),
                            NormalizePath(expected),
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        static bool HasKnownFile(FcLevelSupplementData.KnownFileStatus[] files, string leafFileName)
        {
            if (files == null || files.Length == 0 || string.IsNullOrWhiteSpace(leafFileName))
                return false;

            string expected = "/" + NormalizePath(leafFileName);
            for (int i = 0; i < files.Length; i++)
            {
                if (!files[i].Present || string.IsNullOrWhiteSpace(files[i].Path))
                    continue;

                string path = "/" + NormalizePath(files[i].Path);
                if (path.EndsWith(expected, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        static int CountMaterialLibraryMatches(string[] refs, string[] packageEntries, bool matched)
        {
            if (refs == null || refs.Length == 0)
                return 0;

            var known = new HashSet<string>(StringComparer.Ordinal);
            if (packageEntries != null)
            {
                for (int i = 0; i < packageEntries.Length; i++)
                    known.Add(NormalizePath(packageEntries[i]));
            }

            int count = 0;
            for (int i = 0; i < refs.Length; i++)
            {
                string raw = refs[i];
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                // Material library refs can be relative names or explicit paths.
                string candidate = NormalizePath(raw);
                bool has = known.Contains(candidate) ||
                           known.Contains(candidate + ".xml") ||
                           known.Contains("levels/" + NormalizePath(raw)) ||
                           known.Contains("levels/" + NormalizePath(raw) + ".xml");

                if (has == matched)
                    count++;
            }
            return count;
        }

        static int CountMaterialLibraryDuplicates(int rawCount, string[] uniqueRefs)
        {
            int uniqueCount = uniqueRefs?.Length ?? 0;
            int duplicates = rawCount - uniqueCount;
            return duplicates > 0 ? duplicates : 0;
        }

        static int CountMaterialRoots(FcLevelSupplementData.MaterialDesc[] materials)
        {
            if (materials == null || materials.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i].Depth <= 0)
                    count++;
            }
            return count;
        }

        static int CountInvalidVegetationScales(FcLevelSupplementData.VegetationInstanceDesc[] instances)
        {
            if (instances == null || instances.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i].Scale <= 0f || float.IsNaN(instances[i].Scale) || float.IsInfinity(instances[i].Scale))
                    count++;
            }
            return count;
        }

        static int CountZeroVegetationBrightness(FcLevelSupplementData.VegetationInstanceDesc[] instances)
        {
            if (instances == null || instances.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i].Brightness == 0)
                    count++;
            }
            return count;
        }

        static int CountMaterialNested(FcLevelSupplementData.MaterialDesc[] materials)
        {
            if (materials == null || materials.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i].Depth > 0)
                    count++;
            }
            return count;
        }

        static int GetMaterialMaxDepth(FcLevelSupplementData.MaterialDesc[] materials)
        {
            if (materials == null || materials.Length == 0)
                return 0;

            int max = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i].Depth > max)
                    max = materials[i].Depth;
            }
            return max;
        }

        static int CountMaterialsWithTextureRefs(FcLevelSupplementData.MaterialDesc[] materials)
        {
            if (materials == null || materials.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i].TextureRefs != null && materials[i].TextureRefs.Length > 0)
                    count++;
            }
            return count;
        }

        static int CountMaterialsWithoutName(FcLevelSupplementData.MaterialDesc[] materials)
        {
            if (materials == null || materials.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(materials[i].Name))
                    count++;
            }
            return count;
        }

        static int CountMaterialTextureRefs(FcLevelSupplementData.MaterialDesc[] materials)
        {
            if (materials == null || materials.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                var refs = materials[i].TextureRefs;
                count += refs?.Length ?? 0;
            }
            return count;
        }

        static int CountMaterialTextureRefMatches(
            FcLevelSupplementData.MaterialDesc[] materials,
            string[] packageEntries,
            bool matched)
        {
            if (materials == null || materials.Length == 0)
                return 0;

            var known = new HashSet<string>(StringComparer.Ordinal);
            if (packageEntries != null)
            {
                for (int i = 0; i < packageEntries.Length; i++)
                    known.Add(NormalizePath(packageEntries[i]));
            }

            int count = 0;
            for (int i = 0; i < materials.Length; i++)
            {
                var refs = materials[i].TextureRefs;
                if (refs == null || refs.Length == 0)
                    continue;

                for (int j = 0; j < refs.Length; j++)
                {
                    string tex = refs[j];
                    if (string.IsNullOrWhiteSpace(tex))
                        continue;

                    string candidate = NormalizePath(tex);
                    bool has = known.Contains(candidate) ||
                               known.Contains(candidate + ".dds") ||
                               known.Contains(candidate + ".tga") ||
                               known.Contains(candidate + ".bmp") ||
                               known.Contains(candidate + ".jpg") ||
                               known.Contains(candidate + ".jpeg");
                    if (has == matched)
                        count++;
                }
            }

            return count;
        }

        static int CountVegetationTypeMatches(
            FcLevelSupplementData.VegetationInstanceDesc[] instances,
            FcLevelSupplementData.VegetationTypeDesc[] types,
            bool matched)
        {
            if (instances == null || instances.Length == 0)
                return 0;

            var knownTypes = new HashSet<int>();
            if (types != null)
            {
                for (int i = 0; i < types.Length; i++)
                    knownTypes.Add(types[i].Index);
            }

            int count = 0;
            for (int i = 0; i < instances.Length; i++)
            {
                bool hasType = knownTypes.Contains(instances[i].Type);
                if (hasType == matched)
                    count++;
            }
            return count;
        }

        static int CountVegetationTypesMissingFileName(FcLevelSupplementData.VegetationTypeDesc[] types)
        {
            if (types == null || types.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < types.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(types[i].FileName))
                    count++;
            }
            return count;
        }

        static int CountVegetationTypeDuplicateIndexes(FcLevelSupplementData.VegetationTypeDesc[] types)
        {
            if (types == null || types.Length == 0)
                return 0;

            var seen = new HashSet<int>();
            var duplicates = new HashSet<int>();
            for (int i = 0; i < types.Length; i++)
            {
                int index = types[i].Index;
                if (index < 0)
                    continue;

                if (!seen.Add(index))
                    duplicates.Add(index);
            }
            return duplicates.Count;
        }

        static int CountVegetationTypeInvalidIndexes(FcLevelSupplementData.VegetationTypeDesc[] types)
        {
            if (types == null || types.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < types.Length; i++)
            {
                if (types[i].Index < 0)
                    count++;
            }
            return count;
        }

        readonly struct BrushMaterialOverrideStats
        {
            public readonly int OverrideCount;
            public readonly int MatchedCount;
            public readonly int MissingCount;
            public readonly int MatchedByNameCount;
            public readonly int MatchedByFullNameCount;
            public readonly int MatchedByMaterialIdCount;
            public readonly int MissingWithMaterialIdCount;
            public readonly ImportReportData.CountEntry[] MissingOverrideCounts;

            public BrushMaterialOverrideStats(
                int overrideCount,
                int matchedCount,
                int missingCount,
                int matchedByNameCount,
                int matchedByFullNameCount,
                int matchedByMaterialIdCount,
                int missingWithMaterialIdCount,
                ImportReportData.CountEntry[] missingOverrideCounts)
            {
                OverrideCount = overrideCount;
                MatchedCount = matchedCount;
                MissingCount = missingCount;
                MatchedByNameCount = matchedByNameCount;
                MatchedByFullNameCount = matchedByFullNameCount;
                MatchedByMaterialIdCount = matchedByMaterialIdCount;
                MissingWithMaterialIdCount = missingWithMaterialIdCount;
                MissingOverrideCounts = missingOverrideCounts ?? Array.Empty<ImportReportData.CountEntry>();
            }
        }

        static BrushMaterialOverrideStats BuildBrushMaterialOverrideStats(
            BrushEntry[] brushes,
            FcLevelSupplementData.MaterialDesc[] materials)
        {
            if (brushes == null || brushes.Length == 0)
            {
                return new BrushMaterialOverrideStats(
                    overrideCount: 0,
                    matchedCount: 0,
                    missingCount: 0,
                    matchedByNameCount: 0,
                    matchedByFullNameCount: 0,
                    matchedByMaterialIdCount: 0,
                    missingWithMaterialIdCount: 0,
                    missingOverrideCounts: Array.Empty<ImportReportData.CountEntry>());
            }

            var knownByName = new HashSet<string>(StringComparer.Ordinal);
            var knownByFullName = new HashSet<string>(StringComparer.Ordinal);
            if (materials != null)
            {
                for (int i = 0; i < materials.Length; i++)
                {
                    string name = NormalizeMaterialKey(materials[i].Name);
                    if (!string.IsNullOrEmpty(name))
                        knownByName.Add(name);

                    string fullName = NormalizeMaterialKey(materials[i].FullName);
                    if (!string.IsNullOrEmpty(fullName))
                        knownByFullName.Add(fullName);
                }
            }

            int overrideCount = 0;
            int matchedCount = 0;
            int missingCount = 0;
            int matchedByNameCount = 0;
            int matchedByFullNameCount = 0;
            int matchedByMaterialIdCount = 0;
            int missingWithMaterialIdCount = 0;
            var missingCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < brushes.Length; i++)
            {
                var brush = brushes[i];
                string normalizedToken = NormalizeMaterialKey(brush.MaterialOverride);
                bool hasToken = !string.IsNullOrEmpty(normalizedToken);
                bool hasValidMaterialId = materials != null &&
                                          brush.MaterialId >= 0 &&
                                          brush.MaterialId < materials.Length;

                if (!hasToken && !hasValidMaterialId)
                    continue;

                overrideCount++;

                bool resolved = false;
                if (hasToken)
                {
                    if (knownByName.Contains(normalizedToken))
                    {
                        resolved = true;
                        matchedByNameCount++;
                    }
                    else if (knownByFullName.Contains(normalizedToken))
                    {
                        resolved = true;
                        matchedByFullNameCount++;
                    }
                    else
                    {
                        int slash = normalizedToken.LastIndexOf('/');
                        if (slash >= 0 && slash + 1 < normalizedToken.Length)
                        {
                            string leafName = normalizedToken.Substring(slash + 1);
                            if (knownByName.Contains(leafName))
                            {
                                resolved = true;
                                matchedByNameCount++;
                            }
                        }
                    }
                }

                if (!resolved && hasValidMaterialId)
                {
                    resolved = true;
                    matchedByMaterialIdCount++;
                }

                if (resolved)
                {
                    matchedCount++;
                    continue;
                }

                missingCount++;
                if (brush.MaterialId >= 0)
                    missingWithMaterialIdCount++;

                string missKey = hasToken
                    ? normalizedToken
                    : $"<id:{brush.MaterialId}>";
                if (missingCounts.TryGetValue(missKey, out int current))
                    missingCounts[missKey] = current + 1;
                else
                    missingCounts[missKey] = 1;
            }

            return new BrushMaterialOverrideStats(
                overrideCount,
                matchedCount,
                missingCount,
                matchedByNameCount,
                matchedByFullNameCount,
                matchedByMaterialIdCount,
                missingWithMaterialIdCount,
                ToSortedCountEntries(missingCounts));
        }

        static int CountBrushMaterialOverrides(BrushEntry[] brushes)
        {
            if (brushes == null || brushes.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < brushes.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(brushes[i].MaterialOverride) || brushes[i].MaterialId >= 0)
                    count++;
            }

            return count;
        }

        static int CountBrushMaterialOverrideMatches(
            BrushEntry[] brushes,
            FcLevelSupplementData.MaterialDesc[] materials,
            bool matched)
        {
            var stats = BuildBrushMaterialOverrideStats(brushes, materials);
            return matched ? stats.MatchedCount : stats.MissingCount;
        }

        static string NormalizeMaterialKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return value.Trim().Replace('\\', '/').Trim('/').ToLowerInvariant();
        }

        static int CountEntityModelPaths(EntityEntry[] entities)
        {
            if (entities == null || entities.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(entities[i].ModelVirtualPath))
                    count++;
            }
            return count;
        }

        static int CountEntityModelPathMatches(EntityEntry[] entities, string[] packageEntries, bool matched)
        {
            if (entities == null || entities.Length == 0)
                return 0;

            var known = new HashSet<string>(StringComparer.Ordinal);
            if (packageEntries != null)
            {
                for (int i = 0; i < packageEntries.Length; i++)
                    known.Add(NormalizePath(packageEntries[i]));
            }

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                string path = entities[i].ModelVirtualPath;
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                bool has = known.Contains(NormalizePath(path));
                if (has == matched)
                    count++;
            }
            return count;
        }

        static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            return path.Replace('\\', '/').ToLowerInvariant().Trim('/');
        }

        static int CountObjectModelPaths(LevelObjectEntry[] objects)
        {
            if (objects == null || objects.Length == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < objects.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(GetObjectModelPath(objects[i].Attributes)))
                    count++;
            }
            return count;
        }

        static int CountObjectModelPathMatches(LevelObjectEntry[] objects, string[] packageEntries, bool matched)
        {
            if (objects == null || objects.Length == 0)
                return 0;

            var known = new HashSet<string>(StringComparer.Ordinal);
            if (packageEntries != null)
            {
                for (int i = 0; i < packageEntries.Length; i++)
                    known.Add(NormalizePath(packageEntries[i]));
            }

            int count = 0;
            for (int i = 0; i < objects.Length; i++)
            {
                string path = GetObjectModelPath(objects[i].Attributes);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                bool has = known.Contains(NormalizePath(path));
                if (has == matched)
                    count++;
            }
            return count;
        }

        static string GetObjectModelPath(NameValuePair[] attributes)
        {
            if (attributes == null || attributes.Length == 0)
                return null;

            for (int i = 0; i < ObjectModelPathKeys.Length; i++)
            {
                var key = ObjectModelPathKeys[i];
                for (int j = 0; j < attributes.Length; j++)
                {
                    if (!string.Equals(attributes[j].Key, key, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!string.IsNullOrWhiteSpace(attributes[j].Value))
                        return attributes[j].Value;
                }
            }

            return null;
        }

        static readonly string[] ObjectModelPathKeys =
        {
            "Model",
            "File",
            "FileName",
            "Object",
            "Geometry",
            "Brush",
            "Prefab",
        };

        static void ApplySupplement(FcLevelLayoutDataV2 target, FcLevelSupplementData supplement)
        {
            if (supplement == null)
            {
                target.PackageEntries = Array.Empty<string>();
                target.KnownFiles = Array.Empty<FcLevelSupplementData.KnownFileStatus>();
                target.MissionXmlFiles = Array.Empty<string>();
                target.MusicXmlFiles = Array.Empty<string>();
                target.MaterialLibraries = Array.Empty<string>();
                target.MaterialLibraryRawCount = 0;
                target.NetBaiFiles = Array.Empty<string>();
                target.HideBaiFiles = Array.Empty<string>();
                target.SurfaceTypes = Array.Empty<FcLevelSupplementData.SurfaceTypeDesc>();
                target.VegetationTypes = Array.Empty<FcLevelSupplementData.VegetationTypeDesc>();
                target.Materials = Array.Empty<FcLevelSupplementData.MaterialDesc>();
                target.VegetationInstances = Array.Empty<FcLevelSupplementData.VegetationInstanceDesc>();
                target.VegetationInstancesParseMeta = default;
                target.ParserIssues = Array.Empty<FcLevelSupplementData.ParserIssue>();
                return;
            }

            target.PackageEntries = supplement.PackageEntries ?? Array.Empty<string>();
            target.KnownFiles = supplement.KnownFiles ?? Array.Empty<FcLevelSupplementData.KnownFileStatus>();
            target.MissionXmlFiles = supplement.MissionXmlFiles ?? Array.Empty<string>();
            target.MusicXmlFiles = supplement.MusicXmlFiles ?? Array.Empty<string>();
            target.MaterialLibraries = supplement.MaterialLibraries ?? Array.Empty<string>();
            target.MaterialLibraryRawCount = supplement.MaterialLibraryRawCount;
            target.NetBaiFiles = supplement.NetBaiFiles ?? Array.Empty<string>();
            target.HideBaiFiles = supplement.HideBaiFiles ?? Array.Empty<string>();
            target.SurfaceTypes = supplement.SurfaceTypes ?? Array.Empty<FcLevelSupplementData.SurfaceTypeDesc>();
            target.VegetationTypes = supplement.VegetationTypes ?? Array.Empty<FcLevelSupplementData.VegetationTypeDesc>();
            target.Materials = supplement.Materials ?? Array.Empty<FcLevelSupplementData.MaterialDesc>();
            target.VegetationInstances = supplement.VegetationInstances ?? Array.Empty<FcLevelSupplementData.VegetationInstanceDesc>();
            target.VegetationInstancesParseMeta = supplement.VegetationInstancesParseMeta;
            target.ParserIssues = supplement.ParserIssues ?? Array.Empty<FcLevelSupplementData.ParserIssue>();
        }

        static float ParseInvariantFloat(string value, float fallback)
        {
            if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float v))
                return v;
            return fallback;
        }
    }
}
