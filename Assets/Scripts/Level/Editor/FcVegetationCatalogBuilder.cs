using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenFarCry.Importer.Cgf;
using OpenFarCry.Importer.Texture;
using OpenFarCry.Level.Data;
using OpenFarCry.Level.Services;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenFarCry.Level.Editor
{
    // Builds a single scene containing every distinct vegetation asset Far Cry ships
    // across all levels: one GameObject per unique CGF path, laid out left-to-right
    // with a 1-unit gap between bounding boxes. Textures persist to a temp folder so
    // the saved scene reopens without losing texture references. Materials reuse the
    // standard runtime material cache.
    public static class FcVegetationCatalogBuilder
    {
        const string MenuPath = "OpenFarCry/Tools/Build Vegetation Catalog Scene";
        const string OutputScenePath = "Assets/Scenes/VegetationCatalog.unity";
        const string TempTextureDir = "Assets/FCData/Temp/Vegetation/Textures";
        const string TempMaterialDir = "Assets/FCData/Temp/Vegetation/Materials";
        const string TempMeshDir = "Assets/FCData/Temp/Vegetation/Meshes";
        const string ScopeId = "VegetationCatalog";
        const float GapBetweenInstances = 1f;
        const float VegetationImportScale = 0.01f;

        [MenuItem(MenuPath)]
        public static void BuildCatalogScene()
        {
            if (!EditorUtility.DisplayDialog(
                    "Build Vegetation Catalog",
                    "Scan all levels, instantiate every unique vegetation asset into a new scene at " +
                    OutputScenePath + ". The currently open scene will be closed (with a save prompt " +
                    "for any unsaved changes) and " + OutputScenePath + " will be overwritten if it " +
                    "already exists. Continue?",
                    "Build", "Cancel"))
                return;

            // NewScene(Single) below discards whatever scene is currently open; give the
            // user a chance to save or cancel before that happens.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            try
            {
                BuildInternal();
            }
            catch (Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogError($"[VegetationCatalog] Build failed: {e}");
                EditorUtility.DisplayDialog("Vegetation Catalog", "Build failed: " + e.Message, "OK");
            }
        }

        static void BuildInternal()
        {
            EditorUtility.DisplayProgressBar("Vegetation Catalog", "Scanning levels...", 0f);

            var levelNames = FcLevelLoader.ListLevelNames();
            if (levelNames == null || levelNames.Count == 0)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Vegetation Catalog", "No levels found. Check FC install path.", "OK");
                return;
            }

            var unique = CollectUniqueVegetation(levelNames);
            if (unique.Count == 0)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Vegetation Catalog", "No vegetation types found across any level.", "OK");
                return;
            }

            EnsureDirectoryExists(TempTextureDir);
            EnsureDirectoryExists(TempMaterialDir);
            EnsureDirectoryExists(TempMeshDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SceneManager.SetActiveScene(scene);

            var rootGo = new GameObject("VegetationCatalog");
            var overrideHost = new GameObject("_VegetationCatalogOverrideHost");
            overrideHost.hideFlags = HideFlags.HideAndDontSave;
            var overrideServices = new Dictionary<string, FcLevelMaterialOverrideService>(StringComparer.OrdinalIgnoreCase);
            var lodService = new CgfLodImportService();
            var goBuilder = new CgfGameObjectBuilder();
            var materialService = CgfRuntimeImporter.MaterialService;

            float cursorX = 0f;
            int placed = 0;
            int failed = 0;
            int textureImports = 0;
            int textureSkipped = 0;
            int materialImports = 0;
            int materialSkipped = 0;
            var materialPersistCache = new HashSet<int>();
            int meshImports = 0;
            int meshSkipped = 0;
            var meshPersistCache = new HashSet<int>();

            try
            {
                var sorted = unique.Values.OrderBy(v => v.FileName, StringComparer.OrdinalIgnoreCase).ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    var entry = sorted[i];
                    float progress = (float)i / sorted.Count;
                    EditorUtility.DisplayProgressBar(
                        "Vegetation Catalog",
                        $"[{i + 1}/{sorted.Count}] {entry.FileName}",
                        progress);

                    try
                    {
                        var overrideSvc = GetOrCreateOverrideService(overrideServices, entry.SourceLevel, overrideHost);
                        var built = BuildOneEntry(entry, materialService, lodService, goBuilder, overrideSvc);
                        if (built == null)
                        {
                            failed++;
                            continue;
                        }

                        PersistRuntimeTextures(built, ref textureImports, ref textureSkipped);
                        PersistMaterials(built, materialPersistCache, ref materialImports, ref materialSkipped);
                        PersistMeshes(built, meshPersistCache, ref meshImports, ref meshSkipped);

                        built.transform.SetParent(rootGo.transform, worldPositionStays: false);
                        built.transform.localPosition = Vector3.zero;
                        var bounds = ComputeWorldBounds(built);
                        float halfWidth = Mathf.Max(bounds.extents.x, 0.1f);
                        float centerOffsetX = bounds.center.x - built.transform.position.x;
                        built.transform.localPosition = new Vector3(cursorX + halfWidth - centerOffsetX, 0f, 0f);

                        cursorX += halfWidth * 2f + GapBetweenInstances;
                        placed++;
                    }
                    catch (Exception e)
                    {
                        failed++;
                        Debug.LogWarning($"[VegetationCatalog] '{entry.FileName}' failed: {e.Message}");
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                foreach (var svc in overrideServices.Values)
                {
                    if (svc != null && svc.gameObject != null)
                        UnityEngine.Object.DestroyImmediate(svc.gameObject);
                }
                overrideServices.Clear();
                if (overrideHost != null)
                {
                    UnityEngine.Object.DestroyImmediate(overrideHost);
                    overrideHost = null;
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EnsureDirectoryExists(Path.GetDirectoryName(OutputScenePath));
                EditorSceneManager.SaveScene(scene, OutputScenePath);
            }
            finally
            {
                if (overrideHost != null)
                    UnityEngine.Object.DestroyImmediate(overrideHost);
                ClearRuntimeCaches();
                EditorUtility.ClearProgressBar();
            }

            string summary =
                $"Vegetation catalog built.\n" +
                $"Scene: {OutputScenePath}\n" +
                $"Levels scanned: {levelNames.Count}\n" +
                $"Unique vegetation: {unique.Count}\n" +
                $"Placed: {placed}\n" +
                $"Failed: {failed}\n" +
                $"Textures imported: {textureImports}\n" +
                $"Textures skipped (already present): {textureSkipped}\n" +
                $"Materials saved: {materialImports}\n" +
                $"Materials skipped (already assets): {materialSkipped}\n" +
                $"Meshes saved: {meshImports}\n" +
                $"Meshes skipped (already assets): {meshSkipped}";
            Debug.Log("[VegetationCatalog] " + summary);
            EditorUtility.DisplayDialog("Vegetation Catalog", summary, "OK");
        }

        sealed class UniqueEntry
        {
            public string FileName;
            public string MaterialOverride;
            public string SourceLevel;
        }

        static Dictionary<string, UniqueEntry> CollectUniqueVegetation(IReadOnlyList<string> levelNames)
        {
            var unique = new Dictionary<string, UniqueEntry>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < levelNames.Count; i++)
            {
                string levelName = levelNames[i];
                EditorUtility.DisplayProgressBar(
                    "Vegetation Catalog",
                    $"Scanning level {i + 1}/{levelNames.Count}: {levelName}",
                    (float)i / levelNames.Count);

                FcLevelSupplementData supplement;
                try
                {
                    supplement = FcLevelSupplementLoader.Load(levelName);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VegetationCatalog] Skip level '{levelName}': {e.Message}");
                    continue;
                }

                if (supplement?.VegetationTypes == null) continue;
                foreach (var v in supplement.VegetationTypes)
                {
                    if (string.IsNullOrWhiteSpace(v.FileName)) continue;
                    string key = v.FileName.Trim().ToLowerInvariant();
                    if (unique.ContainsKey(key)) continue;
                    unique.Add(key, new UniqueEntry
                    {
                        FileName = v.FileName.Trim(),
                        MaterialOverride = v.Material,
                        SourceLevel = levelName,
                    });
                }
            }
            return unique;
        }

        static FcLevelMaterialOverrideService GetOrCreateOverrideService(
            Dictionary<string, FcLevelMaterialOverrideService> cache,
            string levelName,
            GameObject parent)
        {
            if (string.IsNullOrEmpty(levelName)) return null;
            if (cache.TryGetValue(levelName, out var svc) && svc != null) return svc;

            FcLevelSupplementData supplement;
            try
            {
                supplement = FcLevelSupplementLoader.Load(levelName);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VegetationCatalog] Override supplement for '{levelName}' failed: {e.Message}");
                return null;
            }

            var go = new GameObject($"_OverrideService_{levelName}");
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.hideFlags = HideFlags.HideAndDontSave;
            svc = go.AddComponent<FcLevelMaterialOverrideService>();
            svc.Configure(levelName, supplement);
            cache[levelName] = svc;
            return svc;
        }

        static GameObject BuildOneEntry(
            UniqueEntry entry,
            CgfMaterialImportService materialService,
            CgfLodImportService lodService,
            CgfGameObjectBuilder goBuilder,
            FcLevelMaterialOverrideService overrideSvc)
        {
            var request = new CgfRuntimeImportRequest(
                virtualPath: entry.FileName,
                importSkeleton: false,
                importAnimations: false,
                importScale: VegetationImportScale,
                useRuntimeMemoryCache: true);
            var result = CgfRuntimeImporter.Service.Import(request, ScopeId);
            if (result == null || !result.Success || result.BuildResult == null || result.BuildResult.Mesh == null)
            {
                Debug.LogWarning($"[VegetationCatalog] Import failed '{entry.FileName}': {result?.ErrorMessage ?? "no mesh"}");
                return null;
            }

            string overrideName = entry.MaterialOverride?.Trim();
            Func<Material[], int[], Material[]> overrider = null;
            if (!string.IsNullOrEmpty(overrideName) && overrideSvc != null)
            {
                overrider = (mats, ids) =>
                {
                    overrideSvc.TryApplyBrushOverrideToRendererSlots(
                        overrideName, requestedMaterialId: -1, ScopeId, mats, ids);
                    return mats;
                };
            }

            string displayName = Path.GetFileNameWithoutExtension(entry.FileName);
            var buildRequest = new CgfGameObjectBuilder.BuildRequest(
                result: result.BuildResult,
                parsedFile: result.ParsedFile,
                rigDefinition: null,
                name: $"{displayName} [{entry.SourceLevel}]",
                materialService: materialService,
                textureScopeId: ScopeId,
                materialOverrider: overrider);
            var output = goBuilder.Build(buildRequest);
            if (output.Root == null) return null;

            var siblingLodPaths = lodService.FindSiblingLodPaths(entry.FileName);
            lodService.ConfigureLodGroup(
                output.Root,
                hasSkeleton: false,
                importScale: VegetationImportScale,
                siblingLodPaths: siblingLodPaths,
                persistMesh: null,
                materialService: materialService,
                textureScopeId: ScopeId,
                materialOverrider: overrider);

            return output.Root;
        }

        static void PersistRuntimeTextures(GameObject root, ref int imported, ref int skipped)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            var cache = new Dictionary<int, Texture2D>();

            foreach (var renderer in renderers)
            {
                var mats = renderer.sharedMaterials;
                if (mats == null) continue;
                foreach (var mat in mats)
                {
                    if (mat == null || mat.shader == null) continue;
                    int propCount = ShaderUtil.GetPropertyCount(mat.shader);
                    for (int i = 0; i < propCount; i++)
                    {
                        if (ShaderUtil.GetPropertyType(mat.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                            continue;
                        string propName = ShaderUtil.GetPropertyName(mat.shader, i);
                        var rawTex = mat.GetTexture(propName);
                        if (rawTex == null) continue;
                        if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(rawTex))) continue;
                        var tex = rawTex as Texture2D;
                        if (tex == null)
                        {
                            Debug.LogWarning(
                                $"[VegetationCatalog] Skip non-Texture2D '{rawTex.name}' " +
                                $"({rawTex.GetType().Name}) on '{mat.name}.{propName}'.");
                            continue;
                        }

                        bool isNormal = LooksLikeNormalMap(tex.name, propName);
                        int key = tex.GetInstanceID();
                        if (!cache.TryGetValue(key, out var assetTex))
                        {
                            assetTex = PersistAsPng(tex, mat.name, propName, isNormal, ref imported, ref skipped);
                            cache[key] = assetTex;
                        }

                        if (assetTex != null)
                            mat.SetTexture(propName, assetTex);
                    }
                }
            }
        }

        // Persists each runtime Mesh referenced by the entry's renderers as a .asset.
        // AssetDatabase.CreateAsset persists the in-memory Mesh; Renderer/MeshFilter
        // references stay valid by InstanceID, no slot reassignment needed. Same as
        // material persistence — order doesn't matter relative to materials, but doing
        // last keeps the save sequence: textures → materials → meshes.
        static void PersistMeshes(GameObject root, HashSet<int> seen, ref int imported, ref int skipped)
        {
            var meshFilters = root.GetComponentsInChildren<MeshFilter>(includeInactive: true);
            foreach (var mf in meshFilters)
            {
                var mesh = mf.sharedMesh;
                if (mesh != null)
                    PersistOneMesh(mesh, seen, ref imported, ref skipped);
            }
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
            foreach (var smr in smrs)
            {
                var mesh = smr.sharedMesh;
                if (mesh != null)
                    PersistOneMesh(mesh, seen, ref imported, ref skipped);
            }
        }

        static void PersistOneMesh(Mesh mesh, HashSet<int> seen, ref int imported, ref int skipped)
        {
            if (!seen.Add(mesh.GetInstanceID())) return;
            if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mesh)))
            {
                skipped++;
                return;
            }
            string baseName = SanitizeAssetName(mesh.name);
            if (string.IsNullOrEmpty(baseName))
                baseName = "mesh";
            string path = AssetDatabase.GenerateUniqueAssetPath(TempMeshDir + "/" + baseName + ".asset");
            AssetDatabase.CreateAsset(mesh, path);
            imported++;
        }

        // Releases all runtime CGF / material / texture caches held by the shared
        // CgfRuntimeImporter. Without this, the next catalog rebuild (or other tool that
        // reads from the same VFS) would receive cached Material/Mesh/Texture2D instances
        // whose underlying assets the user may have deleted in between — resulting in
        // dangling refs and stale slots that look like "everything already exists".
        // Disposes Persistent NativeArrays so they don't leak across domain reload.
        static void ClearRuntimeCaches()
        {
            try
            {
                CgfRuntimeImporter.DisposeParsedNativeData();
                CgfRuntimeImporter.ClearRuntimeCache();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VegetationCatalog] Runtime cache cleanup threw: {e.Message}");
            }
        }

        // Persists every runtime-instance Material referenced by the entry's renderers as
        // a .mat asset. Call AFTER PersistRuntimeTextures so the saved .mat references
        // persistent texture assets, not runtime instances that die on scene reopen.
        // AssetDatabase.CreateAsset converts the in-memory Material into an asset; existing
        // Renderer references stay valid by InstanceID, so no slot reassignment is needed.
        static void PersistMaterials(GameObject root, HashSet<int> seen, ref int imported, ref int skipped)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (var renderer in renderers)
            {
                var mats = renderer.sharedMaterials;
                if (mats == null) continue;
                foreach (var mat in mats)
                {
                    if (mat == null) continue;
                    if (!seen.Add(mat.GetInstanceID())) continue;
                    if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(mat)))
                    {
                        skipped++;
                        continue;
                    }

                    string baseName = SanitizeAssetName(mat.name);
                    if (string.IsNullOrEmpty(baseName))
                        baseName = "material";
                    string path = AssetDatabase.GenerateUniqueAssetPath(TempMaterialDir + "/" + baseName + ".mat");
                    AssetDatabase.CreateAsset(mat, path);
                    imported++;
                }
            }
        }

        static string SanitizeAssetName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString().Trim().TrimEnd('.');
        }

        // Persists a runtime Texture2D as a PNG asset via Graphics.Blit → ReadPixels →
        // EncodeToPNG. Blit handles compressed formats (BC1/BC3/BC7 etc. that the runtime
        // DDS decoder produces) and non-readable flag transparently — direct EncodeToPNG
        // would throw "Unsupported texture format" on compressed sources. PNG round-trip
        // (encode → Unity import) is symmetric, so the asset matches runtime.
        static Texture2D PersistAsPng(
            Texture2D source,
            string matName,
            string propName,
            bool isNormal,
            ref int imported,
            ref int skipped)
        {
            string vpath = source.name;
            string baseName;
            if (!string.IsNullOrEmpty(vpath) && vpath.IndexOf('.') >= 0 &&
                TextureImportService.IsSupportedVirtualPath(vpath))
            {
                baseName = SanitizeAssetName(Path.GetFileNameWithoutExtension(vpath));
            }
            else
            {
                string fallback = !string.IsNullOrEmpty(vpath) ? vpath : $"{matName}_{propName}";
                baseName = SanitizeAssetName(fallback);
            }
            if (string.IsNullOrEmpty(baseName))
                baseName = $"tex_{source.GetInstanceID():X}";

            string assetPath = TempTextureDir + "/" + baseName + ".png";
            string fullPath = Path.GetFullPath(assetPath);

            if (File.Exists(fullPath))
            {
                var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (existing != null)
                {
                    skipped++;
                    return existing;
                }
            }

            var readable = MakeReadableCopyViaBlit(source, isNormal);
            if (readable == null)
            {
                Debug.LogWarning(
                    $"[VegetationCatalog] Blit copy failed for '{vpath}' on '{matName}.{propName}' " +
                    $"(format={source.format}, {source.width}x{source.height}).");
                return null;
            }

            try
            {
                byte[] png = readable.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    Debug.LogWarning(
                        $"[VegetationCatalog] EncodeToPNG returned no data for '{vpath}' " +
                        $"on '{matName}.{propName}' (readableFormat={readable.format}).");
                    return null;
                }

                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(fullPath, png);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(readable);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            ApplyTextureImporterSettings(assetPath, isNormal);
            imported++;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        // Forces a readable RGBA32 copy of any Texture2D regardless of source format /
        // readable flag. Used as fallback when decoding from VFS bytes is not possible
        // (derived textures, missing source).
        static Texture2D MakeReadableCopyViaBlit(Texture2D source, bool linear)
        {
            if (source == null || source.width <= 0 || source.height <= 0)
                return null;

            var rwMode = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
            var rt = RenderTexture.GetTemporary(
                source.width, source.height, 0, RenderTextureFormat.ARGB32, rwMode);
            var prevActive = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, linear);
                copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                copy.Apply();
                return copy;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VegetationCatalog] Blit copy threw: {e.Message}");
                return null;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        static void ApplyTextureImporterSettings(string assetPath, bool isNormal)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            bool changed = false;
            var targetType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != targetType)
            {
                importer.textureType = targetType;
                changed = true;
            }
            bool targetSrgb = !isNormal;
            if (importer.sRGBTexture != targetSrgb)
            {
                importer.sRGBTexture = targetSrgb;
                changed = true;
            }
            if (importer.isReadable)
            {
                importer.isReadable = false;
                changed = true;
            }
            if (changed)
                importer.SaveAndReimport();
        }

        static bool LooksLikeNormalMap(string vpath, string propName)
        {
            string p = propName ?? string.Empty;
            if (p == "_BumpMap" || p == "_NormalMap" || p == "_DetailNormalMap")
                return true;
            string lower = (vpath ?? string.Empty).ToLowerInvariant();
            return lower.Contains("_ddn") || lower.Contains("_ddna") || lower.Contains("_bump") ||
                   lower.EndsWith("_normal.dds", StringComparison.Ordinal) ||
                   lower.EndsWith("_normal.tga", StringComparison.Ordinal) ||
                   lower.EndsWith("_normal.bmp", StringComparison.Ordinal);
        }

        static Bounds ComputeWorldBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.one);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        static void EnsureDirectoryExists(string assetDir)
        {
            if (string.IsNullOrEmpty(assetDir)) return;
            string full = Path.GetFullPath(assetDir);
            if (!Directory.Exists(full))
            {
                Directory.CreateDirectory(full);
                AssetDatabase.Refresh();
            }
        }
    }
}
