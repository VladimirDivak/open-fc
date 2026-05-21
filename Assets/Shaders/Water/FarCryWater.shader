Shader "FarCry/Water"
{
    Properties
    {
        [Header(Color)]
        _ShallowColor    ("Shallow Color", Color) = (0.30, 0.60, 0.55, 1)
        _DeepColor       ("Deep Color",    Color) = (0.02, 0.08, 0.14, 1)

        [Header(Waves)]
        [NoScaleOffset] _NormalsA ("Normals A", 2D) = "bump" {}
        [NoScaleOffset] _NormalsB ("Normals B", 2D) = "bump" {}
        _TilingA         ("Tiling A",  Vector) = (0.25, 0.25, 0, 0)
        _TilingB         ("Tiling B",  Vector) = (0.13, 0.13, 0, 0)
        _ScrollA         ("Scroll A",  Vector) = ( 0.03,  0.02, 0, 0)
        _ScrollB         ("Scroll B",  Vector) = (-0.02,  0.04, 0, 0)
        _NormalStrength  ("Normal Strength", Range(0, 4)) = 1.0

        [Header(Refraction)]
        _DistortionStrength ("Distortion Strength", Range(0, 1)) = 0.05
        _DepthFadeDistance  ("Depth Fade Distance", Range(0.01, 32)) = 4.0

        [Header(Fresnel)]
        _FresnelPower    ("Fresnel Power", Range(0.1, 16)) = 5.0

        [Header(Reflection)]
        _Roughness       ("Reflection Roughness (0 = mirror, 1 = matte)", Range(0, 1)) = 0.3

        [Header(Sun Glint)]
        _SunGlintStrength ("Sun Glint Strength", Range(0, 8)) = 2.0
        _SunGlintExponent ("Sun Glint Exponent", Range(8, 512)) = 128.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        LOD 200
        ZWrite On
        Cull Back
        Blend One Zero

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #pragma multi_compile _ _FC_WATER_PLANAR_ON
            #pragma multi_compile _ _FC_WATER_REFRACTION_ON
            #pragma multi_compile _ _FC_WATER_FALLBACK_PROBE_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NormalsA);  SAMPLER(sampler_NormalsA);
            TEXTURE2D(_NormalsB);  SAMPLER(sampler_NormalsB);
            TEXTURE2D(_FcWaterReflectionTex); SAMPLER(sampler_FcWaterReflectionTex);
            TEXTURECUBE(_FcWaterFallbackCubemap); SAMPLER(sampler_FcWaterFallbackCubemap);

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _TilingA;
                float4 _TilingB;
                float4 _ScrollA;
                float4 _ScrollB;
                float  _NormalStrength;
                float  _DistortionStrength;
                float  _DepthFadeDistance;
                float  _FresnelPower;
                float  _Roughness;
                float  _SunGlintStrength;
                float  _SunGlintExponent;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 tangentWS   : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float2 uv          : TEXCOORD4;
                float4 screenPos   : TEXCOORD5;
                float  fogCoord    : TEXCOORD6;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = nrm.normalWS;
                OUT.tangentWS = nrm.tangentWS;
                OUT.bitangentWS = nrm.bitangentWS;
                OUT.uv = IN.uv;
                OUT.screenPos = ComputeScreenPos(pos.positionCS);
                OUT.fogCoord = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            float3 SampleAnimatedNormal(float2 baseUV)
            {
                float2 uvA = baseUV * _TilingA.xy + _Time.y * _ScrollA.xy;
                float2 uvB = baseUV * _TilingB.xy + _Time.y * _ScrollB.xy;

                float3 nA = UnpackNormal(SAMPLE_TEXTURE2D(_NormalsA, sampler_NormalsA, uvA));
                float3 nB = UnpackNormal(SAMPLE_TEXTURE2D(_NormalsB, sampler_NormalsB, uvB));

                // UDN blend (cheap)
                float3 n = normalize(float3(nA.xy + nB.xy, nA.z * nB.z));
                n.xy *= _NormalStrength;
                return normalize(n);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Tangent-space animated normal -> world space
                float3 nTS = SampleAnimatedNormal(IN.positionWS.xz);
                float3x3 tbn = float3x3(normalize(IN.tangentWS), normalize(IN.bitangentWS), normalize(IN.normalWS));
                float3 nWS = normalize(mul(nTS, tbn));

                float3 viewDirWS = normalize(_WorldSpaceCameraPos - IN.positionWS);

                // Screen-space UV with normal-driven distortion (scaled by inverse depth)
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float surfaceDepth = LinearEyeDepth(IN.positionHCS.z, _ZBufferParams);
                float distortion = _DistortionStrength / max(surfaceDepth, 1.0);
                float2 distortedUV = saturate(screenUV + nWS.xz * distortion);

                // --- Refraction ---
                float3 refractionColor;
            #if _FC_WATER_REFRACTION_ON
                refractionColor = SampleSceneColor(distortedUV);
            #else
                refractionColor = _DeepColor.rgb;
            #endif

                // Depth fade for shoreline/shallow tinting
                float sceneRawDepth = SampleSceneDepth(distortedUV);
                float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
                float waterDepth = max(0.0, sceneEyeDepth - surfaceDepth);
                float depthT = saturate(waterDepth / _DepthFadeDistance);
                float3 waterTint = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthT);
                refractionColor *= waterTint;

                // --- Reflection ---
                float3 reflectionColor;
            #if _FC_WATER_PLANAR_ON
                // Reflection RT shares X-axis orientation with the main camera; only screen UV distortion is applied.
                // Mip-bias controls perceived roughness: 0 = mirror-sharp, larger = blurred reflection.
                float mipBias = _Roughness * 6.0; // ~6 mip levels covers strong blur
                reflectionColor = SAMPLE_TEXTURE2D_LOD(_FcWaterReflectionTex, sampler_FcWaterReflectionTex,
                                                       distortedUV, mipBias).rgb;
            #else
                float3 reflectDir = reflect(-viewDirWS, nWS);
                #if _FC_WATER_FALLBACK_PROBE_ON
                    reflectionColor = GlossyEnvironmentReflection(reflectDir, 0.0, 1.0);
                #else
                    reflectionColor = SAMPLE_TEXTURECUBE(_FcWaterFallbackCubemap, sampler_FcWaterFallbackCubemap, reflectDir).rgb;
                #endif
            #endif

                // --- Fresnel mix ---
                float ndv = saturate(dot(viewDirWS, nWS));
                float fresnel = pow(1.0 - ndv, _FresnelPower);
                float3 baseColor = lerp(refractionColor, reflectionColor, fresnel);

                // --- Sun glint (Blinn-Phong against main directional light) ---
                Light mainLight = GetMainLight();
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float ndh = saturate(dot(nWS, halfDir));
                float spec = pow(ndh, _SunGlintExponent) * _SunGlintStrength;
                baseColor += mainLight.color * spec * fresnel;

                baseColor = MixFog(baseColor, IN.fogCoord);
                return half4(baseColor, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
