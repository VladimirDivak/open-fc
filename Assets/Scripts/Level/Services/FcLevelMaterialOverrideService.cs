using System;
using System.Collections.Generic;
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

        public bool TryResolveBrushOverrideMaterial(
            string overrideName,
            int materialId,
            string scopeId,
            out Material material)
        {
            return TryResolveBrushOverrideMaterial(
                overrideName,
                materialId,
                scopeId,
                out material,
                out _);
        }

        public bool TryResolveBrushOverrideMaterial(
            string overrideName,
            int materialId,
            string scopeId,
            out Material material,
            out BrushMaterialMetadata metadata)
        {
            material = null;
            TryResolveBrushMaterialMetadata(overrideName, materialId, out metadata);

            string key = NormalizeKey(overrideName);
            string idKey = $"id:{materialId}";

            if (!string.IsNullOrEmpty(key))
            {
                if (_materialCache.TryGetValue(key, out material) && material != null)
                    return true;
            }
            if (materialId >= 0 && _materialCache.TryGetValue(idKey, out material) && material != null)
                return true;

            if (!string.IsNullOrEmpty(key) && _materialByName.TryGetValue(key, out var descByName))
            {
                if (TryBuildMaterialFromLevelDesc(descByName, scopeId, out material))
                {
                    _materialCache[key] = material;
                    if (materialId >= 0)
                        _materialCache[idKey] = material;
                    return true;
                }
            }

            if (materialId >= 0 &&
                materialId < _materialsOrdered.Length &&
                TryBuildMaterialFromLevelDesc(_materialsOrdered[materialId], scopeId, out material))
            {
                _materialCache[idKey] = material;
                if (!string.IsNullOrEmpty(key))
                    _materialCache[key] = material;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(overrideName) &&
                TryBuildMaterialFromTexturePath(overrideName, scopeId, out material))
            {
                if (!string.IsNullOrEmpty(key))
                    _materialCache[key] = material;
                if (materialId >= 0)
                    _materialCache[idKey] = material;
                return true;
            }

            return false;
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
                if (!TryResolveBrushOverrideMaterialForSubmesh(
                        overrideName,
                        requestedMaterialId,
                        submeshMaterialId,
                        slots[i] != null ? slots[i].name : string.Empty,
                        scopeId,
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

            if (!TryResolveBrushOverrideMaterial(
                    overrideName,
                    requestedMaterialId,
                    scopeId,
                    out var fallbackMaterial)
                || fallbackMaterial == null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    string inputMaterialName = slots[i] != null ? slots[i].name : string.Empty;
                    diagnostics[i] = new BrushSlotResolutionInfo(
                        i,
                        submeshMaterialIds != null && i < submeshMaterialIds.Length ? submeshMaterialIds[i] : i,
                        targeted: false,
                        applied: false,
                        outcome: "fallback-failed",
                        inputMaterialName: inputMaterialName,
                        outputMaterialName: inputMaterialName,
                        detail: "No targeted slots and generic override fallback failed.");
                }
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                string inputMaterialName = slots[i] != null ? slots[i].name : string.Empty;
                slots[i] = fallbackMaterial;
                diagnostics[i] = new BrushSlotResolutionInfo(
                    i,
                    submeshMaterialIds != null && i < submeshMaterialIds.Length ? submeshMaterialIds[i] : i,
                    targeted: false,
                    applied: true,
                    outcome: "fallback-all-slots",
                    inputMaterialName: inputMaterialName,
                    outputMaterialName: fallbackMaterial.name,
                    detail: "No targeted slots; applied generic override material to all slots.");
            }

            return true;
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

        bool TryBuildMaterialFromLevelDesc(
            FcLevelSupplementData.MaterialDesc desc,
            string scopeId,
            out Material material)
        {
            material = null;

            string shaderNorm = string.IsNullOrWhiteSpace(desc.Shader)
                ? "templmodelcommon"
                : desc.Shader.Trim().ToLowerInvariant();
            bool isDecalShader = shaderNorm.Contains("decal") &&
                                  !shaderNorm.Contains("decalmodulate");
            float resolvedAlphaTest = isDecalShader ? 0f : Mathf.Max(0f, desc.AlphaTest);
            if (isDecalShader && desc.AlphaTest > 0.01f)
                Debug.Log($"[LevelMaterial] '{desc.Name}': decal shader, RGB-only assumed, AlphaTest suppressed ({desc.AlphaTest:F3}→0).");

            var chunk = new CgfMaterialChunk
            {
                Name = string.IsNullOrWhiteSpace(desc.FullName) ? desc.Name : desc.FullName,
                ShaderName = shaderNorm,
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255),
                AlphaTest = resolvedAlphaTest,
                Opacity = Mathf.Clamp01(desc.Opacity > 0f ? desc.Opacity : 1f),
                Flags = (CgfMtlFlags)desc.MtlFlags,
            };

            var resolved = ResolveLevelMaterialTextures(desc, scopeId);
            material = CgfMaterialBuilder.Build(chunk, resolved);

            if (material != null)
                material.name = $"LevelMat_{(string.IsNullOrWhiteSpace(desc.FullName) ? desc.Name : desc.FullName)}";
            return material != null;
        }

        bool TryBuildMaterialFromTexturePath(string rawPath, string scopeId, out Material material)
        {
            material = null;
            if (string.IsNullOrWhiteSpace(rawPath))
                return false;

            string path = NormalizePath(rawPath);
            if (!TryLoadTextureCandidate(path, scopeId, out var tex, out var resolvedPath))
                return false;

            var chunk = new CgfMaterialChunk
            {
                Name = $"override:{rawPath}",
                ShaderName = "templmodelcommon",
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255)
            };

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

            material = CgfMaterialBuilder.Build(chunk, resolved);
            if (material != null)
                material.name = $"LevelMatTex_{rawPath}";
            return material != null;
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
            out Material material,
            out string outcome,
            out string detail)
        {
            material = null;
            outcome = string.Empty;
            detail = string.Empty;

            string normalizedOverride = NormalizeKey(overrideName);
            string normalizedSlotMaterialName = NormalizeMaterialNameForLookup(slotMaterialName);
            string slotCacheKey = $"slot:{normalizedOverride}|req:{requestedMaterialId}|sm:{submeshMaterialId}|src:{normalizedSlotMaterialName}";
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
                string descCacheKey = $"desc:{selectedDescKey}|sm:{submeshMaterialId}";
                if (_materialCache.TryGetValue(descCacheKey, out material) && material != null)
                {
                    _materialCache[slotCacheKey] = material;
                    outcome = $"cache-level-{selectionMode}";
                    detail = $"Material={selectedDesc.FullName}";
                    return true;
                }

                if (TryBuildMaterialFromLevelDesc(selectedDesc, scopeId, out material) && material != null)
                {
                    _materialCache[descCacheKey] = material;
                    _materialCache[slotCacheKey] = material;
                    outcome = $"level-{selectionMode}";
                    detail = $"Material={selectedDesc.FullName}";
                    return true;
                }
            }

            if (TryResolveBrushOverrideMaterial(overrideName, requestedMaterialId, scopeId, out material) && material != null)
            {
                _materialCache[slotCacheKey] = material;
                outcome = "fallback-generic";
                detail = "Used generic override material fallback.";
                return true;
            }

            outcome = "unresolved";
            detail = "No level material/submaterial match and no generic fallback material.";
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
                return children[submeshMaterialId];
            }

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
