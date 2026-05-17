using System;
using System.Linq;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public sealed class CgfGameObjectBuilder
    {
        public readonly struct BuildRequest
        {
            public readonly BuildResult Result;
            public readonly CgfFile ParsedFile;
            public readonly CgfRigDefinition RigDefinition;
            public readonly string Name;
            public readonly CgfMaterialImportService MaterialService; // optional; null = leave slots empty
            public readonly string TextureScopeId;
            // Optional post-resolve override: receives (baseMaterials, submeshMaterialIds), returns final materials.
            public readonly Func<Material[], int[], Material[]> MaterialOverrider;

            public BuildRequest(
                BuildResult result,
                CgfFile parsedFile,
                CgfRigDefinition rigDefinition,
                string name,
                CgfMaterialImportService materialService = null,
                string textureScopeId = null,
                Func<Material[], int[], Material[]> materialOverrider = null)
            {
                Result = result;
                ParsedFile = parsedFile;
                RigDefinition = rigDefinition;
                Name = name;
                MaterialService = materialService;
                TextureScopeId = textureScopeId;
                MaterialOverrider = materialOverrider;
            }
        }

        public readonly struct BuildOutput
        {
            public readonly GameObject Root;
            public readonly Transform[] BoneTransforms;
            public readonly SkinnedMeshRenderer SkinnedMeshRenderer;
            public readonly MeshRenderer MeshRenderer;

            public BuildOutput(
                GameObject root,
                Transform[] boneTransforms,
                SkinnedMeshRenderer skinnedMeshRenderer,
                MeshRenderer meshRenderer)
            {
                Root = root;
                BoneTransforms = boneTransforms;
                SkinnedMeshRenderer = skinnedMeshRenderer;
                MeshRenderer = meshRenderer;
            }
        }

        public BuildOutput Build(BuildRequest request)
        {
            var result = request.Result;
            var go = new GameObject(request.Name);
            go.transform.localPosition = result.NodeLocalOffset;

            if (result.HasSkeleton)
            {
                var boneTransforms = CgfSkeletonBuilder.CreateBoneTransforms(
                    request.ParsedFile,
                    result,
                    request.RigDefinition,
                    go.transform);

                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = result.Mesh;
                smr.bones = boneTransforms;
                if (boneTransforms.Length > 0)
                {
                    var rootBone = boneTransforms.FirstOrDefault(t => t != null && t.parent == go.transform);
                    smr.rootBone = rootBone != null ? rootBone : boneTransforms[0];
                }
                var smrMats = request.MaterialService != null
                    ? request.MaterialService.ResolveSubmeshMaterials(
                        request.ParsedFile,
                        result.Mesh,
                        result.SubmeshMaterialIds,
                        request.TextureScopeId)
                    : new Material[result.Mesh.subMeshCount];
                smr.sharedMaterials = request.MaterialOverrider != null
                    ? request.MaterialOverrider(smrMats, result.SubmeshMaterialIds) ?? smrMats
                    : smrMats;

                if (request.MaterialService != null &&
                    request.MaterialService.RequiresUvScroll(
                        request.ParsedFile,
                        result.Mesh,
                        result.SubmeshMaterialIds))
                {
                    go.AddComponent<CgfUvScrollRuntime>();
                }

                return new BuildOutput(go, boneTransforms, smr, null);
            }

            go.AddComponent<MeshFilter>().sharedMesh = result.Mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mrMats = request.MaterialService != null
                ? request.MaterialService.ResolveSubmeshMaterials(
                    request.ParsedFile,
                    result.Mesh,
                    result.SubmeshMaterialIds,
                    request.TextureScopeId)
                : new Material[result.Mesh.subMeshCount];
            mr.sharedMaterials = request.MaterialOverrider != null
                ? request.MaterialOverrider(mrMats, result.SubmeshMaterialIds) ?? mrMats
                : mrMats;

            if (request.MaterialService != null &&
                request.MaterialService.RequiresUvScroll(
                    request.ParsedFile,
                    result.Mesh,
                    result.SubmeshMaterialIds))
            {
                go.AddComponent<CgfUvScrollRuntime>();
            }
            return new BuildOutput(go, null, null, mr);
        }
    }
}
