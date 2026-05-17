using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Importer.Texture;
using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    // Runtime resolver for level-specific material overrides from materials.xml / brush.lst.
    // Resolves level material metadata + texture slots and builds URP materials for scene instances.
    [DefaultExecutionOrder(-98)]
    public sealed class FcLevelMaterialOverrideService : MonoBehaviour
    {
        public struct BrushMaterialMetadata
        {
            public bool HasMaterial;
            public bool HasSurfaceType;
            public string ResolutionSource;
            public string RequestedOverrideName;
            public int RequestedMaterialId;
            public string MaterialName;
            public string MaterialFullName;
            public string MaterialShader;
            public int SurfaceTypeId;
            public string SurfaceTypeName;
            public string SurfaceTypeMaterial;
            public string SurfaceTypeDetailObject;
        }

        public readonly struct BrushSlotResolutionInfo
        {
            public readonly int SlotIndex;
            public readonly int SubmeshMaterialId;
            public readonly bool Targeted;
            public readonly bool Applied;
            public readonly string Outcome;
            public readonly string InputMaterialName;
            public readonly string OutputMaterialName;
            public readonly string Detail;

            public BrushSlotResolutionInfo(
                int slotIndex,
                int submeshMaterialId,
                bool targeted,
                bool applied,
                string outcome,
                string inputMaterialName,
                string outputMaterialName,
                string detail)
            {
                SlotIndex = slotIndex;
                SubmeshMaterialId = submeshMaterialId;
                Targeted = targeted;
                Applied = applied;
                Outcome = outcome ?? string.Empty;
                InputMaterialName = inputMaterialName ?? string.Empty;
                OutputMaterialName = outputMaterialName ?? string.Empty;
                Detail = detail ?? string.Empty;
            }
        }

        public static FcLevelMaterialOverrideService Current { get; private set; }

        readonly Dictionary<string, FcLevelSupplementData.MaterialDesc> _materialByName =
            new Dictionary<string, FcLevelSupplementData.MaterialDesc>(StringComparer.Ordinal);
        readonly Dictionary<string, List<FcLevelSupplementData.MaterialDesc>> _materialChildrenByParent =
            new Dictionary<string, List<FcLevelSupplementData.MaterialDesc>>(StringComparer.Ordinal);
        readonly Dictionary<string, FcLevelSupplementData.SurfaceTypeDesc> _surfaceByMaterialName =
            new Dictionary<string, FcLevelSupplementData.SurfaceTypeDesc>(StringComparer.Ordinal);
        FcLevelSupplementData.MaterialDesc[] _materialsOrdered = Array.Empty<FcLevelSupplementData.MaterialDesc>();
        readonly Dictionary<string, Material> _materialCache =
            new Dictionary<string, Material>(StringComparer.Ordinal);

        void Awake()
        {
            Current = this;
        }

        void OnDestroy()
        {
            if (Current == this)
                Current = null;

            Clear();
        }

        public void Configure(string levelName, FcLevelSupplementData supplement)
        {
            _ = levelName;
            Clear();

            var materials = supplement != null ? supplement.Materials : null;
            _materialsOrdered = materials ?? Array.Empty<FcLevelSupplementData.MaterialDesc>();

            for (int i = 0; i < _materialsOrdered.Length; i++)
            {
                var desc = _materialsOrdered[i];
                AddNameKey(desc.Name, desc);
                AddNameKey(desc.FullName, desc);
                AddChildMapping(desc);
            }

            var surfaces = supplement != null ? supplement.SurfaceTypes : null;
            if (surfaces == null || surfaces.Length == 0)
                return;

            for (int i = 0; i < surfaces.Length; i++)
            {
                var surface = surfaces[i];
                string key = NormalizeKey(surface.Material);
                if (string.IsNullOrEmpty(key))
                    continue;
                if (!_surfaceByMaterialName.ContainsKey(key))
                    _surfaceByMaterialName.Add(key, surface);
            }
        }

        public void Clear()
        {
            _materialByName.Clear();
            _materialChildrenByParent.Clear();
            _surfaceByMaterialName.Clear();
            _materialsOrdered = Array.Empty<FcLevelSupplementData.MaterialDesc>();

            foreach (var pair in _materialCache)
            {
                var mat = pair.Value;
                if (mat == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(mat);
                else
                    DestroyImmediate(mat);
            }
            _materialCache.Clear();
        }

        public bool HasResolvableOverride(string overrideName, int materialId)
        {
            return TryResolveMaterialDesc(overrideName, materialId, out _, out _);
        }

        public bool TryResolveBrushMaterialMetadata(
            string overrideName,
            int materialId,
            out BrushMaterialMetadata metadata)
        {
            metadata = new BrushMaterialMetadata
            {
                RequestedOverrideName = overrideName ?? string.Empty,
                RequestedMaterialId = materialId,
                ResolutionSource = string.Empty,
                MaterialName = string.Empty,
                MaterialFullName = string.Empty,
                MaterialShader = string.Empty,
                SurfaceTypeId = -1,
                SurfaceTypeName = string.Empty,
                SurfaceTypeMaterial = string.Empty,
                SurfaceTypeDetailObject = string.Empty,
            };

            string normalizedOverride = NormalizeKey(overrideName);
            if (TryResolveMaterialDesc(overrideName, materialId, out var desc, out var source))
            {
                metadata.HasMaterial = true;
                metadata.ResolutionSource = source;
                metadata.MaterialName = desc.Name ?? string.Empty;
                metadata.MaterialFullName = desc.FullName ?? string.Empty;
                metadata.MaterialShader = desc.Shader ?? string.Empty;

                if (TryResolveSurfaceTypeForMaterial(desc, out var surface))
                {
                    metadata.HasSurfaceType = true;
                    metadata.SurfaceTypeId = surface.Id;
                    metadata.SurfaceTypeName = surface.Name ?? string.Empty;
                    metadata.SurfaceTypeMaterial = surface.Material ?? string.Empty;
                    metadata.SurfaceTypeDetailObject = surface.DetailObject ?? string.Empty;
                }

                return metadata.HasMaterial || metadata.HasSurfaceType;
            }

            if (!string.IsNullOrEmpty(normalizedOverride) &&
                _surfaceByMaterialName.TryGetValue(normalizedOverride, out var directSurface))
            {
                metadata.HasSurfaceType = true;
                metadata.ResolutionSource = "override-surface";
                metadata.SurfaceTypeId = directSurface.Id;
                metadata.SurfaceTypeName = directSurface.Name ?? string.Empty;
                metadata.SurfaceTypeMaterial = directSurface.Material ?? string.Empty;
                metadata.SurfaceTypeDetailObject = directSurface.DetailObject ?? string.Empty;
                return true;
            }

            return false;
        }

        public bool TryApplyBrushOverrideToRendererSlots(
            string overrideName,
            int requestedMaterialId,
            string scopeId,
            Material[] slots,
            int[] submeshMaterialIds)
        {
            return TryApplyBrushOverrideToRendererSlots(
                overrideName,
                requestedMaterialId,
                scopeId,
                slots,
                submeshMaterialIds,
                out _);
        }

        public bool TryApplyBrushOverrideToRendererSlots(
            string overrideName,
            int requestedMaterialId,
            string scopeId,
            Material[] slots,
            int[] submeshMaterialIds,
            out BrushSlotResolutionInfo[] diagnostics)
        {
            diagnostics = slots != null && slots.Length > 0
                ? new BrushSlotResolutionInfo[slots.Length]
                : Array.Empty<BrushSlotResolutionInfo>();

            if (slots == null || slots.Length == 0)
                return false;

            bool hasOverrideToken = !string.IsNullOrWhiteSpace(overrideName);
            bool targetedAny = false;
            bool replacedAny = false;

            // DIAG
            {
                string matIdsStr = submeshMaterialIds != null ? string.Join(",", submeshMaterialIds) : "null";
                Debug.Log($"[OvrDiag] '{overrideName}' req={requestedMaterialId} slots={slots.Length} matIds=[{matIdsStr}]");
            }

            for (int i = 0; i < slots.Length; i++)
            {
                string inputMaterialName = slots[i] != null ? slots[i].name : string.Empty;
                if (!IsSlotTargetedByRequest(requestedMaterialId, slots.Length, submeshMaterialIds, i, out var submeshMaterialId))
                {
                    diagnostics[i] = new BrushSlotResolutionInfo(
                        i,
                        submeshMaterialId,
                        targeted: false,
                        applied: false,
                        outcome: "not-targeted",
                        inputMaterialName: inputMaterialName,
                        outputMaterialName: inputMaterialName,
                        detail: $"RequestedMaterialId={requestedMaterialId}");
                    continue;
                }

                targetedAny = true;
                // CryEngine: MatID=0=root material, MatID=1=first sub-material (children[0]), etc.
                // submeshMaterialId - 1 maps face MatID to the supplement children list index.
                // MatID=0 (root) yields -1 which falls through to parent desc (correct for head/root slots).
                int childLookupId = requestedMaterialId < 0 ? submeshMaterialId - 1 : submeshMaterialId;
                Debug.Log($"[OvrDiag] slot={i} submeshMatId={submeshMaterialId} childLookupId={childLookupId} name='{slots[i]?.name}'");
                if (!TryResolveBrushOverrideMaterialForSubmesh(
                        overrideName,
                        requestedMaterialId,
                        childLookupId,
                        slots[i] != null ? slots[i].name : string.Empty,
                        scopeId,
                        slots[i],
                        out var overrideMaterial,
                        out var outcome,
                        out var detail)
                    || overrideMaterial == null)
                {
                    diagnostics[i] = new BrushSlotResolutionInfo(
                        i,
                        submeshMaterialId,
                        targeted: true,
                        applied: false,
                        outcome: string.IsNullOrWhiteSpace(outcome) ? "unresolved" : outcome,
                        inputMaterialName: inputMaterialName,
                        outputMaterialName: inputMaterialName,
                        detail: detail);
                    continue;
                }

                slots[i] = overrideMaterial;
                replacedAny = true;
                diagnostics[i] = new BrushSlotResolutionInfo(
                    i,
                    submeshMaterialId,
                    targeted: true,
                    applied: true,
                    outcome: string.IsNullOrWhiteSpace(outcome) ? "applied" : outcome,
                    inputMaterialName: inputMaterialName,
                    outputMaterialName: overrideMaterial.name,
                    detail: detail);
            }

            if (replacedAny)
                return true;

            if (!hasOverrideToken || targetedAny)
                return false;

            // No targeted slots — apply override textures to each slot's base material.
            bool anyFallback = false;
            TryResolveMaterialDesc(overrideName, requestedMaterialId, out var fallbackDesc, out _);
            for (int i = 0; i < slots.Length; i++)
            {
                string inputMaterialName = slots[i] != null ? slots[i].name : string.Empty;
                int submatId = submeshMaterialIds != null && i < submeshMaterialIds.Length ? submeshMaterialIds[i] : i;
                Material fb = null;
                bool ok = false;
                if (fallbackDesc.Name != null)
                    ok = TryApplyTextureOverride(slots[i], fallbackDesc, scopeId, out fb);
                if (!ok && !string.IsNullOrWhiteSpace(overrideName))
                    ok = TryApplyTexturePathOverride(slots[i], overrideName, scopeId, out fb);

                if (ok && fb != null)
                {
                    slots[i] = fb;
                    anyFallback = true;
                    diagnostics[i] = new BrushSlotResolutionInfo(
                        i, submatId, false, true, "fallback-all-slots",
                        inputMaterialName, fb.name,
                        "No targeted slots; applied override textures to all slots.");
                }
                else
                {
                    diagnostics[i] = new BrushSlotResolutionInfo(
                        i, submatId, false, false, "fallback-failed",
                        inputMaterialName, inputMaterialName,
                        "No targeted slots and texture override failed.");
                }
            }
            return anyFallback;
        }

        public static bool ApplyOverrideToRendererSlots(
            Material[] slots,
            Material overrideMaterial,
            int requestedMaterialId,
            int[] submeshMaterialIds,
            bool allowAllSlotsFallback)
        {
            if (slots == null || slots.Length == 0 || overrideMaterial == null)
                return false;

            if (requestedMaterialId < 0)
            {
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = overrideMaterial;
                return true;
            }

            bool replaced = false;
            if (submeshMaterialIds != null && submeshMaterialIds.Length == slots.Length)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if (submeshMaterialIds[i] != requestedMaterialId)
                        continue;

                    slots[i] = overrideMaterial;
                    replaced = true;
                }
            }

            if (!replaced &&
                requestedMaterialId >= 0 &&
                requestedMaterialId < slots.Length)
            {
                slots[requestedMaterialId] = overrideMaterial;
                replaced = true;
            }

            if (!replaced && allowAllSlotsFallback)
            {
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = overrideMaterial;
                replaced = true;
            }

            return replaced;
        }

        void AddNameKey(string value, FcLevelSupplementData.MaterialDesc desc)
        {
            string key = NormalizeKey(value);
            if (string.IsNullOrEmpty(key))
                return;
            if (!_materialByName.ContainsKey(key))
                _materialByName.Add(key, desc);

            // Also register with '/' replaced by '.' so entity Material="Lib.Name" lookups work.
            // Supplement FullName uses '/' separator; entity XML uses '.'.
            if (key.Contains('/'))
            {
                string dotKey = key.Replace('/', '.');
                if (!_materialByName.ContainsKey(dotKey))
                    _materialByName.Add(dotKey, desc);
            }
        }

        void AddChildMapping(FcLevelSupplementData.MaterialDesc desc)
        {
            string parentKey = NormalizeKey(desc.ParentName);
            if (string.IsNullOrEmpty(parentKey))
                return;

            if (!_materialChildrenByParent.TryGetValue(parentKey, out var children))
            {
                children = new List<FcLevelSupplementData.MaterialDesc>();
                _materialChildrenByParent[parentKey] = children;
            }

            children.Add(desc);
        }

        // Preloads all textures referenced by the override (incl. submaterials) into the runtime cache.
        // Call this before ApplyResult/ApplyLoadResult so override application is a cache-only hit.
        public async UniTask PreloadOverrideTexturesAsync(
            string overrideName,
            int materialId,
            string scopeId,
            CancellationToken ct = default)
        {
            if (!TryResolveMaterialDesc(overrideName, materialId, out var desc, out _))
                return;

            var paths = new List<string>(8);
            CollectTexturePaths(desc, paths);

            string parentKey = NormalizeKey(desc.FullName);
            if (!string.IsNullOrEmpty(parentKey) &&
                _materialChildrenByParent.TryGetValue(parentKey, out var children))
            {
                for (int i = 0; i < children.Count; i++)
                    CollectTexturePaths(children[i], paths);
            }

            if (paths.Count == 0)
                return;

            var tasks = new UniTask[paths.Count];
            for (int i = 0; i < paths.Count; i++)
                tasks[i] = PreloadTexturePathAsync(paths[i], scopeId, ct);
            await UniTask.WhenAll(tasks);
        }

        static void CollectTexturePaths(FcLevelSupplementData.MaterialDesc desc, List<string> paths)
        {
            if (desc.TextureSlots != null)
                for (int i = 0; i < desc.TextureSlots.Length; i++)
                {
                    string file = desc.TextureSlots[i].File;
                    if (!string.IsNullOrWhiteSpace(file))
                        paths.Add(file);
                }

            if (desc.TextureRefs != null)
                for (int i = 0; i < desc.TextureRefs.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(desc.TextureRefs[i]))
                        paths.Add(desc.TextureRefs[i]);
                }
        }

        static async UniTask PreloadTexturePathAsync(string rawPath, string scopeId, CancellationToken ct)
        {
            string path = NormalizePath(rawPath);
            if (string.IsNullOrEmpty(path))
                return;

            if (TextureImportService.IsSupportedVirtualPath(path))
            {
                await TextureImportService.RuntimeService.TryLoadWithInfoAsync(path, scopeId, ct,
                    new TextureRuntimeImportOptions(
                        useRuntimeMemoryCache: true,
                        markNonReadable: true,
                        linearColorSpace: false,
                        generateMipmaps: true));
                return;
            }

            int dot = path.LastIndexOf('.');
            if (dot >= 0)
                return;

            for (int i = 0; i < TextureImportService.SupportedExtensions.Length; i++)
            {
                if (ct.IsCancellationRequested) return;
                string candidate = path + TextureImportService.SupportedExtensions[i];
                if (!TextureImportService.IsSupportedVirtualPath(candidate))
                    continue;
                var result = await TextureImportService.RuntimeService.TryLoadWithInfoAsync(candidate, scopeId, ct,
                    new TextureRuntimeImportOptions(
                        useRuntimeMemoryCache: true,
                        markNonReadable: true,
                        linearColorSpace: false,
                        generateMipmaps: true));
                if (result.Success)
                    return;
            }
        }

        // Instantiates baseMaterial and applies override textures from desc.
        // Returns false if no textures resolved (nothing to override).
        bool TryApplyTextureOverride(
            Material baseMaterial,
            FcLevelSupplementData.MaterialDesc desc,
            string scopeId,
            out Material result)
        {
            result = null;
            if (baseMaterial == null)
                return false;

            var resolved = ResolveLevelMaterialTextures(desc, scopeId);
            if (resolved.BaseMap == null && resolved.NormalMap == null &&
                resolved.SpecularMap == null && resolved.OpacityMap == null && resolved.GlossMap == null)
                return false;

            result = UnityEngine.Object.Instantiate(baseMaterial);
            string descName = string.IsNullOrWhiteSpace(desc.FullName) ? desc.Name : desc.FullName;
            result.name = $"{baseMaterial.name}_ovr_{descName}";
            CgfMaterialBuilder.ApplyResolvedTextures(result, resolved);
            return true;
        }

        // Instantiates baseMaterial and applies a single diffuse texture from rawPath.
        // Returns false if the texture cannot be loaded.
        bool TryApplyTexturePathOverride(
            Material baseMaterial,
            string rawPath,
            string scopeId,
            out Material result)
        {
            result = null;
            if (baseMaterial == null)
                return false;

            string path = NormalizePath(rawPath);
            if (!TryLoadTextureCandidate(path, scopeId, out var tex, out var resolvedPath))
                return false;

            var resolved = new CgfResolvedMaterialTextures(
                diffuseTextureName: resolvedPath,
                normalTextureName: null,
                specularTextureName: null,
                opacityTextureName: null,
                glossTextureName: null,
                baseMapVirtualPath: resolvedPath,
                normalMapVirtualPath: null,
                specularMapVirtualPath: null,
                opacityMapVirtualPath: null,
                glossMapVirtualPath: null,
                baseMap: tex,
                normalMap: null,
                specularMap: null,
                opacityMap: null,
                glossMap: null);

            result = UnityEngine.Object.Instantiate(baseMaterial);
            result.name = $"{baseMaterial.name}_ovrtex";
            CgfMaterialBuilder.ApplyResolvedTextures(result, resolved);
            return true;
        }

        static CgfResolvedMaterialTextures ResolveLevelMaterialTextures(
            FcLevelSupplementData.MaterialDesc desc,
            string scopeId)
        {
            string diffuseName = null;
            string normalName = null;
            string specularName = null;
            string opacityName = null;
            string glossName = null;

            string basePath = null;
            string normalPath = null;
            string specPath = null;
            string opacityPath = null;
            string glossPath = null;

            Texture2D baseTex = null;
            Texture2D normalTex = null;
            Texture2D specTex = null;
            Texture2D opacityTex = null;
            Texture2D glossTex = null;

            if (TryResolveTextureFromSlots(desc.TextureSlots, scopeId, IsPreferredBaseMapSlot, out var baseSlot))
            {
                diffuseName = baseSlot.SourcePath;
                basePath = baseSlot.ResolvedPath;
                baseTex = baseSlot.Texture;
            }

            if (TryResolveTextureFromSlots(desc.TextureSlots, scopeId, IsNormalMapSlot, out var normalSlot))
            {
                normalName = normalSlot.SourcePath;
                normalPath = normalSlot.ResolvedPath;
                normalTex = normalSlot.Texture;
            }

            if (TryResolveTextureFromSlots(desc.TextureSlots, scopeId, IsSpecularMapSlot, out var specSlot))
            {
                specularName = specSlot.SourcePath;
                specPath = specSlot.ResolvedPath;
                specTex = specSlot.Texture;
            }

            if (TryResolveTextureFromSlots(desc.TextureSlots, scopeId, IsOpacityMapSlot, out var opacitySlot))
            {
                opacityName = opacitySlot.SourcePath;
                opacityPath = opacitySlot.ResolvedPath;
                opacityTex = opacitySlot.Texture;
            }

            if (TryResolveTextureFromSlots(desc.TextureSlots, scopeId, IsGlossMapSlot, out var glossSlot))
            {
                glossName = glossSlot.SourcePath;
                glossPath = glossSlot.ResolvedPath;
                glossTex = glossSlot.Texture;
            }

            // Backward-compatibility fallback for old flattened refs data.
            if (baseTex == null && TryResolveTextureFromRefs(desc.TextureRefs, scopeId, out var baseRef))
            {
                diffuseName = baseRef.SourcePath;
                basePath = baseRef.ResolvedPath;
                baseTex = baseRef.Texture;
            }

            return new CgfResolvedMaterialTextures(
                diffuseTextureName: diffuseName,
                normalTextureName: normalName,
                specularTextureName: specularName,
                opacityTextureName: opacityName,
                glossTextureName: glossName,
                baseMapVirtualPath: basePath,
                normalMapVirtualPath: normalPath,
                specularMapVirtualPath: specPath,
                opacityMapVirtualPath: opacityPath,
                glossMapVirtualPath: glossPath,
                baseMap: baseTex,
                normalMap: normalTex,
                specularMap: specTex,
                opacityMap: opacityTex,
                glossMap: glossTex);
        }

        static bool IsPreferredBaseMapSlot(string map)
        {
            if (string.IsNullOrWhiteSpace(map))
                return false;

            switch (map.Trim().ToLowerInvariant())
            {
                case "diffuse":
                case "texture":
                case "decal":
                    return true;
                default:
                    return false;
            }
        }

        static bool IsNormalMapSlot(string map)
        {
            if (string.IsNullOrWhiteSpace(map))
                return false;
            switch (map.Trim().ToLowerInvariant())
            {
                case "bumpmap":
                case "normal":
                case "normalmap":
                    return true;
                default:
                    return false;
            }
        }

        static bool IsSpecularMapSlot(string map)
        {
            if (string.IsNullOrWhiteSpace(map))
                return false;
            return map.Trim().Equals("Specular", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsOpacityMapSlot(string map)
        {
            if (string.IsNullOrWhiteSpace(map))
                return false;
            return map.Trim().Equals("Opacity", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsGlossMapSlot(string map)
        {
            if (string.IsNullOrWhiteSpace(map))
                return false;
            switch (map.Trim().ToLowerInvariant())
            {
                case "gloss":
                case "glossmap":
                    return true;
                default:
                    return false;
            }
        }

        struct ResolvedTexture
        {
            public string SourcePath;
            public string ResolvedPath;
            public Texture2D Texture;
        }

        static bool TryResolveTextureFromSlots(
            FcLevelSupplementData.MaterialDesc.TextureSlotDesc[] slots,
            string scopeId,
            Func<string, bool> match,
            out ResolvedTexture texture)
        {
            texture = default;
            if (slots == null || slots.Length == 0 || match == null)
                return false;

            for (int i = 0; i < slots.Length; i++)
            {
                if (!match(slots[i].Map))
                    continue;
                if (TryResolveTexturePath(slots[i].File, scopeId, out var resolved))
                {
                    texture = resolved;
                    return true;
                }
            }

            return false;
        }

        static bool TryResolveTextureFromRefs(
            string[] refs,
            string scopeId,
            out ResolvedTexture texture)
        {
            texture = default;
            if (refs == null || refs.Length == 0)
                return false;

            for (int i = 0; i < refs.Length; i++)
            {
                if (TryResolveTexturePath(refs[i], scopeId, out var resolved))
                {
                    texture = resolved;
                    return true;
                }
            }

            return false;
        }

        static bool TryResolveTexturePath(string rawPath, string scopeId, out ResolvedTexture texture)
        {
            texture = default;
            if (TryLoadTextureCandidate(rawPath, scopeId, out var loaded, out var resolvedPath))
            {
                texture = new ResolvedTexture
                {
                    SourcePath = NormalizePath(rawPath),
                    ResolvedPath = resolvedPath,
                    Texture = loaded,
                };
                return true;
            }

            return false;
        }

        static bool TryLoadTextureCandidate(string rawPath, string scopeId, out Texture2D texture, out string resolvedPath)
        {
            texture = null;
            resolvedPath = null;

            string normalized = NormalizePath(rawPath);
            if (string.IsNullOrEmpty(normalized))
                return false;

            if (TryLoadExact(normalized, scopeId, out texture))
            {
                resolvedPath = normalized;
                return true;
            }

            int dot = normalized.LastIndexOf('.');
            if (dot >= 0)
                return false;

            for (int i = 0; i < TextureImportService.SupportedExtensions.Length; i++)
            {
                string candidate = normalized + TextureImportService.SupportedExtensions[i];
                if (TryLoadExact(candidate, scopeId, out texture))
                {
                    resolvedPath = candidate;
                    return true;
                }
            }

            return false;
        }

        static bool TryLoadExact(string path, string scopeId, out Texture2D texture)
        {
            texture = null;
            if (!TextureImportService.IsSupportedVirtualPath(path))
                return false;

            return TextureImportService.RuntimeService.TryLoad(
                path,
                out texture,
                scopeId,
                new TextureRuntimeImportOptions(
                    useRuntimeMemoryCache: true,
                    markNonReadable: true,
                    linearColorSpace: false,
                    generateMipmaps: true));
        }

        static string NormalizePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return value.Trim().Replace('\\', '/').Trim('/').ToLowerInvariant();
        }

        static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            return value.Trim().Replace('\\', '/').Trim('/').ToLowerInvariant();
        }

        bool TryResolveBrushOverrideMaterialForSubmesh(
            string overrideName,
            int requestedMaterialId,
            int submeshMaterialId,
            string slotMaterialName,
            string scopeId,
            Material baseMaterial,
            out Material material,
            out string outcome,
            out string detail)
        {
            material = null;
            outcome = string.Empty;
            detail = string.Empty;

            if (baseMaterial == null)
            {
                outcome = "unresolved";
                detail = "No base material for slot.";
                return false;
            }

            string baseName = baseMaterial.name ?? string.Empty;
            string normalizedOverride = NormalizeKey(overrideName);
            string normalizedSlotMaterialName = NormalizeMaterialNameForLookup(slotMaterialName);
            string slotCacheKey = $"slot:{baseName}|{normalizedOverride}|req:{requestedMaterialId}|sm:{submeshMaterialId}|src:{normalizedSlotMaterialName}";
            if (_materialCache.TryGetValue(slotCacheKey, out material) && material != null)
            {
                outcome = "cache-slot";
                detail = "Resolved from slot-specific cache.";
                return true;
            }

            if (TryResolveMaterialDesc(overrideName, requestedMaterialId, out var baseDesc, out _))
            {
                var selectedDesc = ResolveSubmaterialDesc(
                    baseDesc,
                    submeshMaterialId,
                    normalizedSlotMaterialName,
                    out var selectionMode);
                string selectedDescKey = !string.IsNullOrWhiteSpace(selectedDesc.FullName)
                    ? NormalizeKey(selectedDesc.FullName)
                    : NormalizeKey(selectedDesc.Name);
                string descCacheKey = $"desc:{baseName}|{selectedDescKey}|sm:{submeshMaterialId}";
                if (_materialCache.TryGetValue(descCacheKey, out material) && material != null)
                {
                    _materialCache[slotCacheKey] = material;
                    outcome = $"cache-level-{selectionMode}";
                    detail = $"Material={selectedDesc.FullName}";
                    return true;
                }

                if (TryApplyTextureOverride(baseMaterial, selectedDesc, scopeId, out material) && material != null)
                {
                    _materialCache[descCacheKey] = material;
                    _materialCache[slotCacheKey] = material;
                    outcome = $"level-{selectionMode}";
                    detail = $"Material={selectedDesc.FullName}";
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(overrideName) &&
                TryApplyTexturePathOverride(baseMaterial, overrideName, scopeId, out material) && material != null)
            {
                _materialCache[slotCacheKey] = material;
                outcome = "fallback-tex-path";
                detail = "Used texture path fallback.";
                return true;
            }

            outcome = "unresolved";
            detail = "No level material/submaterial match and no texture fallback resolved.";
            return false;
        }

        FcLevelSupplementData.MaterialDesc ResolveSubmaterialDesc(
            FcLevelSupplementData.MaterialDesc parentDesc,
            int submeshMaterialId,
            string normalizedSlotMaterialName,
            out string selectionMode)
        {
            string parentKey = NormalizeKey(parentDesc.FullName);
            if (string.IsNullOrEmpty(parentKey))
            {
                selectionMode = "parent";
                return parentDesc;
            }

            if (!_materialChildrenByParent.TryGetValue(parentKey, out var children) ||
                children == null ||
                children.Count == 0)
            {
                selectionMode = "parent";
                return parentDesc;
            }

            if (submeshMaterialId >= 0 && submeshMaterialId < children.Count)
            {
                selectionMode = "submesh-id";
                var c = children[submeshMaterialId];
                Debug.Log($"[OvrDiag] submesh-id pick: sm={submeshMaterialId} children.Count={children.Count} → '{c.FullName ?? c.Name}'");
                return c;
            }

            Debug.Log($"[OvrDiag] submesh-id miss: sm={submeshMaterialId} children.Count={children.Count} slotName='{normalizedSlotMaterialName}'");

            if (TryResolveChildBySourceMaterialName(children, normalizedSlotMaterialName, out var byName))
            {
                selectionMode = "source-name";
                return byName;
            }

            selectionMode = "parent";
            return parentDesc;
        }

        static bool TryResolveChildBySourceMaterialName(
            List<FcLevelSupplementData.MaterialDesc> children,
            string normalizedSlotMaterialName,
            out FcLevelSupplementData.MaterialDesc resolved)
        {
            if (children == null || children.Count == 0 || string.IsNullOrEmpty(normalizedSlotMaterialName))
            {
                resolved = default;
                return false;
            }

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                string childFull = NormalizeKey(child.FullName);
                string childName = NormalizeKey(child.Name);

                if (!string.IsNullOrEmpty(childFull) &&
                    (normalizedSlotMaterialName == childFull || normalizedSlotMaterialName.EndsWith("/" + childFull, StringComparison.Ordinal)))
                {
                    resolved = child;
                    return true;
                }

                if (!string.IsNullOrEmpty(childName) &&
                    (normalizedSlotMaterialName == childName || normalizedSlotMaterialName.EndsWith("/" + childName, StringComparison.Ordinal)))
                {
                    resolved = child;
                    return true;
                }

                // Reverse-path: child fullname has longer path prefix, slot name is its trailing segment
                if (!string.IsNullOrEmpty(childFull) &&
                    childFull.EndsWith("/" + normalizedSlotMaterialName, StringComparison.Ordinal))
                {
                    resolved = child;
                    return true;
                }
            }

            resolved = default;
            return false;
        }

        static string NormalizeMaterialNameForLookup(string value)
        {
            string key = NormalizeKey(value);
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            const string levelMatPrefix = "levelmat_";
            const string levelMatTexPrefix = "levelmattex_";
            if (key.StartsWith(levelMatPrefix, StringComparison.Ordinal))
                key = key.Substring(levelMatPrefix.Length);
            else if (key.StartsWith(levelMatTexPrefix, StringComparison.Ordinal))
                key = key.Substring(levelMatTexPrefix.Length);

            const string instanceSuffix = " (instance)";
            if (key.EndsWith(instanceSuffix, StringComparison.Ordinal))
                key = key.Substring(0, key.Length - instanceSuffix.Length);

            return key.Trim();
        }

        static bool IsSlotTargetedByRequest(
            int requestedMaterialId,
            int slotCount,
            int[] submeshMaterialIds,
            int slotIndex,
            out int submeshMaterialId)
        {
            submeshMaterialId = submeshMaterialIds != null &&
                                slotIndex >= 0 &&
                                slotIndex < submeshMaterialIds.Length
                ? submeshMaterialIds[slotIndex]
                : slotIndex;

            if (requestedMaterialId < 0)
                return true;

            if (submeshMaterialIds != null && submeshMaterialIds.Length == slotCount)
                return submeshMaterialIds[slotIndex] == requestedMaterialId;

            return slotIndex == requestedMaterialId;
        }

        bool TryResolveMaterialDesc(
            string overrideName,
            int materialId,
            out FcLevelSupplementData.MaterialDesc desc,
            out string source)
        {
            string key = NormalizeKey(overrideName);
            if (!string.IsNullOrEmpty(key) &&
                _materialByName.TryGetValue(key, out desc))
            {
                source = "override-name";
                return true;
            }

            // Entity XML uses "LibraryName.MaterialName" format (e.g. "Mercenaries.Tshirt.Black").
            // Supplement stores only the material name without the library prefix.
            // Strip everything up to and including the first '.' and retry.
            if (!string.IsNullOrEmpty(key))
            {
                int dotIdx = key.IndexOf('.');
                if (dotIdx >= 0 && dotIdx + 1 < key.Length)
                {
                    string withoutLib = key.Substring(dotIdx + 1);
                    if (_materialByName.TryGetValue(withoutLib, out desc))
                    {
                        source = "override-name";
                        return true;
                    }
                }
            }

            if (materialId >= 0 && materialId < _materialsOrdered.Length)
            {
                desc = _materialsOrdered[materialId];
                source = "material-id";
                return true;
            }

            desc = default;
            source = string.Empty;
            return false;
        }

        bool TryResolveSurfaceTypeForMaterial(
            FcLevelSupplementData.MaterialDesc material,
            out FcLevelSupplementData.SurfaceTypeDesc surface)
        {
            if (!string.IsNullOrWhiteSpace(material.FullName) &&
                _surfaceByMaterialName.TryGetValue(NormalizeKey(material.FullName), out surface))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(material.Name) &&
                _surfaceByMaterialName.TryGetValue(NormalizeKey(material.Name), out surface))
            {
                return true;
            }

            surface = default;
            return false;
        }
    }
}
