using System.Collections.Generic;

namespace OpenFarCry.Importer
{
    public interface IResourceImportService
    {
        string ResourceKind { get; }
        IReadOnlyList<string> SupportedExtensions { get; }

        bool IsSupportedVirtualPath(string virtualPath);
        IReadOnlyList<ResourceProjectAssetPath> GetProjectAssetPaths(string virtualPath);

        byte[] LoadRuntimeResourceBytes(string virtualPath);
        byte[] LoadProjectAssetSourceBytes(string virtualPath);
    }
}
