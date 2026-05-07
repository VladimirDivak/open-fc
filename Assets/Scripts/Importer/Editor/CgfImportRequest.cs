using System.Collections.Generic;
using OpenFarCry.Importer.Cgf;
using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    public sealed class CgfImportRequest
    {
        public delegate bool TryInstantiateCachedPrefabDelegate(out GameObject go);
        public delegate GameObject BuildGameObjectDelegate(
            BuildResult result,
            CgfFile parsedFile,
            CgfRigDefinition rigDefinition,
            string name,
            bool importPhysicsBoxColliders,
            bool importRagdollBodies,
            float importScale);
        public delegate void ConfigureLodGroupDelegate(GameObject root, bool hasSkeleton, float importScale, bool saveToProject);
        public delegate List<CgfImportedAnimationClip> AttachAnimationsDelegate(
            GameObject go,
            CgfFile parsedFile,
            CgfRigDefinition rigDefinition,
            string modelVirtualPath,
            float importScale);
        public delegate void ApplyPostTransformDelegate(GameObject go);
        public delegate void SaveAssetsDelegate(Mesh mesh, GameObject go, IReadOnlyList<CgfImportedAnimationClip> clips);

        public readonly string ParsedPath;
        public readonly CgfFile ParsedFile;
        public readonly IReadOnlyList<string> SiblingLodPaths;
        public readonly bool SaveToProject;
        public readonly bool ImportSkeleton;
        public readonly bool ImportAnimations;
        public readonly bool ImportPhysicsBoxColliders;
        public readonly bool ImportRagdollBodies;
        public readonly float ImportScale;
        public readonly CgfRigCachePolicy RigCachePolicy;
        public readonly string LevelScopeId;
        public readonly bool UseRuntimeImportService;
        public readonly bool UseRuntimeMemoryCache;
        public readonly bool PreferProjectCache;

        public readonly TryInstantiateCachedPrefabDelegate TryInstantiateCachedPrefab;
        public readonly BuildGameObjectDelegate BuildGameObject;
        public readonly ConfigureLodGroupDelegate ConfigureLodGroup;
        public readonly AttachAnimationsDelegate AttachAnimations;
        public readonly ApplyPostTransformDelegate ApplyPostTransform;
        public readonly SaveAssetsDelegate SaveAssets;

        public CgfImportRequest(
            string parsedPath,
            CgfFile parsedFile,
            IReadOnlyList<string> siblingLodPaths,
            bool saveToProject,
            bool importSkeleton,
            bool importAnimations,
            bool importPhysicsBoxColliders,
            bool importRagdollBodies,
            float importScale,
            CgfRigCachePolicy rigCachePolicy,
            TryInstantiateCachedPrefabDelegate tryInstantiateCachedPrefab,
            BuildGameObjectDelegate buildGameObject,
            ConfigureLodGroupDelegate configureLodGroup,
            AttachAnimationsDelegate attachAnimations,
            ApplyPostTransformDelegate applyPostTransform,
            SaveAssetsDelegate saveAssets,
            bool useRuntimeImportService = true,
            bool useRuntimeMemoryCache = true,
            bool preferProjectCache = false,
            string levelScopeId = null)
        {
            ParsedPath = parsedPath;
            ParsedFile = parsedFile;
            SiblingLodPaths = siblingLodPaths;
            SaveToProject = saveToProject;
            ImportSkeleton = importSkeleton;
            ImportAnimations = importAnimations;
            ImportPhysicsBoxColliders = importPhysicsBoxColliders;
            ImportRagdollBodies = importRagdollBodies;
            ImportScale = importScale;
            RigCachePolicy = rigCachePolicy;
            TryInstantiateCachedPrefab = tryInstantiateCachedPrefab;
            BuildGameObject = buildGameObject;
            ConfigureLodGroup = configureLodGroup;
            AttachAnimations = attachAnimations;
            ApplyPostTransform = applyPostTransform;
            SaveAssets = saveAssets;
            UseRuntimeImportService = useRuntimeImportService;
            UseRuntimeMemoryCache = useRuntimeMemoryCache;
            PreferProjectCache = preferProjectCache;
            LevelScopeId = levelScopeId;
        }
    }
}
