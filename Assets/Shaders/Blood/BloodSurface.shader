// Blood lying on a surface (BloodFX splats and pools, EnemyGore wounds). Our own code, 2026-11-01.
//
// Why not URP Lit: blood is a near-black red with a wet film on it, so almost all of what the eye reads is its reflection.
// URP Lit's reflection is the sky's (or, with environment reflections off, a flat sky colour), at outdoor strength wherever
// the pool lies. Indoors, with the eye opened up several stops for the room, that sky sheen came out cream-white
// (his play-test, 2026-10-31). Here the reflection is brought to the light that actually reaches the spot: the probe's
// reflection is divided by the probe's own light in the surface's direction and multiplied by the light the probes give
// there (reflection normalisation). In the open the two are the same and the pool mirrors the sky; in a room it mirrors at
// the room's level, whatever reflection probe is or is not on (Medium has no room probes).
// Fresnel is Schlick's with water's 0.02, so a pool seen from a standing height is its own dark red with a wet edge and
// sharp highlights from the sun and lamps, and turns to a mirror only at a grazing angle.
Shader "FPSKit/BloodSurface"
{
    Properties
    {
        _BaseMap ("Shape (A) and shade (RGB)", 2D) = "white" {}
        _BaseColor ("Colour (linear) and opacity", Color) = (0.28, 0.006, 0.008, 0.97)
        _Smoothness ("Smoothness where thick", Range(0, 1)) = 0.86
        _EdgeSmoothness ("Smoothness at the thin edge", Range(0, 1)) = 0.55
        _Reflectance ("Reflectance at normal incidence", Range(0, 0.08)) = 0.02
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BloodVertex
            #pragma fragment BloodFragment

            // Transparent: shadows come from the shadow map, never the screen-space one (URP's Shadows.hlsl reads this).
            #define _SURFACE_TYPE_TRANSPARENT 1
            #define _SPECULAR_SETUP 1

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Smoothness, _EdgeSmoothness, _Reflectance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 texcoord   : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                half4 fogFactorAndVertexLight : TEXCOORD3;
            #else
                half  fogFactor   : TEXCOORD3;
            #endif
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);
            #ifdef USE_APV_PROBE_OCCLUSION
                float4 probeOcclusion : TEXCOORD5;
            #endif
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings BloodVertex(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS);
                o.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.positionCS = p.positionCS;
                half fog = 0;
            #if !defined(_FOG_FRAGMENT)
                fog = ComputeFogFactor(p.positionCS.z);
            #endif
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                o.fogFactorAndVertexLight = half4(fog, VertexLighting(p.positionWS, n.normalWS));
            #else
                o.fogFactor = fog;
            #endif
                OUTPUT_SH4(p.positionWS, o.normalWS.xyz, GetWorldSpaceNormalizeViewDir(p.positionWS), o.vertexSH, o.probeOcclusion);
                return o;
            }

            half4 BloodFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half alpha = tex.a * _BaseColor.a;
                clip(alpha - 0.004h);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
            #endif
            #ifdef _ADDITIONAL_LIGHTS_VERTEX
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
            #else
                inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
            #endif
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
                float4 probeOcclusion = 1;
            #ifdef USE_APV_PROBE_OCCLUSION
                probeOcclusion = input.probeOcclusion;
            #endif
                inputData.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(inputData.positionWS), inputData.normalWS,
                                              inputData.viewDirectionWS, input.positionCS.xy, probeOcclusion, inputData.shadowMask);
            #else
                inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
            #endif

                // Thick blood is a smooth film; the thin rim of a splat dries first.
                half thick = saturate(tex.a * 1.6h);
                SurfaceData s = (SurfaceData)0;
                s.albedo = tex.rgb * _BaseColor.rgb;
                s.specular = _Reflectance.xxx;
                s.smoothness = lerp(_EdgeSmoothness, _Smoothness, thick);
                s.occlusion = 1;
                s.alpha = alpha;

                BRDFData brdf;
                InitializeBRDFData(s, brdf);
                half3 N = inputData.normalWS, V = inputData.viewDirectionWS;

                // Direct: the sun with its shadow, and every other light, with their sharp wet highlights.
                half4 shadowMask = CalculateShadowMask(inputData);
                AmbientOcclusionFactor ao = (AmbientOcclusionFactor)0;
                ao.directAmbientOcclusion = 1; ao.indirectAmbientOcclusion = 1;
                Light mainLight = GetMainLight(inputData, shadowMask, ao);
                half3 color = LightingPhysicallyBased(brdf, mainLight, N, V);
            #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
            #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                    Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
                    color += LightingPhysicallyBased(brdf, light, N, V);
                }
            #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
                    color += LightingPhysicallyBased(brdf, light, N, V);
                LIGHT_LOOP_END
            #endif
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                color += inputData.vertexLighting * brdf.diffuse;
            #endif

                // Indirect diffuse: the probes' light where it lies (the same light the floor under it gets).
                color += inputData.bakedGI * brdf.diffuse;

                // Indirect specular, normalised: the reflection divided by that reflection's own light toward the surface
                // (its blurriest mip) and multiplied by the light the probes give here. Never brighter than the reflection.
                half3 R = reflect(-V, N);
                half3 env = GlossyEnvironmentReflection(R, inputData.positionWS, brdf.perceptualRoughness, 1.0h, inputData.normalizedScreenSpaceUV);
                half3 envLight = GlossyEnvironmentReflection(N, inputData.positionWS, 1.0h, 1.0h, inputData.normalizedScreenSpaceUV);
                half norm = saturate(Luminance(inputData.bakedGI) / max(Luminance(envLight), 1e-4h));
                half NoV = saturate(dot(N, V));
                half fresnel = _Reflectance + (1.0h - _Reflectance) * pow(1.0h - NoV, 5.0h);
                fresnel *= lerp(0.3h, 1.0h, s.smoothness);     // a rough, drying rim reflects less at grazing
                // The film's reflection takes the sky's brightness, not its colour: blue added to a dark red drew a purple pool (his screenshot, 2026-11-04).
                color += Luminance(env) * half3(1.0h, 0.5h, 0.45h) * norm * fresnel;

                color = MixFog(color, inputData.fogCoord);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
