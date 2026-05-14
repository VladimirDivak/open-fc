using System.Collections.Generic;

namespace OpenFarCry.Level.Data
{
    // Converts a per-cell surface type ID map (from land_map.h16 bits 0-2) into a
    // Unity terrain alphamap. Layer layout:
    //   index 0          = cover_low (base weight, always present)
    //   index id+1 (1-7) = detail layer for surface type id (0-6)
    public static class FcTerrainSplatmapBuilder
    {
        // Weight assigned to the matching detail layer per cell.
        // Remainder (1-DetailWeight) goes to cover_low (layer 0).
        public const float DetailWeight = 0.85f;

        // Builds alphamap[splatRes, splatRes, layerCount+1] from surfaceTypeIds.
        // splatRes should equal heightmap resolution (1024) or a power-of-two divisor.
        // surfaceTypeIds is row-major [z*resolution + x], length = resolution*resolution.
        // layerCount = number of FcTerrainLayerDesc entries (≤7).
        public static float[,,] Build(
            byte[] surfaceTypeIds,
            int resolution,
            IReadOnlyList<FcTerrainLayerDesc> layers)
        {
            int splatRes   = resolution;
            int layerCount = layers.Count;
            int totalChannels = layerCount + 1; // +1 for cover_low at index 0

            var alpha = new float[splatRes, splatRes, totalChannels];

            // Build lookup: surfaceTypeId → alphamap channel index (1-based).
            var idToChannel = new int[8]; // surface IDs are 0-6
            for (int i = 0; i < idToChannel.Length; i++)
                idToChannel[i] = -1;
            for (int li = 0; li < layerCount; li++)
                if (layers[li].SurfaceTypeId < 8)
                    idToChannel[layers[li].SurfaceTypeId] = li + 1;

            for (int hz = 0; hz < splatRes; hz++)
            {
                for (int hx = 0; hx < splatRes; hx++)
                {
                    int srcIdx = hz * resolution + hx;
                    if (srcIdx >= surfaceTypeIds.Length)
                    {
                        alpha[hz, hx, 0] = 1f;
                        continue;
                    }

                    byte typeId = surfaceTypeIds[srcIdx];
                    int channel = (typeId < 8) ? idToChannel[typeId] : -1;

                    if (channel >= 1 && channel < totalChannels)
                    {
                        alpha[hz, hx, 0]       = 1f - DetailWeight; // cover_low residual
                        alpha[hz, hx, channel] = DetailWeight;
                    }
                    else
                    {
                        // Hole (7), unknown, or no layer defined for this type → full cover_low.
                        alpha[hz, hx, 0] = 1f;
                    }
                }
            }

            BoxBlurBoundaries(alpha, splatRes, totalChannels);
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
