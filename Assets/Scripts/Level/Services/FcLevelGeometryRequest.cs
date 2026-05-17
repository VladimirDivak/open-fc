using System;
using OpenFarCry.Importer;
using OpenFarCry.Importer.Cgf;

namespace OpenFarCry.Level.Services
{
    public enum FcLevelGeometrySourceKind
    {
        Brush = 0,
        BrushLod = 1,
        Vegetation = 2,
        VegetationLod = 3,
    }

    public sealed class FcLevelGeometryRequest : IEquatable<FcLevelGeometryRequest>
    {
        public readonly string VirtualPath;
        public readonly float ImportScale;
        public readonly bool ImportSkeleton;
        public readonly int SelectedMeshChunkId;
        public readonly FcLevelGeometrySourceKind SourceKind;
        public readonly string ModelCacheKey;

        public FcLevelGeometryRequest(
            string virtualPath,
            float importScale,
            bool importSkeleton,
            int selectedMeshChunkId,
            FcLevelGeometrySourceKind sourceKind)
        {
            if (string.IsNullOrWhiteSpace(virtualPath))
                throw new ArgumentException("Virtual path is null or empty.", nameof(virtualPath));

            VirtualPath = ImportAssetPaths.NormalizeVirtualPath(virtualPath);
            ImportScale = importScale;
            ImportSkeleton = importSkeleton;
            SelectedMeshChunkId = selectedMeshChunkId;
            SourceKind = sourceKind;
            ModelCacheKey = CgfCacheKeys.BuildModelCacheKey(
                VirtualPath,
                SelectedMeshChunkId,
                ImportSkeleton,
                ImportScale);
        }

        public CgfRuntimeImportRequest ToRuntimeImportRequest(bool useRuntimeMemoryCache = true)
        {
            return new CgfRuntimeImportRequest(
                virtualPath: VirtualPath,
                selectedMeshChunkId: SelectedMeshChunkId,
                importSkeleton: ImportSkeleton,
                importAnimations: false,
                importScale: ImportScale,
                useRuntimeMemoryCache: useRuntimeMemoryCache);
        }

        public bool Equals(FcLevelGeometryRequest other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other == null)
                return false;
            return string.Equals(ModelCacheKey, other.ModelCacheKey, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as FcLevelGeometryRequest);
        }

        public override int GetHashCode()
        {
            return ModelCacheKey != null ? StringComparer.Ordinal.GetHashCode(ModelCacheKey) : 0;
        }
    }
}
