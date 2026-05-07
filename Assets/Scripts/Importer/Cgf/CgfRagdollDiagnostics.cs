using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public static class CgfRagdollDiagnostics
    {
        public static void LogImportDiagnostics(
            GameObject root,
            Transform[] boneTransforms,
            BuildResult result,
            Dictionary<int, CgfBonePhysics> physByRuntimeIndex)
        {
            if (root == null || boneTransforms == null || boneTransforms.Length == 0 || result == null)
                return;

            var bindPoses = result.BindPoses;
            var boneNames = result.BoneNames;
            var rootTransform = root.transform;

            int bindChecked = 0;
            int bindBad = 0;
            float worstBindPosErr = 0f;
            float worstBindRotErr = 0f;
            string worstBindBone = null;
            var bindIssueSamples = new List<string>(4);

            int frameChecked = 0;
            int frameBad = 0;
            float worstFrameAlignment = 1f;
            string worstFrameBone = null;
            var frameIssueSamples = new List<string>(4);

            for (int i = 0; i < boneTransforms.Length; i++)
            {
                var bone = boneTransforms[i];
                if (bone == null)
                    continue;

                string boneName = (boneNames != null && i < boneNames.Length && !string.IsNullOrEmpty(boneNames[i]))
                    ? boneNames[i]
                    : bone.name;

                if (bindPoses != null && i < bindPoses.Length)
                {
                    bindChecked++;
                    var expectedBind = bindPoses[i];
                    var actualBind = bone.worldToLocalMatrix * rootTransform.localToWorldMatrix;
                    float bindMatrixErr = MaxAbsMatrixDiff(expectedBind, actualBind);

                    var expectedRoot = expectedBind.inverse;
                    var actualRoot = rootTransform.worldToLocalMatrix * bone.localToWorldMatrix;
                    float posErr = Vector3.Distance(ExtractMatrixTranslation(expectedRoot), ExtractMatrixTranslation(actualRoot));
                    float rotErr = MatrixRotationAngle(expectedRoot, actualRoot);

                    if (posErr > worstBindPosErr || rotErr > worstBindRotErr)
                    {
                        worstBindPosErr = Mathf.Max(worstBindPosErr, posErr);
                        worstBindRotErr = Mathf.Max(worstBindRotErr, rotErr);
                        worstBindBone = boneName;
                    }

                    if (posErr > 0.01f || rotErr > 2f || bindMatrixErr > 0.01f)
                    {
                        bindBad++;
                        if (bindIssueSamples.Count < 4)
                            bindIssueSamples.Add($"{boneName}(p={posErr:F3},r={rotErr:F1},m={bindMatrixErr:F3})");
                    }
                }

                if (physByRuntimeIndex != null &&
                    physByRuntimeIndex.TryGetValue(i, out var phys) &&
                    TryExtractCryFrameAxes(phys.FrameMatrix, out var f0, out var f1, out var f2))
                {
                    frameChecked++;
                    var c0 = CryTransformConversion.DirectionInImporterSpace(f0).normalized;
                    var c1 = CryTransformConversion.DirectionInImporterSpace(f1).normalized;
                    var c2 = CryTransformConversion.DirectionInImporterSpace(f2).normalized;
                    if (!IsValidDirection(c0) || !IsValidDirection(c1) || !IsValidDirection(c2))
                        continue;

                    var bx = (bone.localRotation * Vector3.right).normalized;
                    var by = (bone.localRotation * Vector3.up).normalized;
                    var bz = (bone.localRotation * Vector3.forward).normalized;

                    float a0 = BestAxisAlignment(c0, bx, by, bz);
                    float a1 = BestAxisAlignment(c1, bx, by, bz);
                    float a2 = BestAxisAlignment(c2, bx, by, bz);
                    float avgAlignment = (a0 + a1 + a2) / 3f;

                    if (avgAlignment < worstFrameAlignment)
                    {
                        worstFrameAlignment = avgAlignment;
                        worstFrameBone = boneName;
                    }

                    if (avgAlignment < 0.75f)
                    {
                        frameBad++;
                        if (frameIssueSamples.Count < 4)
                            frameIssueSamples.Add($"{boneName}(a={avgAlignment:F3})");
                    }
                }
            }

            string worstBindBoneText = worstBindBone ?? "n/a";
            string worstFrameBoneText = worstFrameBone ?? "n/a";
            string bindSamplesText = string.Join(", ", bindIssueSamples);
            string frameSamplesText = string.Join(", ", frameIssueSamples);

            Debug.Log(
                "[CgfImporter][Diag] bindCheck=" + bindChecked +
                ", bindIssues=" + bindBad +
                ", worstBindBone=" + worstBindBoneText +
                ", worstBindPosErr=" + worstBindPosErr.ToString("F4") + "m" +
                ", worstBindRotErr=" + worstBindRotErr.ToString("F2") + "deg; " +
                "frameCheck=" + frameChecked +
                ", frameIssues=" + frameBad +
                ", worstFrameBone=" + worstFrameBoneText +
                ", worstFrameAvgAxisAlignment=" + worstFrameAlignment.ToString("F3") + " (1.0=perfect), " +
                "bindSample=[" + bindSamplesText + "], frameSample=[" + frameSamplesText + "].");
        }

        static float BestAxisAlignment(Vector3 axis, Vector3 x, Vector3 y, Vector3 z)
        {
            float ax = Mathf.Abs(Vector3.Dot(axis, x));
            float ay = Mathf.Abs(Vector3.Dot(axis, y));
            float az = Mathf.Abs(Vector3.Dot(axis, z));
            return Mathf.Max(ax, Mathf.Max(ay, az));
        }

        static float MaxAbsMatrixDiff(Matrix4x4 a, Matrix4x4 b)
        {
            float max = 0f;
            for (int r = 0; r < 4; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    float d = Mathf.Abs(a[r, c] - b[r, c]);
                    if (d > max)
                        max = d;
                }
            }

            return max;
        }

        static Vector3 ExtractMatrixTranslation(Matrix4x4 m)
        {
            return new Vector3(m.m03, m.m13, m.m23);
        }

        static float MatrixRotationAngle(Matrix4x4 a, Matrix4x4 b)
        {
            if (!TryExtractRotation(a, out var qa) || !TryExtractRotation(b, out var qb))
                return 180f;

            return Quaternion.Angle(qa, qb);
        }

        static bool TryExtractRotation(Matrix4x4 m, out Quaternion q)
        {
            var x = new Vector3(m.m00, m.m10, m.m20);
            var y = new Vector3(m.m01, m.m11, m.m21);
            var z = new Vector3(m.m02, m.m12, m.m22);
            if (!IsValidDirection(x) || !IsValidDirection(y) || !IsValidDirection(z))
            {
                q = Quaternion.identity;
                return false;
            }

            x.Normalize();
            y = Vector3.ProjectOnPlane(y, x).normalized;
            if (!IsValidDirection(y))
            {
                q = Quaternion.identity;
                return false;
            }

            z = Vector3.Cross(x, y).normalized;
            if (!IsValidDirection(z))
            {
                q = Quaternion.identity;
                return false;
            }

            q = Quaternion.LookRotation(z, y).normalized;
            return true;
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
