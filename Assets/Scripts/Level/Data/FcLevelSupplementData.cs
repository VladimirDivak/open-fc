using System;

namespace OpenFarCry.Level.Data
{
    [Serializable]
    public sealed class FcLevelSupplementData
    {
        [Serializable]
        public struct NameValuePair
        {
            public string Key;
            public string Value;
        }

        [Serializable]
        public struct KnownFileStatus
        {
            public string Path;
            public bool Present;
        }

        [Serializable]
        public struct SurfaceTypeDesc
        {
            public int Id;
            public string Name;
            public string DetailObject;
            public string Material;
            public NameValuePair[] Attributes;
        }

        [Serializable]
        public struct VegetationTypeDesc
        {
            public int Index;
            public string FileName;
            public string Material;
            public NameValuePair[] Attributes;
        }

        [Serializable]
        public struct MaterialDesc
        {
            [Serializable]
            public struct TextureSlotDesc
            {
                public string Map;
                public string File;
                public float Amount;
                public string TexType;
                public NameValuePair[] Attributes;
            }

            public string Name;
            public string FullName;
            public string ParentName;
            public string Shader;
            public int Depth;
            public float AlphaTest;
            public float Opacity;
            public int MtlFlags;
            public string MaterialGuid;
            public TextureSlotDesc[] TextureSlots;
            public NameValuePair[] PublicParams;
            public string[] TextureRefs;
            public NameValuePair[] Attributes;
        }

        [Serializable]
        public struct VegetationInstanceDesc
        {
            public ushort X;
            public ushort Y;
            public ushort Z;
            public byte Type;
            public byte Brightness;
            public float Scale;
        }

        [Serializable]
        public struct ParserIssue
        {
            public string Source;
            public string Message;
        }

        [Serializable]
        public struct VegetationInstanceParseMeta
        {
            public string ParseMode;
            public bool HeaderCountDetected;
            public bool HeaderCountMatched;
            public int HeaderDeclaredCount;
            public int ParsedCount;
            public int TrailingBytes;
            public int SourceByteLength;
            public int CandidateDataOnlyParsedCount;
            public int CandidateDataOnlyTrailingBytes;
            public int CandidateHeaderParsedCount;
            public int CandidateHeaderTrailingBytes;
        }

        public string[] PackageEntries;
        public KnownFileStatus[] KnownFiles;
        public string[] MissionXmlFiles;
        public string[] MusicXmlFiles;
        public string[] MaterialLibraries;
        public int MaterialLibraryRawCount;
        public string[] NetBaiFiles;
        public string[] HideBaiFiles;
        public SurfaceTypeDesc[] SurfaceTypes;
        public VegetationTypeDesc[] VegetationTypes;
        public MaterialDesc[] Materials;
        public VegetationInstanceDesc[] VegetationInstances;
        public VegetationInstanceParseMeta VegetationInstancesParseMeta;
        public ParserIssue[] ParserIssues;
    }
}
