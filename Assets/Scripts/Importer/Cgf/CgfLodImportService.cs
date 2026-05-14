using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
    public sealed class CgfLodImportService
    {
        static readonly Regex s_extractLodIndex = new Regex(
            "_lod(\\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        static readonly Regex s_stripLodSuffix = new Regex(
            "^(.*)_lod\\d+$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        static readonly Dictionary<string, Regex> s_siblingRegexCache =
            new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase);

        static Regex GetSiblingLodRegex(string baseName)
        {
            if (!s_siblingRegexCache.TryGetValue(baseName, out var rx))
                s_siblingRegexCache[baseName] = rx = new Regex(
                    $"^{Regex.Escape(baseName)}_lod(\\d+)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            return rx;
        }

        public List<string> FindSiblingLodPaths(string modelVirtualPath)
        {
            string noExt = RemoveExtension(modelVirtualPath).Replace('\\', '/');
            string dir = GetVirtualDirectory(noExt);
            return FindSiblingLodPaths(modelVirtualPath, OpenFarCry.FileSystem.FcFileSystem.GetEntries(dir));
        }

        public List<string> FindSiblingLodPaths(string modelVirtualPath, IEnumerable<string> dirEntries)
        {
            var result = new List<(int lod, string path)>();
            string noExt = RemoveExtension(modelVirtualPath).Replace('\\', '/');

            string fileNoExt = Path.GetFileName(noExt);
            string baseName = StripLodSuffix(fileNoExt);
            if (string.IsNullOrEmpty(baseName))
                baseName = fileNoExt;

            var regex = GetSiblingLodRegex(baseName);

            foreach (var path in dirEntries)
            {
                if (!CgfResourceImportService.Instance.IsSupportedVirtualPath(path))
                    continue;
                if (string.Equals(path, modelVirtualPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                string candidateNoExt = RemoveExtension(path);
                string candidateName = Path.GetFileName(candidateNoExt);
                var m = regex.Match(candidateName);
                if (!m.Success)
                    continue;

                if (!int.TryParse(m.Groups[1].Value, out int lodIndex))
                    continue;
                if (lodIndex < 1)
                    continue;

                result.Add((lodIndex, path));
            }

            return result
                .OrderBy(x => x.lod)
                .ThenBy(x => x.path, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.path)
                .ToList();
        }

        public void ConfigureLodGroup(
            GameObject root,
            bool hasSkeleton,
            float importScale,
            IReadOnlyList<string> siblingLodPaths,
            Func<Mesh, string, Mesh> persistMesh = null,
            CgfMaterialImportService materialService = null,
            string textureScopeId = null)
        {
            if (root == null)
                return;

            var lodRenderers = new List<Renderer>();
            if (hasSkeleton)
            {
                var smr0 = root.GetComponent<SkinnedMeshRenderer>();
                if (smr0 == null)
                    return;
                lodRenderers.Add(smr0);
            }
            else
            {
                var mr0 = root.GetComponent<MeshRenderer>();
                if (mr0 == null)
                    return;
                lodRenderers.Add(mr0);
            }

            if (siblingLodPaths != null)
            {
                for (int i = 0; i < siblingLodPaths.Count; i++)
                {
                    string lodPath = siblingLodPaths[i];
                    try
                    {
                        byte[] bytes = CgfResourceImportService.Instance.LoadRuntimeResourceBytes(lodPath);
                        var parsedLod = CgfParser.Parse(bytes);
                        parsedLod.SourceVirtualPath = lodPath;
                        var buildLod = CgfMeshBuilder.Build(parsedLod, hasSkeleton, importScale);
                        if (buildLod?.Mesh == null)
                            continue;

                        Mesh lodMesh = buildLod.Mesh;
                        if (persistMesh != null)
                            lodMesh = persistMesh(lodMesh, lodPath);
                        if (lodMesh == null)
                            continue;

                        int lodIndex = ExtractLodIndexFromPath(lodPath);
                        string childName = lodIndex > 0 ? $"LOD{lodIndex}" : $"LOD{lodRenderers.Count}";
                        var lodGo = new GameObject(childName);
                        lodGo.transform.SetParent(root.transform, worldPositionStays: false);

                        if (hasSkeleton)
                        {
                            var baseSmr = (SkinnedMeshRenderer)lodRenderers[0];
                            var lodSmr = lodGo.AddComponent<SkinnedMeshRenderer>();
                            lodSmr.sharedMesh = lodMesh;
                            lodSmr.bones = baseSmr.bones;
                            lodSmr.rootBone = baseSmr.rootBone;
                            lodSmr.sharedMaterials = materialService != null
                                ? materialService.ResolveSubmeshMaterials(parsedLod, lodMesh, buildLod.SubmeshMaterialIds, textureScopeId)
                                : new Material[lodMesh.subMeshCount];
                            lodRenderers.Add(lodSmr);
                        }
                        else
                        {
                            var mf = lodGo.AddComponent<MeshFilter>();
                            mf.sharedMesh = lodMesh;
                            var mr = lodGo.AddComponent<MeshRenderer>();
                            mr.sharedMaterials = materialService != null
                                ? materialService.ResolveSubmeshMaterials(parsedLod, lodMesh, buildLod.SubmeshMaterialIds, textureScopeId)
                                : new Material[lodMesh.subMeshCount];
                            lodRenderers.Add(mr);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[CgfImporter] Failed to build LOD from '{lodPath}': {e.Message}");
                    }
                }
            }

            var lods = BuildLodSettings(lodRenderers);
            if (lods.Length == 0)
                return;

            var lodGroup = root.GetComponent<LODGroup>();
            if (lodGroup == null)
                lodGroup = root.AddComponent<LODGroup>();

            lodGroup.animateCrossFading = false;
            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();

//            Debug.Log($"[CgfImporter] Configured LODGroup with {lods.Length} level(s).");
        }

        static LOD[] BuildLodSettings(List<Renderer> renderers)
        {
            if (renderers == null || renderers.Count == 0)
                return Array.Empty<LOD>();

            int count = renderers.Count;
            var lods = new LOD[count];
            const float maxHeight = 0.7f;
            const float minHeight = 0.02f;

            if (count == 1)
            {
                lods[0] = new LOD(maxHeight, new[] { renderers[0] });
                return lods;
            }

            float step = (maxHeight - minHeight) / (count - 1);
            for (int i = 0; i < count; i++)
            {
                float h = Mathf.Clamp(maxHeight - step * i, minHeight, maxHeight);
                lods[i] = new LOD(h, new[] { renderers[i] });
            }

            return lods;
        }

        static int ExtractLodIndexFromPath(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath))
                return -1;

            string noExt = RemoveExtension(virtualPath);
            string name = Path.GetFileName(noExt);
            var m = s_extractLodIndex.Match(name);
            if (m.Success && int.TryParse(m.Groups[1].Value, out int lod))
                return lod;

            return -1;
        }

        static string GetVirtualDirectory(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath))
                return string.Empty;

            int slash = virtualPath.LastIndexOf('/');
            if (slash <= 0)
                return string.Empty;
            return virtualPath.Substring(0, slash);
        }

        static string StripLodSuffix(string nameWithoutExtension)
        {
            if (string.IsNullOrEmpty(nameWithoutExtension))
                return nameWithoutExtension;

            var m = s_stripLodSuffix.Match(nameWithoutExtension);
            if (m.Success && !string.IsNullOrEmpty(m.Groups[1].Value))
                return m.Groups[1].Value;

            return nameWithoutExtension;
        }

        static string RemoveExtension(string virtualPath)
        {
            int ext = virtualPath.LastIndexOf('.');
            return ext > 0 ? virtualPath.Substring(0, ext) : virtualPath;
        }
    }
}
