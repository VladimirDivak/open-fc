using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Level.Data
{
    public sealed class FcEntityDesc
    {
        public int Id;
        public int ParentId;
        public string EntityClass;
        public string Name;
        public string Layer;
        public Vector3 Pos;       // Unity world space (Cry→Unity converted)
        public Vector3 Angles;    // raw Cry Euler degrees (XYZ order, Z-up); converted in FcLevelSceneBuilder
        public float Scale = 1f;
        public bool HiddenInGame;
        public bool CastShadows;
        public bool SkipOnLowSpec;
        public int ViewDistRatio = 100;
        // Raw attributes from <Entity ...> root node (lossless cache path).
        public readonly Dictionary<string, string> RootAttributes =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        // Flat Properties: attributes from <Properties> + child node attrs (dot-notation prefix)
        public readonly Dictionary<string, string> Properties =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> Properties2 =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        // Returns first matching model virtual path from well-known property keys.
        public string GetModelVirtualPath()
        {
            foreach (string key in ModelPathKeys)
                if (Properties.TryGetValue(key, out string v) && !string.IsNullOrWhiteSpace(v))
                    return v;
            return null;
        }

        // Returns the entity-level material override name from the root Material attribute.
        // Format is "LibraryName.MaterialName" (e.g. "Level.CrateWoodFragile0").
        public string GetMaterialOverride()
            => RootAttributes.TryGetValue("Material", out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        static readonly string[] ModelPathKeys =
            { "object_Model", "objModel", "fileModel", "fileModel01", "fileHelmetModel" };
    }

    public sealed class FcObjectDesc
    {
        public string Type;   // "TagPoint", "Respawn", "Shape", "AreaBox", "AIAnchor", "Group"
        public string Name;
        public Vector3 Pos;
        public Vector3 Angles;
        public int AreaId;
        public Vector3[] ShapePoints; // for Type=Shape
        public Vector3 AreaBoxDims;   // (Width, Height, Length) for Type=AreaBox
        // Raw attributes from <Object ...> root node (lossless cache path).
        public readonly Dictionary<string, string> Attributes =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
    }

    // Generic mission object record used by content import pipeline.
    public sealed class FcLevelObjectDesc
    {
        public string Type;
        public string Name;
        public Vector3 Pos;
        public Vector3 Angles;
        public int AreaId;
        public readonly Dictionary<string, string> Attributes =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        public readonly List<Vector3> ShapePoints = new List<Vector3>();
    }

    // Metadata record for one CryMovie cutscene sequence from moviedata.xml.
    public sealed class FcMovieSequenceDesc
    {
        public string Name;
        public float StartTime;
        public float EndTime;
        public int NodeCount;
    }

    public sealed class FcTerrainLayerDesc
    {
        public byte   SurfaceTypeId;
        public string DetailTexturePath; // VFS path, e.g. "terrain/detail0.dds"
        public float  ScaleX = 8f;
        public float  ScaleY = 8f;
        public char   ProjAxis = 'Z';
    }

    public sealed class FcLevelEnvironmentDesc
    {
        // <Lighting>
        public Vector3 SunVector;           // Unity space (converted from Cry in loader)
        public Color SunColor = Color.white;
        public Color SkyColor = Color.grey;
        public float SunMultiplier = 1f;

        // <EnvState>
        public Color AmbientColor = new Color(0.3f, 0.3f, 0.3f);

        // <Fog>
        public Color FogColor = Color.grey;
        public float FogStart = 200f;
        public float FogEnd = 900f;

        // <LevelInfo>
        public float WaterLevel;
    }

    public sealed class FcMissionDesc
    {
        public string LevelName;
        public string MissionName;
        public readonly List<FcEntityDesc> Entities = new List<FcEntityDesc>();
        public readonly List<FcLevelObjectDesc> LevelObjects = new List<FcLevelObjectDesc>();
        public readonly List<FcObjectDesc> Objects = new List<FcObjectDesc>();
        public FcLevelEnvironmentDesc Environment;
    }
}
