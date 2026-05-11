using System.Collections.Generic;
using NUnit.Framework;
using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Importer.Tests.Editor
{
    public sealed class CgfMeshBuilderTests
    {
        // ── helpers ───────────────────────────────────────────────────────────

        static CgfFile SimpleFile(CgfMeshChunk mesh)
        {
            return new CgfFile { MeshChunk = mesh };
        }

        static CgfMeshChunk StaticMesh(
            CryVertex[] verts,
            CryFace[] faces,
            CryUV[] uvs = null,
            CryTexFace[] texFaces = null)
        {
            return new CgfMeshChunk
            {
                ChunkID = 1,
                HasBoneInfo = false,
                Vertices = verts,
                Faces = faces,
                UVs = uvs,
                TexFaces = texFaces
            };
        }

        static CryVertex V(float x = 0f, float y = 0f, float z = 0f) =>
            new CryVertex { PX = x, PY = y, PZ = z };

        static void AddNode(CgfFile file, int chunkId, int objectId, int parentId, Matrix4x4 transform)
        {
            var node = new CgfNodeChunk
            {
                ChunkID = chunkId,
                ObjectID = objectId,
                ParentID = parentId,
                Transform = transform
            };
            file.NodeChunks.Add(node);
            file.NodeByChunkID[chunkId] = node;
        }

        static Matrix4x4 OldMatrix44WithRowTranslation(float x, float y, float z)
        {
            var m = Matrix4x4.identity;
            m.m30 = x;
            m.m31 = y;
            m.m32 = z;
            return m;
        }

        static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-5f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-5f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-5f));
        }

        // ── vertex deduplication ──────────────────────────────────────────────

        [Test]
        public void VertexDedup_SamePiSameTi_SharedOutputVertex()
        {
            // Corner 0 and corner 2 both reference (pi=0, ti=0) → only 2 unique vertices
            var file = SimpleFile(StaticMesh(
                verts:    new[] { V(0, 0, 0), V(1, 0, 0) },
                faces:    new[] { new CryFace { V0 = 0, V1 = 1, V2 = 0, MatID = 0 } },
                uvs:      new[] { new CryUV { U = 0f, V = 0f } },
                texFaces: new[] { new CryTexFace { T0 = 0, T1 = 0, T2 = 0 } }));

            var result = CgfMeshBuilder.Build(file, importSkeleton: false);

            Assert.That(result.Mesh.vertexCount, Is.EqualTo(2));
        }

        [Test]
        public void VertexDedup_SamePiDifferentTi_SeparateOutputVertices()
        {
            // Corner 0 = (pi=0, ti=0); corner 2 = (pi=0, ti=1) → same position, different UV → split
            var file = SimpleFile(StaticMesh(
                verts:    new[] { V(0, 0, 0), V(1, 0, 0) },
                faces:    new[] { new CryFace { V0 = 0, V1 = 1, V2 = 0, MatID = 0 } },
                uvs:      new[] { new CryUV { U = 0f, V = 0f }, new CryUV { U = 1f, V = 0f } },
                texFaces: new[] { new CryTexFace { T0 = 0, T1 = 0, T2 = 1 } }));

            var result = CgfMeshBuilder.Build(file, importSkeleton: false);

            Assert.That(result.Mesh.vertexCount, Is.EqualTo(3));
        }

        // ── UV V-flip ─────────────────────────────────────────────────────────

        [Test]
        public void Uv_VCoord_IsFlipped()
        {
            var file = SimpleFile(StaticMesh(
                verts:    new[] { V(0, 0, 0), V(1, 0, 0), V(0, 1, 0) },
                faces:    new[] { new CryFace { V0 = 0, V1 = 1, V2 = 2, MatID = 0 } },
                uvs:      new[] { new CryUV { U = 0.5f, V = 0.75f } },
                texFaces: new[] { new CryTexFace { T0 = 0, T1 = 0, T2 = 0 } }));

            var result = CgfMeshBuilder.Build(file, importSkeleton: false);

            var uvList = new List<Vector2>();
            result.Mesh.GetUVs(0, uvList);
            Assert.That(uvList[0].x, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(uvList[0].y, Is.EqualTo(0.25f).Within(1e-5f), "V should be flipped: 1 - 0.75");
        }

        // ── node transform bake ────────────────────────────────────────────────

        [Test]
        public void StaticMesh_NodeMatrix44RowTranslation_IsBakedIntoVertices()
        {
            var file = SimpleFile(StaticMesh(
                verts: new[] { V(0, 0, 0) },
                faces: new[] { new CryFace { V0 = 0, V1 = 0, V2 = 0, MatID = 0 } }));
            AddNode(file, chunkId: 10, objectId: 1, parentId: -1, transform: OldMatrix44WithRowTranslation(10f, 20f, 30f));

            var result = CgfMeshBuilder.Build(file, importSkeleton: false);

            AssertVector(result.Mesh.vertices[0], new Vector3(10f, 30f, -20f));
            AssertVector(result.NodeLocalOffset, Vector3.zero);
        }

        [Test]
        public void StaticMesh_NodeParentChain_IsAccumulatedBeforeBake()
        {
            var file = SimpleFile(StaticMesh(
                verts: new[] { V(0, 0, 0) },
                faces: new[] { new CryFace { V0 = 0, V1 = 0, V2 = 0, MatID = 0 } }));
            AddNode(file, chunkId: 10, objectId: -1, parentId: -1, transform: OldMatrix44WithRowTranslation(100f, 0f, 0f));
            AddNode(file, chunkId: 11, objectId: 1, parentId: 10, transform: OldMatrix44WithRowTranslation(10f, 20f, 30f));

            var result = CgfMeshBuilder.Build(file, importSkeleton: false);

            AssertVector(result.Mesh.vertices[0], new Vector3(110f, 30f, -20f));
        }

        // ── submesh grouping ──────────────────────────────────────────────────

        [Test]
        public void Submeshes_TwoDistinctMatIDs_ProduceTwoSubmeshes()
        {
            var file = SimpleFile(StaticMesh(
                verts: new[]
                {
                    V(0, 0, 0), V(1, 0, 0), V(0, 1, 0),
                    V(2, 0, 0), V(3, 0, 0), V(2, 1, 0)
                },
                faces: new[]
                {
                    new CryFace { V0 = 0, V1 = 1, V2 = 2, MatID = 0 },
                    new CryFace { V0 = 3, V1 = 4, V2 = 5, MatID = 1 }
                }));

            var result = CgfMeshBuilder.Build(file, importSkeleton: false);

            Assert.That(result.Mesh.subMeshCount, Is.EqualTo(2));
            Assert.That(result.SubmeshMaterialIds, Is.EqualTo(new[] { 0, 1 }));
        }

        // ── TryBuildBoneIndexMaps ─────────────────────────────────────────────

        [Test]
        public void TryBuildBoneIndexMaps_ValidFlatTree_BuildsCorrectMaps()
        {
            // Root bone 0 with one child bone 1
            var boneAnim = new CgfBoneAnimChunk
            {
                Bones = new[]
                {
                    new CgfBoneEntity { BoneID = 0, ChildrenCount = 1 },
                    new CgfBoneEntity { BoneID = 1, ChildrenCount = 0 }
                }
            };

            bool ok = CgfMeshBuilder.TryBuildBoneIndexMaps(boneAnim, boneCount: 2,
                out var idToIndex, out var indexToId);

            Assert.That(ok, Is.True);
            Assert.That(idToIndex[0], Is.EqualTo(0));
            Assert.That(idToIndex[1], Is.EqualTo(1));
            Assert.That(indexToId[0], Is.EqualTo(0));
            Assert.That(indexToId[1], Is.EqualTo(1));
        }

        [Test]
        public void TryBuildBoneIndexMaps_CountMismatch_ReturnsFalse()
        {
            var boneAnim = new CgfBoneAnimChunk
            {
                Bones = new[] { new CgfBoneEntity { BoneID = 0, ChildrenCount = 0 } }
            };

            bool ok = CgfMeshBuilder.TryBuildBoneIndexMaps(boneAnim, boneCount: 3,
                out _, out _);

            Assert.That(ok, Is.False);
        }

        [Test]
        public void TryBuildBoneIndexMaps_NullBoneAnim_ReturnsFalse()
        {
            bool ok = CgfMeshBuilder.TryBuildBoneIndexMaps(null, boneCount: 2,
                out _, out _);

            Assert.That(ok, Is.False);
        }

        // ── bone weights ──────────────────────────────────────────────────────

        [Test]
        public void BoneWeights_UnnormalizedLinks_SumToOne()
        {
            // Links with weights 2.0 and 1.0 (sum=3) → normalized 2/3 and 1/3
            var file = MakeSkeletalFile(
                boneCount: 2,
                links: new[]
                {
                    new CryLink { BoneID = 0, Blending = 2f },
                    new CryLink { BoneID = 1, Blending = 1f }
                });

            var result = CgfMeshBuilder.Build(file, importSkeleton: true);

            var bw = result.Mesh.boneWeights[0];
            Assert.That(bw.weight0 + bw.weight1, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(bw.weight0, Is.EqualTo(2f / 3f).Within(1e-5f));
            Assert.That(bw.weight1, Is.EqualTo(1f / 3f).Within(1e-5f));
        }

        [Test]
        public void BoneWeights_FiveLinks_OnlyTopFourIncluded()
        {
            // 5 links; top 4 by weight must cover all weight (sum = 1 after normalization)
            var file = MakeSkeletalFile(
                boneCount: 5,
                links: new[]
                {
                    new CryLink { BoneID = 0, Blending = 0.50f },
                    new CryLink { BoneID = 1, Blending = 0.30f },
                    new CryLink { BoneID = 2, Blending = 0.10f },
                    new CryLink { BoneID = 3, Blending = 0.05f },
                    new CryLink { BoneID = 4, Blending = 0.05f }
                });

            var result = CgfMeshBuilder.Build(file, importSkeleton: true);

            var bw = result.Mesh.boneWeights[0];
            float total = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
            Assert.That(total, Is.EqualTo(1f).Within(1e-5f),
                "Top-4 weights must sum to 1 after normalization");
        }

        // ── bind poses ────────────────────────────────────────────────────────

        [Test]
        public void BindPoses_IdentityBindMatrix_ProducesIdentityBindPose()
        {
            // Identity bind matrix → MatrixInImporterSpace(identity) = identity → inverse = identity
            var file = MakeSkeletalFile(
                boneCount: 1,
                links: new[] { new CryLink { BoneID = 0, Blending = 1f } },
                bindMatrices: new[] { Matrix4x4.identity });

            var result = CgfMeshBuilder.Build(file, importSkeleton: true);

            Assert.That(result.HasSkeleton, Is.True);
            var bp = result.BindPoses[0];
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    Assert.That(bp[r, c],
                        Is.EqualTo(Matrix4x4.identity[r, c]).Within(1e-4f),
                        $"bindPose[{r},{c}]");
        }

        // ── fixture builder ───────────────────────────────────────────────────

        // Builds a minimal CgfFile with a one-vertex degenerate triangle so we can
        // inspect boneWeights and bindPoses without driving the full geometry path.
        static CgfFile MakeSkeletalFile(int boneCount, CryLink[] links, Matrix4x4[] bindMatrices = null)
        {
            var boneNames = new string[boneCount];
            for (int i = 0; i < boneCount; i++)
                boneNames[i] = $"Bone{i}";

            // Flat BoneAnim tree: bone 0 is root with (boneCount-1) children.
            var bones = new CgfBoneEntity[boneCount];
            bones[0] = new CgfBoneEntity { BoneID = 0, ChildrenCount = boneCount - 1 };
            for (int i = 1; i < boneCount; i++)
                bones[i] = new CgfBoneEntity { BoneID = i, ChildrenCount = 0 };

            var matrices = new Matrix4x4[boneCount];
            for (int i = 0; i < boneCount; i++)
                matrices[i] = bindMatrices != null && i < bindMatrices.Length
                    ? bindMatrices[i]
                    : Matrix4x4.identity;

            var mesh = new CgfMeshChunk
            {
                ChunkID = 1,
                HasBoneInfo = true,
                Vertices  = new[] { V(0, 0, 0) },
                Faces     = new[] { new CryFace { V0 = 0, V1 = 0, V2 = 0, MatID = 0 } },
                BoneLinks = new[] { links }
            };

            return new CgfFile
            {
                MeshChunk   = mesh,
                BoneNames   = new CgfBoneNameListChunk { Names = boneNames },
                BoneAnim    = new CgfBoneAnimChunk { Bones = bones },
                BoneInitPos = new CgfBoneInitPosChunk { BindMatrices = matrices }
            };
        }
    }
}
