using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    static class CgfConstants
    {
        public const string Magic = "CryTek";
        public const int FileVersion = 0x0744;

        public const uint ChunkMesh         = 0xCCCC0000u;
        public const uint ChunkHelper       = 0xCCCC0001u;
        public const uint ChunkBoneAnim     = 0xCCCC0003u;
        public const uint ChunkBoneNameList = 0xCCCC0005u;
        public const uint ChunkNode         = 0xCCCC000Bu;
        public const uint ChunkController   = 0xCCCC000Du;
        public const uint ChunkTiming       = 0xCCCC000Eu;
        public const uint ChunkBoneMesh     = 0xCCCC000Fu;
        public const uint ChunkBoneLightBinding = 0xCCCC0010u;
        public const uint ChunkMeshMorphTarget  = 0xCCCC0011u;
        public const uint ChunkMtl          = 0xCCCC000Cu;
        public const uint ChunkBoneInitPos  = 0xCCCC0012u;

        public const int ChunkHeaderSize = 16; // ChunkType(4) + ChunkVersion(4) + FileOffset(4) + ChunkID(4)
        public const int BoneNameEntitySize0744 = 64; // NAME_ENTITY.name[64]
    }

    public static class CryTransformConversion
    {
        // Cry/Far Cry assets are Z-up. Use a proper rotation into Unity's Y-up
        // importer space instead of a reflection, so generated Transforms keep
        // ordinary positive scales and non-mirrored local axes.
        static readonly Matrix4x4 BasisChange = new Matrix4x4(
            new Vector4(1f, 0f, 0f, 0f),
            new Vector4(0f, 0f, -1f, 0f),
            new Vector4(0f, 1f, 0f, 0f),
            new Vector4(0f, 0f, 0f, 1f));
        static readonly Matrix4x4 InverseBasisChange = BasisChange.inverse;

        public static Vector3 PositionInImporterSpace(Vector3 p, float scale = 1f)
        {
            return new Vector3(p.x, p.z, -p.y) * scale;
        }

        public static Vector3 DirectionInImporterSpace(Vector3 v)
        {
            return new Vector3(v.x, v.z, -v.y);
        }

        // Cry animation matrices are row-vector based. Unity evaluates local rotations
        // as column-vector transforms, so transpose(R) is the equivalent basis.
        public static Quaternion LocalRotationInImporterSpace(Quaternion q)
        {
            var rowVectorEquivalent = Matrix4x4.Rotate(new Quaternion(-q.x, -q.y, -q.z, q.w).normalized);
            return RotationFromMatrix(BasisChange * rowVectorEquivalent * InverseBasisChange);
        }

        // For OLD row-vector matrices (NODE_CHUNK_DESC.tm / SBoneInitPosMatrix / CAF controllers).
        // Matrix44 nodes keep translation in row 3; Matrix43 bind poses are normalized
        // into Unity's column slot while reading.
        public static Matrix4x4 MatrixInImporterSpace(Matrix4x4 m, float scale = 1f)
        {
            var converted = BasisChange * OldRowVectorMatrixToUnityColumnMatrix(m) * InverseBasisChange;
            converted.m03 *= scale;
            converted.m13 *= scale;
            converted.m23 *= scale;
            return converted;
        }

        // For standard column-vector matrices (brush.lst Matrix34 and similar world-space matrices).
        // ReadMatrix44 already delivers a correct Unity column-vector form; only the
        // Z-up → Y-up basis change is needed — no 3x3 transpose.
        public static Matrix4x4 NodeMatrixInImporterSpace(Matrix4x4 m, float scale = 1f)
        {
            var converted = BasisChange * m * InverseBasisChange;
            converted.m03 *= scale;
            converted.m13 *= scale;
            converted.m23 *= scale;
            return converted;
        }

        public static Matrix4x4 RemoveScale(Matrix4x4 m)
        {
            var right = new Vector3(m.m00, m.m10, m.m20);
            var up = new Vector3(m.m01, m.m11, m.m21);
            var forward = new Vector3(m.m02, m.m12, m.m22);

            if (right.sqrMagnitude < 1e-10f || up.sqrMagnitude < 1e-10f || forward.sqrMagnitude < 1e-10f)
                return m;

            right.Normalize();
            up = (up - Vector3.Dot(up, right) * right).normalized;
            forward = Vector3.Cross(right, up).normalized;
            up = Vector3.Cross(forward, right).normalized;

            if (Vector3.Dot(Vector3.Cross(right, up), forward) < 0f)
                right = -right;

            var outM = Matrix4x4.identity;
            outM.m00 = right.x; outM.m10 = right.y; outM.m20 = right.z;
            outM.m01 = up.x; outM.m11 = up.y; outM.m21 = up.z;
            outM.m02 = forward.x; outM.m12 = forward.y; outM.m22 = forward.z;
            outM.m03 = m.m03; outM.m13 = m.m13; outM.m23 = m.m23;
            return outM;
        }

        static Matrix4x4 OldRowVectorMatrixToUnityColumnMatrix(Matrix4x4 m)
        {
            var outM = Matrix4x4.identity;

            outM.m00 = m.m00; outM.m01 = m.m10; outM.m02 = m.m20;
            outM.m10 = m.m01; outM.m11 = m.m11; outM.m12 = m.m21;
            outM.m20 = m.m02; outM.m21 = m.m12; outM.m22 = m.m22;

            var rowTranslation = new Vector3(m.m30, m.m31, m.m32);
            var columnTranslation = new Vector3(m.m03, m.m13, m.m23);
            var translation = rowTranslation.sqrMagnitude > 1e-12f
                ? rowTranslation
                : columnTranslation;

            outM.m03 = translation.x;
            outM.m13 = translation.y;
            outM.m23 = translation.z;
            return outM;
        }

        static Quaternion RotationFromMatrix(Matrix4x4 m)
        {
            var forward = new Vector3(m.m02, m.m12, m.m22);
            var up = new Vector3(m.m01, m.m11, m.m21);

            if (forward.sqrMagnitude < 1e-10f || up.sqrMagnitude < 1e-10f)
                return Quaternion.identity;

            return Quaternion.LookRotation(forward.normalized, up.normalized).normalized;
        }
    }

    public struct ChunkHeader
    {
        public uint ChunkType;
        public int  ChunkVersion;
        public int  FileOffset;
        public int  ChunkID;
        public int  SizeBytes;
    }

    // 24 bytes: position (Vec3) + normal (Vec3)
    public struct CryVertex
    {
        public float PX, PY, PZ;
        public float NX, NY, NZ;
    }

    // 20 bytes: 3 vertex indices + MatID + smoothing group
    public struct CryFace
    {
        public int V0, V1, V2;
        public int MatID;
        public int SmGroup;
    }

    // 8 bytes: texture coordinates
    public struct CryUV
    {
        public float U, V;
    }

    // 12 bytes: 3 UV indices per triangle
    public struct CryTexFace
    {
        public int T0, T1, T2;
    }

    // 16 bytes: bone skinning link for one vertex
    public struct CryLink
    {
        public int   BoneID;
        public float OX, OY, OZ; // bind-pose vertex offset in this bone's local space
        public float Blending;   // influence weight
    }

    public class CgfMeshChunk
    {
        public int ChunkID;
        public int ChunkVersion;
        public bool HasBoneInfo;
        public bool HasVertexColor;
        public CryVertex[]  Vertices;   // [nVerts]
        public CryFace[]    Faces;      // [nFaces]
        public CryUV[]      UVs;        // [nTVerts]
        public CryTexFace[] TexFaces;   // [nFaces]
        public CryLink[][]  BoneLinks;  // [nVerts][], null if !HasBoneInfo
    }

    public class CgfNodeChunk
    {
        public int       ChunkID;
        public string    Name;
        public int       ObjectID;    // ChunkID of referenced Mesh chunk
        public int       ParentID;    // ChunkID of parent Node, or -1
        public int       MatID;
        public int[]     ChildrenIDs;
        public Matrix4x4 Transform;   // local transform matrix (row-major, CryEngine space)
        public Vector3   Pos;
        public Quaternion Rot;
        public Vector3   Scale;
        public string    Properties;
    }

    public class CgfBoneNameListChunk
    {
        public string[] Names;
    }

    public struct CgfBonePhysics
    {
        // Chunk id of ChunkBoneMesh in source CGF. -1 when unavailable.
        public int PhysGeomChunkID;
        public int Flags;
        public Vector3 MinAngles;
        public Vector3 MaxAngles;
        public Vector3 SpringAngle;
        public Vector3 SpringTension;
        public Vector3 Damping;
        // Joint frame matrix from BONE_PHYSICS_COMP.framemtx.
        public Matrix4x4 FrameMatrix;
    }

    public struct CgfBoneEntity
    {
        public int BoneID;
        public int ParentID;
        public int ChildrenCount;
        public uint ControllerID;
        public string Properties;
        public CgfBonePhysics Physics;
    }

    public class CgfBoneAnimChunk
    {
        public int ChunkID;
        public CgfBoneEntity[] Bones;
    }

    public class CgfBoneMeshChunk
    {
        public int ChunkID;
        public CgfMeshChunk Mesh;
    }

    // From CryHeaders.h MtlTypes enum.
    public enum CgfMtlType { Unknown = 0, Standard = 1, Multi = 2, TwoSided = 3 }

    // From CryHeaders.h MTL_CHUNK_FLAGS enum.
    [System.Flags]
    public enum CgfMtlFlags
    {
        None         = 0,
        TwoSided     = 0x002,  // double-sided render
        Subtractive  = 0x020,  // subtractive blending
        Additive     = 0x010,  // additive blending
        CryShader    = 0x040,  // material uses CryEngine shader system — NOT alpha-test (~68% of all chunks have this)
        Physicalize  = 0x080,  // has physics collision geometry
        AdditiveDecal = 0x100, // additive decal variant
    }

    // Parsed data from MTL_CHUNK_DESC_0744 / 0745 / 0746 (ChunkType_Mtl = 0xCCCC000C).
    // Texture names and specular data populated when version >= 0x0744;
    // specLevel/specShininess/gloss only in 0x0745+; alphaTest float only in 0x0746.
    public class CgfMaterialChunk
    {
        public int        ChunkID;
        public int        ChunkVersion;     // 0x0744, 0x0745, or 0x0746
        public int        TableIndex;       // index in material chunk-table order
        public string     Name;
        public string     ShaderName;       // extracted from Name: "3dsMax(ShaderName)/physics" → lowercase shader id
        public CgfMtlType MtlType;
        public int        ChildCount;       // MTL_MULTI only: number of sub-materials
        public Color32    DiffuseColor;     // col_d
        public Color32    SpecularColor;    // col_s (0x0744+)
        public float      SpecLevel;        // specular intensity multiplier [0..1] (0x0745+)
        public float      SpecShininess;    // Phong shininess [0.01..1.0] → Unity Smoothness via sqrt (0x0745+)
        public float      Opacity;          // 0x0745+; 0-1, default 1 (fully opaque)
        public float      AlphaTest;        // 0x0746+; > 0 means alpha-test is active
        public CgfMtlFlags Flags;           // 0x0745+
        public string     DiffuseTextureName;  // tex_d
        public string     NormalTextureName;   // tex_b
        public string     SpecularTextureName; // tex_s
        public string     OpacityTextureName;  // tex_o
        public string     GlossTextureName;    // tex_g (0x0745+)
    }

    // Each matrix converts from mesh-space to bone-space in bind pose (CryEngine RH Z-up)
    public class CgfBoneInitPosChunk
    {
        public int MeshChunkID;
        public Matrix4x4[] BindMatrices;
    }

    public class CgfFile
    {
        public int FileType;
        public int Version;
        public string SourceVirtualPath;

        // Backward-compatible "selected" chunks used by existing builder/editor flow.
        public int SelectedMeshChunkID = -1;
        public CgfMeshChunk          MeshChunk;
        public CgfBoneInitPosChunk   BoneInitPos;

        // Full parsed chunk collections keyed by ChunkID links.
        public List<CgfMeshChunk>                MeshChunks = new List<CgfMeshChunk>();
        public Dictionary<int, CgfMeshChunk>     MeshByChunkID = new Dictionary<int, CgfMeshChunk>();
        public List<CgfBoneMeshChunk>            BoneMeshChunks = new List<CgfBoneMeshChunk>();
        public Dictionary<int, CgfBoneMeshChunk> BoneMeshByChunkID = new Dictionary<int, CgfBoneMeshChunk>();
        public List<CgfNodeChunk>    NodeChunks  = new List<CgfNodeChunk>();
        public Dictionary<int, CgfNodeChunk>     NodeByChunkID = new Dictionary<int, CgfNodeChunk>();
        public Dictionary<int, CgfBoneInitPosChunk> BoneInitPosByMeshChunkID = new Dictionary<int, CgfBoneInitPosChunk>();
        public CgfBoneNameListChunk  BoneNames;
        public CgfBoneAnimChunk      BoneAnim;

        // Material chunks keyed by ChunkID; LeafMaterials lists non-multi chunks in table order.
        public List<CgfMaterialChunk>            MaterialChunks      = new List<CgfMaterialChunk>();
        public Dictionary<int, CgfMaterialChunk> MaterialByChunkID   = new Dictionary<int, CgfMaterialChunk>();
        public List<CgfMaterialChunk>            LeafMaterials       = new List<CgfMaterialChunk>();
        public Dictionary<int, List<CgfMaterialChunk>> MaterialChildrenByParentChunkID =
            new Dictionary<int, List<CgfMaterialChunk>>();
    }
}
