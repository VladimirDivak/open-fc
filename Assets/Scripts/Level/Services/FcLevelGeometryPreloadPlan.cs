using System;
using System.Collections.Generic;
using OpenFarCry.Importer;
using OpenFarCry.Importer.Cgf;

namespace OpenFarCry.Level.Services
{
    public sealed class FcLevelGeometryPreloadPlan
    {
        readonly List<FcLevelGeometryRequest> _uniqueBaseRequests = new List<FcLevelGeometryRequest>();
        readonly List<FcLevelGeometryRequest> _uniqueLodRequests = new List<FcLevelGeometryRequest>();
        readonly List<FcLevelGeometryRequest> _uniqueAllRequests = new List<FcLevelGeometryRequest>();
        readonly Dictionary<string, FcLevelGeometryRequest> _requestByModelKey =
            new Dictionary<string, FcLevelGeometryRequest>(StringComparer.Ordinal);
        readonly Dictionary<string, FcLevelGeometryRequest> _baseRequestByVirtualPath =
            new Dictionary<string, FcLevelGeometryRequest>(StringComparer.Ordinal);
        readonly Dictionary<string, List<string>> _lodModelKeysByBaseModelKey =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public IReadOnlyList<FcLevelGeometryRequest> UniqueBaseRequests => _uniqueBaseRequests;
        public IReadOnlyList<FcLevelGeometryRequest> UniqueLodRequests => _uniqueLodRequests;
        public IReadOnlyList<FcLevelGeometryRequest> UniqueAllRequests => _uniqueAllRequests;

        public int UniqueBaseCount => _uniqueBaseRequests.Count;
        public int UniqueLodCount => _uniqueLodRequests.Count;
        public int UniqueRequestCount => _uniqueAllRequests.Count;

        public void AddBaseRequest(FcLevelGeometryRequest request)
        {
            if (request == null)
                return;

            if (!_requestByModelKey.ContainsKey(request.ModelCacheKey))
            {
                _requestByModelKey[request.ModelCacheKey] = request;
                _uniqueAllRequests.Add(request);
            }

            if (!_baseRequestByVirtualPath.ContainsKey(request.VirtualPath))
                _baseRequestByVirtualPath[request.VirtualPath] = request;

            if (!ContainsModelKey(_uniqueBaseRequests, request.ModelCacheKey))
                _uniqueBaseRequests.Add(request);
        }

        public void AddLodRequest(FcLevelGeometryRequest baseRequest, FcLevelGeometryRequest lodRequest)
        {
            if (baseRequest == null || lodRequest == null)
                return;

            if (!_requestByModelKey.ContainsKey(lodRequest.ModelCacheKey))
            {
                _requestByModelKey[lodRequest.ModelCacheKey] = lodRequest;
                _uniqueAllRequests.Add(lodRequest);
            }

            if (!ContainsModelKey(_uniqueLodRequests, lodRequest.ModelCacheKey))
                _uniqueLodRequests.Add(lodRequest);

            if (!_lodModelKeysByBaseModelKey.TryGetValue(baseRequest.ModelCacheKey, out var lodKeys))
            {
                lodKeys = new List<string>();
                _lodModelKeysByBaseModelKey[baseRequest.ModelCacheKey] = lodKeys;
            }

            if (!lodKeys.Contains(lodRequest.ModelCacheKey))
                lodKeys.Add(lodRequest.ModelCacheKey);
        }

        public bool TryGetBaseRequestByVirtualPath(string virtualPath, out FcLevelGeometryRequest request)
        {
            request = null;
            if (string.IsNullOrWhiteSpace(virtualPath))
                return false;

            string normalized = ImportAssetPaths.NormalizeVirtualPath(virtualPath);
            return _baseRequestByVirtualPath.TryGetValue(normalized, out request);
        }

        public bool TryGetLodModelKeys(string baseModelKey, out IReadOnlyList<string> lodModelKeys)
        {
            lodModelKeys = null;
            if (string.IsNullOrWhiteSpace(baseModelKey))
                return false;
            if (!_lodModelKeysByBaseModelKey.TryGetValue(baseModelKey, out var list) || list.Count == 0)
                return false;

            lodModelKeys = list;
            return true;
        }

        public Dictionary<string, FcLevelGeometryAssetHandle> BuildVegetationHandles(
            IReadOnlyDictionary<string, CgfRuntimeImportResult> preloadedByModelKey)
        {
            return BuildHandlesForSourceKind(FcLevelGeometrySourceKind.Vegetation, preloadedByModelKey);
        }

        public Dictionary<string, FcLevelGeometryAssetHandle> BuildBrushHandles(
            IReadOnlyDictionary<string, CgfRuntimeImportResult> preloadedByModelKey)
        {
            return BuildHandlesForSourceKind(FcLevelGeometrySourceKind.Brush, preloadedByModelKey);
        }

        Dictionary<string, FcLevelGeometryAssetHandle> BuildHandlesForSourceKind(
            FcLevelGeometrySourceKind sourceKind,
            IReadOnlyDictionary<string, CgfRuntimeImportResult> preloadedByModelKey)
        {
            var handles = new Dictionary<string, FcLevelGeometryAssetHandle>(StringComparer.Ordinal);
            if (preloadedByModelKey == null || preloadedByModelKey.Count == 0)
                return handles;

            for (int i = 0; i < _uniqueBaseRequests.Count; i++)
            {
                var baseReq = _uniqueBaseRequests[i];
                if (baseReq == null || baseReq.SourceKind != sourceKind)
                    continue;
                if (!preloadedByModelKey.TryGetValue(baseReq.ModelCacheKey, out var baseResult) ||
                    baseResult == null ||
                    !baseResult.Success)
                    continue;

                var lodResults = new List<CgfRuntimeImportResult>();
                if (_lodModelKeysByBaseModelKey.TryGetValue(baseReq.ModelCacheKey, out var lodKeys))
                {
                    for (int l = 0; l < lodKeys.Count; l++)
                    {
                        string lodKey = lodKeys[l];
                        if (!preloadedByModelKey.TryGetValue(lodKey, out var lodResult) ||
                            lodResult == null ||
                            !lodResult.Success)
                            continue;
                        lodResults.Add(lodResult);
                    }
                }

                handles[baseReq.VirtualPath] = new FcLevelGeometryAssetHandle(
                    baseRequest: baseReq,
                    baseResult: baseResult,
                    lodResults: lodResults);
            }

            return handles;
        }

        static bool ContainsModelKey(IReadOnlyList<FcLevelGeometryRequest> requests, string modelKey)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                if (string.Equals(requests[i].ModelCacheKey, modelKey, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }
    }
}
