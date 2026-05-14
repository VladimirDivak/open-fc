namespace OpenFarCry.Level.Services
{
    public static class FcVegetationDefaultPolicy
    {
        // Returns the default collision mode for a vegetation type based on its virtual path and
        // whether sibling LOD files were found on disk. Path keyword matching mirrors the Far Cry
        // vegetation naming conventions (tree/trunk/palm etc. = large blocking geometry).
        public static FcVegetationCollisionMode ResolveCollisionMode(string virtualPath, bool hasSiblingLods)
        {
            if (!hasSiblingLods || string.IsNullOrWhiteSpace(virtualPath))
                return FcVegetationCollisionMode.None;

            string path = virtualPath.Replace('\\', '/').ToLowerInvariant();
            if (path.Contains("tree") ||
                path.Contains("trunk") ||
                path.Contains("palm") ||
                path.Contains("cedar") ||
                path.Contains("pine") ||
                path.Contains("oak") ||
                path.Contains("cypress") ||
                path.Contains("stump") ||
                path.Contains("log"))
            {
                return FcVegetationCollisionMode.LowLodMesh;
            }

            return FcVegetationCollisionMode.None;
        }
    }
}
