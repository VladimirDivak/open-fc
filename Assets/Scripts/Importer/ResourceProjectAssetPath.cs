namespace OpenFarCry.Importer
{
    public readonly struct ResourceProjectAssetPath
    {
        public readonly string Role;
        public readonly string Path;

        public ResourceProjectAssetPath(string role, string path)
        {
            Role = role;
            Path = path;
        }
    }
}
