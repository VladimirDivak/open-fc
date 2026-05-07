namespace OpenFarCry.Importer.Cgf
{
    public sealed class CgfRuntimeImportRequest
    {
        public readonly string VirtualPath;
        public readonly int SelectedMeshChunkId;
        public readonly bool ImportSkeleton;
        public readonly bool ImportAnimations;
        public readonly bool ImportPhysicsBoxColliders;
        public readonly bool ImportRagdollBodies;
        public readonly float ImportScale;
        public readonly CgfRigCachePolicy RigCachePolicy;
        public readonly bool UseRuntimeMemoryCache;
        public readonly bool PreferProjectCache;

        public CgfRuntimeImportRequest(
            string virtualPath,
            int selectedMeshChunkId = -1,
            bool importSkeleton = true,
            bool importAnimations = true,
            bool importPhysicsBoxColliders = false,
            bool importRagdollBodies = false,
            float importScale = 1f,
            CgfRigCachePolicy rigCachePolicy = default,
            bool useRuntimeMemoryCache = true,
            bool preferProjectCache = false)
        {
            VirtualPath = virtualPath;
            SelectedMeshChunkId = selectedMeshChunkId;
            ImportSkeleton = importSkeleton;
            ImportAnimations = importAnimations;
            ImportPhysicsBoxColliders = importPhysicsBoxColliders;
            ImportRagdollBodies = importRagdollBodies;
            ImportScale = importScale;
            RigCachePolicy = rigCachePolicy;
            UseRuntimeMemoryCache = useRuntimeMemoryCache;
            PreferProjectCache = preferProjectCache;
        }
    }
}
