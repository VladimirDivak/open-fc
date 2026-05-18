#ifndef FC_TERRAIN_COMMON_INCLUDED
#define FC_TERRAIN_COMMON_INCLUDED

// Blends Cover textures from a Texture2DArray using splatmap weights.
// Splatmap0 (RGBA) -> Slices 0, 1, 2, 3
// Splatmap1 (RGBA) -> Slices 4, 5, 6, 7
void FcBlendCovers_float(
    Texture2DArray CoverArray, SamplerState Sampler, float2 UV,
    float4 Splatmap0, 
    float4 Splatmap1, 
    out float3 OutCoverColor)
{
    float3 acc = 0;
    float weightSum = 0;

    #define ADD_COVER(w, slice) \
        if (w > 0.001) { \
            acc += CoverArray.Sample(Sampler, float3(UV, slice)).rgb * w; \
            weightSum += w; \
        }

    ADD_COVER(Splatmap0.r, 0);
    ADD_COVER(Splatmap0.g, 1);
    ADD_COVER(Splatmap0.b, 2);
    ADD_COVER(Splatmap0.a, 3);
    
    ADD_COVER(Splatmap1.r, 4);
    ADD_COVER(Splatmap1.g, 5);
    ADD_COVER(Splatmap1.b, 6);
    ADD_COVER(Splatmap1.a, 7);

    if (weightSum < 0.001) {
        OutCoverColor = 0.5; // Neutral fallback
    } else {
        OutCoverColor = acc / weightSum;
    }
}

// CryEngine Terrain Multiply: Base * (Detail * 2.0)
void FcTerrainMultiply_float(float3 BaseColor, float3 DetailColor, out float3 OutColor)
{
    OutColor = BaseColor * (DetailColor * 2.0);
}

// Triplanar detail: blends Texture2DArray slices by per-surface-type weights,
// each slice projected from three world axes blended by the world normal.
// Triplanar removes UV stretching on steep terrain (cliffs); on flat ground the
// Y projection dominates, matching plain top-down sampling. The splat weights
// (SplatA rgba = types 0-3, SplatB rgba = types 4-7) are pre-blurred so detail
// fades smoothly between surface types instead of switching in hard squares.
void FcTriplanarDetail_float(
    UnityTexture2DArray DetailArray, UnitySamplerState DetailSampler,
    UnityTexture2D SplatA, UnityTexture2D SplatB, UnitySamplerState SplatSampler,
    float4 ScaleA, float4 ScaleB,
    float2 TerrainUV, float3 WorldPos, float3 WorldNormal,
    out float3 OutDetail)
{
    float4 wA = SAMPLE_TEXTURE2D(SplatA.tex, SplatSampler.samplerstate, TerrainUV);
    float4 wB = SAMPLE_TEXTURE2D(SplatB.tex, SplatSampler.samplerstate, TerrainUV);
    float weights[8] = { wA.r, wA.g, wA.b, wA.a, wB.r, wB.g, wB.b, wB.a };
    // Per-surface-type tiling (tiles per metre): ScaleA = types 0-3, ScaleB = 4-7.
    float scales[8]  = { ScaleA.x, ScaleA.y, ScaleA.z, ScaleA.w,
                         ScaleB.x, ScaleB.y, ScaleB.z, ScaleB.w };

    float3 blend = abs(WorldNormal);
    blend /= max(blend.x + blend.y + blend.z, 1e-5);

    float3 acc = 0;
    float wsum = 0;
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float s = scales[i];
        float2 uvX = WorldPos.zy * s;
        float2 uvY = WorldPos.xz * s;
        float2 uvZ = WorldPos.xy * s;
        float3 cx = SAMPLE_TEXTURE2D_ARRAY(DetailArray.tex, DetailSampler.samplerstate, uvX, i).rgb;
        float3 cy = SAMPLE_TEXTURE2D_ARRAY(DetailArray.tex, DetailSampler.samplerstate, uvY, i).rgb;
        float3 cz = SAMPLE_TEXTURE2D_ARRAY(DetailArray.tex, DetailSampler.samplerstate, uvZ, i).rgb;
        acc  += (cx * blend.x + cy * blend.y + cz * blend.z) * weights[i];
        wsum += weights[i];
    }

    OutDetail = wsum > 1e-4 ? acc / wsum : float3(0.5, 0.5, 0.5);
}

#endif
