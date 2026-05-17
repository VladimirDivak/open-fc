using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace OpenFarCry.Importer.Cgf
{
    [BurstCompile]
    internal struct CafRotationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> RawRotationLogs;
        [WriteOnly] public NativeArray<quaternion> OutRotations;

        public void Execute(int i)
        {
            float3 logVec = RawRotationLogs[i];
            float d = math.sqrt(math.dot(logVec, logVec));

            quaternion q;
            if (d > 1e-4f)
            {
                float m = math.sin(d) / d;
                q = new quaternion(logVec.x * m, logVec.y * m, logVec.z * m, math.cos(d));
                q = math.normalize(q);
            }
            else
            {
                q = new quaternion(logVec.x, logVec.y, logVec.z, 1.0f - d * d);
                q = math.normalize(q);
            }

            OutRotations[i] = q;
        }
    }

    [BurstCompile]
    internal struct CafEnsureContinuityJob : IJob
    {
        public NativeArray<quaternion> Rotations;

        public void Execute()
        {
            if (Rotations.Length < 2) return;

            for (int i = 1; i < Rotations.Length; i++)
            {
                if (math.dot(Rotations[i - 1], Rotations[i]) < 0f)
                {
                    Rotations[i] = new quaternion(-Rotations[i].value.x, -Rotations[i].value.y, -Rotations[i].value.z, -Rotations[i].value.w);
                }
            }
        }
    }

    [BurstCompile]
    internal struct CafTrackNormalizationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> RawPositions;
        [ReadOnly] public NativeArray<quaternion> RawRotations;
        
        [WriteOnly] public NativeArray<float3> OutPositions;
        [WriteOnly] public NativeArray<quaternion> OutRotations;

        public float ImportScale;

        public void Execute(int i)
        {
            // PositionInImporterSpace: (x, z, -y) * scale
            float3 p = RawPositions[i];
            OutPositions[i] = new float3(p.x, p.z, -p.y) * ImportScale;

            // LocalRotationInImporterSpace: q_basis * conjugate(q_cry) * q_basis_inv
            // BasisChange is 90 deg around X
            quaternion qBasis = quaternion.RotateX(math.radians(90f));
            quaternion qCry = RawRotations[i];
            
            // Cry quaternions might not be normalized in raw block
            qCry = math.normalizesafe(qCry);
            
            // rowVectorEquivalent = conjugate(q_cry)
            quaternion qRow = math.conjugate(qCry);
            
            quaternion qUnity = math.mul(math.mul(qBasis, qRow), math.conjugate(qBasis));
            OutRotations[i] = math.normalizesafe(qUnity);
        }
    }
}
