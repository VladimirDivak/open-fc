using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public enum CgfRigCacheMode
    {
        ProjectOnly,
        MemoryOnly,
        Hybrid
    }

    public readonly struct CgfRigCachePolicy
    {
        public readonly CgfRigCacheMode Mode;
        public readonly bool UseProjectCache;
        public readonly bool UseMemoryCache;
        public readonly bool PersistProjectAssets;
        public readonly string ProjectAssetRoot;

        public CgfRigCachePolicy(
            CgfRigCacheMode mode,
            bool useProjectCache,
            bool useMemoryCache,
            bool persistProjectAssets,
            string projectAssetRoot)
        {
            Mode = mode;
            UseProjectCache = useProjectCache;
            UseMemoryCache = useMemoryCache;
            PersistProjectAssets = persistProjectAssets;
            ProjectAssetRoot = string.IsNullOrWhiteSpace(projectAssetRoot)
                ? "Assets/FCData/RigCache"
                : projectAssetRoot.Replace('\\', '/').TrimEnd('/');
        }
    }

    [CreateAssetMenu(fileName = "CgfRigCacheSettings", menuName = "OpenFarCry/CGF Rig Cache Settings")]
    public class CgfRigCacheSettings : ScriptableObject
    {
        public const string ResourceName = "CgfRigCacheSettings";
        const string DefaultAssetRoot = "Assets/FCData/RigCache";

        [Header("Mode")]
        public CgfRigCacheMode editorMode = CgfRigCacheMode.Hybrid;
        public CgfRigCacheMode playerMode = CgfRigCacheMode.MemoryOnly;

        [Header("Project Cache")]
        public bool persistRigAssetsInEditor = true;
        public string rigAssetRoot = DefaultAssetRoot;

        public static CgfRigCacheSettings LoadOrDefault()
        {
            var loaded = Resources.Load<CgfRigCacheSettings>(ResourceName);
            if (loaded != null)
                return loaded;

            var fallback = CreateInstance<CgfRigCacheSettings>();
            fallback.editorMode = CgfRigCacheMode.Hybrid;
            fallback.playerMode = CgfRigCacheMode.MemoryOnly;
            fallback.persistRigAssetsInEditor = true;
            fallback.rigAssetRoot = DefaultAssetRoot;
            return fallback;
        }
    }

    public static class CgfRigCachePolicyResolver
    {
        public static CgfRigCachePolicy Resolve(CgfRigCacheSettings settings)
        {
            settings ??= CgfRigCacheSettings.LoadOrDefault();

            var mode = Application.isEditor ? settings.editorMode : settings.playerMode;
            bool useProject = false;
            bool useMemory = false;

            switch (mode)
            {
                case CgfRigCacheMode.ProjectOnly:
                    useProject = true;
                    break;
                case CgfRigCacheMode.MemoryOnly:
                    useMemory = true;
                    break;
                case CgfRigCacheMode.Hybrid:
                    useProject = true;
                    useMemory = true;
                    break;
            }

            if (!Application.isEditor)
            {
                useProject = false;
                useMemory = true;
            }

            if (!useProject && !useMemory)
                useMemory = true;

            bool persistProject = Application.isEditor && useProject && settings.persistRigAssetsInEditor;
            return new CgfRigCachePolicy(
                mode,
                useProject,
                useMemory,
                persistProject,
                settings.rigAssetRoot);
        }
    }
}
