using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenFarCry.Importer.Editor
{
    // Bakes CgfMaterialChunk → persistent Unity .mat project assets (no textures embedded).
    // Path: Assets/FCData/Materials/{cgf_virtual_path_without_ext}/{chunk.TableIndex}.mat
    // Runtime injects textures later via CgfMaterialBuilder.ApplyResolvedTextures.
    public static class CgfMaterialEditorBakeService
    {
        const string MaterialCacheRoot = "Assets/FCData/Materials";

        public static Material GetOrBakeMaterial(string cgfVirtualPath, Cgf.CgfMaterialChunk chunk)
        {
            if (chunk == null || string.IsNullOrWhiteSpace(cgfVirtualPath))
                return null;

            string assetPath = GetBakedMaterialPath(cgfVirtualPath, chunk);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null)
                return existing;

            EnsureDirectory(Path.GetDirectoryName(assetPath));

            var mat = Cgf.CgfMaterialBuilder.Build(chunk);
            if (mat == null)
                return null;

            AssetDatabase.CreateAsset(mat, assetPath);
            return AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        }

        // Baked .mat path: Assets/FCData/Materials/{cgf_path}/{sanitized_material_name}.mat.
        // The file name keeps the original Cry material name (e.g. "wall(TemplBumpDiffuse)/mat_x"
        // -> "wall_TemplBumpDiffuse__mat_x") for readability. The manifest keys on
        // (virtualPath, TableIndex), so the file name only needs folder-local uniqueness.
        public static string GetBakedMaterialPath(string cgfVirtualPath, Cgf.CgfMaterialChunk chunk)
        {
            string normalized = ImportAssetPaths.NormalizeVirtualPath(cgfVirtualPath);
            int dotIdx = normalized.LastIndexOf('.');
            string withoutExt = dotIdx > 0 ? normalized.Substring(0, dotIdx) : normalized;
            string fileName = SanitizeMaterialFileName(chunk?.Name, chunk?.TableIndex ?? 0);
            return $"{MaterialCacheRoot}/{withoutExt}/{fileName}.mat";
        }

        // Maps a Cry material name to a filesystem-safe file name: any character that is
        // not a letter, digit, '_', '-' or '.' becomes '_'. Falls back to material_{index}
        // when the name is empty or sanitizes to nothing.
        static string SanitizeMaterialFileName(string materialName, int tableIndex)
        {
            if (string.IsNullOrWhiteSpace(materialName))
                return $"material_{tableIndex}";

            var sb = new System.Text.StringBuilder(materialName.Length);
            foreach (char c in materialName)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                    sb.Append(c);
                else
                    sb.Append('_');
            }

            string result = sb.ToString().Trim('_', '.', ' ');
            return string.IsNullOrEmpty(result) ? $"material_{tableIndex}" : result;
        }

        // Replaces non-persistent (runtime) materials on go's renderers with baked project assets.
        // Call in applyPostTransform before the prefab is saved.
        public static void PostProcessGameObjectMaterials(
            GameObject go,
            Cgf.CgfFile parsedFile,
            string cgfVirtualPath)
        {
            if (go == null || parsedFile?.MaterialChunks == null || string.IsNullOrWhiteSpace(cgfVirtualPath))
                return;

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
                    if (baked != null)
                    {
                        mats[i] = baked;
                        changed = true;
                    }
                }

                if (changed)
                    renderer.sharedMaterials = mats;
            }
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
        // Manifest path: Assets/FCData/Materials/{levelName}_manifest.asset
        public static Cgf.FcMaterialManifest BakeOrUpdateLevelManifest(
            string levelName,
            IEnumerable<string> cgfVirtualPaths)
        {
            EnsureDirectory(MaterialCacheRoot);

            string manifestPath = $"{MaterialCacheRoot}/{levelName}_manifest.asset";
            var manifest = AssetDatabase.LoadAssetAtPath<Cgf.FcMaterialManifest>(manifestPath);
            if (manifest == null)
            {
                manifest = UnityEngine.ScriptableObject.CreateInstance<Cgf.FcMaterialManifest>();
                AssetDatabase.CreateAsset(manifest, manifestPath);
            }

            var entries = new List<Cgf.FcMaterialManifest.Entry>();
            foreach (var virtualPath in cgfVirtualPaths)
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

                for (int c = 0; c < parsedFile.MaterialChunks.Count; c++)
                {
                    var chunk = parsedFile.MaterialChunks[c];
                    if (chunk == null || chunk.MtlType == Cgf.CgfMtlType.Multi)
                        continue;

                    var mat = GetOrBakeMaterial(virtualPath, chunk);
                    if (mat == null)
                        continue;

                    entries.Add(new Cgf.FcMaterialManifest.Entry
                    {
                        VirtualPath = ImportAssetPaths.NormalizeVirtualPath(virtualPath),
                        TableIndex = chunk.TableIndex,
                        Material = mat,
                    });
                }
            }

            manifest.SetEntries(entries.ToArray());
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssets();
            return manifest;
        }

        // Returns the manifest asset path for a level (may not exist yet).
        public static string GetLevelManifestPath(string levelName)
            => $"{MaterialCacheRoot}/{levelName}_manifest.asset";

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
