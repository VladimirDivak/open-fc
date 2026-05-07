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

            public BuildRequest(
                BuildResult result,
                CgfFile parsedFile,
                CgfRigDefinition rigDefinition,
                string name)
            {
                Result = result;
                ParsedFile = parsedFile;
                RigDefinition = rigDefinition;
                Name = name;
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
                smr.sharedMaterials = new Material[result.Mesh.subMeshCount];

                return new BuildOutput(go, boneTransforms, smr, null);
            }

            go.AddComponent<MeshFilter>().sharedMesh = result.Mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new Material[result.Mesh.subMeshCount];
            return new BuildOutput(go, null, null, mr);
        }
    }
}
