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

        [Header(Beer Lambert Absorption)]
        // Per-meter extinction (RGB). Higher red = water turns blue-green with depth.
        _ExtinctionRGB   ("Extinction (per meter)", Vector) = (0.45, 0.18, 0.12, 0)

        [Header(Fresnel)]
        _FresnelPower    ("Fresnel Power", Range(0.1, 16)) = 5.0

        [Header(Reflection)]
        _Roughness       ("Reflection Roughness (0 = mirror, 1 = matte)", Range(0, 1)) = 0.3

        [Header(Sun Glint GGX)]
        _SunGlintStrength ("Sun Glint Strength", Range(0, 16)) = 4.0

        [Header(Gerstner Waves)]
        _WaveAmplitude   ("Wave Amplitude", Range(0, 3)) = 0.4
        _WaveLength      ("Base Wavelength", Range(1, 200)) = 24.0
        _WaveSteepness   ("Steepness (0..1)", Range(0, 1)) = 0.6
        _WaveSpeed       ("Wave Speed", Range(0, 4)) = 1.0
        _WaveCount       ("Wave Count", Range(1, 6)) = 4

        [Header(Foam)]
        [NoScaleOffset] _FoamTexture ("Foam (R)", 2D) = "white" {}
        _FoamColor          ("Foam Color", Color) = (1, 1, 1, 1)
        _FoamShoreDistance  ("Shore Foam Distance (0 = off)", Range(0, 16)) = 2.0
        _FoamTiling         ("Foam Tiling", Vector) = (0.2, 0.2, 0, 0)
        _FoamScroll         ("Foam Scroll", Vector) = (0.02, 0.015, 0, 0)
        _FoamCrestThreshold ("Crest Foam Threshold", Range(0, 3)) = 0.7
        _FoamCrestSoftness  ("Crest Foam Softness", Range(0.01, 2)) = 0.4
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

            // P5 — stencil mask: tag every water pixel with ref 16 so later passes (aggressive blur,
            // underwater post) can restrict themselves to the water silhouette. Bit 16 reserved in CLAUDE.md.
            Stencil
            {
                Ref 16
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_fog
            #pragma multi_compile _ _FC_WATER_PLANAR_ON
            #pragma multi_compile _ _FC_WATER_REFRACTION_ON
            #pragma multi_compile _ _FC_WATER_FALLBACK_PROBE_ON
            #pragma multi_compile _ _FC_WATER_GERSTNER_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_NormalsA);  SAMPLER(sampler_NormalsA);
            TEXTURE2D(_NormalsB);  SAMPLER(sampler_NormalsB);
            TEXTURE2D(_FoamTexture); SAMPLER(sampler_FoamTexture);
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
                float4 _ExtinctionRGB;
                float  _FresnelPower;
                float  _Roughness;
                float  _SunGlintStrength;
                float  _WaveAmplitude;
                float  _WaveLength;
                float  _WaveSteepness;
                float  _WaveSpeed;
                float  _WaveCount;
                float4 _FoamColor;
                float  _FoamShoreDistance;
                float4 _FoamTiling;
                float4 _FoamScroll;
                float  _FoamCrestThreshold;
                float  _FoamCrestSoftness;
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
                float  waveHeight  : TEXCOORD7;
            };

            // Deterministic spread of wave directions via the golden angle.
            float2 WaveDir(int i)
            {
                float ang = (float)i * 2.39996323;
                return float2(cos(ang), sin(ang));
            }

            // One Gerstner wave: returns displacement, accumulates analytic tangent/binormal.
            // Phase is evaluated at the UNDISPLACED base position to keep waves coherent.
            float3 GerstnerWave(float2 dir, float steepness, float wavelength, float amp, float speed,
                                float3 basePos, inout float3 tangent, inout float3 binormal)
            {
                float k = 2.0 * PI / max(0.01, wavelength);
                float c = sqrt(9.8 / k);
                float2 d = normalize(dir);
                float f = k * (dot(d, basePos.xz) - c * speed * _Time.y);
                float a = amp;
                float WA = k * a;
                float S = sin(f);
                float C = cos(f);
                float Q = steepness;

                tangent += float3(
                    -Q * d.x * d.x * WA * S,
                     d.x * WA * C,
                    -Q * d.x * d.y * WA * S);
                binormal += float3(
                    -Q * d.x * d.y * WA * S,
                     d.y * WA * C,
                    -Q * d.y * d.y * WA * S);

                return float3(Q * a * d.x * C, a * S, Q * a * d.y * C);
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 basePos = posWS;
                float3 tangent = float3(1, 0, 0);
                float3 binormal = float3(0, 0, 1);
                float waveHeight = 0.0;

            #if _FC_WATER_GERSTNER_ON
                int count = (int)_WaveCount;
                float steep = _WaveSteepness / max(1.0, (float)count);
                [loop]
                for (int i = 0; i < count; i++)
                {
                    float wl  = _WaveLength * pow(0.68, (float)i);
                    float amp = _WaveAmplitude * pow(0.74, (float)i);
                    float3 disp = GerstnerWave(WaveDir(i), steep, wl, amp, _WaveSpeed, basePos, tangent, binormal);
                    posWS += disp;
                    waveHeight += disp.y;
                }
            #endif

                float3 normalWS = normalize(cross(binormal, tangent));

                OUT.positionWS = posWS;
                OUT.positionHCS = TransformWorldToHClip(posWS);
                OUT.normalWS = normalWS;
                OUT.tangentWS = normalize(tangent);
                OUT.bitangentWS = normalize(binormal);
                OUT.uv = IN.uv;
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                OUT.fogCoord = ComputeFogFactor(OUT.positionHCS.z);
                OUT.waveHeight = waveHeight;
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
                // Tangent-space animated normal -> world space (TBN from Gerstner basis)
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
                refractionColor = _ShallowColor.rgb;
            #endif

                // Water depth from scene depth vs surface depth
                float sceneRawDepth = SampleSceneDepth(distortedUV);
                float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
                float waterDepth = max(0.0, sceneEyeDepth - surfaceDepth);

                // P10 — Beer-Lambert: scene color is attenuated per channel by depth, tinted toward deep color.
                float3 transmit = exp(-_ExtinctionRGB.rgb * waterDepth);
                refractionColor = refractionColor * transmit + _DeepColor.rgb * (1.0 - transmit);

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

                // --- Sun glint (P10 GGX against main directional light) ---
                Light mainLight = GetMainLight();
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float ndh = saturate(dot(nWS, halfDir));
                float rough = max(0.03, _Roughness);
                float a = rough * rough;
                float a2 = a * a;
                float denom = ndh * ndh * (a2 - 1.0) + 1.0;
                float ggx = a2 / max(1e-4, PI * denom * denom);
                baseColor += mainLight.color * (ggx * _SunGlintStrength) * fresnel;

                // --- P9 Foam (shoreline depth-fade + wave crest) ---
                float2 foamUV = IN.positionWS.xz * _FoamTiling.xy + _Time.y * _FoamScroll.xy;
                float foamTex = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, foamUV).r;
                float shoreFoam = 1.0 - saturate(waterDepth / max(0.001, _FoamShoreDistance));
                float crestFoam = saturate((IN.waveHeight - _FoamCrestThreshold) / max(0.001, _FoamCrestSoftness));
                float foamMask = saturate(max(shoreFoam, crestFoam)) * foamTex;
                baseColor = lerp(baseColor, _FoamColor.rgb, foamMask);

                baseColor = MixFog(baseColor, IN.fogCoord);
                return half4(baseColor, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
