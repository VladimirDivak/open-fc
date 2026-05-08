using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using OpenFarCry.FileSystem;
using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Level.Data
{
    public static class FcLevelLoader
    {
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
            return mission;
        }

        // ── Mount ───────────────────────────────────────────────────────────────

        public static void EnsureLevelMounted(string levelName)
        {
            if (!TryGetInstallPath(out string installPath))
            {
                Debug.LogError("[FcLevelLoader] Game install path not configured.");
                return;
            }

            string pakPath = Path.Combine(installPath, "Levels", levelName, "level.pak");
            if (!File.Exists(pakPath))
            {
                Debug.LogWarning($"[FcLevelLoader] Level PAK not found: '{pakPath}'");
                return;
            }

            FcFileSystem.Mount(pakPath, bindRoot: $"levels/{levelName.ToLowerInvariant()}");
        }

        // ── Coordinate conversion ────────────────────────────────────────────────

        // Cry Z-up (x,y,z) → Unity Y-up (x,z,−y). No scale: level positions are in metres.
        public static Vector3 ConvertPosition(float x, float y, float z)
            => CryTransformConversion.PositionInImporterSpace(new Vector3(x, y, z), 1f);

        // Cry direction (Z-up) → Unity direction (Y-up).
        public static Vector3 ConvertDirection(float x, float y, float z)
            => CryTransformConversion.DirectionInImporterSpace(new Vector3(x, y, z));

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
                    var desc = ParseObjectNode(child);
                    if (desc != null) mission.Objects.Add(desc);
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

        static FcObjectDesc ParseObjectNode(XmlNode node)
        {
            string type = node.Attributes?["Type"]?.Value;
            string name = node.Attributes?["Name"]?.Value;
            string posStr = node.Attributes?["Pos"]?.Value;
            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(posStr)) return null;

            var desc = new FcObjectDesc();
            desc.Type = type;
            desc.Name = name ?? type;

            if (TryParseVec3(posStr, out Vector3 pos))
                desc.Pos = ConvertPosition(pos.x, pos.y, pos.z);

            string angStr = node.Attributes?["Angles"]?.Value;
            if (!string.IsNullOrEmpty(angStr) && TryParseVec3(angStr, out Vector3 ang))
                desc.Angles = ang; // raw Cry angles; converted in FcLevelSceneBuilder

            desc.AreaId = ParseInt(node.Attributes?["AreaId"]?.Value);

            if (type.Equals("Shape", StringComparison.OrdinalIgnoreCase))
                desc.ShapePoints = ParseShapePoints(node);

            if (type.Equals("AreaBox", StringComparison.OrdinalIgnoreCase))
            {
                float w = ParseFloat(node.Attributes?["Width"]?.Value, 5f);
                float h = ParseFloat(node.Attributes?["Height"]?.Value, 5f);
                float l = ParseFloat(node.Attributes?["Length"]?.Value, 5f);
                desc.AreaBoxDims = new Vector3(w, h, l);
            }

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
