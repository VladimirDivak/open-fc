using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public sealed class CgfRagdollBuilder
    {
        public int AddBonePhysicsBoxColliders(
            CgfFile parsedFile,
            BuildResult result,
            Transform[] boneTransforms,
            float importScale)
        {
            if (parsedFile == null)
                return 0;
            var entities = parsedFile.BoneAnim?.Bones;
            if (entities == null || entities.Length == 0)
                return 0;
            if (parsedFile.BoneMeshByChunkID == null || parsedFile.BoneMeshByChunkID.Count == 0)
                return 0;
            if (boneTransforms == null || boneTransforms.Length == 0)
                return 0;

            var runtimeBoneByController = CgfSkeletonBuilder.BuildRuntimeBoneIndexByControllerId(parsedFile, result);
            int[] boneIdToIndex = result?.BoneIdToIndex;
            int colliderCount = 0;
            int mappedBoneCount = 0;
            var processedRuntimeBones = new HashSet<int>();

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                int boneId = entity.BoneID;
                if (boneId < 0)
                    continue;

                int runtimeBoneIndex = CgfSkeletonBuilder.ResolveRuntimeBoneIndex(
                    entity,
                    boneIdToIndex,
                    runtimeBoneByController,
                    boneTransforms.Length);
                if (runtimeBoneIndex < 0 || runtimeBoneIndex >= boneTransforms.Length)
                    continue;
                if (processedRuntimeBones.Contains(runtimeBoneIndex))
                    continue;

                int physGeomChunkId = entity.Physics.PhysGeomChunkID;
                if (physGeomChunkId < 0)
                    continue;
                if (!parsedFile.BoneMeshByChunkID.TryGetValue(physGeomChunkId, out var boneMeshChunk))
                    continue;

                var mesh = boneMeshChunk?.Mesh;
                if (mesh == null || !mesh.Vertices.IsCreated || mesh.Vertices.Length == 0)
                    continue;

                var verts = mesh.Vertices;
                var bone = boneTransforms[runtimeBoneIndex];
                if (bone == null)
                    continue;

                mappedBoneCount++;

                Vector3 first = CryTransformConversion.PositionInImporterSpace(
                    new Vector3(verts[0].PX, verts[0].PY, verts[0].PZ),
                    importScale);
                var bounds = new Bounds(first, Vector3.zero);
                for (int vi = 1; vi < verts.Length; vi++)
                {
                    var v = verts[vi];
                    var p = CryTransformConversion.PositionInImporterSpace(
                        new Vector3(v.PX, v.PY, v.PZ),
                        importScale);
                    bounds.Encapsulate(p);
                }

                var box = bone.GetComponent<BoxCollider>();
                if (box == null)
                    box = bone.gameObject.AddComponent<BoxCollider>();

                box.center = bounds.center;
                var size = bounds.size;
                box.size = new Vector3(
                    Mathf.Max(size.x, 0.0001f),
                    Mathf.Max(size.y, 0.0001f),
                    Mathf.Max(size.z, 0.0001f));
                processedRuntimeBones.Add(runtimeBoneIndex);
                colliderCount++;
            }

            if (colliderCount > 0)
            {
                Debug.Log($"[CgfImporter] Added {colliderCount} BoxCollider(s) from BoneMesh data.");
            }
            else if (parsedFile.BoneMeshChunks.Count > 0)
            {
                Debug.LogWarning(
                    "[CgfImporter] BoneMesh chunks exist, " +
                    $"but no BoxCollider was created (mapped bones: {mappedBoneCount}).");
            }

            return colliderCount;
        }

        public RagdollBuildResult AddRagdollBodiesAndJoints(
            GameObject root,
            Transform[] boneTransforms,
            CgfFile parsedFile,
            BuildResult result)
        {
            if (root == null || boneTransforms == null || boneTransforms.Length == 0)
                return default;

            var physicalBones = new List<Transform>(boneTransforms.Length);
            float totalVolume = 0f;
            for (int i = 0; i < boneTransforms.Length; i++)
            {
                var bone = boneTransforms[i];
                if (bone == null)
                    continue;

                var box = bone.GetComponent<BoxCollider>();
                if (box == null)
                    continue;

                physicalBones.Add(bone);
                float volume = Mathf.Max(1e-6f, box.size.x * box.size.y * box.size.z);
                totalVolume += volume;
            }

            if (physicalBones.Count == 0)
                return default;

            const float targetMassKg = 80f;
            var bodyByBone = new Dictionary<Transform, Rigidbody>(physicalBones.Count);
            for (int i = 0; i < physicalBones.Count; i++)
            {
                var bone = physicalBones[i];
                var box = bone.GetComponent<BoxCollider>();
                float volume = Mathf.Max(1e-6f, box.size.x * box.size.y * box.size.z);
                float ratio = totalVolume > 1e-6f ? volume / totalVolume : 1f / physicalBones.Count;

                var rb = bone.GetComponent<Rigidbody>();
                if (rb == null)
                    rb = bone.gameObject.AddComponent<Rigidbody>();

                rb.mass = Mathf.Max(0.05f, targetMassKg * ratio);
                rb.isKinematic = true;
                rb.useGravity = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                bodyByBone[bone] = rb;
            }

            var physByRuntimeIndex = BuildPhysicsByRuntimeIndex(parsedFile, result, boneTransforms.Length);
            int jointCount = 0;
            int physicsDrivenJointCount = 0;
            int fallbackJointCount = 0;
            var jointDiagLines = new List<string>(32);
            for (int i = 0; i < physicalBones.Count; i++)
            {
                var bone = physicalBones[i];
                if (!bodyByBone.TryGetValue(bone, out var rb))
                    continue;

                var parent = FindNearestPhysicalParent(bone, bodyByBone);
                if (parent == null)
                    continue;
                var parentRb = bodyByBone[parent];
                if (parentRb == null || parentRb == rb)
                    continue;

                var joint = bone.GetComponent<ConfigurableJoint>();
                if (joint == null)
                    joint = bone.gameObject.AddComponent<ConfigurableJoint>();

                joint.connectedBody = parentRb;
                joint.autoConfigureConnectedAnchor = true;
                joint.xMotion = ConfigurableJointMotion.Locked;
                joint.yMotion = ConfigurableJointMotion.Locked;
                joint.zMotion = ConfigurableJointMotion.Locked;
                joint.angularXMotion = ConfigurableJointMotion.Limited;
                joint.angularYMotion = ConfigurableJointMotion.Limited;
                joint.angularZMotion = ConfigurableJointMotion.Limited;

                int runtimeIndex = Array.IndexOf(boneTransforms, bone);
                bool usedPhysics = false;
                Vector3Int axisMapping = new Vector3Int(0, 1, 2);
                if (runtimeIndex >= 0 &&
                    physByRuntimeIndex.TryGetValue(runtimeIndex, out var phys) &&
                    TryApplyCryPhysicsLimitsToJoint(joint, phys, out axisMapping))
                {
                    ApplyJointAxesFromCryFrameMatrix(joint, phys, axisMapping);
                    usedPhysics = true;
                    physicsDrivenJointCount++;
                    if (ShouldLogJointForAxisDebug(bone.name))
                    {
                        string boneName = (result.BoneNames != null && runtimeIndex >= 0 && runtimeIndex < result.BoneNames.Length)
                            ? result.BoneNames[runtimeIndex]
                            : bone.name;
                        jointDiagLines.Add(BuildJointAxisDebugLine(boneName, joint, phys, axisMapping));
                    }
                }
                else
                {
                    ApplyDefaultJointLimits(joint);
                    fallbackJointCount++;
                }

                joint.projectionMode = JointProjectionMode.PositionAndRotation;
                joint.projectionDistance = 0.1f;
                joint.projectionAngle = 15f;
                joint.enablePreprocessing = false;
                if (usedPhysics)
                    joint.rotationDriveMode = RotationDriveMode.Slerp;
                jointCount++;
            }

            var controller = root.GetComponent<FcRagdollController>();
            if (controller == null)
                controller = root.AddComponent<FcRagdollController>();
            controller.RebuildCache();
            controller.SetAnimated();

            return new RagdollBuildResult(
                physicalBoneCount: physicalBones.Count,
                jointCount: jointCount,
                physicsDrivenJointCount: physicsDrivenJointCount,
                fallbackJointCount: fallbackJointCount,
                physicsByRuntimeIndex: physByRuntimeIndex,
                jointDiagnostics: jointDiagLines);
        }

        public readonly struct RagdollBuildResult
        {
            public readonly int PhysicalBoneCount;
            public readonly int JointCount;
            public readonly int PhysicsDrivenJointCount;
            public readonly int FallbackJointCount;
            public readonly Dictionary<int, CgfBonePhysics> PhysicsByRuntimeIndex;
            public readonly IReadOnlyList<string> JointDiagnostics;

            public RagdollBuildResult(
                int physicalBoneCount,
                int jointCount,
                int physicsDrivenJointCount,
                int fallbackJointCount,
                Dictionary<int, CgfBonePhysics> physicsByRuntimeIndex,
                IReadOnlyList<string> jointDiagnostics)
            {
                PhysicalBoneCount = physicalBoneCount;
                JointCount = jointCount;
                PhysicsDrivenJointCount = physicsDrivenJointCount;
                FallbackJointCount = fallbackJointCount;
                PhysicsByRuntimeIndex = physicsByRuntimeIndex;
                JointDiagnostics = jointDiagnostics;
            }
        }

        static bool ShouldLogJointForAxisDebug(string boneName)
        {
            if (string.IsNullOrEmpty(boneName))
                return false;

            string n = boneName.ToLowerInvariant();
            return n.Contains("calf") ||
                   n.Contains("thigh") ||
                   n.Contains("shin") ||
                   n.Contains("knee") ||
                   n.Contains("forearm") ||
                   n.Contains("upperarm") ||
                   n.Contains("clavicle") ||
                   n.Contains("spine") ||
                   n.Contains("neck") ||
                   n.Contains("head");
        }

        static string BuildJointAxisDebugLine(string boneName, ConfigurableJoint joint, CgfBonePhysics phys, Vector3Int axisMapping)
        {
            var ja = joint.axis.normalized;
            var js = joint.secondaryAxis.normalized;
            var jf = Vector3.Cross(ja, js).normalized;

            float min0 = NormalizeCryAngleToDegrees(phys.MinAngles.x);
            float min1 = NormalizeCryAngleToDegrees(phys.MinAngles.y);
            float min2 = NormalizeCryAngleToDegrees(phys.MinAngles.z);
            float max0 = NormalizeCryAngleToDegrees(phys.MaxAngles.x);
            float max1 = NormalizeCryAngleToDegrees(phys.MaxAngles.y);
            float max2 = NormalizeCryAngleToDegrees(phys.MaxAngles.z);

            int ix = Mathf.Clamp(axisMapping.x, 0, 2);
            int iy = Mathf.Clamp(axisMapping.y, 0, 2);
            int iz = Mathf.Clamp(axisMapping.z, 0, 2);
            float[] mins = { min0, min1, min2 };
            float[] maxs = { max0, max1, max2 };

            Vector3 fa = Vector3.zero;
            Vector3 fs = Vector3.zero;
            Vector3 ft = Vector3.zero;
            float dotA = -1f;
            float dotS = -1f;
            float dotT = -1f;
            if (TryExtractCryFrameAxes(phys.FrameMatrix, out var b0, out var b1, out var b2))
            {
                var basis = new Vector3[3] { b0, b1, b2 };
                fa = CryTransformConversion.DirectionInImporterSpace(basis[ix]).normalized;
                fs = CryTransformConversion.DirectionInImporterSpace(basis[iy]).normalized;
                ft = CryTransformConversion.DirectionInImporterSpace(basis[iz]).normalized;
                if (IsValidDirection(ja) && IsValidDirection(fa))
                    dotA = Mathf.Abs(Vector3.Dot(ja, fa));
                if (IsValidDirection(js) && IsValidDirection(fs))
                    dotS = Mathf.Abs(Vector3.Dot(js, fs));
                if (IsValidDirection(jf) && IsValidDirection(ft))
                    dotT = Mathf.Abs(Vector3.Dot(jf, ft));
            }

            return
                $"{boneName}: map=({axisMapping.x},{axisMapping.y},{axisMapping.z}), " +
                $"rawDeg=[x:{min0:F1}..{max0:F1}, y:{min1:F1}..{max1:F1}, z:{min2:F1}..{max2:F1}], " +
                $"mappedDeg=[X:{mins[ix]:F1}..{maxs[ix]:F1}, Y:{mins[iy]:F1}..{maxs[iy]:F1}, Z:{mins[iz]:F1}..{maxs[iz]:F1}], " +
                $"X=[{joint.lowAngularXLimit.limit:F1},{joint.highAngularXLimit.limit:F1}] " +
                $"Y={joint.angularYLimit.limit:F1} Z={joint.angularZLimit.limit:F1}, " +
                $"jointA={FormatVector(ja)} jointS={FormatVector(js)} jointT={FormatVector(jf)}, " +
                $"frameA={FormatVector(fa)} frameS={FormatVector(fs)} frameT={FormatVector(ft)}, " +
                $"align=({dotA:F3},{dotS:F3},{dotT:F3})";
        }

        static string FormatVector(Vector3 v)
        {
            return $"({v.x:F3},{v.y:F3},{v.z:F3})";
        }

        static Transform FindNearestPhysicalParent(Transform bone, Dictionary<Transform, Rigidbody> bodyByBone)
        {
            var parent = bone != null ? bone.parent : null;
            while (parent != null)
            {
                if (bodyByBone.ContainsKey(parent))
                    return parent;
                parent = parent.parent;
            }

            return null;
        }

        static Dictionary<int, CgfBonePhysics> BuildPhysicsByRuntimeIndex(CgfFile parsedFile, BuildResult result, int boneCount)
        {
            var map = new Dictionary<int, CgfBonePhysics>();
            var entities = parsedFile?.BoneAnim?.Bones;
            if (entities == null || entities.Length == 0)
                return map;

            int[] boneIdToIndex = result?.BoneIdToIndex;
            var runtimeBoneByController = CgfSkeletonBuilder.BuildRuntimeBoneIndexByControllerId(parsedFile, result);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                int runtimeBoneIndex = CgfSkeletonBuilder.ResolveRuntimeBoneIndex(
                    entity,
                    boneIdToIndex,
                    runtimeBoneByController,
                    boneCount);
                if (runtimeBoneIndex < 0 || runtimeBoneIndex >= boneCount)
                    continue;

                map[runtimeBoneIndex] = entity.Physics;
            }

            return map;
        }

        static void ApplyDefaultJointLimits(ConfigurableJoint joint)
        {
            joint.lowAngularXLimit = new SoftJointLimit { limit = -25f };
            joint.highAngularXLimit = new SoftJointLimit { limit = 25f };
            joint.angularYLimit = new SoftJointLimit { limit = 20f };
            joint.angularZLimit = new SoftJointLimit { limit = 20f };
            joint.angularXLimitSpring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
            joint.angularYZLimitSpring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
            joint.slerpDrive = new JointDrive
            {
                positionSpring = 0f,
                positionDamper = 0f,
                maximumForce = 0f
            };
        }

        static bool TryApplyCryPhysicsLimitsToJoint(ConfigurableJoint joint, CgfBonePhysics phys, out Vector3Int axisMapping)
        {
            float[] mins =
            {
                NormalizeCryAngleToDegrees(phys.MinAngles.x),
                NormalizeCryAngleToDegrees(phys.MinAngles.y),
                NormalizeCryAngleToDegrees(phys.MinAngles.z)
            };
            float[] maxs =
            {
                NormalizeCryAngleToDegrees(phys.MaxAngles.x),
                NormalizeCryAngleToDegrees(phys.MaxAngles.y),
                NormalizeCryAngleToDegrees(phys.MaxAngles.z)
            };

            bool[] unconstrained = new bool[3];
            for (int i = 0; i < 3; i++)
            {
                unconstrained[i] = IsCryUnconstrainedAngle(mins[i]) || IsCryUnconstrainedAngle(maxs[i]);
                if (unconstrained[i])
                {
                    mins[i] = float.NaN;
                    maxs[i] = float.NaN;
                }
            }

            bool hasAnyFinite =
                (IsFinite(mins[0]) && IsFinite(maxs[0])) ||
                (IsFinite(mins[1]) && IsFinite(maxs[1])) ||
                (IsFinite(mins[2]) && IsFinite(maxs[2]));
            if (!hasAnyFinite)
            {
                axisMapping = new Vector3Int(0, 1, 2);
                return false;
            }

            axisMapping = DetermineCryAxisMapping(mins, maxs, unconstrained);
            int ix = axisMapping.x;
            int iy = axisMapping.y;
            int iz = axisMapping.z;
            float minX = IsFinite(mins[ix]) ? mins[ix] : -10f;
            float maxX = IsFinite(maxs[ix]) ? maxs[ix] : 10f;
            float minY = mins[iy];
            float maxY = maxs[iy];
            float minZ = mins[iz];
            float maxZ = maxs[iz];

            float xExtent = Mathf.Max(Mathf.Abs(minX), Mathf.Abs(maxX));
            float yExtent = Mathf.Max(Mathf.Abs(minY), Mathf.Abs(maxY));
            float zExtent = Mathf.Max(Mathf.Abs(minZ), Mathf.Abs(maxZ));
            if (xExtent < 0.01f && yExtent < 0.01f && zExtent < 0.01f)
            {
                axisMapping = new Vector3Int(0, 1, 2);
                return false;
            }

            if (minX > maxX)
            {
                float tmp = minX;
                minX = maxX;
                maxX = tmp;
            }

            minX = Mathf.Clamp(minX, -89f, 0f);
            maxX = Mathf.Clamp(maxX, 0f, 89f);
            if (maxX - minX < 1f)
            {
                minX = -10f;
                maxX = 10f;
            }

            float yLimit = (IsFinite(minY) && IsFinite(maxY))
                ? Mathf.Clamp(Mathf.Max(Mathf.Abs(minY), Mathf.Abs(maxY)), 1f, 85f)
                : 2f;
            float zLimit = (IsFinite(minZ) && IsFinite(maxZ))
                ? Mathf.Clamp(Mathf.Max(Mathf.Abs(minZ), Mathf.Abs(maxZ)), 1f, 85f)
                : 2f;

            joint.lowAngularXLimit = new SoftJointLimit { limit = minX };
            joint.highAngularXLimit = new SoftJointLimit { limit = maxX };
            joint.angularYLimit = new SoftJointLimit { limit = yLimit };
            joint.angularZLimit = new SoftJointLimit { limit = zLimit };

            float spring = Mathf.Clamp(
                Mathf.Abs(phys.SpringTension.x) +
                Mathf.Abs(phys.SpringTension.y) +
                Mathf.Abs(phys.SpringTension.z),
                0f,
                200f);
            float damper = Mathf.Clamp(
                Mathf.Abs(phys.Damping.x) +
                Mathf.Abs(phys.Damping.y) +
                Mathf.Abs(phys.Damping.z),
                0f,
                100f);

            if (spring > 0.01f || damper > 0.01f)
            {
                var xSpring = new SoftJointLimitSpring
                {
                    spring = spring,
                    damper = damper
                };
                var yzSpring = new SoftJointLimitSpring
                {
                    spring = spring,
                    damper = damper
                };
                joint.angularXLimitSpring = xSpring;
                joint.angularYZLimitSpring = yzSpring;
                joint.slerpDrive = new JointDrive
                {
                    positionSpring = spring,
                    positionDamper = damper,
                    maximumForce = Mathf.Max(10f, spring * 10f)
                };
            }
            else
            {
                joint.angularXLimitSpring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
                joint.angularYZLimitSpring = new SoftJointLimitSpring { spring = 0f, damper = 0f };
                joint.slerpDrive = new JointDrive
                {
                    positionSpring = 0f,
                    positionDamper = 0f,
                    maximumForce = 0f
                };
            }

            return true;
        }

        static Vector3Int DetermineCryAxisMapping(float[] mins, float[] maxs, bool[] unconstrained)
        {
            float[] extent = new float[3];
            float[] asymmetry = new float[3];
            for (int i = 0; i < 3; i++)
            {
                if (unconstrained != null && i < unconstrained.Length && unconstrained[i])
                {
                    extent[i] = 0f;
                    asymmetry[i] = 0f;
                    continue;
                }

                if (!IsFinite(mins[i]) || !IsFinite(maxs[i]))
                {
                    extent[i] = 0f;
                    asymmetry[i] = 0f;
                    continue;
                }

                extent[i] = Mathf.Max(Mathf.Abs(mins[i]), Mathf.Abs(maxs[i]));
                asymmetry[i] = Mathf.Abs(mins[i] + maxs[i]);
            }

            int ix = 0;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < 3; i++)
            {
                if (unconstrained != null && i < unconstrained.Length && unconstrained[i])
                    continue;

                float score = asymmetry[i] * 2f + extent[i] * 0.1f;
                if (score > bestScore)
                {
                    bestScore = score;
                    ix = i;
                }
            }

            int a = (ix + 1) % 3;
            int b = (ix + 2) % 3;
            int iy = extent[a] >= extent[b] ? a : b;
            int iz = iy == a ? b : a;
            return new Vector3Int(ix, iy, iz);
        }

        static bool IsCryUnconstrainedAngle(float value)
        {
            return IsFinite(value) && Mathf.Abs(value) > 1000000f;
        }

        static void ApplyJointAxesFromCryFrameMatrix(ConfigurableJoint joint, CgfBonePhysics phys, Vector3Int axisMapping)
        {
            if (!TryExtractCryFrameAxes(phys.FrameMatrix, out var b0, out var b1, out var b2))
                return;

            var basis = new Vector3[3] { b0, b1, b2 };
            var axisCry = basis[Mathf.Clamp(axisMapping.x, 0, 2)];
            var secondaryCry = basis[Mathf.Clamp(axisMapping.y, 0, 2)];
            var tertiaryCry = basis[Mathf.Clamp(axisMapping.z, 0, 2)];

            var axis = CryTransformConversion.DirectionInImporterSpace(axisCry).normalized;
            var secondary = CryTransformConversion.DirectionInImporterSpace(secondaryCry).normalized;
            var tertiary = CryTransformConversion.DirectionInImporterSpace(tertiaryCry).normalized;
            if (!IsValidDirection(axis) || !IsValidDirection(secondary))
                return;

            secondary = Vector3.ProjectOnPlane(secondary, axis).normalized;
            if (!IsValidDirection(secondary))
                return;

            var forward = Vector3.Cross(axis, secondary).normalized;
            if (!IsValidDirection(forward))
                return;

            if (IsValidDirection(tertiary) && Vector3.Dot(forward, tertiary) < 0f)
                secondary = -secondary;

            joint.axis = axis;
            joint.secondaryAxis = secondary;
        }

        static bool TryExtractCryFrameAxes(Matrix4x4 frame, out Vector3 axis, out Vector3 secondary, out Vector3 tertiary)
        {
            var row0 = new Vector3(frame.m00, frame.m01, frame.m02);
            var row1 = new Vector3(frame.m10, frame.m11, frame.m12);
            var row2 = new Vector3(frame.m20, frame.m21, frame.m22);
            var col0 = new Vector3(frame.m00, frame.m10, frame.m20);
            var col1 = new Vector3(frame.m01, frame.m11, frame.m21);
            var col2 = new Vector3(frame.m02, frame.m12, frame.m22);

            if (IsValidDirection(row0) && IsValidDirection(row1) && IsValidDirection(row2))
            {
                axis = row0.normalized;
                secondary = row1.normalized;
                tertiary = row2.normalized;
                return true;
            }

            if (IsValidDirection(col0) && IsValidDirection(col1) && IsValidDirection(col2))
            {
                axis = col0.normalized;
                secondary = col1.normalized;
                tertiary = col2.normalized;
                return true;
            }

            axis = Vector3.right;
            secondary = Vector3.up;
            tertiary = Vector3.forward;
            return false;
        }

        static float NormalizeCryAngleToDegrees(float value)
        {
            if (!IsFinite(value))
                return 0f;

            float abs = Mathf.Abs(value);
            if (abs > 0.0001f && abs <= Mathf.PI + 0.1f)
                return value * Mathf.Rad2Deg;

            return value;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool IsValidDirection(Vector3 v)
        {
            return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z) && v.sqrMagnitude > 1e-8f;
        }
    }
}
