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

#endif
