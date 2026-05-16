using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using OpenFarCry.FileSystem;
using UnityEngine;

namespace OpenFarCry.Level.Data
{
    public static class FcLevelLoader
    {
        static readonly HashSet<string> MountedLevelKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Public API ──────────────────────────────────────────────────────────

        public static IReadOnlyList<string> ListLevelNames()
        {
            if (!TryGetInstallPath(out string installPath))
                return Array.Empty<string>();

            string levelsDir = Path.Combine(installPath, "Levels");
            if (!Directory.Exists(levelsDir))
                return Array.Empty<string>();

            var result = new List<string>();
            foreach (string dir in Directory.GetDirectories(levelsDir))
            {
                string pak = Path.Combine(dir, "level.pak");
                if (File.Exists(pak))
                    result.Add(Path.GetFileName(dir));
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public static IReadOnlyList<string> ListMissionNames(string levelName)
        {
            EnsureLevelMounted(levelName);
            string xmlPath = LevelXmlPath(levelName, "leveldata.xml");
            if (!FcFileSystem.Exists(xmlPath))
            {
                // Fallback: try levelinfo.xml
                xmlPath = LevelXmlPath(levelName, "levelinfo.xml");
                if (!FcFileSystem.Exists(xmlPath))
                    return new[] { levelName };
            }

            try
            {
                byte[] bytes = FcFileSystem.ReadAllBytes(xmlPath);
                var doc = LoadXml(bytes);
                var missions = new List<string>();
                var nodes = doc.GetElementsByTagName("Mission");
                for (int i = 0; i < nodes.Count; i++)
                {
                    string name = nodes[i].Attributes?["Name"]?.Value;
                    if (!string.IsNullOrEmpty(name))
                        missions.Add(name);
                }
                return missions.Count > 0 ? missions : new List<string> { levelName };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FcLevelLoader] Could not read mission list for '{levelName}': {e.Message}");
                return new[] { levelName };
            }
        }

        public static FcMissionDesc LoadMission(string levelName, string missionName)
        {
            EnsureLevelMounted(levelName);

            string missionXmlPath = LevelXmlPath(levelName, $"mission_{missionName.ToLowerInvariant()}.xml");
            if (!FcFileSystem.Exists(missionXmlPath))
                throw new FileNotFoundException($"Mission XML not found: '{missionXmlPath}'");

            byte[] bytes = FcFileSystem.ReadAllBytes(missionXmlPath);
            XmlDocument doc = LoadXml(bytes);

            var mission = new FcMissionDesc { LevelName = levelName, MissionName = missionName };
            ParseMissionXml(doc, mission);
            ParseEnvironmentFromMission(doc, mission);
            ParseEnvironmentFromLevelData(levelName, mission);
            return mission;
        }

        public static bool TryLoadTerrainSettings(
            string levelName,
            out int heightmapSize,
            out int heightmapUnitSize,
            out List<FcTerrainLayerDesc> surfaceLayers)
        {
            heightmapSize = 1024;
            heightmapUnitSize = 2;
            surfaceLayers = new List<FcTerrainLayerDesc>();

            EnsureLevelMounted(levelName);

            string levelDataPath = LevelXmlPath(levelName, "leveldata.xml");
            if (!FcFileSystem.Exists(levelDataPath))
                return false;

            try
            {
                byte[] bytes = FcFileSystem.ReadAllBytes(levelDataPath);
                XmlDocument doc = LoadXml(bytes);

                XmlNodeList infoNodes = doc.GetElementsByTagName("LevelInfo");
                if (infoNodes.Count == 0)
                    return false;

                var attrs = infoNodes[0].Attributes;
                heightmapSize = ParseInt(attrs?["HeightmapSize"]?.Value, heightmapSize);
                heightmapUnitSize = ParseInt(attrs?["HeightmapUnitSize"]?.Value, heightmapUnitSize);

                ParseSurfaceTypes(doc, levelName, surfaceLayers);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FcLevelLoader] Failed to parse terrain settings from leveldata.xml for '{levelName}': {e.Message}");
                return false;
            }
        }

        // Backward-compat overload for callers that don't need surface layers.
        public static bool TryLoadTerrainSettings(string levelName, out int heightmapSize, out int heightmapUnitSize)
            => TryLoadTerrainSettings(levelName, out heightmapSize, out heightmapUnitSize, out _);

        static void ParseSurfaceTypes(XmlDocument doc, string levelName, List<FcTerrainLayerDesc> result)
        {
            XmlNodeList surfaceTypeNodes = doc.GetElementsByTagName("SurfaceType");
            byte id = 0;
            foreach (XmlNode node in surfaceTypeNodes)
            {
                if (id >= 7) break; // STYPE_BIT_MASK covers 0-6; 7 = hole

                string texAttr = node.Attributes?["DetailTexture"]?.Value;
                if (string.IsNullOrEmpty(texAttr))
                {
                    id++;
                    continue;
                }

                // DetailTexture paths in leveldata.xml are VFS-root-relative paths stored in
                // FCData/*.pak (mounted with empty bindRoot), not in the level PAK.
                string vfsPath = texAttr.ToLowerInvariant().Replace('\\', '/');

                string scaleXStr = node.Attributes?["DetailScaleX"]?.Value;
                string scaleYStr = node.Attributes?["DetailScaleY"]?.Value;
                string projAxis  = node.Attributes?["ProjAxis"]?.Value;

                result.Add(new FcTerrainLayerDesc
                {
                    SurfaceTypeId     = id,
                    DetailTexturePath = vfsPath,
                    ScaleX            = ParseFloat(scaleXStr, 8f),
                    ScaleY            = ParseFloat(scaleYStr, 8f),
                    ProjAxis          = string.IsNullOrEmpty(projAxis) ? 'Z' : projAxis[0],
                });
                id++;
            }
        }

        // ── Mount ───────────────────────────────────────────────────────────────

        public static void EnsureLevelMounted(string levelName)
        {
            if (string.IsNullOrWhiteSpace(levelName))
                return;

            string levelKey = levelName.Trim().ToLowerInvariant();
            if (MountedLevelKeys.Contains(levelKey))
                return;

            if (!TryGetInstallPath(out string installPath))
            {
                Debug.LogError("[FcLevelLoader] Game install path not configured.");
                return;
            }

            string resolvedLevelDirName = ResolveLevelDirectoryName(installPath, levelName);
            string pakPath = Path.Combine(installPath, "Levels", resolvedLevelDirName, "level.pak");
            if (!File.Exists(pakPath))
            {
                Debug.LogWarning($"[FcLevelLoader] Level PAK not found: '{pakPath}'");
                return;
            }

            FcFileSystem.Mount(pakPath, bindRoot: $"levels/{levelKey}");
            MountedLevelKeys.Add(levelKey);
        }

        static string ResolveLevelDirectoryName(string installPath, string levelName)
        {
            string levelsDir = Path.Combine(installPath, "Levels");
            if (!Directory.Exists(levelsDir))
                return levelName;

            var dirs = Directory.GetDirectories(levelsDir);
            for (int i = 0; i < dirs.Length; i++)
            {
                string dirName = Path.GetFileName(dirs[i]);
                if (string.Equals(dirName, levelName, StringComparison.OrdinalIgnoreCase))
                    return dirName;
            }

            return levelName;
        }

        // ── Coordinate conversion ────────────────────────────────────────────────

        // Cry level space (x,y,z) -> Unity scene space (x,z,y). No scale: level positions are in metres.
        // This keeps Far Cry's north/south axis aligned with Unity +Z for level layouts.
        public static Vector3 ConvertPosition(float x, float y, float z)
            => new Vector3(x, z, y);

        // Cry direction (Z-up) -> Unity scene direction (Y-up).
        public static Vector3 ConvertDirection(float x, float y, float z)
            => new Vector3(x, z, y);

        // ── Private ──────────────────────────────────────────────────────────────

        static void ParseMissionXml(XmlDocument doc, FcMissionDesc mission)
        {
            // Find <Objects> node
            XmlNodeList objectsNodes = doc.GetElementsByTagName("Objects");
            if (objectsNodes.Count == 0)
                return;

            XmlNode objectsNode = objectsNodes[0];

            foreach (XmlNode child in objectsNode.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;

                string tag = child.Name;

                if (tag.Equals("Entity", StringComparison.OrdinalIgnoreCase))
                {
                    var desc = ParseEntityNode(child);
                    if (desc != null) mission.Entities.Add(desc);
                }
                else if (tag.Equals("Object", StringComparison.OrdinalIgnoreCase))
                {
                    var levelObject = ParseLevelObjectNode(child);
                    if (levelObject == null) continue;

                    mission.LevelObjects.Add(levelObject);
                    mission.Objects.Add(ToLegacyObject(levelObject));
                }
            }
        }

        static FcEntityDesc ParseEntityNode(XmlNode node)
        {
            string cls = node.Attributes?["EntityClass"]?.Value;
            string name = node.Attributes?["Name"]?.Value;
            string posStr = node.Attributes?["Pos"]?.Value;
            if (string.IsNullOrEmpty(cls) || string.IsNullOrEmpty(posStr)) return null;

            var desc = new FcEntityDesc();
            CopyAttributes(node.Attributes, desc.RootAttributes);
            desc.EntityClass = cls;
            desc.Name = name ?? cls;
            desc.Layer = node.Attributes?["Layer"]?.Value ?? string.Empty;
            desc.Id = ParseInt(node.Attributes?["EntityId"]?.Value);
            desc.ParentId = ParseInt(node.Attributes?["ParentId"]?.Value);
            desc.HiddenInGame = ParseInt(node.Attributes?["HiddenInGame"]?.Value) != 0;
            desc.CastShadows = ParseInt(node.Attributes?["CastShadows"]?.Value) != 0;
            desc.SkipOnLowSpec = ParseInt(node.Attributes?["SkipOnLowSpec"]?.Value) != 0;
            desc.ViewDistRatio = ParseInt(node.Attributes?["ViewDistRatio"]?.Value, 100);

            if (TryParseVec3(posStr, out Vector3 pos))
                desc.Pos = ConvertPosition(pos.x, pos.y, pos.z);

            string angStr = node.Attributes?["Angles"]?.Value;
            if (!string.IsNullOrEmpty(angStr) && TryParseVec3(angStr, out Vector3 ang))
                desc.Angles = ang; // raw Cry angles (degrees); converted in FcLevelSceneBuilder

            string scaleStr = node.Attributes?["Scale"]?.Value;
            if (!string.IsNullOrEmpty(scaleStr) && TryParseVec3(scaleStr, out Vector3 scaleVec))
                desc.Scale = scaleVec.x;

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                if (child.Name.Equals("Properties", StringComparison.OrdinalIgnoreCase))
                    FlattenProperties(child, desc.Properties);
                else if (child.Name.Equals("Properties2", StringComparison.OrdinalIgnoreCase))
                    FlattenProperties(child, desc.Properties2);
            }

            return desc;
        }

        static FcLevelObjectDesc ParseLevelObjectNode(XmlNode node)
        {
            string type = node.Attributes?["Type"]?.Value;
            string name = node.Attributes?["Name"]?.Value;
            string posStr = node.Attributes?["Pos"]?.Value;
            if (string.IsNullOrEmpty(type)) return null;

            var desc = new FcLevelObjectDesc();
            CopyAttributes(node.Attributes, desc.Attributes);
            desc.Type = type;
            desc.Name = name ?? type;

            if (!string.IsNullOrEmpty(posStr) && TryParseVec3(posStr, out Vector3 pos))
                desc.Pos = ConvertPosition(pos.x, pos.y, pos.z);

            string angStr = node.Attributes?["Angles"]?.Value;
            if (!string.IsNullOrEmpty(angStr) && TryParseVec3(angStr, out Vector3 ang))
                desc.Angles = ang; // raw Cry angles; converted in FcLevelSceneBuilder

            desc.AreaId = ParseInt(node.Attributes?["AreaId"]?.Value);
            FlattenObjectChildAttributes(node, desc.Attributes);

            if (type.Equals("Shape", StringComparison.OrdinalIgnoreCase))
                desc.ShapePoints.AddRange(ParseShapePoints(node));

            return desc;
        }

        static FcObjectDesc ToLegacyObject(FcLevelObjectDesc src)
        {
            var desc = new FcObjectDesc
            {
                Type = src.Type,
                Name = src.Name,
                Pos = src.Pos,
                Angles = src.Angles,
                AreaId = src.AreaId,
                ShapePoints = src.ShapePoints.Count > 0 ? src.ShapePoints.ToArray() : null,
            };

            if (src.Attributes.TryGetValue("Width", out string wText) &&
                src.Attributes.TryGetValue("Height", out string hText) &&
                src.Attributes.TryGetValue("Length", out string lText))
            {
                desc.AreaBoxDims = new Vector3(
                    ParseFloat(wText, 5f),
                    ParseFloat(hText, 5f),
                    ParseFloat(lText, 5f));
            }

            CopyDictionary(src.Attributes, desc.Attributes);
            return desc;
        }

        static Vector3[] ParseShapePoints(XmlNode shapeNode)
        {
            var points = new List<Vector3>();
            foreach (XmlNode child in shapeNode.ChildNodes)
            {
                if (!child.Name.Equals("Points", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (XmlNode pt in child.ChildNodes)
                {
                    string posStr = pt.Attributes?["Pos"]?.Value;
                    if (TryParseVec3(posStr, out Vector3 p))
                        points.Add(ConvertPosition(p.x, p.y, p.z));
                }
            }
            return points.ToArray();
        }

        static void ParseEnvironmentFromMission(XmlDocument doc, FcMissionDesc mission)
        {
            XmlNodeList envNodes = doc.GetElementsByTagName("Environment");
            if (envNodes.Count == 0) return;

            var env = new FcLevelEnvironmentDesc();
            XmlNode envNode = envNodes[0];

            foreach (XmlNode child in envNode.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;

                if (child.Name.Equals("Lighting", StringComparison.OrdinalIgnoreCase))
                {
                    string svStr = child.Attributes?["SunVector"]?.Value;
                    if (TryParseVec3(svStr, out Vector3 sv))
                        env.SunVector = ConvertDirection(sv.x, sv.y, sv.z);

                    string sunColorInt = child.Attributes?["SunColor"]?.Value;
                    if (!string.IsNullOrEmpty(sunColorInt) && int.TryParse(sunColorInt, out int sc))
                        env.SunColor = IntToColor(sc);

                    string skyColorInt = child.Attributes?["SkyColor"]?.Value;
                    if (!string.IsNullOrEmpty(skyColorInt) && int.TryParse(skyColorInt, out int skc))
                        env.SkyColor = IntToColor(skc);

                    env.SunMultiplier = ParseFloat(child.Attributes?["SunMultiplier"]?.Value, 1f);
                }
                else if (child.Name.Equals("EnvState", StringComparison.OrdinalIgnoreCase))
                {
                    string ambStr = child.Attributes?["OutdoorAmbientColor"]?.Value;
                    if (!string.IsNullOrEmpty(ambStr))
                        env.AmbientColor = ParseRgb255(ambStr);
                }
                else if (child.Name.Equals("Fog", StringComparison.OrdinalIgnoreCase))
                {
                    string fogColorStr = child.Attributes?["Color"]?.Value;
                    if (!string.IsNullOrEmpty(fogColorStr))
                        env.FogColor = ParseRgb255(fogColorStr);

                    env.FogStart = ParseFloat(child.Attributes?["Start"]?.Value, 200f);
                    env.FogEnd = ParseFloat(child.Attributes?["End"]?.Value, 900f);
                }
            }

            mission.Environment = env;
        }

        static void ParseEnvironmentFromLevelData(string levelName, FcMissionDesc mission)
        {
            if (mission == null)
                return;

            if (mission.Environment == null)
                mission.Environment = new FcLevelEnvironmentDesc();

            string levelDataPath = LevelXmlPath(levelName, "leveldata.xml");
            if (!FcFileSystem.Exists(levelDataPath))
                return;

            try
            {
                byte[] bytes = FcFileSystem.ReadAllBytes(levelDataPath);
                XmlDocument doc = LoadXml(bytes);
                XmlNodeList infoNodes = doc.GetElementsByTagName("LevelInfo");
                if (infoNodes.Count == 0)
                    return;

                var attrs = infoNodes[0].Attributes;
                string waterText = attrs?["WaterLevel"]?.Value;
                mission.Environment.WaterLevel = ParseFloat(waterText, mission.Environment.WaterLevel);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FcLevelLoader] Failed to parse WaterLevel from leveldata.xml for '{levelName}': {e.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        static void FlattenProperties(XmlNode node, Dictionary<string, string> dict, string prefix = "")
        {
            if (node.Attributes != null)
            {
                foreach (XmlAttribute attr in node.Attributes)
                    dict[prefix + attr.Name] = attr.Value;
            }

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element) continue;
                string childPrefix = string.IsNullOrEmpty(prefix)
                    ? child.Name + "."
                    : prefix + child.Name + ".";
                FlattenProperties(child, dict, childPrefix);
            }
        }

        static void FlattenObjectChildAttributes(XmlNode objectNode, Dictionary<string, string> dict)
        {
            foreach (XmlNode child in objectNode.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                    continue;

                string prefix = child.Name + ".";
                FlattenProperties(child, dict, prefix);
            }
        }

        static void CopyAttributes(XmlAttributeCollection attributes, Dictionary<string, string> dict)
        {
            dict.Clear();
            if (attributes == null) return;

            foreach (XmlAttribute attr in attributes)
                dict[attr.Name] = attr.Value;
        }

        static void CopyDictionary(Dictionary<string, string> source, Dictionary<string, string> target)
        {
            target.Clear();
            foreach (var kv in source)
                target[kv.Key] = kv.Value;
        }

        static XmlDocument LoadXml(byte[] bytes)
        {
            string text;
            try { text = Encoding.UTF8.GetString(bytes); }
            catch { text = Encoding.GetEncoding("iso-8859-1").GetString(bytes); }

            var doc = new XmlDocument();
            doc.LoadXml(text);
            return doc;
        }

        static string LevelXmlPath(string levelName, string fileName)
            => $"levels/{levelName.ToLowerInvariant()}/{fileName.ToLowerInvariant()}";

        static bool TryGetInstallPath(out string path)
        {
            var settings = Resources.Load<FcFileSystemSettings>("FcFileSystemSettings");
            if (settings == null || string.IsNullOrWhiteSpace(settings.gameInstallPath))
            {
                path = null;
                return false;
            }
            path = settings.gameInstallPath;
            return true;
        }

        static bool TryParseVec3(string s, out Vector3 v)
        {
            v = default;
            if (string.IsNullOrEmpty(s)) return false;

            string[] parts = s.Split(',');
            if (parts.Length < 3) return false;

            if (float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float z))
            {
                v = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        static int ParseInt(string s, int fallback = 0)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return int.TryParse(s, out int v) ? v : fallback;
        }

        static float ParseFloat(string s, float fallback = 0f)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }

        // "R,G,B" with values 0-255
        static Color ParseRgb255(string s)
        {
            if (TryParseVec3(s, out Vector3 v))
                return new Color(v.x / 255f, v.y / 255f, v.z / 255f);
            return Color.white;
        }

        // CryEngine packed BGR int → Color
        static Color IntToColor(int packed)
        {
            float r = ((packed) & 0xFF) / 255f;
            float g = ((packed >> 8) & 0xFF) / 255f;
            float b = ((packed >> 16) & 0xFF) / 255f;
            return new Color(r, g, b);
        }
    }
}
