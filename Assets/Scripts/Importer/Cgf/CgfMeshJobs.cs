using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    // Transforms a batch of unique (deduplicated) CGF vertices into Unity importer-space.
    // Inputs are per-unique-vertex index arrays produced by BuildVertexRemapping.
    // Math inlined from CryTransformConversion.PositionInImporterSpace / DirectionInImporterSpace.
    [BurstCompile]
    internal struct StaticVertexTransformJob : IJobParallelFor
    {
        // Per-unique-vertex: which raw position/UV index to read
        [ReadOnly] public NativeArray<int> UniquePosIdx;
        [ReadOnly] public NativeArray<int> UniqueUvIdx;

        // Raw source data
        [ReadOnly] public NativeArray<CryVertex> RawVertices;
        [ReadOnly] public NativeArray<CryUV>     RawUVs;

        // Transform parameters
        public float4x4 NodeMatrix;
        public float    ImportScale;
        public int      UvCount;  // rawUVs.Length; 0 means no UVs available

        // Output (slices into per-level NativeArrays, owned by MeshBuildData)
        [WriteOnly] public NativeArray<float3> OutPositions;
        [WriteOnly] public NativeArray<float3> OutNormals;
        [WriteOnly] public NativeArray<float2> OutUvs;

        public void Execute(int i)
        {
            var v = RawVertices[UniquePosIdx[i]];

            // CryTransformConversion.PositionInImporterSpace: (x,y,z) -> (x,z,-y) * scale
            var rawPos = new float3(v.PX, v.PZ, -v.PY) * ImportScale;
            // CryTransformConversion.DirectionInImporterSpace: (x,y,z) -> (x,z,-y)
            var rawNrm = new float3(v.NX, v.NZ, -v.NY);

            // NodeMatrix is Unity column-major float4x4 cast from Matrix4x4
            OutPositions[i] = math.mul(NodeMatrix, new float4(rawPos, 1f)).xyz;
            OutNormals[i]   = math.normalizesafe(math.mul((float3x3)NodeMatrix, rawNrm));

            int ti = UniqueUvIdx[i];
            if (UvCount > 0 && ti >= 0 && ti < UvCount)
            {
                var uv = RawUVs[ti];
                OutUvs[i] = new float2(uv.U, 1f - uv.V);
            }
            else
            {
                OutUvs[i] = float2.zero;
            }
        }
    }

    [BurstCompile]
    internal struct StaticIndexRemapJob : IJob
    {
        [ReadOnly] public NativeArray<int> SourceIndices;
        public NativeArray<int> OutIndices;
        public int VertexOffset;

        public void Execute()
        {
            for (int i = 0; i < SourceIndices.Length; i++)
            {
                OutIndices[i] = SourceIndices[i] + VertexOffset;
            }
        }
    }

    [BurstCompile]
    internal struct SkinnedVertexTransformJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> UniquePosIdx;
        [ReadOnly] public NativeArray<CryVertex> RawVertices;
        
        [ReadOnly] public NativeArray<CryLink> RawLinks;
        [ReadOnly] public NativeArray<int> LinkOffsets;
        [ReadOnly] public NativeArray<int> LinkCounts;
        
        [ReadOnly] public NativeArray<float4x4> BindGlobalsByBoneId;
        public float ImportScale;

        [WriteOnly] public NativeArray<float3> OutPositions;
        [WriteOnly] public NativeArray<float3> OutNormals;

        public void Execute(int i)
        {
            int pi = UniquePosIdx[i];
            var v = RawVertices[pi];
            
            // CryTransformConversion.PositionInImporterSpace: (x,y,z) -> (x,z,-y) * scale
            float3 rawPos = new float3(v.PX, v.PZ, -v.PY) * ImportScale;
            float3 rawNrm = new float3(v.NX, v.NZ, -v.NY);

            int linkCount = LinkCounts[pi];
            int linkOffset = LinkOffsets[pi];

            float3 pos = float3.zero;
            bool linked = false;

            if (linkCount > 0)
            {
                int used = math.min(4, linkCount);
                float topTotal = 0f;
                for (int j = 0; j < used; j++)
                {
                    var link = RawLinks[linkOffset + j];
                    if (link.BoneID >= 0 && link.BoneID < BindGlobalsByBoneId.Length)
                        topTotal += link.Blending;
                }

                if (topTotal > 1e-6f)
                {
                    float norm = 1f / topTotal;
                    for (int j = 0; j < used; j++)
                    {
                        var link = RawLinks[linkOffset + j];
                        if (link.BoneID < 0 || link.BoneID >= BindGlobalsByBoneId.Length)
                            continue;

                        // Link offset is already in Cry space, needs conversion
                        float3 offset = new float3(link.OX, link.OZ, -link.OY) * ImportScale;
                        pos += math.mul(BindGlobalsByBoneId[link.BoneID], new float4(offset, 1f)).xyz * (link.Blending * norm);
                    }
                    linked = true;
                }
            }

            OutPositions[i] = linked ? pos : rawPos;
            OutNormals[i]   = math.normalizesafe(rawNrm);
        }
    }

    [BurstCompile]
    internal struct BoneWeightBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> UniquePosIdx;
        [ReadOnly] public NativeArray<CryLink> RawLinks;
        [ReadOnly] public NativeArray<int> LinkOffsets;
        [ReadOnly] public NativeArray<int> LinkCounts;
        
        [ReadOnly] public NativeArray<int> BoneIdToIndex;
        public int BoneCount;

        [WriteOnly] public NativeArray<BoneWeight> OutWeights;

        public void Execute(int i)
        {
            int pi = UniquePosIdx[i];
            int linkCount = LinkCounts[pi];
            int linkOffset = LinkOffsets[pi];

            if (linkCount <= 0)
            {
                OutWeights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                return;
            }

            // Simple sort-of-top-4 (CryEngine links are already somewhat ordered, but we'll pick top 4)
            // For now we assume they are ordered or just take first 4 as in original code logic.
            int used = math.min(4, linkCount);
            float topTotal = 0f;
            for (int j = 0; j < used; j++)
                topTotal += RawLinks[linkOffset + j].Blending;
            
            if (topTotal < 1e-6f) topTotal = 1f;
            float norm = 1f / topTotal;

            BoneWeight bw = new BoneWeight();
            if (used > 0) { bw.boneIndex0 = Remap(RawLinks[linkOffset + 0].BoneID); bw.weight0 = RawLinks[linkOffset + 0].Blending * norm; }
            if (used > 1) { bw.boneIndex1 = Remap(RawLinks[linkOffset + 1].BoneID); bw.weight1 = RawLinks[linkOffset + 1].Blending * norm; }
            if (used > 2) { bw.boneIndex2 = Remap(RawLinks[linkOffset + 2].BoneID); bw.weight2 = RawLinks[linkOffset + 2].Blending * norm; }
            if (used > 3) { bw.boneIndex3 = Remap(RawLinks[linkOffset + 3].BoneID); bw.weight3 = RawLinks[linkOffset + 3].Blending * norm; }

            OutWeights[i] = bw;
        }

        int Remap(int boneId)
        {
            if (boneId >= 0 && boneId < BoneIdToIndex.Length)
            {
                int idx = BoneIdToIndex[boneId];
                if (idx >= 0) return idx;
            }
            return math.clamp(boneId, 0, BoneCount - 1);
        }
    }
}
