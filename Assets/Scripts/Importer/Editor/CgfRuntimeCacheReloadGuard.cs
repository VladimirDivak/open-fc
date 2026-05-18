using OpenFarCry.Importer.Cgf;
using UnityEditor;

namespace OpenFarCry.Importer.Editor
{
    // Frees the runtime CGF cache's Allocator.Persistent native data before a domain
    // reload. The runtime caches are static; a script recompile resets those statics and
    // would orphan the parsed-geometry NativeArrays, which Unity's leak detector then
    // reports ("Persistent allocates N individual allocations").
    //
    // Only native parsed data is freed — built meshes are left intact because the live
    // scene still references them through MeshFilter/SkinnedMeshRenderer.
    [InitializeOnLoad]
    static class CgfRuntimeCacheReloadGuard
    {
        static CgfRuntimeCacheReloadGuard()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= FlushBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += FlushBeforeReload;
        }

        static void FlushBeforeReload()
        {
            CgfRuntimeImporter.DisposeParsedNativeData();
        }
    }
}
