using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using OpenFarCry.FileSystem;
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
        static readonly int FcCoverTexId    = Shader.PropertyToID("_FcCoverTex");
        static readonly int FcCoverScaleId  = Shader.PropertyToID("_FcCoverScale");
        static readonly int FcCoverOffsetId = Shader.PropertyToID("_FcCoverOffset");

        [Serializable]
        public struct LayerDef
        {
            public string VfsPath;
            public float  TileSizeX;
            public float  TileSizeY;
        }

        // Layer 0: cover_low.dds (global megatexture, tileSize = worldSize).
        public LayerDef CoverLayer;
        public string CoverCtcPath;
        public int CoverSectorCount;

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
            Texture coverTexture = await BuildCoverTextureForMaterialAsync(runtimeLayers[0], ct);
            if (ct.IsCancellationRequested) return;
            ApplyCoverMaterialProperties(terrain, runtimeLayers[0], coverTexture);
        }

        async UniTask<Texture> BuildCoverTextureForMaterialAsync(TerrainLayer fallbackCoverLayer, CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(CoverCtcPath) &&
                CoverSectorCount > 0 &&
                FcFileSystem.Exists(CoverCtcPath))
            {
                try
                {
                    var atlas = await BuildCoverAtlasFromCtcAsync(CoverCtcPath, CoverSectorCount, ct);
                    if (atlas != null)
                        return atlas;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[FcTerrainTextureService] Failed to build cover atlas from '{CoverCtcPath}': {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning(
                    $"[FcTerrainTextureService] cover.ctc is unavailable for runtime cover atlas. " +
                    $"path='{CoverCtcPath}', sectorCount={CoverSectorCount}, exists=" +
                    $"{(!string.IsNullOrEmpty(CoverCtcPath) && FcFileSystem.Exists(CoverCtcPath))}.");
            }

            Debug.LogWarning("[FcTerrainTextureService] Falling back to cover_low.dds for terrain cover.");
            return fallbackCoverLayer != null ? fallbackCoverLayer.diffuseTexture : null;
        }

        static async UniTask<Texture2D> BuildCoverAtlasFromCtcAsync(
            string virtualPath,
            int sectorCount,
            CancellationToken ct)
        {
            byte[] fileBytes = await FcFileSystem.ReadAllBytesAsync(virtualPath, ct);
            if (fileBytes == null || fileBytes.Length < 4)
                throw new InvalidOperationException("CTC file is empty or too small.");

            int sectorTexSize = BitConverter.ToInt32(fileBytes, 0);
            if (sectorTexSize <= 0)
                throw new InvalidOperationException($"Invalid sector texture size: {sectorTexSize}.");

            int bytesPerTopMip = sectorTexSize * sectorTexSize / 2; // DXT1 = 4bpp
            int bytesPerSector = CalculateDxt1MipChainBytes(sectorTexSize);
            int expectedMinSize = 4 + sectorCount * sectorCount * bytesPerSector;
            if (fileBytes.Length < expectedMinSize)
            {
                throw new InvalidOperationException(
                    $"CTC file is smaller than expected. bytes={fileBytes.Length}, expected>={expectedMinSize}, " +
                    $"sectorCount={sectorCount}, sectorTexSize={sectorTexSize}.");
            }

            int atlasSize = sectorTexSize * sectorCount;

            await UniTask.SwitchToMainThread(ct);
            var atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, mipChain: true, linear: false)
            {
                name = "FcTerrainCoverAtlas"
            };
            var tile = new Texture2D(sectorTexSize, sectorTexSize, TextureFormat.DXT1, mipChain: false, linear: false);
            var topMipBytes = new byte[bytesPerTopMip];

            int sectorTotal = sectorCount * sectorCount;
            for (int secIndex = 0; secIndex < sectorTotal; secIndex++)
            {
                ct.ThrowIfCancellationRequested();

                int fileOffset = 4 + secIndex * bytesPerSector;
                Buffer.BlockCopy(fileBytes, fileOffset, topMipBytes, 0, bytesPerTopMip);

                tile.LoadRawTextureData(topMipBytes);
                tile.Apply(updateMipmaps: false, makeNoLongerReadable: false);

                int sx = secIndex % sectorCount;
                int sy = secIndex / sectorCount;
                atlas.SetPixels32(sx * sectorTexSize, sy * sectorTexSize, sectorTexSize, sectorTexSize, tile.GetPixels32());

                if ((secIndex & 31) == 31)
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            atlas.wrapMode = TextureWrapMode.Clamp;
            atlas.filterMode = FilterMode.Bilinear;
            atlas.Apply(updateMipmaps: true, makeNoLongerReadable: false);
            UnityEngine.Object.Destroy(tile);

            var transposedAtlas = TransposeTexture(atlas);
            transposedAtlas.name = "FcTerrainCoverAtlas_Transposed";
            transposedAtlas.wrapMode = TextureWrapMode.Clamp;
            transposedAtlas.filterMode = FilterMode.Bilinear;
            UnityEngine.Object.Destroy(atlas);
            return transposedAtlas;
        }

        static int CalculateDxt1MipChainBytes(int size)
        {
            int total = 0;
            int mipSize = size;
            while (mipSize >= 4)
            {
                int blockCount = mipSize / 4;
                total += blockCount * blockCount * 8; // DXT1 block = 8 bytes, minimum 1 block per mip
                mipSize /= 2;
            }

            return total;
        }

        static void ApplyCoverMaterialProperties(Terrain terrain, TerrainLayer coverLayer, Texture coverTexture)
        {
            if (terrain == null || coverLayer == null)
                return;

            Material material = terrain.materialTemplate;
            if (material == null)
                return;

            material.SetTexture(FcCoverTexId, coverTexture);

            Vector2 tileSize = coverLayer.tileSize;
            float scaleX = tileSize.x > 0f ? 1f / tileSize.x : 1f;
            float scaleY = tileSize.y > 0f ? 1f / tileSize.y : 1f;
            material.SetVector(FcCoverScaleId, new Vector4(scaleX, scaleY, 0f, 0f));
            material.SetVector(FcCoverOffsetId, Vector4.zero);
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
