//pipelinedefine
#define H_URP


#if UNITY_VERSION >= 600000
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AmbientProbe.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolume.hlsl"

float3 EvaluateFallbackSky(float3 Direction)
{
    unity_SHAr = H_SHAr;
    unity_SHAg = H_SHAg;
    unity_SHAb = H_SHAb;
    unity_SHBr = H_SHBr;
    unity_SHBg = H_SHBg;
    unity_SHBb = H_SHBb;
    unity_SHC =  H_SHC;
    
    return EvaluateAmbientProbe(Direction);
}

float3 EvaluateFallbackAPV(float4 APVParams, float3 PositionWS, float3 NormalWS, float3 ViewDirection, float2 pixCoord)
{
    float3 BakedAPV = 0;
    float3 Unused = 0;

    _APVSamplingNoise = APVParams.z;
    PositionWS = AddNoiseToSamplingPosition(PositionWS, pixCoord, ViewDirection);
    PositionWS = (PositionWS + NormalWS * APVParams.x) + ViewDirection * APVParams.y;

    unity_SHAr = H_SHAr;
    unity_SHAg = H_SHAg;
    unity_SHAb = H_SHAb;
    unity_SHBr = H_SHBr;
    unity_SHBg = H_SHBg;
    unity_SHBb = H_SHBb;
    unity_SHC =  H_SHC;
    
    EvaluateAdaptiveProbeVolume(PositionWS, NormalWS, -NormalWS, ViewDirection, pixCoord, BakedAPV, Unused);

    #ifdef FALLBACK_STAGE
    BakedAPV *= APVParams.w;
    #endif
    
    return BakedAPV;
}
#else
#include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolumeBlendStates.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Lighting/ProbeVolume/ProbeVolume.hlsl"

uint _EnableProbeVolumes;

float3 EvaluateFallbackAPV(float4 APVParams, float3 PositionWS, float3 NormalWS, float3 ViewDirection, int2 pixCoord)
{
    return 0;
}

float3 EvaluateFallbackSky(float3 Direction)
{   
    float3 Sky = SHEvalLinearL0L1(Direction, H_SHAr, H_SHAg, H_SHAb);
    Sky += SHEvalLinearL2(Direction, H_SHBr, H_SHBg, H_SHBb, H_SHC);

    return max(Sky, 0);
}
#endif



