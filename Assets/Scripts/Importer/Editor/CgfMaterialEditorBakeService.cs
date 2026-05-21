using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    // Bakes CgfMaterialChunk → persistent Unity .mat project assets (no textures embedded).
    // Baked .mat files are content-addressed and shared: a material with identical build
    // state is baked once into Assets/Materials/Shared and reused by every CGF that
    // references it (LOD siblings, shared material libraries). The manifest still keys on
    // (virtualPath, TableIndex) so each chunk resolves to its shared asset.
    // Runtime injects textures later via CgfMaterialBuilder.ApplyResolvedTextures.
    public static class CgfMaterialEditorBakeService
    {
        // Editable project assets live under Assets/Materials/. Assets/FCData/ is reserved
        // for generated runtime cache (geometry, terrain bakes) and must not host editable
        // .mat files. New bakes write here; the editor scan still picks up legacy paths
        // under Assets/FCData/Materials/ until a level has been re-baked into the new layout.
        const string MaterialAssetRoot = "Assets/Materials";
        const string SharedMaterialFolder = MaterialAssetRoot + "/Shared";
        const string LevelsMaterialFolder = MaterialAssetRoot + "/Levels";
        const string LegacyMaterialCacheRoot = "Assets/FCData/Materials";

        // Session registry: (chunkName + overrideName + contentHash) -> existing baked Material.
        // Populated lazily from FcMaterialMetadata sidecars on first lookup of a bake session,
        // cleared at the end of BakeOrUpdateLevelManifest. Lets repeated GetOrBakeMaterial
        // calls within one bake reuse already-discovered assets without an AssetDatabase scan
        // per call.
        static Dictionary<string, Material> _sessionRegistry;

        public static Material GetOrBakeMaterial(string cgfVirtualPath, Cgf.CgfMaterialChunk chunk)
            => GetOrBakeMaterial(cgfVirtualPath, chunk, out _);

        public static Material GetOrBakeMaterial(
            string cgfVirtualPath,
            Cgf.CgfMaterialChunk chunk,
            out bool wasCreated)
        {
            wasCreated = false;
            if (chunk == null || string.IsNullOrWhiteSpace(cgfVirtualPath))
                return null;

            // 2-arg path (PostProcess prefab cache, PreBake UI) bakes/reuses without
            // reshuffling folders — only the level-manifest bake carries routing intent.
            return GetOrBakeMaterialInternal(
                cgfVirtualPath,
                chunk,
                overrideName: null,
                targetFolder: SharedMaterialFolder,
                allowPromotion: false,
                out wasCreated);
        }

        // Bakes (or reuses) a material at a specific target folder. overrideName non-empty
        // marks the asset as a brush.lst / entity / vegetation override; the sidecar then
        // pins the material to that overrideName so the runtime manifest lookup can route
        // through (vpath, tableIndex, overrideName) → this asset.
        // allowPromotion: when true, a registry-hit material currently under a Levels/
        // folder is moved to Shared if this bake routes it there (multi-level material).
        internal static Material GetOrBakeMaterialInternal(
            string cgfVirtualPath,
            Cgf.CgfMaterialChunk chunk,
            string overrideName,
            string targetFolder,
            out bool wasCreated)
            => GetOrBakeMaterialInternal(
                cgfVirtualPath, chunk, overrideName, targetFolder,
                allowPromotion: true, out wasCreated);

        internal static Material GetOrBakeMaterialInternal(
            string cgfVirtualPath,
            Cgf.CgfMaterialChunk chunk,
            string overrideName,
            string targetFolder,
            bool allowPromotion,
            out bool wasCreated)
        {
            wasCreated = false;
            if (chunk == null || string.IsNullOrWhiteSpace(cgfVirtualPath))
                return null;

            string contentHash = ComputeContentHash(chunk);
            string registryKey = BuildSidecarRegistryKey(chunk.Name, overrideName, contentHash);

            var registry = GetOrBuildSessionRegistry();
            if (registry.TryGetValue(registryKey, out var existing) && existing != null)
            {
                // A material routed to Shared this bake but currently living in a
                // Levels/{lvl}/ folder is multi-level: promote (move) it to Shared so
                // editing it no longer silently couples one level's folder to another.
                if (allowPromotion)
                    PromoteToSharedIfNeeded(existing, targetFolder);
                return existing;
            }

            EnsureDirectory(targetFolder);
            string sanitizedBase = SanitizeMaterialFileName(chunk.Name);
            string overrideSuffix = string.IsNullOrEmpty(overrideName)
                ? string.Empty
                : "_" + SanitizeMaterialFileName(overrideName);
            string finalName = ResolveCollisionFreeAssetName(
                targetFolder,
                sanitizedBase + overrideSuffix);

            string matPath = $"{targetFolder}/{finalName}.mat";
            string sidecarPath = $"{targetFolder}/{finalName}.metadata.asset";

            var mat = Cgf.CgfMaterialBuilder.Build(chunk);
            if (mat == null)
                return null;
            AssetDatabase.CreateAsset(mat, matPath);

            var meta = ScriptableObject.CreateInstance<FcMaterialMetadata>();
            meta.SourceCgfVirtualPath = cgfVirtualPath;
            meta.SourceChunkId = chunk.ChunkID;
            meta.SourceTableIndex = chunk.TableIndex;
            meta.OverrideName = overrideName ?? string.Empty;
            meta.ContentHash = contentHash;
            meta.ChunkName = chunk.Name ?? string.Empty;
            AssetDatabase.CreateAsset(meta, sidecarPath);

            var loaded = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            registry[registryKey] = loaded;
            wasCreated = true;
            return loaded;
        }

        public static void BeginBakeSession() => _sessionRegistry = null;
        public static void EndBakeSession() => _sessionRegistry = null;

        // Builds the (chunkName + overrideName + contentHash) → Material map from existing
        // sidecar assets. Reads both the new Assets/Materials root and the legacy
        // Assets/FCData/Materials root so cross-session dedup survives migration.
        static Dictionary<string, Material> GetOrBuildSessionRegistry()
        {
            if (_sessionRegistry != null)
                return _sessionRegistry;

            _sessionRegistry = new Dictionary<string, Material>(System.StringComparer.Ordinal);
            string[] roots = GetManifestSearchRoots();
            if (roots.Length == 0)
                return _sessionRegistry;

            var guids = AssetDatabase.FindAssets("t:FcMaterialMetadata", roots);
            for (int i = 0; i < guids.Length; i++)
            {
                string sidecarPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(sidecarPath))
                    continue;

                var meta = AssetDatabase.LoadAssetAtPath<FcMaterialMetadata>(sidecarPath);
                if (meta == null)
                    continue;

                // Pair .mat to its sidecar by suffix swap. The sidecar path is "X.metadata.asset";
                // the material path is "X.mat".
                const string sidecarSuffix = ".metadata.asset";
                if (!sidecarPath.EndsWith(sidecarSuffix, System.StringComparison.Ordinal))
                    continue;

                string matPath = sidecarPath.Substring(0, sidecarPath.Length - sidecarSuffix.Length) + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                    continue;

                string key = BuildSidecarRegistryKey(meta.ChunkName, meta.OverrideName, meta.ContentHash);
                _sessionRegistry[key] = mat;
            }

            return _sessionRegistry;
        }

        static string BuildSidecarRegistryKey(string chunkName, string overrideName, string contentHash)
        {
            string n = (chunkName ?? string.Empty).ToLowerInvariant();
            string o = string.IsNullOrEmpty(overrideName) ? string.Empty : overrideName.ToLowerInvariant();
            string h = contentHash ?? string.Empty;
            return string.Concat(n, "|", o, "|", h);
        }

        // Picks {baseName}.mat in targetFolder if free; otherwise tries {baseName}_002,
        // _003, ... up to 999. Existing .mat assets are claim-by-anyone (chunk content
        // is verified via sidecar registry above before we get here), so collision suffix
        // is only reached when a *different* chunk content wants the same readable name.
        static string ResolveCollisionFreeAssetName(string folder, string baseName)
        {
            string candidate = baseName;
            int suffix = 2;
            while (AssetDatabase.LoadAssetAtPath<Material>($"{folder}/{candidate}.mat") != null)
            {
                candidate = $"{baseName}_{suffix:D3}";
                suffix++;
                if (suffix > 999)
                    throw new System.InvalidOperationException(
                        $"[CgfMaterialBake] Collision suffix exhausted for '{baseName}' in '{folder}'.");
            }
            return candidate;
        }

        // Moves a baked material (and its sidecar) from a Levels/{lvl}/ folder up to the
        // shared folder when the bake has decided it is used by more than one level. No-op
        // if it is already shared, or not under a Levels/ folder, or targetFolder is not
        // the shared folder. GUIDs survive AssetDatabase.MoveAsset so manifest references
        // remain valid.
        static void PromoteToSharedIfNeeded(Material existing, string targetFolder)
        {
            if (existing == null || targetFolder != SharedMaterialFolder)
                return;

            string matPath = AssetDatabase.GetAssetPath(existing);
            if (string.IsNullOrEmpty(matPath))
                return;
            if (matPath.StartsWith(SharedMaterialFolder + "/", System.StringComparison.Ordinal))
                return;
            if (!matPath.StartsWith(LevelsMaterialFolder + "/", System.StringComparison.Ordinal))
                return;

            EnsureDirectory(SharedMaterialFolder);
            string fileName = Path.GetFileNameWithoutExtension(matPath);
            string finalName = ResolveCollisionFreeAssetName(SharedMaterialFolder, fileName);

            string sidecarPath = matPath.Substring(0, matPath.Length - ".mat".Length) + ".metadata.asset";
            string destMat = $"{SharedMaterialFolder}/{finalName}.mat";
            string destSidecar = $"{SharedMaterialFolder}/{finalName}.metadata.asset";

            if (AssetDatabase.LoadAssetAtPath<FcMaterialMetadata>(sidecarPath) != null)
                AssetDatabase.MoveAsset(sidecarPath, destSidecar);
            AssetDatabase.MoveAsset(matPath, destMat);
        }

        // Resolves the bake target folder for a CGF's default materials. A CGF whose
        // virtual path also appears in another level's manifest is multi-level → shared.
        // A CGF used only by the level being baked → that level's folder.
        static string ResolveDefaultTargetFolder(
            string cgfVirtualPath,
            string levelName,
            HashSet<string> cgfPathsUsedByOtherLevels)
        {
            if (cgfPathsUsedByOtherLevels == null || string.IsNullOrEmpty(levelName))
                return SharedMaterialFolder;

            string normalized = ImportAssetPaths.NormalizeVirtualPath(cgfVirtualPath);
            return cgfPathsUsedByOtherLevels.Contains(normalized)
                ? SharedMaterialFolder
                : $"{LevelsMaterialFolder}/{levelName}";
        }

        // Collects normalized CGF virtual paths referenced by every level manifest other
        // than the one being baked. Used to detect multi-level CGFs for routing.
        static HashSet<string> CollectCgfPathsFromOtherLevelManifests(string currentLevelName)
        {
            var result = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (!AssetDatabase.IsValidFolder(LevelsMaterialFolder))
                return result;

            string currentLevelFolder = $"{LevelsMaterialFolder}/{currentLevelName}/";
            var guids = AssetDatabase.FindAssets("t:FcMaterialManifest", new[] { LevelsMaterialFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path) ||
                    path.StartsWith(currentLevelFolder, System.StringComparison.Ordinal))
                    continue;

                var manifest = AssetDatabase.LoadAssetAtPath<Cgf.FcMaterialManifest>(path);
                if (manifest == null)
                    continue;

                var entries = manifest.Entries;
                for (int e = 0; e < entries.Count; e++)
                {
                    if (!string.IsNullOrEmpty(entries[e].VirtualPath))
                        result.Add(entries[e].VirtualPath);
                }
            }
            return result;
        }

        // MD5 of the chunk fields that CgfMaterialBuilder.Build (and the name-driven
        // classifier) consume. Texture names are included so two materials only dedup when
        // fully identical — conservative: never collapses visually distinct materials.
        static string ComputeContentHash(Cgf.CgfMaterialChunk chunk)
        {
            if (chunk == null)
                return "00000000";

            var sb = new StringBuilder(256);
            sb.Append(chunk.Name).Append('|')
              .Append(chunk.ShaderName).Append('|')
              .Append((int)chunk.MtlType).Append('|')
              .Append(chunk.ChunkVersion).Append('|')
              .Append(chunk.DiffuseColor.r).Append(',').Append(chunk.DiffuseColor.g).Append(',')
              .Append(chunk.DiffuseColor.b).Append(',').Append(chunk.DiffuseColor.a).Append('|')
              .Append(chunk.SpecularColor.r).Append(',').Append(chunk.SpecularColor.g).Append(',')
              .Append(chunk.SpecularColor.b).Append(',').Append(chunk.SpecularColor.a).Append('|')
              .Append(chunk.SpecLevel.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(chunk.SpecShininess.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(chunk.Opacity.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(chunk.AlphaTest.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append((int)chunk.Flags).Append('|')
              .Append(chunk.SelfIllum.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(chunk.DiffuseTextureName).Append('|')
              .Append(chunk.NormalTextureName).Append('|')
              .Append(chunk.SpecularTextureName).Append('|')
              .Append(chunk.OpacityTextureName).Append('|')
              .Append(chunk.GlossTextureName);

            using (var md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                    hex.Append(bytes[i].ToString("x2"));
                return hex.ToString();
            }
        }

        // Maps a Cry material name to a filesystem-safe file name: any character that is
        // not a letter, digit, '_', '-' or '.' becomes '_'. Falls back to "material" when
        // the name is empty or sanitizes to nothing (the hash suffix keeps it unique).
        static string SanitizeMaterialFileName(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName))
                return "material";

            var sb = new StringBuilder(materialName.Length);
            foreach (char c in materialName)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                    sb.Append(c);
                else
                    sb.Append('_');
            }

            string result = sb.ToString().Trim('_', '.', ' ');
            return string.IsNullOrEmpty(result) ? "material" : result;
        }

        const string SharedTextureFolder = SharedMaterialFolder + "/Textures";

        // Replaces non-persistent (runtime) materials on go's renderers with baked project assets.
        // Call in applyPostTransform before the prefab is saved.
        // Textures from the runtime material are persisted as shared .asset files and injected
        // into the baked .mat the FIRST time it is seen (subsequent CGFs share the same baked
        // asset by content hash and reuse the already-injected textures). Without this step the
        // baked materials are texture-less skeletons and cached prefabs render untextured after
        // domain reload, since the runtime Texture2D refs they would otherwise carry die when
        // the editor reloads.
        public static void PostProcessGameObjectMaterials(
            GameObject go,
            Cgf.CgfFile parsedFile,
            string cgfVirtualPath)
        {
            if (go == null || parsedFile?.MaterialChunks == null || string.IsNullOrWhiteSpace(cgfVirtualPath))
                return;

            CgfAssetCacheService.EnsureDirectory(SharedTextureFolder);
            var textureByKey = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);

            var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (int ri = 0; ri < renderers.Length; ri++)
            {
                var renderer = renderers[ri];
                if (renderer == null)
                    continue;

                var mats = renderer.sharedMaterials;
                if (mats == null || mats.Length == 0)
                    continue;

                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null || EditorUtility.IsPersistent(mat))
                        continue;
                    if (mat.name != null && mat.name.EndsWith("_fallback", System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    var chunk = FindChunkByName(parsedFile, mat.name);
                    if (chunk == null)
                        continue;

                    var baked = GetOrBakeMaterial(cgfVirtualPath, chunk);
                    if (baked == null)
                        continue;

                    InjectTexturesIntoBakedMaterial(baked, mat, textureByKey);

                    mats[i] = baked;
                    changed = true;
                }

                if (changed)
                    renderer.sharedMaterials = mats;
            }
        }

        // Walks every shader texture property on the runtime source mat, persists the
        // runtime Texture2D as a shared .asset, and writes the persistent ref onto the
        // baked .mat. Idempotent: if the baked .mat already has a texture in a slot we
        // do not overwrite (avoids stomping a previously-baked texture from a sibling
        // CGF that resolved earlier).
        static void InjectTexturesIntoBakedMaterial(
            Material baked,
            Material runtimeSource,
            Dictionary<string, Texture2D> textureByKey)
        {
            if (baked == null || runtimeSource == null)
                return;

            var shader = baked.shader;
            if (shader == null)
                return;

            bool changed = false;
            int propertyCount = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                    continue;

                string propertyName = ShaderUtil.GetPropertyName(shader, i);
                if (string.IsNullOrEmpty(propertyName))
                    continue;

                if (!runtimeSource.HasProperty(propertyName))
                    continue;

                var sourceTexture = runtimeSource.GetTexture(propertyName) as Texture2D;
                if (sourceTexture == null)
                    continue;

                var existingOnBaked = baked.GetTexture(propertyName);
                if (existingOnBaked != null && EditorUtility.IsPersistent(existingOnBaked))
                    continue;

                var persisted = CgfAssetCacheService.PersistTextureAsset(
                    sourceTexture,
                    propertyName,
                    SharedTextureFolder,
                    textureByKey);
                if (persisted == null)
                    continue;

                baked.SetTexture(propertyName, persisted);
                changed = true;
            }

            if (changed)
                EditorUtility.SetDirty(baked);
        }

        static Cgf.CgfMaterialChunk FindChunkByName(Cgf.CgfFile parsedFile, string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            var chunks = parsedFile.MaterialChunks;
            for (int i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                if (chunk != null && string.Equals(chunk.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    return chunk;
            }
            return null;
        }

        // Bakes all material chunks for the given CGF paths and creates/updates a level manifest SO.
        // Manifest path: Assets/Materials/Levels/{levelName}/manifest.asset
        public static Cgf.FcMaterialManifest BakeOrUpdateLevelManifest(
            string levelName,
            IEnumerable<string> cgfVirtualPaths)
        {
            string levelFolder = $"{LevelsMaterialFolder}/{levelName}";
            EnsureDirectory(levelFolder);

            string manifestPath = $"{levelFolder}/manifest.asset";
            var manifest = AssetDatabase.LoadAssetAtPath<Cgf.FcMaterialManifest>(manifestPath);
            if (manifest == null)
            {
                manifest = UnityEngine.ScriptableObject.CreateInstance<Cgf.FcMaterialManifest>();
                AssetDatabase.CreateAsset(manifest, manifestPath);
            }

            BeginBakeSession();
            try
            {
                // CGFs referenced by other levels' manifests → multi-level → Shared/.
                // CGFs used only by this level → Levels/{levelName}/.
                var otherLevelsCgfs = CollectCgfPathsFromOtherLevelManifests(levelName);

                // Sibling LOD CGFs (*_lodN.cgf) carry their own material chunk tables that do
                // NOT line up with the base CGF by TableIndex. Bake each LOD path explicitly so
                // the manifest resolves (lodPath, TableIndex) to that LOD's own material.
                var lodService = new Cgf.CgfLodImportService();
                var allPaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var virtualPath in cgfVirtualPaths)
                {
                    if (string.IsNullOrWhiteSpace(virtualPath))
                        continue;
                    allPaths.Add(virtualPath);
                    foreach (var lodPath in lodService.FindSiblingLodPaths(virtualPath))
                        allPaths.Add(lodPath);
                }

                var entries = new List<Cgf.FcMaterialManifest.Entry>();
                foreach (var virtualPath in allPaths)
                {
                    if (string.IsNullOrWhiteSpace(virtualPath))
                        continue;

                    Cgf.CgfFile parsedFile;
                    try
                    {
                        var bytes = Cgf.CgfResourceImportService.Instance.LoadRuntimeResourceBytes(virtualPath);
                        parsedFile = Cgf.CgfParser.Parse(bytes);
                        parsedFile.SourceVirtualPath = virtualPath;
                    }
                    catch
                    {
                        continue;
                    }

                    if (parsedFile.MaterialChunks == null)
                        continue;

                    string targetFolder = ResolveDefaultTargetFolder(
                        virtualPath, levelName, otherLevelsCgfs);

                    for (int c = 0; c < parsedFile.MaterialChunks.Count; c++)
                    {
                        var chunk = parsedFile.MaterialChunks[c];
                        if (chunk == null || chunk.MtlType == Cgf.CgfMtlType.Multi)
                            continue;

                        var mat = GetOrBakeMaterialInternal(
                            virtualPath, chunk, overrideName: null,
                            targetFolder: targetFolder, out _);
                        if (mat == null)
                            continue;

                        entries.Add(new Cgf.FcMaterialManifest.Entry
                        {
                            VirtualPath = ImportAssetPaths.NormalizeVirtualPath(virtualPath),
                            TableIndex = chunk.TableIndex,
                            OverrideName = string.Empty,
                            ChunkName = chunk.Name ?? string.Empty,
                            Material = mat,
                        });
                    }
                }

                manifest.SetEntries(entries.ToArray());
                EditorUtility.SetDirty(manifest);
                AssetDatabase.SaveAssets();
                return manifest;
            }
            finally
            {
                EndBakeSession();
            }
        }

        // Override bake request: a (CGF, overrideName) pair sourced from brush.lst,
        // entity XML "Material" attribute or vegetation type Material. The bake produces
        // one override .mat per non-Multi leaf chunk in the CGF, so a renderer slot built
        // from any chunk of that CGF can resolve its level-specific override material.
        public readonly struct OverrideBakeRequest
        {
            public readonly string CgfVirtualPath;
            public readonly string OverrideName;

            public OverrideBakeRequest(string cgfVirtualPath, string overrideName)
            {
                CgfVirtualPath = cgfVirtualPath;
                OverrideName = overrideName;
            }
        }

        // Bakes level-specific override materials into Assets/Materials/Levels/{levelName}/Overrides
        // and appends manifest entries keyed by (vpath, tableIndex, overrideName). The override
        // .mat is a clone of the base chunk material — the runtime FcLevelMaterialOverrideService
        // still injects level textures on top, so the asset only persists editable shader state.
        // Appends to an existing manifest (call after BakeOrUpdateLevelManifest).
        public static void BakeLevelOverrideMaterials(
            string levelName,
            Cgf.FcMaterialManifest manifest,
            IEnumerable<OverrideBakeRequest> requests)
        {
            if (manifest == null || requests == null)
                return;

            string overridesFolder = $"{LevelsMaterialFolder}/{levelName}/Overrides";

            BeginBakeSession();
            try
            {
                // Dedup (cgfPath, overrideName) — many brushes share a CGF + override.
                var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                var appended = new List<Cgf.FcMaterialManifest.Entry>(manifest.Entries);

                foreach (var request in requests)
                {
                    if (string.IsNullOrWhiteSpace(request.CgfVirtualPath) ||
                        string.IsNullOrWhiteSpace(request.OverrideName))
                        continue;

                    string dedupKey = request.CgfVirtualPath + "|" + request.OverrideName;
                    if (!seen.Add(dedupKey))
                        continue;

                    Cgf.CgfFile parsedFile;
                    try
                    {
                        var bytes = Cgf.CgfResourceImportService.Instance
                            .LoadRuntimeResourceBytes(request.CgfVirtualPath);
                        parsedFile = Cgf.CgfParser.Parse(bytes);
                        parsedFile.SourceVirtualPath = request.CgfVirtualPath;
                    }
                    catch
                    {
                        continue;
                    }

                    if (parsedFile.MaterialChunks == null)
                        continue;

                    for (int c = 0; c < parsedFile.MaterialChunks.Count; c++)
                    {
                        var chunk = parsedFile.MaterialChunks[c];
                        if (chunk == null || chunk.MtlType == Cgf.CgfMtlType.Multi)
                            continue;

                        var mat = GetOrBakeMaterialInternal(
                            request.CgfVirtualPath,
                            chunk,
                            overrideName: request.OverrideName,
                            targetFolder: overridesFolder,
                            out _);
                        if (mat == null)
                            continue;

                        appended.Add(new Cgf.FcMaterialManifest.Entry
                        {
                            VirtualPath = ImportAssetPaths.NormalizeVirtualPath(request.CgfVirtualPath),
                            TableIndex = chunk.TableIndex,
                            OverrideName = request.OverrideName,
                            ChunkName = chunk.Name ?? string.Empty,
                            Material = mat,
                        });
                    }
                }

                manifest.SetEntries(appended.ToArray());
                EditorUtility.SetDirty(manifest);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                EndBakeSession();
            }
        }

        // Returns the manifest asset path for a level (may not exist yet).
        public static string GetLevelManifestPath(string levelName)
            => $"{LevelsMaterialFolder}/{levelName}/manifest.asset";

        // Legacy manifest path under Assets/FCData/Materials/. Kept for migration: the
        // editor scan still loads these so existing levels keep rendering until the user
        // re-bakes them into the new Assets/Materials/Levels/{levelName}/ layout.
        public static string GetLegacyLevelManifestPath(string levelName)
            => $"{LegacyMaterialCacheRoot}/{levelName}_manifest.asset";

        // Search roots for the editor manifest scan. Includes the legacy FCData root so
        // levels that have not yet been re-baked continue to resolve materials.
        public static string[] GetManifestSearchRoots()
        {
            var roots = new List<string>(2);
            if (AssetDatabase.IsValidFolder(MaterialAssetRoot))
                roots.Add(MaterialAssetRoot);
            if (AssetDatabase.IsValidFolder(LegacyMaterialCacheRoot))
                roots.Add(LegacyMaterialCacheRoot);
            return roots.ToArray();
        }

        static void EnsureDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                return;

            string[] parts = path.Replace('\\', '/').Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
