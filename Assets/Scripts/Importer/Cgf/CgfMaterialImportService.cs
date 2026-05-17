using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer.Texture;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public readonly struct CgfResolvedMaterialTextures
    {
        public readonly string DiffuseTextureName;
        public readonly string NormalTextureName;
        public readonly string SpecularTextureName;
        public readonly string OpacityTextureName;
        public readonly string GlossTextureName;
        public readonly string BaseMapVirtualPath;
        public readonly string NormalMapVirtualPath;
        public readonly string SpecularMapVirtualPath;
        public readonly string OpacityMapVirtualPath;
        public readonly string GlossMapVirtualPath;
        public readonly Texture2D BaseMap;
        public readonly Texture2D NormalMap;
        public readonly Texture2D SpecularMap;
        public readonly Texture2D OpacityMap;
        public readonly Texture2D GlossMap;

        public CgfResolvedMaterialTextures(
            string diffuseTextureName,
            string normalTextureName,
            string specularTextureName,
            string opacityTextureName,
            string glossTextureName,
            string baseMapVirtualPath,
            string normalMapVirtualPath,
            string specularMapVirtualPath,
            string opacityMapVirtualPath,
            string glossMapVirtualPath,
            Texture2D baseMap,
            Texture2D normalMap,
            Texture2D specularMap,
            Texture2D opacityMap,
            Texture2D glossMap)
        {
            DiffuseTextureName = diffuseTextureName;
            NormalTextureName = normalTextureName;
            SpecularTextureName = specularTextureName;
            OpacityTextureName = opacityTextureName;
            GlossTextureName = glossTextureName;
            BaseMapVirtualPath = baseMapVirtualPath;
            NormalMapVirtualPath = normalMapVirtualPath;
            SpecularMapVirtualPath = specularMapVirtualPath;
            OpacityMapVirtualPath = opacityMapVirtualPath;
            GlossMapVirtualPath = glossMapVirtualPath;
            BaseMap = baseMap;
            NormalMap = normalMap;
            SpecularMap = specularMap;
            OpacityMap = opacityMap;
            GlossMap = glossMap;
        }
    }

    // Resolves per-submesh Material[] for an imported CGF mesh.
    // Uses CgfMaterialRuntimeCache to reuse built materials per source file/chunk.
    //
    // Mapping logic:
    //   Node.MatID → ChunkID of root material chunk.
    //   MTL_MULTI root: submesh MatID (from faces) -> sub-material index.
    //   MTL_STANDARD / MTL_2SIDED root: same material for all submeshes.
    //   No material chunk found: fallback (magenta) for all slots.
    public sealed class CgfMaterialImportService
    {
        readonly CgfMaterialRuntimeCache _cache;
        readonly TextureRuntimeImportService _textureRuntimeService;

        // Optional project-asset lookup: (cgfVirtualPath, chunkTableIndex) → persistent Material.
        // Set by editor init to return pre-baked .mat assets; null in runtime builds.
        // When set, GetOrBuild instantiates the project asset and injects textures instead of
        // building a material from scratch.
        public Func<string, int, Material> ProjectMaterialLookup { get; set; }

        public CgfMaterialImportService(
            CgfMaterialRuntimeCache cache = null,
            TextureRuntimeImportService textureRuntimeService = null)
        {
            _cache = cache ?? new CgfMaterialRuntimeCache();
            _textureRuntimeService = textureRuntimeService ?? TextureImportService.RuntimeService;
        }

        public Material[] ResolveSubmeshMaterials(
            CgfFile parsedFile,
            Mesh mesh,
            int[] submeshMaterialIds = null,
            string textureScopeId = null)
        {
            int subCount = mesh != null ? mesh.subMeshCount : 0;
            if (subCount == 0 || parsedFile == null)
                return new Material[subCount];

            // Find primary node (the one that owns SelectedMeshChunkID).
            CgfNodeChunk primaryNode = null;
            if (parsedFile.NodeChunks != null)
                foreach (var node in parsedFile.NodeChunks)
                    if (node.ObjectID == parsedFile.SelectedMeshChunkID)
                    { primaryNode = node; break; }

            int matChunkId = primaryNode?.MatID ?? -1;

            if (matChunkId < 0 ||
                !parsedFile.MaterialByChunkID.TryGetValue(matChunkId, out var rootMat))
                return BuildFallbackArray(subCount, "(no material chunk)");

            if (rootMat.MtlType == CgfMtlType.Multi)
            {
                if (!parsedFile.MaterialChildrenByParentChunkID.TryGetValue(rootMat.ChunkID, out var children))
                    children = null;

                var mats = new Material[subCount];
                for (int i = 0; i < subCount; i++)
                {
                    int matId = submeshMaterialIds != null && i < submeshMaterialIds.Length
                        ? submeshMaterialIds[i]
                        : i;
                    var chunk = ResolveMultiMaterialChild(parsedFile, rootMat, children, matId);
                    mats[i] = chunk != null
                        ? GetOrBuild(parsedFile, chunk, textureScopeId)
                        : CgfMaterialBuilder.BuildFallback($"missing_matid_{matId}");
                }
                return mats;
            }
            else
            {
                if (TryResolveMaterialTableIndexSlots(parsedFile, subCount, submeshMaterialIds, textureScopeId, out var tableIndexMats))
                    return tableIndexMats;

                var single = GetOrBuild(parsedFile, rootMat, textureScopeId);
                var mats = new Material[subCount];
                for (int i = 0; i < subCount; i++)
                    mats[i] = single;
                return mats;
            }
        }

        public void ClearCache() => _cache.Clear();
        public int CachedCount => _cache.Count;

        public bool RequiresUvScroll(
            CgfFile parsedFile,
            Mesh mesh,
            int[] submeshMaterialIds = null)
        {
            int subCount = mesh != null ? mesh.subMeshCount : 0;
            if (parsedFile == null || subCount <= 0)
                return false;

            var chunks = CollectMaterialChunks(parsedFile, subCount, submeshMaterialIds);
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                if (chunk == null)
                    continue;

                if (CgfMaterialClassifier.Analyze(chunk).UsesUvScroll)
                    return true;
            }

            return false;
        }

        public async UniTask PreloadTexturesAsync(
            CgfFile parsedFile,
            Mesh mesh,
            int[] submeshMaterialIds = null,
            string textureScopeId = null,
            CancellationToken cancellationToken = default)
        {
            int subCount = submeshMaterialIds?.Length ?? 0;
            if (parsedFile == null || mesh == null || subCount <= 0)
                return;

            var chunks = CollectMaterialChunks(parsedFile, subCount, submeshMaterialIds);
            if (chunks.Count == 0)
                return;

            cancellationToken.ThrowIfCancellationRequested();

            var tasks = new System.Collections.Generic.List<UniTask>(chunks.Count * 5);
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                if (chunk == null)
                    continue;
                var classification = CgfMaterialClassifier.Analyze(chunk);
                bool keepBaseReadable = classification.UsesGlowFromDiffuseAlpha;

                tasks.Add(PreloadTextureNameAsync(parsedFile,
                    CgfTexturePathResolver.NormalizeTextureName(chunk.DiffuseTextureName),
                    textureScopeId, linearColorSpace: false, markNonReadable: !keepBaseReadable, cancellationToken));
                tasks.Add(PreloadTextureNameAsync(parsedFile,
                    CgfTexturePathResolver.NormalizeTextureName(chunk.NormalTextureName),
                    textureScopeId, linearColorSpace: true, markNonReadable: true, cancellationToken));
                tasks.Add(PreloadTextureNameAsync(parsedFile,
                    CgfTexturePathResolver.NormalizeTextureName(chunk.SpecularTextureName),
                    textureScopeId, linearColorSpace: true, markNonReadable: true, cancellationToken));
                tasks.Add(PreloadTextureNameAsync(parsedFile,
                    CgfTexturePathResolver.NormalizeTextureName(chunk.OpacityTextureName),
                    textureScopeId, linearColorSpace: true, markNonReadable: true, cancellationToken));
                tasks.Add(PreloadTextureNameAsync(parsedFile,
                    CgfTexturePathResolver.NormalizeTextureName(chunk.GlossTextureName),
                    textureScopeId, linearColorSpace: true, markNonReadable: true, cancellationToken));
            }

            if (tasks.Count > 0)
                await UniTask.WhenAll(tasks);
        }

        public async UniTask PreloadTexturesForResultsAsync(
            IReadOnlyList<CgfRuntimeImportResult> results,
            string textureScopeId = null,
            CancellationToken cancellationToken = default)
        {
            var preloadRequests = CollectUniqueTexturePreloadRequests(results);
            if (preloadRequests.Count == 0)
                return;

            var tasks = new List<UniTask>(preloadRequests.Count);
            for (int i = 0; i < preloadRequests.Count; i++)
            {
                var request = preloadRequests[i];
                tasks.Add(PreloadTexturePathAsync(
                    request.VirtualPath,
                    textureScopeId,
                    request.LinearColorSpace,
                    request.MarkNonReadable,
                    cancellationToken));
            }

            await UniTask.WhenAll(tasks);
        }

        public int CountUniqueTexturePreloadRequests(IReadOnlyList<CgfRuntimeImportResult> results)
        {
            return CollectUniqueTexturePreloadRequests(results).Count;
        }

        static CgfMaterialChunk ResolveMultiMaterialChild(
            CgfFile parsedFile,
            CgfMaterialChunk rootMat,
            List<CgfMaterialChunk> children,
            int matId)
        {
            // Some LOD meshes store -1 in every face MatID even though the file still
            // has a valid MTL_MULTI table. Cry treats this as the default material.
            if (matId < 0)
            {
                if (children != null && children.Count > 0)
                    return children[0];

                matId = 0;
            }

            if (children != null && matId >= 0 && matId < children.Count)
                return children[matId];

            // Fallback for malformed/partial material tables: walk forward from root.
            int relLeafIndex = 0;
            int start = Math.Max(0, rootMat.TableIndex + 1);
            for (int i = start; i < parsedFile.MaterialChunks.Count; i++)
            {
                var chunk = parsedFile.MaterialChunks[i];
                if (chunk == null || chunk.MtlType == CgfMtlType.Multi)
                    continue;
                if (relLeafIndex == matId)
                    return chunk;
                relLeafIndex++;
            }

            return null;
        }

        List<CgfMaterialChunk> CollectMaterialChunks(CgfFile parsedFile, int subCount, int[] submeshMaterialIds)
        {
            var result = new List<CgfMaterialChunk>();

            CgfNodeChunk primaryNode = null;
            if (parsedFile.NodeChunks != null)
            {
                for (int i = 0; i < parsedFile.NodeChunks.Count; i++)
                {
                    var node = parsedFile.NodeChunks[i];
                    if (node.ObjectID == parsedFile.SelectedMeshChunkID)
                    {
                        primaryNode = node;
                        break;
                    }
                }
            }

            int matChunkId = primaryNode?.MatID ?? -1;
            if (matChunkId < 0 || !parsedFile.MaterialByChunkID.TryGetValue(matChunkId, out var rootMat))
                return result;

            if (rootMat.MtlType != CgfMtlType.Multi)
            {
                if (TryCollectMaterialTableIndexSlots(parsedFile, subCount, submeshMaterialIds, result))
                    return result;

                result.Add(rootMat);
                return result;
            }

            parsedFile.MaterialChildrenByParentChunkID.TryGetValue(rootMat.ChunkID, out var children);
            for (int i = 0; i < subCount; i++)
            {
                int matId = submeshMaterialIds != null && i < submeshMaterialIds.Length
                    ? submeshMaterialIds[i]
                    : i;
                var chunk = ResolveMultiMaterialChild(parsedFile, rootMat, children, matId);
                if (chunk != null)
                    result.Add(chunk);
            }

            return result;
        }

        bool TryResolveMaterialTableIndexSlots(
            CgfFile parsedFile,
            int subCount,
            int[] submeshMaterialIds,
            string textureScopeId,
            out Material[] materials)
        {
            // Some static CGFs store several MTL_STANDARD chunks instead of one MTL_MULTI.
            // In that layout face MatID is the material table index, not an index under
            // the primary node material chunk.
            materials = null;
            if (parsedFile?.MaterialChunks == null || parsedFile.MaterialChunks.Count == 0 || subCount <= 0)
                return false;

            var resolved = new Material[subCount];
            for (int i = 0; i < subCount; i++)
            {
                int tableIndex = submeshMaterialIds != null && i < submeshMaterialIds.Length
                    ? submeshMaterialIds[i]
                    : i;

                if (!TryResolveMaterialByTableIndex(parsedFile, tableIndex, out var chunk))
                    return false;

                resolved[i] = GetOrBuild(parsedFile, chunk, textureScopeId);
            }

            materials = resolved;
            return true;
        }

        bool TryCollectMaterialTableIndexSlots(
            CgfFile parsedFile,
            int subCount,
            int[] submeshMaterialIds,
            List<CgfMaterialChunk> result)
        {
            if (parsedFile?.MaterialChunks == null || parsedFile.MaterialChunks.Count == 0 || subCount <= 0)
                return false;

            var resolved = new List<CgfMaterialChunk>(subCount);
            for (int i = 0; i < subCount; i++)
            {
                int tableIndex = submeshMaterialIds != null && i < submeshMaterialIds.Length
                    ? submeshMaterialIds[i]
                    : i;

                if (!TryResolveMaterialByTableIndex(parsedFile, tableIndex, out var chunk))
                    return false;

                resolved.Add(chunk);
            }

            result.AddRange(resolved);
            return true;
        }

        static bool TryResolveMaterialByTableIndex(CgfFile parsedFile, int tableIndex, out CgfMaterialChunk chunk)
        {
            chunk = null;
            if (parsedFile?.MaterialChunks == null ||
                tableIndex < 0 ||
                tableIndex >= parsedFile.MaterialChunks.Count)
                return false;

            chunk = parsedFile.MaterialChunks[tableIndex];
            return chunk != null && chunk.MtlType != CgfMtlType.Multi;
        }

        Material GetOrBuild(CgfFile parsedFile, CgfMaterialChunk chunk, string textureScopeId)
        {
            var textures = ResolveTextures(parsedFile, chunk, textureScopeId);
            string name = string.IsNullOrEmpty(chunk.Name) ? "__unnamed" : chunk.Name.ToLowerInvariant();
            string shader = string.IsNullOrEmpty(chunk.ShaderName) ? "-" : chunk.ShaderName.ToLowerInvariant();
            string diffuseKey = string.IsNullOrEmpty(textures.BaseMapVirtualPath)
                ? (string.IsNullOrEmpty(textures.DiffuseTextureName) ? "-" : textures.DiffuseTextureName)
                : textures.BaseMapVirtualPath;
            string normalKey = string.IsNullOrEmpty(textures.NormalMapVirtualPath)
                ? (string.IsNullOrEmpty(textures.NormalTextureName) ? "-" : textures.NormalTextureName)
                : textures.NormalMapVirtualPath;
            string specularKey = string.IsNullOrEmpty(textures.SpecularMapVirtualPath)
                ? (string.IsNullOrEmpty(textures.SpecularTextureName) ? "-" : textures.SpecularTextureName)
                : textures.SpecularMapVirtualPath;
            string opacityKey = string.IsNullOrEmpty(textures.OpacityMapVirtualPath)
                ? (string.IsNullOrEmpty(textures.OpacityTextureName) ? "-" : textures.OpacityTextureName)
                : textures.OpacityMapVirtualPath;
            string glossKey = string.IsNullOrEmpty(textures.GlossMapVirtualPath)
                ? (string.IsNullOrEmpty(textures.GlossTextureName) ? "-" : textures.GlossTextureName)
                : textures.GlossMapVirtualPath;
            var dc = chunk.DiffuseColor;
            string colorKey = $"{dc.r:X2}{dc.g:X2}{dc.b:X2}";
            string key = $"name:{name}|sh:{shader}|type:{(int)chunk.MtlType}|flags:{(int)chunk.Flags}|alpha:{chunk.AlphaTest:F3}|color:{colorKey}|d:{diffuseKey}|n:{normalKey}|s:{specularKey}|o:{opacityKey}|g:{glossKey}";
            var lookup = ProjectMaterialLookup;
            var material = _cache.GetOrCreate(key, textureScopeId, () =>
            {
                if (lookup != null && !string.IsNullOrEmpty(parsedFile?.SourceVirtualPath))
                {
                    var projectMat = lookup(parsedFile.SourceVirtualPath, chunk.TableIndex);
                    if (projectMat != null)
                    {
                        var instance = UnityEngine.Object.Instantiate(projectMat);
                        instance.name = projectMat.name;
                        return instance;
                    }
                }
                return CgfMaterialBuilder.Build(chunk, textures);
            });
            CgfMaterialBuilder.ApplyResolvedTextures(material, textures);
            return material;
        }

        CgfResolvedMaterialTextures ResolveTextures(CgfFile parsedFile, CgfMaterialChunk chunk, string textureScopeId)
        {
            var classification = CgfMaterialClassifier.Analyze(chunk);
            bool keepBaseReadable = classification.UsesGlowFromDiffuseAlpha;

            string diffuseName = CgfTexturePathResolver.NormalizeTextureName(chunk?.DiffuseTextureName);
            string normalName = CgfTexturePathResolver.NormalizeTextureName(chunk?.NormalTextureName);
            string specularName = CgfTexturePathResolver.NormalizeTextureName(chunk?.SpecularTextureName);
            string opacityName = CgfTexturePathResolver.NormalizeTextureName(chunk?.OpacityTextureName);
            string glossName = CgfTexturePathResolver.NormalizeTextureName(chunk?.GlossTextureName);

            var baseMap = ResolveAndLoadTexture(
                parsedFile,
                diffuseName,
                textureScopeId,
                linearColorSpace: false,
                markNonReadable: !keepBaseReadable);
            var normalMap = ResolveAndLoadTexture(
                parsedFile,
                normalName,
                textureScopeId,
                linearColorSpace: true,
                markNonReadable: true);
            var specularMap = ResolveAndLoadTexture(
                parsedFile,
                specularName,
                textureScopeId,
                linearColorSpace: true,
                markNonReadable: true);
            var opacityMap = ResolveAndLoadTexture(
                parsedFile,
                opacityName,
                textureScopeId,
                linearColorSpace: true,
                markNonReadable: true);
            var glossMap = ResolveAndLoadTexture(
                parsedFile,
                glossName,
                textureScopeId,
                linearColorSpace: true,
                markNonReadable: true);

            return new CgfResolvedMaterialTextures(
                diffuseTextureName: diffuseName,
                normalTextureName: normalName,
                specularTextureName: specularName,
                opacityTextureName: opacityName,
                glossTextureName: glossName,
                baseMapVirtualPath: baseMap.VirtualPath,
                normalMapVirtualPath: normalMap.VirtualPath,
                specularMapVirtualPath: specularMap.VirtualPath,
                opacityMapVirtualPath: opacityMap.VirtualPath,
                glossMapVirtualPath: glossMap.VirtualPath,
                baseMap: baseMap.Texture,
                normalMap: normalMap.Texture,
                specularMap: specularMap.Texture,
                opacityMap: opacityMap.Texture,
                glossMap: glossMap.Texture);
        }

        (string VirtualPath, Texture2D Texture, bool HasAlphaChannel, bool HasTransparentPixels) ResolveAndLoadTexture(
            CgfFile parsedFile,
            string normalizedTextureName,
            string textureScopeId,
            bool linearColorSpace,
            bool markNonReadable)
        {
            if (string.IsNullOrEmpty(normalizedTextureName))
                return (null, null, false, false);

            string firstSupportedCandidate = null;
            foreach (var candidate in CgfTexturePathResolver.BuildTexturePathCandidates(parsedFile, normalizedTextureName))
            {
                if (!TextureImportService.IsSupportedVirtualPath(candidate))
                    continue;

                if (firstSupportedCandidate == null)
                    firstSupportedCandidate = candidate;

                if (_textureRuntimeService.TryLoadWithInfo(
                        candidate,
                        out var loadedInfo,
                        textureScopeId,
                        new TextureRuntimeImportOptions(
                            useRuntimeMemoryCache: true,
                            markNonReadable: markNonReadable,
                            linearColorSpace: linearColorSpace,
                            generateMipmaps: true)))
                {
                    return (
                        candidate,
                        loadedInfo.Texture,
                        loadedInfo.HasAlphaChannel,
                        loadedInfo.HasTransparentPixels);
                }
            }

            if (firstSupportedCandidate != null)
                return (firstSupportedCandidate, null, false, false);

            return (normalizedTextureName, null, false, false);
        }

        async UniTask PreloadTextureNameAsync(
            CgfFile parsedFile,
            string normalizedTextureName,
            string textureScopeId,
            bool linearColorSpace,
            bool markNonReadable,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(normalizedTextureName))
                return;

            foreach (var candidate in CgfTexturePathResolver.BuildTexturePathCandidates(parsedFile, normalizedTextureName))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TextureImportService.IsSupportedVirtualPath(candidate))
                    continue;

                var load = await _textureRuntimeService.TryLoadWithInfoAsync(
                    candidate,
                    textureScopeId,
                    cancellationToken,
                    new TextureRuntimeImportOptions(
                        useRuntimeMemoryCache: true,
                        markNonReadable: markNonReadable,
                        linearColorSpace: linearColorSpace,
                        generateMipmaps: true));

                if (load.Success)
                    return;
            }
        }

        async UniTask PreloadTexturePathAsync(
            string virtualPath,
            string textureScopeId,
            bool linearColorSpace,
            bool markNonReadable,
            CancellationToken cancellationToken)
        {
            await _textureRuntimeService.TryLoadWithInfoAsync(
                virtualPath,
                textureScopeId,
                cancellationToken,
                new TextureRuntimeImportOptions(
                    useRuntimeMemoryCache: true,
                    markNonReadable: markNonReadable,
                    linearColorSpace: linearColorSpace,
                    generateMipmaps: true));
        }

        internal List<TexturePreloadRequest> CollectUniqueTexturePreloadRequests(
            IReadOnlyList<CgfRuntimeImportResult> results)
        {
            var requests = new List<TexturePreloadRequest>();
            if (results == null || results.Count == 0)
                return requests;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                if (result == null || !result.Success || result.ParsedFile == null)
                    continue;

                int subCount = result.Mesh != null ? result.Mesh.subMeshCount : 0;
                if (subCount <= 0)
                    continue;

                var chunks = CollectMaterialChunks(
                    result.ParsedFile,
                    subCount,
                    result.BuildResult?.SubmeshMaterialIds);

                for (int c = 0; c < chunks.Count; c++)
                {
                    var chunk = chunks[c];
                    if (chunk == null)
                        continue;

                    TryCollectTextureCandidate(
                        result.ParsedFile,
                        CgfTexturePathResolver.NormalizeTextureName(chunk.DiffuseTextureName),
                        linearColorSpace: false,
                        markNonReadable: !CgfMaterialClassifier.Analyze(chunk).UsesGlowFromDiffuseAlpha,
                        seen,
                        requests);
                    TryCollectTextureCandidate(
                        result.ParsedFile,
                        CgfTexturePathResolver.NormalizeTextureName(chunk.NormalTextureName),
                        linearColorSpace: true,
                        markNonReadable: true,
                        seen,
                        requests);
                    TryCollectTextureCandidate(
                        result.ParsedFile,
                        CgfTexturePathResolver.NormalizeTextureName(chunk.SpecularTextureName),
                        linearColorSpace: true,
                        markNonReadable: true,
                        seen,
                        requests);
                    TryCollectTextureCandidate(
                        result.ParsedFile,
                        CgfTexturePathResolver.NormalizeTextureName(chunk.OpacityTextureName),
                        linearColorSpace: true,
                        markNonReadable: true,
                        seen,
                        requests);
                    TryCollectTextureCandidate(
                        result.ParsedFile,
                        CgfTexturePathResolver.NormalizeTextureName(chunk.GlossTextureName),
                        linearColorSpace: true,
                        markNonReadable: true,
                        seen,
                        requests);
                }
            }

            return requests;
        }

        static void TryCollectTextureCandidate(
            CgfFile parsedFile,
            string normalizedTextureName,
            bool linearColorSpace,
            bool markNonReadable,
            HashSet<string> seen,
            List<TexturePreloadRequest> requests)
        {
            if (parsedFile == null || string.IsNullOrEmpty(normalizedTextureName))
                return;

            foreach (var candidate in CgfTexturePathResolver.BuildTexturePathCandidates(parsedFile, normalizedTextureName))
            {
                if (!TextureImportService.IsSupportedVirtualPath(candidate))
                    continue;

                string normalizedPath = ImportAssetPaths.NormalizeVirtualPath(candidate);
                string key = $"{normalizedPath}|lin:{(linearColorSpace ? 1 : 0)}|nr:{(markNonReadable ? 1 : 0)}";
                if (seen.Add(key))
                    requests.Add(new TexturePreloadRequest(normalizedPath, linearColorSpace, markNonReadable));

                // Keep the first supported candidate semantics from PreloadTextureNameAsync.
                return;
            }
        }

        internal readonly struct TexturePreloadRequest
        {
            public readonly string VirtualPath;
            public readonly bool LinearColorSpace;
            public readonly bool MarkNonReadable;

            public TexturePreloadRequest(string virtualPath, bool linearColorSpace, bool markNonReadable)
            {
                VirtualPath = virtualPath;
                LinearColorSpace = linearColorSpace;
                MarkNonReadable = markNonReadable;
            }
        }

        static Material[] BuildFallbackArray(int count, string reason)
        {
            var mats = new Material[count];
            for (int i = 0; i < count; i++)
                mats[i] = CgfMaterialBuilder.BuildFallback(reason);
            return mats;
        }
    }
}
