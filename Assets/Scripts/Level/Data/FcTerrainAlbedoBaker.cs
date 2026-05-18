using System.Threading.Tasks;
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
            int res = outRes; // local copy: out params cannot be used in lambdas
            var acc = new Color32[res * res];

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
                    ? BuildAutogenMask(layer, samples, hmRes, unit, res)
                    : null;

                // Rows are independent: parallelize within a layer (layer order
                // itself stays sequential). Per-row painted counts avoid a race.
                var rowPainted = new long[res];
                Parallel.For(0, res, oz =>
                {
                    int lz = texHPow2 ? (oz & (texH - 1)) : (oz % texH);
                    int rowBase = oz * res;
                    long rp = 0;
                    for (int ox = 0; ox < res; ox++)
                    {
                        int lx = texWPow2 ? (ox & (texW - 1)) : (ox % texW);
                        int li = (lz * texW + lx) * 4;
                        int i = rowBase + ox;

                        if (firstLayer)
                        {
                            acc[i] = new Color32(rgba[li], rgba[li + 1], rgba[li + 2], 255);
                            rp++;
                            continue;
                        }

                        int w = autogenMask != null
                            ? autogenMask[i]
                            : SampleMaskWeight(layer, ox, oz, res);
                        if (w <= 0)
                            continue;
                        rp++;

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
                    rowPainted[oz] = rp;
                });

                long painted = 0;
                for (int r = 0; r < res; r++)
                    painted += rowPainted[r];

                float coverage = 100f * painted / (res * (long)res);
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

        // Soft transition half-width at autogen band edges. Hard altitude/slope
        // cuts produce sharp visible lines (e.g. at the beach line); a smoothstep
        // window fades the layer in/out instead.
        const float AutogenAltFalloff = 2.5f;    // metres
        const float AutogenSlopeFalloff = 16f;   // slope units (0..255)

        // Builds a full-resolution autogen mask via soft altitude+slope windows,
        // optionally further softened by a 3x3 box blur when Smooth is set.
        // Falloff is skipped at the 0 / 255 clamp extremes (not real boundaries).
        static byte[] BuildAutogenMask(
            FcTerrainPaintLayer layer, ushort[] samples, int hmRes, float unit, int outRes)
        {
            var mask = new byte[outRes * outRes];
            bool slopeFiltered = layer.MinSlope > 0 || layer.MaxSlope < 255;

            float band = Mathf.Max(0f, layer.AltEnd - layer.AltStart);
            float af = Mathf.Max(0.25f, Mathf.Min(AutogenAltFalloff, band * 0.5f));

            Parallel.For(0, outRes, oz =>
            {
                int hz = oz * hmRes / outRes;
                int rowBase = oz * outRes;
                for (int ox = 0; ox < outRes; ox++)
                {
                    int hx = ox * hmRes / outRes;
                    float h = HeightMeters(samples, hmRes, hx, hz);

                    float w = 1f;
                    if (layer.AltStart > 0)
                        w *= SmoothStep01(layer.AltStart - af, layer.AltStart + af, h);
                    if (layer.AltEnd < 255)
                        w *= 1f - SmoothStep01(layer.AltEnd - af, layer.AltEnd + af, h);
                    if (w <= 0f)
                        continue;

                    if (slopeFiltered)
                    {
                        float s = SlopeValue(samples, hmRes, unit, hx, hz);
                        if (layer.MinSlope > 0)
                            w *= SmoothStep01(
                                layer.MinSlope - AutogenSlopeFalloff, layer.MinSlope + AutogenSlopeFalloff, s);
                        if (layer.MaxSlope < 255)
                            w *= 1f - SmoothStep01(
                                layer.MaxSlope - AutogenSlopeFalloff, layer.MaxSlope + AutogenSlopeFalloff, s);
                    }

                    mask[rowBase + ox] = (byte)Mathf.Clamp(Mathf.RoundToInt(w * 255f), 0, 255);
                }
            });

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

        // HLSL-style smoothstep: 0 below edge0, 1 above edge1, smooth between.
        // (Unity's Mathf.SmoothStep interpolates a value between from/to instead.)
        static float SmoothStep01(float edge0, float edge1, float x)
        {
            if (edge1 <= edge0)
                return x >= edge1 ? 1f : 0f;
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        static void BoxBlur3x3(byte[] mask, int res)
        {
            var src = (byte[])mask.Clone();
            Parallel.For(0, res, z =>
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
            });
        }
    }
}
