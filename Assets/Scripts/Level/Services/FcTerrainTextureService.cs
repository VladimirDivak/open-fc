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
            public string BaseVfsPath;
            public string DetailVfsPath;
            public float  TileSizeX;
            public float  TileSizeY;
        }

        // Layer 0: cover_low.dds (global fallback megatexture).
        public string CoverLowVfsPath;

        // Layers 1..N: textures per surface type, ordered by SurfaceTypeId.
        public LayerDef[] DetailLayers;

        [Header("Runtime Debug")]
        [SerializeField] private Texture2DArray _generatedCoverArray;
        [SerializeField] private Texture2D _generatedCoverLow;

        // Transposes the texture: output[hz,hx] = input[hx,hz].
        static Texture2D TransposeTexture(Texture2D src)
        {
            if (src == null) return null;
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
            {
                Debug.LogWarning("[FcTerrainTextureService] Terrain or TerrainData is missing.");
                return;
            }

            int detailCount = DetailLayers?.Length ?? 0;
            Debug.Log($"[FcTerrainTextureService] Applying terrain textures. Layers={detailCount}, CoverLow='{CoverLowVfsPath}'");

            if (detailCount == 0)
            {
                Debug.LogWarning("[FcTerrainTextureService] No detail layers defined. Rebuild the scene?");
                return;
            }

            // 1. Build Cover Array (Base textures)
            Texture2DArray coverArray = null;
            try
            {
                coverArray = await BuildCoverArrayAsync(DetailLayers, ct);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FcTerrainTextureService] Failed to build cover array: {ex.Message}\n{ex.StackTrace}");
            }
            _generatedCoverArray = coverArray;

            if (coverArray == null)
            {
                Debug.LogWarning("[FcTerrainTextureService] coverArray is null after build.");
            }
            else
            {
                Debug.Log($"[FcTerrainTextureService] Successfully generated coverArray: {coverArray.width}x{coverArray.height} x {coverArray.depth}");
            }

            // 2. Build Terrain Layers (standard Unity Detail blending)
            var runtimeLayers = new TerrainLayer[detailCount];
            for (int i = 0; i < detailCount; i++)
            {
                if (ct.IsCancellationRequested) return;
                runtimeLayers[i] = await BuildDetailLayerAsync(DetailLayers[i], ct);
            }

            if (ct.IsCancellationRequested) return;
            terrain.terrainData.terrainLayers = runtimeLayers;

            // 3. Load Global Fallback (cover_low)
            Texture2D coverLow = null;
            if (!string.IsNullOrEmpty(CoverLowVfsPath))
            {
                var (ok, info) = await TextureImportService.RuntimeService.TryLoadWithInfoAsync(CoverLowVfsPath, null, ct);
                if (ok) coverLow = TransposeTexture(info.Texture);
            }
            _generatedCoverLow = coverLow;

            // 4. Apply to Material
            Material material = terrain.materialTemplate;
            if (material != null)
            {
                if (coverArray != null)
                {
                    material.SetTexture("_FcCoverTexArray", coverArray);
                }
                if (coverLow != null)
                    material.SetTexture("_FcCoverLow", coverLow);
            }
        }

        async UniTask<Texture2DArray> BuildCoverArrayAsync(LayerDef[] layers, CancellationToken ct)
        {
            int count = layers.Length;
            var textures = new Texture2D[count];
            int maxW = 0, maxH = 0;

            for (int i = 0; i < count; i++)
            {
                string path = layers[i].BaseVfsPath;
                if (string.IsNullOrEmpty(path))
                {
                    Debug.Log($"[FcTerrainTextureService] Layer[{i}] has no BaseVfsPath.");
                    continue;
                }

                Debug.Log($"[FcTerrainTextureService] Loading base texture for layer[{i}]: '{path}'");
                var (ok, info) = await TextureImportService.RuntimeService.TryLoadWithInfoAsync(path, null, ct);
                if (ok && info.Texture != null)
                {
                    textures[i] = info.Texture;
                    maxW = Math.Max(maxW, textures[i].width);
                    maxH = Math.Max(maxH, textures[i].height);
                    Debug.Log($"[FcTerrainTextureService] Loaded layer[{i}]: {textures[i].width}x{textures[i].height}");
                }
                else
                {
                    Debug.LogWarning($"[FcTerrainTextureService] Failed to load base texture '{path}' for layer[{i}].");
                }
            }

            if (maxW == 0)
            {
                Debug.LogWarning("[FcTerrainTextureService] maxW is 0, no base textures were loaded.");
                return null;
            }

            await UniTask.SwitchToMainThread(ct);
            var array = new Texture2DArray(maxW, maxH, count, TextureFormat.RGBA32, true, false);
            array.name = "FcTerrainCoverArray";
            array.wrapMode = TextureWrapMode.Repeat;
            array.filterMode = FilterMode.Bilinear;

            for (int i = 0; i < count; i++)
            {
                var src = textures[i];
                if (src == null)
                {
                    // Fill with neutral grey if texture is missing
                    continue;
                }
                
                if (src.width != maxW || src.height != maxH)
                {
                    var rt = RenderTexture.GetTemporary(maxW, maxH, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(src, rt);
                    var temp = new Texture2D(maxW, maxH, TextureFormat.RGBA32, true);
                    RenderTexture.active = rt;
                    temp.ReadPixels(new Rect(0, 0, maxW, maxH), 0, 0);
                    temp.Apply();
                    RenderTexture.active = null;
                    RenderTexture.ReleaseTemporary(rt);
                    Graphics.CopyTexture(temp, 0, 0, array, i, 0);
                    Destroy(temp);
                }
                else
                {
                    Graphics.CopyTexture(src, 0, 0, array, i, 0);
                }
            }

            array.Apply(false, true);
            return array;
        }

        static async UniTask<TerrainLayer> BuildDetailLayerAsync(LayerDef def, CancellationToken ct)
        {
            Texture2D tex = null;
            if (!string.IsNullOrEmpty(def.DetailVfsPath))
            {
                var (ok, info) = await TextureImportService.RuntimeService.TryLoadWithInfoAsync(def.DetailVfsPath, null, ct);
                if (ok) tex = info.Texture;
            }

            await UniTask.SwitchToMainThread(ct);
            return new TerrainLayer
            {
                diffuseTexture = tex,
                tileSize       = new Vector2(def.TileSizeX, def.TileSizeY),
            };
        }
    }
}
