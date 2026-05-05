//pipelinedefine
#define H_URP
//#pragma once

// TODO: check if we need all these includes or some can be removed?
// --------------------------------- INCLUDE FILES ----------------------------- //



#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Sampling/Sampling.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"


// --------------------------------- VALUES  ----------------------------- //

float4 _HRenderScale;
float4 _HRenderScalePrevious;

#define HRenderScale _HRenderScale
#define HRenderScalePrevious _HRenderScalePrevious
//Unity's _RTHandleScale in URP always (1,1,1,1)?



int _FrameCount;


// --------------------------------- CONSTANTS  ----------------------------- //
#define H_TWO_PI (6.28318530718f)
#define H_PI (3.1415926535897932384626433832795)
#define H_PI_HALF (1.5707963267948966192313216916398)


// --------------------------------- TEXTURE SAMPLERS ----------------------------- //


SamplerState sampler_point_clamp;
SamplerState sampler_linear_clamp;
SamplerState sampler_point_repeat;
SamplerState sampler_point_mirror;
SamplerState sampler_linear_repeat;
SamplerState sampler_trilinear_clamp;

#define H_SAMPLER_POINT_CLAMP                     sampler_point_clamp
#define H_SAMPLER_LINEAR_CLAMP                    sampler_linear_clamp
#define H_SAMPLER_LINEAR_REPEAT                   sampler_linear_repeat
#define H_SAMPLER_TRILINEAR_CLAMP                 sampler_trilinear_clamp


// --------------------------------- TEXTURE READ / WRITE HELPERS ----------------------------- //


#define H_COORD(pixelCoord)     uint2(pixelCoord) //todo: do we need defines for VR?
#define H_INDEX_ARRAY(slot)     (slot)


// ----------------------------- TEXTURE PROPERTY DECLARATIONS ----------------------------- //


#define H_TEXTURE(textureName)                      TEXTURE2D_X(textureName) //todo: do we need defines for VR?
#define H_TEXTURE_ARRAY(textureName)                TEXTURE2D_ARRAY(textureName)
#define H_TEXTURE_UINT2(textureName)                Texture2D<uint2> textureName //todo: do we need defines for VR?

#define H_RW_TEXTURE(type, textureName)             RW_TEXTURE2D(type, textureName) //todo: do we need defines for VR?
#define H_RW_TEXTURE_ARRAY(type, textureName)       RW_TEXTURE2D_ARRAY(type, textureName)
#define H_RW_TEXTURE_UINT2(textureName)             RW_TEXTURE2D_X_UINT2(textureName)


// ----------------------------- TEXTURE FETCH ----------------------------- //


#define H_LOAD(textureName, unCoord2)                                           LOAD_TEXTURE2D_X(textureName, unCoord2)
#define H_LOAD_LOD(textureName, unCoord2, lod)                                  LOAD_TEXTURE2D_X_LOD(textureName, unCoord2, lod)
#define H_LOAD_ARRAY(textureName, unCoord2, index)                              LOAD_TEXTURE2D_ARRAY(textureName, unCoord2, index)
#define H_LOAD_ARRAY_LOD(textureName, unCoord2, index, lod)                     LOAD_TEXTURE2D_ARRAY_LOD(textureName, unCoord2, index, lod)

#define H_GATHER_RED(textureName, samplerName, coord2, offset)                  GATHER_RED_TEXTURE2D_X(textureName, samplerName, coord2)
#define H_GATHER_BLUE(textureName, samplerName, coord2, offset)                 GATHER_BLUE_TEXTURE2D_X(textureName, samplerName, coord2)
#define H_GATHER_GREEN(textureName, samplerName, coord2, offset)                GATHER_GREEN_TEXTURE2D_X(textureName, samplerName, coord2)
#define H_GATHER_ALPHA(textureName, samplerName, coord2, offset)                GATHER_ALPHA_TEXTURE2D_X(textureName, samplerName, coord2)

#define H_SAMPLE(textureName, samplerName, coord2)                              SAMPLE_TEXTURE2D_X(textureName, samplerName, coord2)
#define H_SAMPLE_LOD(textureName, samplerName, coord2, lod)                     SAMPLE_TEXTURE2D_X_LOD(textureName, samplerName, coord2, lod)
#define H_SAMPLE_ARRAY_LOD(textureName, samplerName, coord2, index, lod)        SAMPLE_TEXTURE2D_ARRAY_LOD(textureName, samplerName, coord2, index, lod)


// ---------------------------------- MATRICES ----------------------------- //


float4x4 _H_MATRIX_PREV_VP;
float4x4 _H_MATRIX_PREV_I_VP;
#define H_MATRIX_PREV_VP                    _H_MATRIX_PREV_VP
#define H_MATRIX_PREV_I_VP                  _H_MATRIX_PREV_I_VP
#define H_MATRIX_I_VP                       UNITY_MATRIX_I_VP
#define H_MATRIX_VP                         UNITY_MATRIX_VP
#define H_MATRIX_V                          UNITY_MATRIX_V
#define H_MATRIX_I_V                        UNITY_MATRIX_I_V


// ------------------------------------- GBUFFER RESOURCES ----------------------------- //


H_TEXTURE(_CameraColorAttachmentA);
H_TEXTURE(_MotionVectorTexture);
H_TEXTURE(_GBuffer0);
H_TEXTURE(_GBuffer2);
H_TEXTURE(g_HTraceMotionVectors);
H_TEXTURE(g_HTraceMotionMask);


// --------------------------------- GBUFFER FETCH ----------------------------- //
#define HBUFFER_NORMAL_WS(pixCoord)             GetNormalWS(pixCoord)
#define HBUFFER_ROUGHNESS(pixCoord)             GetRoughness(pixCoord)
#define HBUFFER_DEPTH(pixCoord)                 GetDepth(pixCoord)
#define HBUFFER_COLOR(pixCoord)                 GetColor(pixCoord)
#define HBUFFER_DIFFUSE(pixCoord)               GetDiffuse(pixCoord)
#define HBUFFER_MOTION_VECTOR(pixCoord)         GetMotionVector(pixCoord)
#define HBUFFER_MOTION_MASK(pixCoord)           GetMotionMask(pixCoord)


float3 GetNormalWS(uint2 pixCoordWS)
{
    
    
    #ifdef _GBUFFER_NORMALS_OCT
    float3 packNormalWS = LOAD_TEXTURE2D_X(_CameraNormalsTexture, pixCoordWS).xyz;
    float2 octNormalWS = Unpack888ToFloat2(packNormalWS);
    return UnpackNormalOctQuadEncode(octNormalWS * 2.0 - 1.0);
    #else
    return LOAD_TEXTURE2D_X(_CameraNormalsTexture, pixCoordWS).xyz;
    #endif
}

float GetRoughness(uint2 pixCoord)
{
    
    
    return LOAD_TEXTURE2D_X(_GBuffer0, pixCoord).w;
}

float GetDepth(uint2 pixCoord)
{
    
    
    return H_LOAD(_CameraDepthTexture, pixCoord).x; // LOAD_TEXTURE2D_X(_CameraDepthTexture, pixCoord).x;
}

float4 GetColor(uint2 pixCoord)
{
    
    
    return LOAD_TEXTURE2D_X(_CameraColorAttachmentA, pixCoord);
}

float3 GetDiffuse(uint2 pixCoord)
{
    
    
    return LOAD_TEXTURE2D_X(_GBuffer0, pixCoord).xyz;
}


float2 GetMotionVector(uint2 pixCoord)
{
    
    
    return  H_LOAD(g_HTraceMotionVectors, pixCoord).xy;
}


float GetMotionMask(uint2 pixCoord)
{
    
    
    return any(H_LOAD(g_HTraceMotionMask, pixCoord).xy != 0);
}

