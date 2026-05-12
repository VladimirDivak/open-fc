using System.Collections.Generic;
using OpenFarCry.Importer.Cgf;

namespace OpenFarCry.Level.Services
{
    public sealed class FcLevelGeometryAssetHandle
    {
        public readonly FcLevelGeometryRequest BaseRequest;
        public readonly CgfRuntimeImportResult BaseResult;
        public readonly IReadOnlyList<CgfRuntimeImportResult> LodResults;

        public FcLevelGeometryAssetHandle(
            FcLevelGeometryRequest baseRequest,
            CgfRuntimeImportResult baseResult,
            IReadOnlyList<CgfRuntimeImportResult> lodResults)
        {
            BaseRequest = baseRequest;
            BaseResult = baseResult;
            LodResults = lodResults ?? System.Array.Empty<CgfRuntimeImportResult>();
        }

        public bool IsValid => BaseRequest != null && BaseResult != null && BaseResult.Success;
    }
}
