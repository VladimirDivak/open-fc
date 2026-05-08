using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenFarCry.Importer.Cgf
{
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

        public CgfMaterialImportService(CgfMaterialRuntimeCache cache = null)
        {
            _cache = cache ?? new CgfMaterialRuntimeCache();
        }

        public Material[] ResolveSubmeshMaterials(CgfFile parsedFile, Mesh mesh, int[] submeshMaterialIds = null)
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
                        ? GetOrBuild(parsedFile, chunk)
                        : CgfMaterialBuilder.BuildFallback($"missing_matid_{matId}");
                }
                return mats;
            }
            else
            {
                var single = GetOrBuild(parsedFile, rootMat);
                var mats = new Material[subCount];
                for (int i = 0; i < subCount; i++)
                    mats[i] = single;
                return mats;
            }
        }

        public void ClearCache() => _cache.Clear();
        public int CachedCount => _cache.Count;

        static CgfMaterialChunk ResolveMultiMaterialChild(
            CgfFile parsedFile,
            CgfMaterialChunk rootMat,
            List<CgfMaterialChunk> children,
            int matId)
        {
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

        Material GetOrBuild(CgfFile parsedFile, CgfMaterialChunk chunk)
        {
            string source = string.IsNullOrEmpty(parsedFile?.SourceVirtualPath)
                ? "__unknown_source"
                : parsedFile.SourceVirtualPath.ToLowerInvariant();
            string name = string.IsNullOrEmpty(chunk.Name) ? "__unnamed" : chunk.Name.ToLowerInvariant();
            string key = $"{source}|mat:{chunk.ChunkID}|idx:{chunk.TableIndex}|name:{name}";
            return _cache.GetOrCreate(key, () => CgfMaterialBuilder.Build(chunk));
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
