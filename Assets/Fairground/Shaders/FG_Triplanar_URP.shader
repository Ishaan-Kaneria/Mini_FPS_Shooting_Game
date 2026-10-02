// URP rewrite of Flooded_Grounds/Triplanar_BumpSpec (our own code, same property names).
// Projects one texture along the three object-space axes and blends by normal, so meshes with
// no usable UVs (the pack's bushes) still get a texture. Alpha-tested, vertex-colour tinted.
// Extra: _Smoothness (the original was pure Lambert, so default is matte).
Shader "Fairground/FG_Triplanar_URP"
{
    Properties
    {
        _TexScale ("Tex Scale", Range(0.1, 10.0)) = 1.0
        _BlendPlateau ("Blend Plateau", Range(0.0, 1.0)) = 0.2
        _MainTex ("Base (RGB) Alpha (A)", 2D) = "white" {}
        _BumpMap1 ("Normal Map", 2D) = "bump" {}
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        LOD 300

        HLSLINCLUDE
        #define FG_ALPHATEST
        #include "FG_Common.hlsl"

        TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
        TEXTURE2D(_BumpMap1); SAMPLER(sampler_BumpMap1);

        CBUFFER_START(UnityPerMaterial)
            half _TexScale, _BlendPlateau, _Cutoff, _Smoothness, _Cull;
        CBUFFER_END

        half3 FGTriWeights(float3 normalOS)
        {
            half3 w = abs(normalOS);
            w = max(w - _BlendPlateau, 0);
            return w / max(w.x + w.y + w.z, 0.0001h);
        }

        float FGAlpha(float2 uv, float3 positionOS)
        {
            // Without a normal here the shadow/depth passes use an even blend, which is
            // close enough for a clip mask and keeps those passes cheap.
            float3 p = positionOS * _TexScale;
            half a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, p.yz).a
                   + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, p.zx).a
                   + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, p.xy).a;
            return a * (1.0 / 3.0);
        }

        SurfaceData FGSurface(FGInput i)
        {
            // The mesh normal in object space, rebuilt from the world normal.
            float3 nOS = normalize(TransformWorldToObjectNormal(i.normalWS));
            half3 w = FGTriWeights(nOS);
            float3 p = i.positionOS * _TexScale;
            float2 c1 = p.yz, c2 = p.zx, c3 = p.xy;

            half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, c1) * w.x
                      + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, c2) * w.y
                      + SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, c3) * w.z;

            half2 b1 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap1, sampler_BumpMap1, c1)).xy;
            half2 b2 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap1, sampler_BumpMap1, c2)).xy;
            half2 b3 = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap1, sampler_BumpMap1, c3)).xy;
            half3 bump = half3(0, b1.x, b1.y) * w.x + half3(b2.y, 0, b2.x) * w.y + half3(b3.x, b3.y, 0) * w.z;

            SurfaceData s = FGDefaultSurface();
            s.albedo = col.rgb * i.color.rgb;
            s.alpha = col.a;
            s.smoothness = _Smoothness;
            s.normalTS = normalize(half3(0, 0, 1) + bump);
            return s;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull [_Cull]

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
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FGDepthVertex
            #pragma fragment FGDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define FG_PASS_SHADOW
            #include "FG_DepthPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FGDepthVertex
            #pragma fragment FGDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #define FG_PASS_DEPTH
            #include "FG_DepthPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FGDepthVertex
            #pragma fragment FGDepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #define FG_PASS_DEPTHNORMALS
            #include "FG_DepthPasses.hlsl"
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
