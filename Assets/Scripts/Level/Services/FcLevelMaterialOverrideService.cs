using System;
using System.Collections.Generic;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Importer.Texture;
using OpenFarCry.Level.Data;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    // Runtime resolver for level-specific material overrides from materials.xml / brush.lst.
    // First pass: resolve by material name/fullname and build a URP material with the first loadable texture ref.
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

        public static FcLevelMaterialOverrideService Current { get; private set; }

        readonly Dictionary<string, FcLevelSupplementData.MaterialDesc> _materialByName =
            new Dictionary<string, FcLevelSupplementData.MaterialDesc>(StringComparer.Ordinal);
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

        void AddNameKey(string value, FcLevelSupplementData.MaterialDesc desc)
        {
            string key = NormalizeKey(value);
            if (string.IsNullOrEmpty(key))
                return;
            if (!_materialByName.ContainsKey(key))
                _materialByName.Add(key, desc);
        }

        bool TryBuildMaterialFromLevelDesc(
            FcLevelSupplementData.MaterialDesc desc,
            string scopeId,
            out Material material)
        {
            material = null;
            string texturePath = FindFirstLoadableTexture(desc.TextureRefs, scopeId, out var baseMap);

            var chunk = new CgfMaterialChunk
            {
                Name = string.IsNullOrWhiteSpace(desc.FullName) ? desc.Name : desc.FullName,
                ShaderName = string.IsNullOrWhiteSpace(desc.Shader)
                    ? "templmodelcommon"
                    : desc.Shader.Trim().ToLowerInvariant(),
                MtlType = CgfMtlType.Standard,
                DiffuseColor = new Color32(255, 255, 255, 255)
            };

            if (baseMap != null)
            {
                var resolved = new CgfResolvedMaterialTextures(
                    diffuseTextureName: texturePath,
                    normalTextureName: null,
                    specularTextureName: null,
                    opacityTextureName: null,
                    glossTextureName: null,
                    baseMapVirtualPath: texturePath,
                    normalMapVirtualPath: null,
                    specularMapVirtualPath: null,
                    opacityMapVirtualPath: null,
                    glossMapVirtualPath: null,
                    baseMap: baseMap,
                    normalMap: null,
                    specularMap: null,
                    opacityMap: null,
                    glossMap: null);

                material = CgfMaterialBuilder.Build(chunk, resolved);
            }
            else
            {
                material = CgfMaterialBuilder.Build(chunk);
            }

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

        static string FindFirstLoadableTexture(string[] refs, string scopeId, out Texture2D texture)
        {
            texture = null;
            if (refs == null || refs.Length == 0)
                return null;

            for (int i = 0; i < refs.Length; i++)
            {
                if (TryLoadTextureCandidate(refs[i], scopeId, out texture, out var resolvedPath))
                    return resolvedPath;
            }

            return null;
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
