using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Level.Data
{
    // ScriptableObject that serializes a Far Cry level's layout metadata — paths,
    // transforms, entity parameters — without any copyrighted geometry or texture data.
    // Created by FcLevelSceneBuilder when building a level scene; can be used to
    // rebuild scenes faster without re-reading PAK archives.
    [CreateAssetMenu(fileName = "FcLevelLayoutData",
        menuName = "OpenFarCry/Level Layout Data")]
    public sealed class FcLevelLayoutData : ScriptableObject
    {
        // ── Nested types ──────────────────────────────────────────────────────────

        [Serializable]
        public struct BrushEntry
        {
            public int    Id;
            public string VirtualPath;
            public bool   NoPhysics;
            public float[] Matrix;          // 12 floats, Cry Matrix34 row-major
            public byte   LodRatio;
            public string MaterialOverride; // null/empty if none
        }

        [Serializable]
        public struct EntityEntry
        {
            public int    Id;
            public int    ParentId;
            public string EntityClass;
            public string Name;
            public string Layer;
            public Vector3 Pos;
            public Vector3 Angles;
            public float  Scale;
            public bool   HiddenInGame;
            public bool   CastShadows;
            public bool   SkipOnLowSpec;
            public int    ViewDistRatio;
            public string ModelVirtualPath; // precomputed GetModelVirtualPath() result
            // Properties serialized as parallel arrays (Dictionary not serializable)
            public string[] PropertyKeys;
            public string[] PropertyValues;
            public string[] PropertyKeys2;
            public string[] PropertyValues2;
        }

        [Serializable]
        public struct ObjectEntry
        {
            public string   Type;
            public string   Name;
            public Vector3  Pos;
            public Vector3  Angles;
            public int      AreaId;
            public Vector3  AreaBoxDims;
            public Vector3[] ShapePoints;
        }

        [Serializable]
        public struct EnvironmentData
        {
            public Vector3 SunVector;
            public Color   SunColor;
            public Color   SkyColor;
            public float   SunMultiplier;
            public Color   AmbientColor;
            public Color   FogColor;
            public float   FogStart;
            public float   FogEnd;
            public float   WaterLevel;
        }

        // ── Fields ────────────────────────────────────────────────────────────────

        public string LevelName;
        public string MissionName;
        public BrushEntry[]  Brushes;
        public EntityEntry[] Entities;
        public ObjectEntry[] Objects;
        public EnvironmentData Environment;

        // ── Factory ───────────────────────────────────────────────────────────────

        public static FcLevelLayoutData FromMission(
            FcMissionDesc mission,
            IReadOnlyList<FcBrushDesc> brushes)
        {
            var data = CreateInstance<FcLevelLayoutData>();
            data.LevelName   = mission.LevelName;
            data.MissionName = mission.MissionName;

            // Brushes
            data.Brushes = new BrushEntry[brushes?.Count ?? 0];
            for (int i = 0; i < data.Brushes.Length; i++)
            {
                var b = brushes[i];
                data.Brushes[i] = new BrushEntry
                {
                    Id               = b.Id,
                    VirtualPath      = b.VirtualPath,
                    NoPhysics        = b.NoPhysics,
                    Matrix           = b.Matrix != null ? (float[])b.Matrix.Clone() : null,
                    LodRatio         = b.LodRatio,
                    MaterialOverride = b.MaterialOverride,
                };
            }

            // Entities
            data.Entities = new EntityEntry[mission.Entities.Count];
            for (int i = 0; i < data.Entities.Length; i++)
            {
                var e = mission.Entities[i];
                data.Entities[i] = new EntityEntry
                {
                    Id              = e.Id,
                    ParentId        = e.ParentId,
                    EntityClass     = e.EntityClass,
                    Name            = e.Name,
                    Layer           = e.Layer,
                    Pos             = e.Pos,
                    Angles          = e.Angles,
                    Scale           = e.Scale,
                    HiddenInGame    = e.HiddenInGame,
                    CastShadows     = e.CastShadows,
                    SkipOnLowSpec   = e.SkipOnLowSpec,
                    ViewDistRatio   = e.ViewDistRatio,
                    ModelVirtualPath = e.GetModelVirtualPath() ?? string.Empty,
                    PropertyKeys    = DictKeys(e.Properties),
                    PropertyValues  = DictValues(e.Properties),
                    PropertyKeys2   = DictKeys(e.Properties2),
                    PropertyValues2 = DictValues(e.Properties2),
                };
            }

            // Objects
            data.Objects = new ObjectEntry[mission.Objects.Count];
            for (int i = 0; i < data.Objects.Length; i++)
            {
                var o = mission.Objects[i];
                data.Objects[i] = new ObjectEntry
                {
                    Type        = o.Type,
                    Name        = o.Name,
                    Pos         = o.Pos,
                    Angles      = o.Angles,
                    AreaId      = o.AreaId,
                    AreaBoxDims = o.AreaBoxDims,
                    ShapePoints = o.ShapePoints != null ? (Vector3[])o.ShapePoints.Clone() : null,
                };
            }

            // Environment
            if (mission.Environment != null)
            {
                var env = mission.Environment;
                data.Environment = new EnvironmentData
                {
                    SunVector    = env.SunVector,
                    SunColor     = env.SunColor,
                    SkyColor     = env.SkyColor,
                    SunMultiplier = env.SunMultiplier,
                    AmbientColor = env.AmbientColor,
                    FogColor     = env.FogColor,
                    FogStart     = env.FogStart,
                    FogEnd       = env.FogEnd,
                    WaterLevel   = env.WaterLevel,
                };
            }

            return data;
        }

        // ── Runtime conversion ────────────────────────────────────────────────────

        public FcMissionDesc ToMission()
        {
            var mission = new FcMissionDesc
            {
                LevelName   = LevelName,
                MissionName = MissionName,
            };

            if (Entities != null)
            {
                foreach (var e in Entities)
                {
                    var desc = new FcEntityDesc
                    {
                        Id            = e.Id,
                        ParentId      = e.ParentId,
                        EntityClass   = e.EntityClass,
                        Name          = e.Name,
                        Layer         = e.Layer,
                        Pos           = e.Pos,
                        Angles        = e.Angles,
                        Scale         = e.Scale,
                        HiddenInGame  = e.HiddenInGame,
                        CastShadows   = e.CastShadows,
                        SkipOnLowSpec = e.SkipOnLowSpec,
                        ViewDistRatio = e.ViewDistRatio,
                    };
                    RestoreDict(desc.Properties,  e.PropertyKeys,  e.PropertyValues);
                    RestoreDict(desc.Properties2, e.PropertyKeys2, e.PropertyValues2);
                    mission.Entities.Add(desc);
                }
            }

            if (Objects != null)
            {
                foreach (var o in Objects)
                {
                    mission.Objects.Add(new FcObjectDesc
                    {
                        Type        = o.Type,
                        Name        = o.Name,
                        Pos         = o.Pos,
                        Angles      = o.Angles,
                        AreaId      = o.AreaId,
                        AreaBoxDims = o.AreaBoxDims,
                        ShapePoints = o.ShapePoints,
                    });
                }
            }

            mission.Environment = new FcLevelEnvironmentDesc
            {
                SunVector    = Environment.SunVector,
                SunColor     = Environment.SunColor,
                SkyColor     = Environment.SkyColor,
                SunMultiplier = Environment.SunMultiplier,
                AmbientColor = Environment.AmbientColor,
                FogColor     = Environment.FogColor,
                FogStart     = Environment.FogStart,
                FogEnd       = Environment.FogEnd,
                WaterLevel   = Environment.WaterLevel,
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
                    Id               = b.Id,
                    VirtualPath      = b.VirtualPath,
                    NoPhysics        = b.NoPhysics,
                    Matrix           = b.Matrix != null ? (float[])b.Matrix.Clone() : null,
                    LodRatio         = b.LodRatio,
                    MaterialOverride = b.MaterialOverride,
                };
            }
            return list;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        static string[] DictKeys(Dictionary<string, string> d)
        {
            if (d == null || d.Count == 0) return Array.Empty<string>();
            var arr = new string[d.Count];
            int i = 0;
            foreach (var kv in d) arr[i++] = kv.Key;
            return arr;
        }

        static string[] DictValues(Dictionary<string, string> d)
        {
            if (d == null || d.Count == 0) return Array.Empty<string>();
            var arr = new string[d.Count];
            int i = 0;
            foreach (var kv in d) arr[i++] = kv.Value;
            return arr;
        }

        static void RestoreDict(Dictionary<string, string> target, string[] keys, string[] values)
        {
            if (keys == null || values == null) return;
            int count = Mathf.Min(keys.Length, values.Length);
            for (int i = 0; i < count; i++)
                if (!string.IsNullOrEmpty(keys[i]))
                    target[keys[i]] = values[i];
        }
    }
}
