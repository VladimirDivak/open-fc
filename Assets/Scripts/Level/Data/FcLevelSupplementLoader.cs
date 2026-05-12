using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using OpenFarCry.FileSystem;

namespace OpenFarCry.Level.Data
{
    public static class FcLevelSupplementLoader
    {
        static readonly string[] KnownFiles =
        {
            "leveldata.xml",
            "materials.xml",
            "brush.lst",
            "objects.lst",
            "terrain/land_map.h16",
            "terrain/cover.ctc",
            "terrain/cover_low.dds",
            "particles.lst",
            "moviedata.xml",
        };

        public static FcLevelSupplementData Load(string levelName)
        {
            FcLevelLoader.EnsureLevelMounted(levelName);
            string basePath = $"levels/{levelName.ToLowerInvariant()}";
            var issues = new List<FcLevelSupplementData.ParserIssue>();
            var parsedMaterialLibraries = Safe(
                () => ParseMaterialLibraries(basePath),
                new ParsedMaterialLibraries
                {
                    Libraries = Array.Empty<string>(),
                    RawCount = 0,
                },
                issues,
                "leveldata.xml/material-libraries");
            var vegetationParsed = Safe(
                () => ParseVegetationInstances(basePath),
                new ParsedVegetationInstances
                {
                    Instances = Array.Empty<FcLevelSupplementData.VegetationInstanceDesc>(),
                    Meta = default,
                },
                issues,
                "objects.lst/vegetation-instances");

            var data = new FcLevelSupplementData
            {
                PackageEntries = Safe(() => CollectEntries(basePath), Array.Empty<string>(), issues, "package-index"),
                KnownFiles = Safe(() => CollectKnownFileStatus(basePath), Array.Empty<FcLevelSupplementData.KnownFileStatus>(), issues, "known-files"),
                MissionXmlFiles = Safe(() => CollectByPrefix(basePath, "mission_", ".xml"), Array.Empty<string>(), issues, "mission-xml-index"),
                MusicXmlFiles = Safe(() => CollectMusicXmlFiles(basePath), Array.Empty<string>(), issues, "music-xml-index"),
                MaterialLibraries = parsedMaterialLibraries.Libraries,
                MaterialLibraryRawCount = parsedMaterialLibraries.RawCount,
                NetBaiFiles = Safe(() => CollectByPrefix(basePath, "net", ".bai"), Array.Empty<string>(), issues, "net-bai-index"),
                HideBaiFiles = Safe(() => CollectByPrefix(basePath, "hide", ".bai"), Array.Empty<string>(), issues, "hide-bai-index"),
                SurfaceTypes = Safe(() => ParseSurfaceTypes(basePath), Array.Empty<FcLevelSupplementData.SurfaceTypeDesc>(), issues, "leveldata.xml/surface-types"),
                VegetationTypes = Safe(() => ParseVegetationTypes(basePath), Array.Empty<FcLevelSupplementData.VegetationTypeDesc>(), issues, "leveldata.xml/vegetation-types"),
                Materials = Safe(() => ParseMaterials(basePath), Array.Empty<FcLevelSupplementData.MaterialDesc>(), issues, "materials.xml/materials"),
                VegetationInstances = vegetationParsed.Instances,
                VegetationInstancesParseMeta = vegetationParsed.Meta,
                ParserIssues = issues.ToArray(),
            };

            return data;
        }

        static string[] CollectEntries(string basePath)
        {
            var entries = FcFileSystem.GetEntries(basePath);
            var list = new List<string>();
            foreach (var e in entries)
                list.Add(e);
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        static FcLevelSupplementData.KnownFileStatus[] CollectKnownFileStatus(string basePath)
        {
            var result = new FcLevelSupplementData.KnownFileStatus[KnownFiles.Length];
            for (int i = 0; i < KnownFiles.Length; i++)
            {
                string path = $"{basePath}/{KnownFiles[i]}";
                result[i] = new FcLevelSupplementData.KnownFileStatus
                {
                    Path = path,
                    Present = FcFileSystem.Exists(path),
                };
            }
            return result;
        }

        static string[] CollectByPrefix(string basePath, string filePrefix, string extension)
        {
            var entries = FcFileSystem.GetEntries(basePath);
            var list = new List<string>();
            string prefix = basePath + "/";

            foreach (var path in entries)
            {
                if (!path.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                string name = path.Substring(prefix.Length);
                if (name.IndexOf('/') >= 0)
                    continue;

                if (!name.StartsWith(filePrefix, StringComparison.Ordinal) ||
                    !name.EndsWith(extension, StringComparison.Ordinal))
                    continue;

                list.Add(path);
            }

            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        static string[] CollectMusicXmlFiles(string basePath)
        {
            var entries = FcFileSystem.GetEntries($"{basePath}/music");
            var list = new List<string>();
            foreach (var path in entries)
            {
                if (path.EndsWith(".xml", StringComparison.Ordinal))
                    list.Add(path);
            }
            list.Sort(StringComparer.Ordinal);
            return list.ToArray();
        }

        static FcLevelSupplementData.SurfaceTypeDesc[] ParseSurfaceTypes(string basePath)
        {
            var doc = LoadXmlIfExists($"{basePath}/leveldata.xml");
            if (doc == null) return Array.Empty<FcLevelSupplementData.SurfaceTypeDesc>();

            var list = new List<FcLevelSupplementData.SurfaceTypeDesc>();
            var nodes = doc.GetElementsByTagName("SurfaceType");
            for (int i = 0; i < nodes.Count; i++)
            {
                var a = nodes[i].Attributes;
                list.Add(new FcLevelSupplementData.SurfaceTypeDesc
                {
                    Id = ParseInt(a?["Id"]?.Value, i),
                    Name = a?["Name"]?.Value ?? string.Empty,
                    DetailObject = a?["DetailObject"]?.Value ?? string.Empty,
                    Material = a?["Material"]?.Value ?? string.Empty,
                    Attributes = ToPairs(a),
                });
            }
            return list.ToArray();
        }

        struct ParsedMaterialLibraries
        {
            public string[] Libraries;
            public int RawCount;
        }

        static ParsedMaterialLibraries ParseMaterialLibraries(string basePath)
        {
            var doc = LoadXmlIfExists($"{basePath}/leveldata.xml");
            if (doc == null)
            {
                return new ParsedMaterialLibraries
                {
                    Libraries = Array.Empty<string>(),
                    RawCount = 0,
                };
            }

            var list = new List<string>();
            var nodes = doc.SelectNodes("//MaterialsLibrary");
            if (nodes == null)
            {
                return new ParsedMaterialLibraries
                {
                    Libraries = Array.Empty<string>(),
                    RawCount = 0,
                };
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                var attrs = nodes[i].Attributes;
                string path = attrs?["File"]?.Value ?? attrs?["Path"]?.Value ?? attrs?["Name"]?.Value;
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                list.Add(NormalizePath(path));
            }

            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(list[i]))
                    unique.Add(list[i]);
            }

            var result = new List<string>(unique);
            result.Sort(StringComparer.Ordinal);
            return new ParsedMaterialLibraries
            {
                Libraries = result.ToArray(),
                RawCount = list.Count,
            };
        }

        static FcLevelSupplementData.VegetationTypeDesc[] ParseVegetationTypes(string basePath)
        {
            var doc = LoadXmlIfExists($"{basePath}/leveldata.xml");
            if (doc == null) return Array.Empty<FcLevelSupplementData.VegetationTypeDesc>();

            var list = new List<FcLevelSupplementData.VegetationTypeDesc>();
            var nodes = doc.SelectNodes("//Vegetation/Object");
            if (nodes == null) return Array.Empty<FcLevelSupplementData.VegetationTypeDesc>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var a = nodes[i].Attributes;
                if (a?["Index"] == null || a?["FileName"] == null)
                    continue;

                list.Add(new FcLevelSupplementData.VegetationTypeDesc
                {
                    Index = ParseInt(a["Index"].Value, -1),
                    FileName = NormalizePath(a["FileName"].Value),
                    Material = a["Material"]?.Value ?? string.Empty,
                    Attributes = ToPairs(a),
                });
            }
            return list.ToArray();
        }

        static FcLevelSupplementData.MaterialDesc[] ParseMaterials(string basePath)
        {
            var doc = LoadXmlIfExists($"{basePath}/materials.xml");
            if (doc == null) return Array.Empty<FcLevelSupplementData.MaterialDesc>();

            var list = new List<FcLevelSupplementData.MaterialDesc>();
            var root = doc.DocumentElement;
            if (root == null)
                return Array.Empty<FcLevelSupplementData.MaterialDesc>();

            foreach (XmlNode child in root.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                    continue;
                if (!child.Name.Equals("Material", StringComparison.OrdinalIgnoreCase))
                    continue;

                TraverseMaterialNode(child, parentFullName: string.Empty, depth: 0, list);
            }
            return list.ToArray();
        }

        static void TraverseMaterialNode(
            XmlNode node,
            string parentFullName,
            int depth,
            List<FcLevelSupplementData.MaterialDesc> output)
        {
            var attrs = node.Attributes;
            string name = attrs?["Name"]?.Value ?? string.Empty;
            string fullName = BuildMaterialFullName(parentFullName, name);

            output.Add(new FcLevelSupplementData.MaterialDesc
            {
                Name = name,
                FullName = fullName,
                ParentName = parentFullName ?? string.Empty,
                Shader = attrs?["Shader"]?.Value ?? string.Empty,
                Depth = depth,
                TextureRefs = CollectMaterialTextureRefs(node),
                Attributes = ToPairs(attrs),
            });

            foreach (XmlNode child in node.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                    continue;
                if (!child.Name.Equals("Material", StringComparison.OrdinalIgnoreCase))
                    continue;
                TraverseMaterialNode(child, fullName, depth + 1, output);
            }
        }

        static string BuildMaterialFullName(string parentFullName, string name)
        {
            string safeName = name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(parentFullName))
                return safeName;
            if (string.IsNullOrWhiteSpace(safeName))
                return parentFullName;
            return parentFullName + "/" + safeName;
        }

        static string[] CollectMaterialTextureRefs(XmlNode materialNode)
        {
            var refs = new HashSet<string>(StringComparer.Ordinal);

            // Common direct attributes on material nodes.
            var attrs = materialNode.Attributes;
            if (attrs != null)
            {
                TryAddPath(refs, attrs["Diffuse"]?.Value);
                TryAddPath(refs, attrs["Texture"]?.Value);
                TryAddPath(refs, attrs["NormalMap"]?.Value);
                TryAddPath(refs, attrs["Bumpmap"]?.Value);
                TryAddPath(refs, attrs["Specular"]?.Value);
                TryAddPath(refs, attrs["Opacity"]?.Value);
                TryAddPath(refs, attrs["Emissive"]?.Value);
            }

            // Typical texture subnodes in Cry material XMLs.
            foreach (XmlNode child in materialNode.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                    continue;

                var childAttrs = child.Attributes;
                TryAddPath(refs, childAttrs?["File"]?.Value);
                TryAddPath(refs, childAttrs?["Map"]?.Value);
                TryAddPath(refs, childAttrs?["Texture"]?.Value);
            }

            var result = new List<string>(refs);
            result.Sort(StringComparer.Ordinal);
            return result.ToArray();
        }

        struct ParsedVegetationInstances
        {
            public FcLevelSupplementData.VegetationInstanceDesc[] Instances;
            public FcLevelSupplementData.VegetationInstanceParseMeta Meta;
        }

        struct ObjectsLstParseCandidate
        {
            public int Offset;
            public int ParsedCount;
            public int TrailingBytes;
            public string Mode;
        }

        static ParsedVegetationInstances ParseVegetationInstances(string basePath)
        {
            string path = $"{basePath}/objects.lst";
            if (!FcFileSystem.Exists(path))
            {
                return new ParsedVegetationInstances
                {
                    Instances = Array.Empty<FcLevelSupplementData.VegetationInstanceDesc>(),
                    Meta = default,
                };
            }

            byte[] bytes = FcFileSystem.ReadAllBytes(path);
            if (bytes == null || bytes.Length == 0)
            {
                return new ParsedVegetationInstances
                {
                    Instances = Array.Empty<FcLevelSupplementData.VegetationInstanceDesc>(),
                    Meta = new FcLevelSupplementData.VegetationInstanceParseMeta
                    {
                        ParseMode = "empty",
                        SourceByteLength = bytes?.Length ?? 0,
                    },
                };
            }

            // CStatObjInstForLoading: ushort x/y/z, byte type, byte brightness, float scale (12 bytes).
            bool headerDetected = false;
            bool headerMatched = false;
            int headerDeclaredCount = 0;
            if (bytes.Length >= 4)
            {
                headerDetected = true;
                headerDeclaredCount = BitConverter.ToInt32(bytes, 0);
                if (headerDeclaredCount == (bytes.Length - 4) / 12 && ((bytes.Length - 4) % 12 == 0))
                {
                    headerMatched = true;
                }
            }

            var dataOnlyCandidate = MakeCandidate(bytes.Length, offset: 0, "data-only");
            var headerCandidate = MakeCandidate(bytes.Length, offset: 4, "header+data");
            var chosen = ChooseObjectsLstCandidate(dataOnlyCandidate, headerCandidate, headerDetected, headerMatched);
            int count = chosen.ParsedCount;
            var result = new FcLevelSupplementData.VegetationInstanceDesc[count];

            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms, Encoding.ASCII, leaveOpen: false);
            ms.Position = chosen.Offset;

            for (int i = 0; i < count; i++)
            {
                result[i] = new FcLevelSupplementData.VegetationInstanceDesc
                {
                    X = r.ReadUInt16(),
                    Y = r.ReadUInt16(),
                    Z = r.ReadUInt16(),
                    Type = r.ReadByte(),
                    Brightness = r.ReadByte(),
                    Scale = r.ReadSingle(),
                };
            }

            return new ParsedVegetationInstances
            {
                Instances = result,
                Meta = new FcLevelSupplementData.VegetationInstanceParseMeta
                {
                    ParseMode = chosen.Mode ?? string.Empty,
                    HeaderCountDetected = headerDetected,
                    HeaderCountMatched = headerMatched,
                    HeaderDeclaredCount = headerDeclaredCount,
                    ParsedCount = count,
                    TrailingBytes = chosen.TrailingBytes,
                    SourceByteLength = bytes.Length,
                    CandidateDataOnlyParsedCount = dataOnlyCandidate.ParsedCount,
                    CandidateDataOnlyTrailingBytes = dataOnlyCandidate.TrailingBytes,
                    CandidateHeaderParsedCount = headerCandidate.ParsedCount,
                    CandidateHeaderTrailingBytes = headerCandidate.TrailingBytes,
                },
            };
        }

        static ObjectsLstParseCandidate MakeCandidate(int byteLength, int offset, string mode)
        {
            if (offset < 0 || offset > byteLength)
            {
                return new ObjectsLstParseCandidate
                {
                    Offset = offset,
                    ParsedCount = 0,
                    TrailingBytes = byteLength,
                    Mode = mode,
                };
            }

            int bytesLeft = byteLength - offset;
            int count = bytesLeft / 12;
            int trailing = bytesLeft - (count * 12);
            return new ObjectsLstParseCandidate
            {
                Offset = offset,
                ParsedCount = count,
                TrailingBytes = trailing,
                Mode = mode,
            };
        }

        static ObjectsLstParseCandidate ChooseObjectsLstCandidate(
            ObjectsLstParseCandidate dataOnly,
            ObjectsLstParseCandidate headerData,
            bool headerDetected,
            bool headerMatched)
        {
            if (headerDetected && headerMatched)
                return headerData;

            // Prefer the candidate with fewer trailing bytes; tie-break by higher parsed count.
            if (headerData.TrailingBytes < dataOnly.TrailingBytes)
                return headerData;
            if (dataOnly.TrailingBytes < headerData.TrailingBytes)
                return dataOnly;
            if (headerData.ParsedCount > dataOnly.ParsedCount)
                return headerData;
            return dataOnly;
        }

        static XmlDocument LoadXmlIfExists(string path)
        {
            if (!FcFileSystem.Exists(path))
                return null;

            byte[] bytes = FcFileSystem.ReadAllBytes(path);
            string text;
            try { text = Encoding.UTF8.GetString(bytes); }
            catch { text = Encoding.GetEncoding("iso-8859-1").GetString(bytes); }

            var doc = new XmlDocument();
            doc.LoadXml(text);
            return doc;
        }

        static int ParseInt(string s, int fallback)
            => int.TryParse(s, out int v) ? v : fallback;

        static T Safe<T>(Func<T> read, T fallback, List<FcLevelSupplementData.ParserIssue> issues, string source)
        {
            try { return read(); }
            catch (Exception e)
            {
                issues?.Add(new FcLevelSupplementData.ParserIssue
                {
                    Source = source ?? string.Empty,
                    Message = e.Message ?? string.Empty,
                });
                return fallback;
            }
        }

        static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            return path.Replace('\\', '/').ToLowerInvariant().Trim('/');
        }

        static FcLevelSupplementData.NameValuePair[] ToPairs(XmlAttributeCollection attrs)
        {
            if (attrs == null || attrs.Count == 0)
                return Array.Empty<FcLevelSupplementData.NameValuePair>();

            var result = new FcLevelSupplementData.NameValuePair[attrs.Count];
            for (int i = 0; i < attrs.Count; i++)
            {
                var attr = attrs[i];
                result[i] = new FcLevelSupplementData.NameValuePair
                {
                    Key = attr?.Name ?? string.Empty,
                    Value = attr?.Value ?? string.Empty,
                };
            }
            return result;
        }

        static void TryAddPath(HashSet<string> set, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string normalized = NormalizePath(path);
            if (!string.IsNullOrWhiteSpace(normalized))
                set.Add(normalized);
        }
    }
}
