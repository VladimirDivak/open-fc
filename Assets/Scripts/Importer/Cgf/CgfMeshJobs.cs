using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OpenFarCry.Importer.Cgf
{
    // Blittable mirrors of CryVertex/CryUV — same memory layout, no managed references.
    // Used to copy managed arrays into NativeArray for Burst jobs.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct CryVertexBlittable
    {
        public float PX, PY, PZ;
        public float NX, NY, NZ;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct CryUvBlittable
    {
        public float U, V;
    }

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
        [ReadOnly] public NativeArray<CryVertexBlittable> RawVertices;
        [ReadOnly] public NativeArray<CryUvBlittable>     RawUVs;

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
}
