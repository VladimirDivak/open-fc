using System.Collections.Generic;

namespace OpenFarCry.Level.Data
{
    // Converts a per-cell surface type ID map (from land_map.h16 bits 0-2) into a
    // Unity terrain alphamap. Channels 0..7 map directly to surface types 0..7.
    public static class FcTerrainSplatmapBuilder
    {
        // Builds alphamap[splatRes, splatRes, layerCount] from surfaceTypeIds.
        // splatRes should equal heightmap resolution (1024) or a power-of-two divisor.
        // surfaceTypeIds is row-major [z*resolution + x], length = resolution*resolution.
        // layerCount = number of FcTerrainLayerDesc entries (≤8).
        public static float[,,] Build(
            byte[] surfaceTypeIds,
            int resolution,
            IReadOnlyList<FcTerrainLayerDesc> layers)
        {
            int splatRes   = resolution;
            int layerCount = layers.Count;

            var alpha = new float[splatRes, splatRes, layerCount];

            // Build lookup: surfaceTypeId → alphamap channel index.
            var idToChannel = new int[8]; // surface IDs are 0-6
            for (int i = 0; i < idToChannel.Length; i++)
                idToChannel[i] = -1;
            for (int li = 0; li < layerCount; li++)
                if (layers[li].SurfaceTypeId < 8)
                    idToChannel[layers[li].SurfaceTypeId] = li;

            for (int hz = 0; hz < splatRes; hz++)
            {
                for (int hx = 0; hx < splatRes; hx++)
                {
                    int srcIdx = hz * resolution + hx;
                    if (srcIdx >= surfaceTypeIds.Length)
                        continue;

                    byte typeId = surfaceTypeIds[srcIdx];
                    int channel = (typeId < 8) ? idToChannel[typeId] : -1;

                    if (channel >= 0 && channel < layerCount)
                    {
                        alpha[hz, hx, channel] = 1.0f;
                    }
                }
            }

            BoxBlurBoundaries(alpha, splatRes, layerCount);
            return alpha;
        }

        // 3×3 box blur only on pixels where the dominant detail channel changes between
        // neighbours. Softens hard surface type edges to approximate vertex-alpha blending.
        static void BoxBlurBoundaries(float[,,] alpha, int res, int channels)
        {
            // Detect boundary pixels (dominant detail channel differs from any neighbour).
            var boundary = new bool[res, res];
            for (int r = 1; r < res - 1; r++)
            {
                for (int c = 1; c < res - 1; c++)
                {
                    int dom = DominantChannel(alpha, r, c, channels);
                    if (dom != DominantChannel(alpha, r - 1, c, channels) ||
                        dom != DominantChannel(alpha, r + 1, c, channels) ||
                        dom != DominantChannel(alpha, r, c - 1, channels) ||
                        dom != DominantChannel(alpha, r, c + 1, channels))
                        boundary[r, c] = true;
                }
            }

            // Average 3×3 neighbourhood for boundary pixels only.
            for (int r = 1; r < res - 1; r++)
            {
                for (int c = 1; c < res - 1; c++)
                {
                    if (!boundary[r, c]) continue;
                    for (int ch = 0; ch < channels; ch++)
                    {
                        float sum = 0f;
                        for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                            sum += alpha[r + dr, c + dc, ch];
                        alpha[r, c, ch] = sum / 9f;
                    }
                }
            }
        }

        static int DominantChannel(float[,,] alpha, int r, int c, int channels)
        {
            int best = 0;
            float bestVal = alpha[r, c, 0];
            for (int ch = 1; ch < channels; ch++)
            {
                if (alpha[r, c, ch] > bestVal)
                {
                    bestVal = alpha[r, c, ch];
                    best = ch;
                }
            }
            return best;
        }
    }
}
