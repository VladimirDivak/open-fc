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
        readonly CgfScopedTextureCache _emissionMasks = new CgfScopedTextureCache();

        // Diagnostic logging: when enabled, ResolveSubmeshMaterials and ResolveAndLoadTexture
        // dump per-submesh chunk resolution and per-texture candidate probing. DiagnosticAssetFilter
        // is an optional case-insensitive substring of the CGF source path that scopes the logs.
        public static bool DiagnosticLogging;
        public static string DiagnosticAssetFilter;

        static readonly int PropBaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int PropBumpMap = Shader.PropertyToID("_BumpMap");

        static bool DiagnosticEnabledFor(CgfFile parsedFile)
        {
            if (!DiagnosticLogging)
                return false;
            if (string.IsNullOrEmpty(DiagnosticAssetFilter))
                return true;
            string src = parsedFile?.SourceVirtualPath;
            return !string.IsNullOrEmpty(src) &&
                   src.IndexOf(DiagnosticAssetFilter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

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

            var leaves = CollectGlobalLeafMaterials(parsedFile);
            if (leaves.Count == 0)
                return BuildFallbackArray(subCount, textureScopeId);

            bool diag = DiagnosticEnabledFor(parsedFile);
            if (diag)
                LogResolutionContext(parsedFile, subCount, submeshMaterialIds, leaves);

            var mats = new Material[subCount];
            for (int i = 0; i < subCount; i++)
            {
                bool fromArray = submeshMaterialIds != null && i < submeshMaterialIds.Length;
                int matId = fromArray ? submeshMaterialIds[i] : i;
                var chunk = ResolveLeafByFaceMatId(leaves, matId);
                mats[i] = chunk != null
                    ? GetOrBuild(parsedFile, chunk, textureScopeId)
                    : GetSharedFallback(textureScopeId);

                if (diag)
                    Debug.Log(
                        $"[CgfMatDiag] {parsedFile?.SourceVirtualPath} submesh={i} " +
                        $"matId={matId}{(fromArray ? "" : "(fallback=i)")} -> " +
                        (chunk != null
                            ? $"chunkID={chunk.ChunkID} tableIndex={chunk.TableIndex} type={chunk.MtlType} name='{chunk.Name}'"
                            : "<no chunk: shared fallback>"));
            }
            return mats;
        }

        // A CGF face MatID is a GLOBAL leaf-material index: the position of the material
        // among all non-MULTI material chunks, in chunk-table order, uniform across every
        // node. Verified against merc_cover, fence_collision, gunboatdamaged, hut and
        // outdoor_simplefoldable. MTL_MULTI chunks are containers and are skipped.
        static List<CgfMaterialChunk> CollectGlobalLeafMaterials(CgfFile parsedFile)
        {
            var leaves = new List<CgfMaterialChunk>();
            var chunks = parsedFile?.MaterialChunks;
            if (chunks == null)
                return leaves;

            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                if (chunk != null && chunk.MtlType != CgfMtlType.Multi)
                    leaves.Add(chunk);
            }
            return leaves;
        }

        // Dumps the leaf-material table and node->material links so the diagnostic log shows
        // exactly what face MatID indexes into and which Multi root each node references.
        static void LogResolutionContext(
            CgfFile parsedFile,
            int subCount,
            int[] submeshMaterialIds,
            List<CgfMaterialChunk> leaves)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("[CgfMatDiag] ").Append(parsedFile?.SourceVirtualPath)
              .Append(" subCount=").Append(subCount)
              .Append(" submeshMaterialIds=")
              .Append(submeshMaterialIds == null ? "<null>" : "[" + string.Join(",", submeshMaterialIds) + "]");

            sb.Append("\n  leaves (global non-Multi, chunk-table order):");
            for (int i = 0; i < leaves.Count; i++)
            {
                var c = leaves[i];
                sb.Append("\n    [").Append(i).Append("] chunkID=").Append(c.ChunkID)
                  .Append(" tableIndex=").Append(c.TableIndex)
                  .Append(" type=").Append(c.MtlType)
                  .Append(" name='").Append(c.Name).Append('\'')
                  .Append(" diffuse='").Append(c.DiffuseTextureName).Append('\'')
                  .Append(" normal='").Append(c.NormalTextureName).Append('\'');
            }

            var nodes = parsedFile?.NodeChunks;
            if (nodes != null)
            {
                sb.Append("\n  nodes (name -> objectID/matChunkID):");
                for (int i = 0; i < nodes.Count; i++)
                {
                    var n = nodes[i];
                    sb.Append("\n    '").Append(n.Name).Append("' objectID=").Append(n.ObjectID)
                      .Append(" matChunkID=").Append(n.MatID);
                }
            }

            var hierarchy = parsedFile?.MaterialChildrenByParentChunkID;
            if (hierarchy != null && hierarchy.Count > 0)
            {
                sb.Append("\n  Multi roots -> children chunkIDs:");
                foreach (var kv in hierarchy)
                {
                    sb.Append("\n    root chunkID=").Append(kv.Key).Append(" children=[");
                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(kv.Value[i].ChunkID);
                    }
                    sb.Append(']');
                }
            }

            Debug.Log(sb.ToString());
        }

        // Diagnostic: walks the FINAL renderer state after the whole build/override/LOD
        // pipeline and prints, per submesh, the texture the resolved material chunk SHOULD
        // carry (chunk diffuse/normal name) vs the texture actually bound to _BaseMap /
        // _BumpMap. Catches desyncs that happen downstream of ResolveSubmeshMaterials.
        public static void LogFinalTextureBinding(
            CgfFile parsedFile,
            Renderer renderer,
            int[] submeshMaterialIds)
        {
            if (!DiagnosticEnabledFor(parsedFile) || renderer == null)
                return;

            var leaves = CollectGlobalLeafMaterials(parsedFile);
            var mats = renderer.sharedMaterials;
            int count = mats != null ? mats.Length : 0;

            var sb = new System.Text.StringBuilder();
            sb.Append("[CgfFinalBind] ").Append(parsedFile?.SourceVirtualPath)
              .Append(" renderer='").Append(renderer.name).Append("' submeshes=").Append(count)
              .Append(" submeshMaterialIds=")
              .Append(submeshMaterialIds == null ? "<null>" : "[" + string.Join(",", submeshMaterialIds) + "]");

            for (int i = 0; i < count; i++)
            {
                int matId = submeshMaterialIds != null && i < submeshMaterialIds.Length
                    ? submeshMaterialIds[i]
                    : i;
                var chunk = ResolveLeafByFaceMatId(leaves, matId);
                var mat = mats[i];

                string expDiffuse = chunk != null
                    ? CgfTexturePathResolver.NormalizeTextureName(chunk.DiffuseTextureName)
                    : "<no chunk>";
                string expNormal = chunk != null
                    ? CgfTexturePathResolver.NormalizeTextureName(chunk.NormalTextureName)
                    : "<no chunk>";

                var boundBase = mat != null && mat.HasProperty(PropBaseMap) ? mat.GetTexture(PropBaseMap) : null;
                var boundBump = mat != null && mat.HasProperty(PropBumpMap) ? mat.GetTexture(PropBumpMap) : null;
                string gotDiffuse = boundBase != null ? boundBase.name : "<null>";
                string gotNormal = boundBump != null ? boundBump.name : "<null>";

                sb.Append("\n  submesh=").Append(i).Append(" matId=").Append(matId)
                  .Append(" mat='").Append(mat != null ? mat.name : "<null mat>").Append('\'')
                  .Append("\n    diffuse expected->'").Append(expDiffuse)
                  .Append("' got->'").Append(gotDiffuse).Append('\'')
                  .Append(TextureNameMatches(expDiffuse, gotDiffuse) ? "" : "  <<< MISMATCH")
                  .Append("\n    normal  expected->'").Append(expNormal)
                  .Append("' got->'").Append(gotNormal).Append('\'')
                  .Append(TextureNameMatches(expNormal, gotNormal) ? "" : "  <<< MISMATCH");
            }

            Debug.Log(sb.ToString());
        }

        // Compares two texture identifiers by base file name (path + extension stripped,
        // lower-cased). Empty/sentinel values count as a match so they raise no false alarm.
        static bool TextureNameMatches(string expected, string got)
        {
            string e = TextureBaseName(expected);
            string g = TextureBaseName(got);
            if (e.Length == 0 || g.Length == 0)
                return true;
            return e == g;
        }

        static string TextureBaseName(string value)
        {
            if (string.IsNullOrEmpty(value) || value[0] == '<')
                return string.Empty;
            int slash = value.LastIndexOfAny(new[] { '/', '\\' });
            string name = slash >= 0 ? value.Substring(slash + 1) : value;
            int dot = name.LastIndexOf('.');
            if (dot > 0)
                name = name.Substring(0, dot);
            return name.ToLowerInvariant();
        }

        static CgfMaterialChunk ResolveLeafByFaceMatId(List<CgfMaterialChunk> leaves, int matId)
        {
            if (leaves == null || leaves.Count == 0)
                return null;

            // Some LOD meshes store -1 for every face MatID; Cry treats that as default.
            if (matId < 0)
                return leaves[0];

            return matId < leaves.Count ? leaves[matId] : null;
        }

        public void ClearCache()
        {
            _cache.Clear();
            _emissionMasks.Clear();
        }

        public void ReleaseLevelScope(string scopeId)
        {
            _cache.ReleaseLevelScope(scopeId);
            _emissionMasks.ReleaseLevelScope(scopeId);
        }

        public int TrimUnused() => _cache.TrimUnused() + _emissionMasks.TrimUnused();

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

                if (chunk.Classification.UsesUvScroll)
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
                var classification = chunk.Classification;
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

        // Resolves the leaf material chunks for each submesh, in the same global
        // leaf-index space ResolveSubmeshMaterials uses. Drives texture preload.
        static List<CgfMaterialChunk> CollectMaterialChunks(CgfFile parsedFile, int subCount, int[] submeshMaterialIds)
        {
            var result = new List<CgfMaterialChunk>();
            var leaves = CollectGlobalLeafMaterials(parsedFile);
            if (leaves.Count == 0 || subCount <= 0)
                return result;

            for (int i = 0; i < subCount; i++)
            {
                int matId = submeshMaterialIds != null && i < submeshMaterialIds.Length
                    ? submeshMaterialIds[i]
                    : i;
                var chunk = ResolveLeafByFaceMatId(leaves, matId);
                if (chunk != null)
                    result.Add(chunk);
            }

            return result;
        }

        Material GetOrBuild(CgfFile parsedFile, CgfMaterialChunk chunk, string textureScopeId)
        {
            // Key by chunk identity (source file + chunk id). A given (file, chunk) always
            // resolves to the same textures, so the cache lookup can run before any texture
            // is loaded. Texture resolution happens inside the factory, on a miss only.
            string key = BuildMaterialCacheKey(parsedFile, chunk);
            var lookup = ProjectMaterialLookup;
            return _cache.GetOrCreate(key, textureScopeId, () =>
            {
                var textures = ResolveTextures(parsedFile, chunk, textureScopeId);
                Material mat = null;
                if (lookup != null && !string.IsNullOrEmpty(parsedFile?.SourceVirtualPath))
                {
                    var projectMat = lookup(parsedFile.SourceVirtualPath, chunk.TableIndex);
                    if (projectMat != null)
                    {
                        mat = UnityEngine.Object.Instantiate(projectMat);
                        mat.name = projectMat.name;
                        CgfMaterialBuilder.ApplyResolvedTextures(mat, textures);
                    }
                }
                if (mat == null)
                    mat = CgfMaterialBuilder.Build(chunk, textures);

                ApplyScopedEmissionMask(mat, chunk, textures, textureScopeId);
                return mat;
            });
        }

        // Upgrades _EmissionMap to a level-scoped alpha-derived mask for glow materials.
        // The mask is destroyed when the level scope is released.
        void ApplyScopedEmissionMask(
            Material mat,
            CgfMaterialChunk chunk,
            CgfResolvedMaterialTextures textures,
            string textureScopeId)
        {
            if (mat == null || chunk == null || !chunk.Classification.UsesGlowFromDiffuseAlpha)
                return;

            var baseMap = textures.BaseMap;
            if (baseMap == null || !baseMap.isReadable)
                return;

            string baseKey = string.IsNullOrEmpty(textures.BaseMapVirtualPath)
                ? baseMap.name
                : textures.BaseMapVirtualPath;
            if (string.IsNullOrEmpty(baseKey))
                return;

            var mask = _emissionMasks.GetOrCreate(
                string.Concat("emission:", baseKey),
                textureScopeId,
                () => CgfMaterialBuilder.CreateEmissionMaskFromBaseAlpha(baseMap));

            if (mask != null)
                mat.SetTexture("_EmissionMap", mask);
        }

        static string BuildMaterialCacheKey(CgfFile parsedFile, CgfMaterialChunk chunk)
        {
            string source = parsedFile != null && !string.IsNullOrEmpty(parsedFile.SourceVirtualPath)
                ? parsedFile.SourceVirtualPath.ToLowerInvariant()
                : "__nofile";
            return string.Concat(source, "#", chunk.ChunkID.ToString());
        }

        CgfResolvedMaterialTextures ResolveTextures(CgfFile parsedFile, CgfMaterialChunk chunk, string textureScopeId)
        {
            var classification = chunk != null ? chunk.Classification : default;
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

            bool diag = DiagnosticEnabledFor(parsedFile);

            string firstSupportedCandidate = null;
            foreach (var candidate in CgfTexturePathResolver.BuildTexturePathCandidates(parsedFile, normalizedTextureName))
            {
                bool supported = TextureImportService.IsSupportedVirtualPath(candidate);
                if (!supported)
                {
                    if (diag)
                        Debug.Log($"[CgfTexDiag] {parsedFile?.SourceVirtualPath} name='{normalizedTextureName}' candidate='{candidate}' -> unsupported path");
                    continue;
                }

                if (firstSupportedCandidate == null)
                    firstSupportedCandidate = candidate;

                bool loaded = _textureRuntimeService.TryLoadWithInfo(
                        candidate,
                        out var loadedInfo,
                        textureScopeId,
                        new TextureRuntimeImportOptions(
                            useRuntimeMemoryCache: true,
                            markNonReadable: markNonReadable,
                            linearColorSpace: linearColorSpace,
                            generateMipmaps: true));

                if (diag)
                    Debug.Log(
                        $"[CgfTexDiag] {parsedFile?.SourceVirtualPath} name='{normalizedTextureName}' " +
                        $"candidate='{candidate}' -> loaded={loaded} texture={(loaded && loadedInfo.Texture != null ? "ok" : "<null>")}");

                if (loaded)
                {
                    return (
                        candidate,
                        loadedInfo.Texture,
                        loadedInfo.HasAlphaChannel,
                        loadedInfo.HasTransparentPixels);
                }
            }

            if (diag)
                Debug.LogWarning(
                    $"[CgfTexDiag] {parsedFile?.SourceVirtualPath} name='{normalizedTextureName}' " +
                    $"-> NO texture loaded (firstSupportedCandidate='{firstSupportedCandidate ?? "<none>"}')");

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
                        markNonReadable: !chunk.Classification.UsesGlowFromDiffuseAlpha,
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

        // One shared magenta material for the whole cache, level-scoped so it releases
        // with the level instead of leaking one instance per missing submesh.
        Material GetSharedFallback(string textureScopeId)
        {
            return _cache.GetOrCreate("__shared_fallback", textureScopeId,
                () => CgfMaterialBuilder.BuildFallback("shared"));
        }

        Material[] BuildFallbackArray(int count, string textureScopeId)
        {
            var mats = new Material[count];
            for (int i = 0; i < count; i++)
                mats[i] = GetSharedFallback(textureScopeId);
            return mats;
        }
    }
}
