// World-space triplanar PBR for the fairground's props and structures (our own code).
// One CC0 material set (albedo, GL normal, roughness, AO) is projected along the three world axes, so a
// box of any size keeps its texel density and never stretches. Normals are blended with the whiteout method.
// _Wetness darkens and glosses for the night look. _FG_LOW (Low tier) projects along the dominant axis only.
Shader "Fairground/FG_WorldPBR_URP"
{
    Properties
    {
        _BaseMap ("Albedo (RGB)", 2D) = "white" {}
        [NoScaleOffset] _BumpMap ("Normal (GL)", 2D) = "bump" {}
        [NoScaleOffset] _RoughMap ("Roughness (R)", 2D) = "white" {}
        [NoScaleOffset] _AOMap ("Ambient Occlusion (R)", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Tiling ("Metres Per Tile", Float) = 3
        _BumpScale ("Normal Strength", Range(0, 2)) = 1
        _SmoothScale ("Smoothness Scale", Range(0, 1.5)) = 1
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Wetness ("Wetness (night look)", Range(0, 1)) = 0.25
        _Sharp ("Projection Blend Sharpness", Range(1, 8)) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        LOD 300

        HLSLINCLUDE
        #define FG_NORMAL_WORLD
        #include "FG_Common.hlsl"

        TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);
        TEXTURE2D(_RoughMap); SAMPLER(sampler_RoughMap);
        TEXTURE2D(_AOMap);    SAMPLER(sampler_AOMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _Tint;
            half _Tiling, _BumpScale, _SmoothScale, _Metallic, _Wetness, _Sharp;
        CBUFFER_END

        SurfaceData FGSurface(FGInput i)
        {
            float3 p = i.positionWS / max(_Tiling, 0.01);
            float3 n = i.normalWS;
            half3 w = pow(abs(half3(n)), _Sharp);
            w /= max(w.x + w.y + w.z, 0.0001h);
            float2 ux = p.zy, uy = p.xz, uz = p.xy;

            half3 alb; half rough; half ao; half3 nWS;
        #if defined(_FG_LOW)
            // Low tier: the dominant axis only, one read per map.
            float2 uv = (w.y > w.x && w.y > w.z) ? uy : (w.x > w.z ? ux : uz);
            alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
            rough = SAMPLE_TEXTURE2D(_RoughMap, sampler_RoughMap, uv).r;
            ao = SAMPLE_TEXTURE2D(_AOMap, sampler_AOMap, uv).r;
            nWS = n;
        #else
            alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, ux).rgb * w.x
                + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uy).rgb * w.y
                + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uz).rgb * w.z;
            rough = SAMPLE_TEXTURE2D(_RoughMap, sampler_RoughMap, ux).r * w.x
                  + SAMPLE_TEXTURE2D(_RoughMap, sampler_RoughMap, uy).r * w.y
                  + SAMPLE_TEXTURE2D(_RoughMap, sampler_RoughMap, uz).r * w.z;
            ao = SAMPLE_TEXTURE2D(_AOMap, sampler_AOMap, uy).r;
            half3 tx = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, ux), _BumpScale);
            half3 ty = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uy), _BumpScale);
            half3 tz = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uz), _BumpScale);
            // whiteout blend (Ben Golus): lay each projection's normal over the geometric normal
            tx = half3(tx.xy + n.zy, abs(tx.z) * n.x);
            ty = half3(ty.xy + n.xz, abs(ty.z) * n.y);
            tz = half3(tz.xy + n.xy, abs(tz.z) * n.z);
            nWS = normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
        #endif

            half sm = saturate((1.0h - rough) * _SmoothScale);
            sm = lerp(sm, max(sm, 0.85h), _Wetness * 0.7h);

            SurfaceData s = FGDefaultSurface();
            s.albedo = alb * _Tint.rgb * lerp(1.0h, 0.6h, _Wetness);
            s.metallic = _Metallic;
            s.smoothness = sm;
            s.occlusion = ao;
            s.normalTS = nWS;      // world space, see FG_NORMAL_WORLD
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
