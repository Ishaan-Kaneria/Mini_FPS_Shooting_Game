// URP rewrite of Flooded_Grounds/PBR_TopBlend (our own code, same property names so a converted
// material copy keeps its textures). A second material (moss, dirt, mud, leaves, rust) is laid over
// every UP-facing surface of the base material, broken up by a mask and a detail normal. This is
// the pack's main "everything is weathered" trick.
// Extra: _Wetness darkens the albedo and raises smoothness (0 = identical to the original).
Shader "Fairground/FG_TopBlend_URP"
{
    Properties
    {
        _MainTex ("Base Albedo (RGB)", 2D) = "white" {}
        _Spc ("Base Metalness (R) Smoothness (A)", 2D) = "black" {}
        _BumpMap ("Base Normal", 2D) = "bump" {}
        _AO ("Base AO", 2D) = "white" {}
        _layer1Tex ("Top Albedo (RGB) Smoothness (A)", 2D) = "white" {}
        _layer1Metal ("Top Metalness", Range(0, 1)) = 0
        _layer1Norm ("Top Normal", 2D) = "bump" {}
        _layer1Breakup ("Top Breakup (R)", 2D) = "white" {}
        _layer1BreakupAmnt ("Top Breakup Amount", Range(0, 1)) = 0.5
        _layer1Tiling ("Top Tiling", Float) = 10
        _Power ("Top Blend Amount", Float) = 1
        _Shift ("Top Blend Height", Float) = 1
        _DetailBump ("Detail Normal", 2D) = "bump" {}
        _DetailInt ("Detail Normal Intensity", Range(0, 1)) = 0.4
        _DetailTiling ("Detail Normal Tiling", Float) = 2
        _Wetness ("Wetness (night look)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        LOD 300

        HLSLINCLUDE
        #include "FG_Common.hlsl"

        TEXTURE2D(_MainTex);      SAMPLER(sampler_MainTex);
        TEXTURE2D(_Spc);          SAMPLER(sampler_Spc);
        TEXTURE2D(_BumpMap);      SAMPLER(sampler_BumpMap);
        TEXTURE2D(_AO);           SAMPLER(sampler_AO);
        TEXTURE2D(_layer1Tex);    SAMPLER(sampler_layer1Tex);
        TEXTURE2D(_layer1Norm);   SAMPLER(sampler_layer1Norm);
        TEXTURE2D(_layer1Breakup);SAMPLER(sampler_layer1Breakup);
        TEXTURE2D(_DetailBump);   SAMPLER(sampler_DetailBump);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half _layer1Metal, _layer1BreakupAmnt, _layer1Tiling, _Power, _Shift;
            half _DetailInt, _DetailTiling, _Wetness;
        CBUFFER_END

        SurfaceData FGSurface(FGInput i)
        {
            float2 uv = i.uv * _MainTex_ST.xy + _MainTex_ST.zw;
            float2 uvTop = uv * _layer1Tiling;

            half3 main = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
            half3 norm = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv));
            half4 spec = SAMPLE_TEXTURE2D(_Spc, sampler_Spc, uv);
            half ao = SAMPLE_TEXTURE2D(_AO, sampler_AO, uv).r;
            half4 top = SAMPLE_TEXTURE2D(_layer1Tex, sampler_layer1Tex, uvTop);
            half3 topNorm = UnpackNormal(SAMPLE_TEXTURE2D(_layer1Norm, sampler_layer1Norm, uvTop));
            half breakup = SAMPLE_TEXTURE2D(_layer1Breakup, sampler_layer1Breakup, uvTop).r;
        #if defined(_FG_LOW)
            half3 detail = half3(0, 0, 1);      // Low tier: skip the detail normal read
        #else
            half3 detail = UnpackNormal(SAMPLE_TEXTURE2D(_DetailBump, sampler_DetailBump, uv * _DetailTiling));
        #endif

            // Where is "up", after the base normal is perturbed by the top layer's relief?
            half3 modNormal = norm + half3(topNorm.r * 0.6h, topNorm.g * 0.6h, 0);
            half upness = normalize(mul(modNormal, i.tbn)).y;

            half blend = (upness * _Power + _Shift) * lerp(1.0h, breakup, _layer1BreakupAmnt);
            blend = saturate(blend * blend * blend);   // cubed: a soft edge that hugs the top

            half3 n = lerp(norm, topNorm, blend) + detail * half3(_DetailInt, _DetailInt, 0);

            SurfaceData s = FGDefaultSurface();
            s.albedo = lerp(main, top.rgb, blend);
            s.metallic = lerp(spec.r, _layer1Metal, blend);
            s.smoothness = lerp(spec.a, top.a, blend);
            s.occlusion = ao;
            s.normalTS = normalize(n);

            s.albedo *= lerp(1.0h, 0.55h, _Wetness);
            s.smoothness = lerp(s.smoothness, max(s.smoothness, 0.85h), _Wetness * 0.7h);
            return s;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FGForwardVertex
            #pragma fragment FGForwardFragment
            #pragma shader_feature_local _FG_LOW          // Low tier: fewer texture reads, no depth fade

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            // Lightmap/shadowmask variants are dropped on purpose: the fairground is lit by realtime lights and
            // adaptive probe volumes, and every dropped keyword halves the variant count (WebGL/Android build size).
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "FG_ForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FGDepthVertex
            #pragma fragment FGDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define FG_PASS_SHADOW
            #include "FG_Common.hlsl"
            #include "FG_DepthPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FGDepthVertex
            #pragma fragment FGDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #define FG_PASS_DEPTH
            #include "FG_Common.hlsl"
            #include "FG_DepthPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FGDepthVertex
            #pragma fragment FGDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #define FG_PASS_DEPTHNORMALS
            #include "FG_Common.hlsl"
            #include "FG_DepthPasses.hlsl"
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
