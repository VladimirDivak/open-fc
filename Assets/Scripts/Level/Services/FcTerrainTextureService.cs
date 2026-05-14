using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenFarCry.Importer.Texture;
using UnityEngine;

namespace OpenFarCry.Level.Services
{
    // Populates terrain layer textures at runtime by loading DDS files from the PAK VFS.
    // Build Scene writes layer metadata (VFS paths, tile sizes) into this component.
    // At Start(), textures are decoded and assigned as fresh in-memory TerrainLayer instances
    // so that the persisted TerrainLayer stub assets are never dirtied at runtime.
    [RequireComponent(typeof(Terrain))]
    public sealed class FcTerrainTextureService : MonoBehaviour
    {
        [Serializable]
        public struct LayerDef
        {
            public string VfsPath;
            public float  TileSizeX;
            public float  TileSizeY;
        }

        // Layer 0: cover_low.dds (global megatexture, tileSize = worldSize).
        public LayerDef CoverLayer;

        // Layers 1..N: detail textures per surface type, ordered by SurfaceTypeId.
        public LayerDef[] DetailLayers;

        // Transposes the cover texture: output[hz,hx] = input[hx,hz].
        // Matches the X↔Z swap applied to the h16 heightmap so cover_low.dds aligns with terrain.
        // Source may be GPU-only (non-readable DDS), so it is first copied via RenderTexture.
        static Texture2D TransposeTexture(Texture2D src)
        {
            int w = src.width;
            int h = src.height;

            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var readable = new Texture2D(w, h, TextureFormat.ARGB32, mipChain: false, linear: true);
            readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readable.Apply(updateMipmaps: false);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            var srcPixels = readable.GetPixels32();
            UnityEngine.Object.DestroyImmediate(readable);

            var dst = new Texture2D(h, w, TextureFormat.ARGB32, mipChain: false, linear: true);
            var dstPixels = new Color32[srcPixels.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    dstPixels[x * h + y] = srcPixels[y * w + x];
            dst.SetPixels32(dstPixels);
            dst.Apply(updateMipmaps: false);
            return dst;
        }

        void Start()
        {
            ApplyAsync(destroyCancellationToken).Forget();
        }

        async UniTaskVoid ApplyAsync(CancellationToken ct)
        {
            var terrain = GetComponent<Terrain>();
            if (terrain == null || terrain.terrainData == null)
                return;

            int detailCount   = DetailLayers?.Length ?? 0;
            int buildCount    = 1 + detailCount;
            int existingCount = terrain.terrainData.terrainLayers?.Length ?? 0;

            if (buildCount != existingCount)
            {
                Debug.LogWarning(
                    $"[FcTerrainTextureService] Layer count mismatch: terrain has {existingCount}, service has {buildCount}. " +
                    "Rebuild the scene to regenerate TerrainLayers.");
                return;
            }

            var runtimeLayers = new TerrainLayer[buildCount];
            runtimeLayers[0] = await BuildLayerAsync(CoverLayer, ct, transposeTex: true);

            for (int i = 0; i < detailCount; i++)
            {
                if (ct.IsCancellationRequested) return;
                runtimeLayers[i + 1] = await BuildLayerAsync(DetailLayers[i], ct, transposeTex: false);
            }

            if (ct.IsCancellationRequested) return;
            terrain.terrainData.terrainLayers = runtimeLayers;
        }

        static async UniTask<TerrainLayer> BuildLayerAsync(LayerDef def, CancellationToken ct, bool transposeTex = false)
        {
            Texture2D tex = null;
            if (string.IsNullOrEmpty(def.VfsPath))
            {
                Debug.LogWarning(
                    $"[FcTerrainTextureService] Terrain layer has empty VfsPath (tileSize={def.TileSizeX}x{def.TileSizeY}). " +
                    "Rebuild the scene.");
            }
            else if (!TextureImportService.IsSupportedVirtualPath(def.VfsPath))
            {
                Debug.LogWarning($"[FcTerrainTextureService] Unsupported texture path '{def.VfsPath}'.");
            }
            else
            {
                var (ok, info) = await TextureImportService.RuntimeService
                    .TryLoadWithInfoAsync(def.VfsPath, scopeId: null, ct);
                if (ok)
                    tex = info.Texture;
                else
                    Debug.LogWarning($"[FcTerrainTextureService] Failed to load terrain texture '{def.VfsPath}'.");
            }

            await UniTask.SwitchToMainThread(ct);
            if (tex != null && transposeTex)
                tex = TransposeTexture(tex);
            return new TerrainLayer
            {
                diffuseTexture = tex,
                tileSize       = new Vector2(def.TileSizeX, def.TileSizeY),
                tileOffset     = Vector2.zero,
            };
        }
    }
}
