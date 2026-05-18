using UnityEngine;

namespace OpenFarCry.Level.Data
{
    // Bakes a clean albedo megatexture by compositing Far Cry terrain paint layers,
    // reproducing CTerrainTexGen::GenerateSurfaceTexture MINUS lighting/shadows.
    //
    // Layers composite in paint order: the first layer is copied raw, each
    // subsequent layer blends over the accumulator by its per-texel weight
    // (manual mask value, or an autogen mask derived from altitude/slope).
    //
    // Output pixels are row-major [oz * outRes + ox] (row = terrain Z, col = X),
    // pulling heightmap data with the same index expression as
    // FcTerrainHeightmapDecoder.TryDecodeToUnityHeights so the albedo aligns
    // with the Unity terrain geometry.
    public static class FcTerrainAlbedoBaker
    {
        // Bakes the albedo. samples is the raw h16 array (length hmRes*hmRes).
        // Returns row-major Color32[outRes*outRes]; outRes echoed via out param.
        public static bool TryBake(
            FcTerrainLayerSet layerSet,
            ushort[] samples,
            int hmRes,
            int heightmapUnitSize,
            out Color32[] albedo,
            out int outRes)
        {
            albedo = null;
            outRes = 0;
            if (layerSet == null || layerSet.Layers.Count == 0)
                return false;
            if (samples == null || hmRes <= 0 || samples.Length != hmRes * hmRes)
                return false;

            outRes = Mathf.Max(1, layerSet.TextureSize);
            var acc = new Color32[outRes * outRes];

            float unit = heightmapUnitSize > 0 ? heightmapUnitSize : 1f;
            bool firstLayer = true;

            foreach (var layer in layerSet.Layers)
            {
                int texW = layer.TextureWidth;
                int texH = layer.TextureHeight;
                bool texWPow2 = (texW & (texW - 1)) == 0;
                bool texHPow2 = (texH & (texH - 1)) == 0;
                byte[] rgba = layer.TextureRgba;

                byte[] autogenMask = layer.AutoGenMask
                    ? BuildAutogenMask(layer, samples, hmRes, unit, outRes)
                    : null;

                long painted = 0;
                for (int oz = 0; oz < outRes; oz++)
                {
                    int lz = texHPow2 ? (oz & (texH - 1)) : (oz % texH);
                    int rowBase = oz * outRes;
                    for (int ox = 0; ox < outRes; ox++)
                    {
                        int lx = texWPow2 ? (ox & (texW - 1)) : (ox % texW);
                        int li = (lz * texW + lx) * 4;
                        int i = rowBase + ox;

                        if (firstLayer)
                        {
                            acc[i] = new Color32(rgba[li], rgba[li + 1], rgba[li + 2], 255);
                            painted++;
                            continue;
                        }

                        int w = autogenMask != null
                            ? autogenMask[i]
                            : SampleMaskWeight(layer, ox, oz, outRes);
                        if (w <= 0)
                            continue;
                        painted++;

                        if (w >= 255)
                        {
                            acc[i] = new Color32(rgba[li], rgba[li + 1], rgba[li + 2], 255);
                            continue;
                        }

                        int iw = 255 - w;
                        var d = acc[i];
                        acc[i] = new Color32(
                            (byte)((d.r * iw + rgba[li] * w) / 255),
                            (byte)((d.g * iw + rgba[li + 1] * w) / 255),
                            (byte)((d.b * iw + rgba[li + 2] * w) / 255),
                            255);
                    }
                }

                float coverage = 100f * painted / (outRes * (long)outRes);
                Debug.Log(
                    $"[FcTerrainAlbedoBaker] layer '{layer.Name}' " +
                    $"({(layer.AutoGenMask ? "autogen" : "manual")}) coverage={coverage:F1}%" +
                    (firstLayer ? " [base]" : string.Empty));

                firstLayer = false;
            }

            albedo = acc;
            return true;
        }

        // Samples a hand-painted layer mask at output texel (ox,oz), nearest-resampled.
        // Returns 255 (fully opaque) when the layer has no mask.
        static byte SampleMaskWeight(FcTerrainPaintLayer layer, int ox, int oz, int outRes)
        {
            if (layer.Mask == null)
                return 255;
            int m = layer.MaskResolution;
            int mx = ox * m / outRes;
            int mz = oz * m / outRes;
            // Mask raw layout mirrors the h16 array (X-major); see class comment.
            return layer.Mask[mx * m + mz];
        }

        // Builds a full-resolution autogen mask: hard altitude+slope test, 0xFF/0x00,
        // optionally softened by a 3x3 box blur when the layer requests Smooth.
        static byte[] BuildAutogenMask(
            FcTerrainPaintLayer layer, ushort[] samples, int hmRes, float unit, int outRes)
        {
            var mask = new byte[outRes * outRes];
            bool slopeFiltered = layer.MinSlope > 0 || layer.MaxSlope < 255;

            for (int oz = 0; oz < outRes; oz++)
            {
                int hz = oz * hmRes / outRes;
                int rowBase = oz * outRes;
                for (int ox = 0; ox < outRes; ox++)
                {
                    int hx = ox * hmRes / outRes;
                    float h = HeightMeters(samples, hmRes, hx, hz);
                    if (h < layer.AltStart || h > layer.AltEnd)
                        continue;

                    if (slopeFiltered)
                    {
                        int s = SlopeValue(samples, hmRes, unit, hx, hz);
                        if (s < layer.MinSlope || s > layer.MaxSlope)
                            continue;
                    }

                    mask[rowBase + ox] = 0xFF;
                }
            }

            if (layer.Smooth)
                BoxBlur3x3(mask, outRes);

            return mask;
        }

        // Height in metres for Unity-terrain coord (X=hx, Z=hz); mirrors
        // TryDecodeToUnityHeights: sample index hx*res+hz, value (raw & 0xFFE0)/256.
        static float HeightMeters(ushort[] samples, int hmRes, int hx, int hz)
        {
            hx = Mathf.Clamp(hx, 0, hmRes - 1);
            hz = Mathf.Clamp(hz, 0, hmRes - 1);
            return (samples[hx * hmRes + hz] & 0xFFE0) / 256f;
        }

        // Terrain slope at (hx,hz) mapped to 0..255 (0deg..90deg), matching the
        // editor's MinSlope/MaxSlope range. Approximate; tune visually.
        static int SlopeValue(ushort[] samples, int hmRes, float unit, int hx, int hz)
        {
            float hL = HeightMeters(samples, hmRes, hx - 1, hz);
            float hR = HeightMeters(samples, hmRes, hx + 1, hz);
            float hD = HeightMeters(samples, hmRes, hx, hz - 1);
            float hU = HeightMeters(samples, hmRes, hx, hz + 1);
            float gx = (hR - hL) / (2f * unit);
            float gz = (hU - hD) / (2f * unit);
            float angleDeg = Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
            return Mathf.Clamp(Mathf.RoundToInt(angleDeg / 90f * 255f), 0, 255);
        }

        static void BoxBlur3x3(byte[] mask, int res)
        {
            var src = (byte[])mask.Clone();
            for (int z = 0; z < res; z++)
            {
                int z0 = Mathf.Max(0, z - 1), z1 = Mathf.Min(res - 1, z + 1);
                for (int x = 0; x < res; x++)
                {
                    int x0 = Mathf.Max(0, x - 1), x1 = Mathf.Min(res - 1, x + 1);
                    int sum = 0, n = 0;
                    for (int zz = z0; zz <= z1; zz++)
                        for (int xx = x0; xx <= x1; xx++)
                        {
                            sum += src[zz * res + xx];
                            n++;
                        }
                    mask[z * res + x] = (byte)(sum / n);
                }
            }
        }
    }
}
