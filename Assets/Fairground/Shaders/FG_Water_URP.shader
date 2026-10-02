// Dark, murky floodwater for the Abandoned Fairground. Our own code; it replaces
// Flooded_Grounds/PBR_Water (an opaque plane) and keeps what made it work:
// two normal maps scrolling in different directions, a vertex wobble, very high smoothness.
// What it adds:
//   * depth darkening: shallow water is a lighter murky green, deep water almost black
//   * soft shoreline: the water fades out where it meets the ground (needs the camera depth
//     texture; the Mobile pipeline asset has it off, so turn _DEPTH_FADE off there)
//   * transparency, so submerged litter and kerbs show through the shallows
// Flat water only: the normal map is applied in world space, so use it on horizontal planes.
Shader "Fairground/FG_Water_URP"
{
    Properties
    {
        _ShallowColor ("Shallow Colour", Color) = (0.10, 0.13, 0.10, 1)
        _DeepColor ("Deep Colour", Color) = (0.012, 0.022, 0.022, 1)
        _ShallowAlpha ("Shallow Opacity", Range(0, 1)) = 0.55
        _DeepAlpha ("Deep Opacity", Range(0, 1)) = 0.96
        _DepthMax ("Depth For Full Darkness (m)", Float) = 1.2
        _EdgeSoftness ("Shoreline Softness (m)", Float) = 0.25
        [Toggle(_DEPTH_FADE)] _UseDepth ("Use Depth Texture", Float) = 1

        _Smoothness ("Smoothness", Range(0, 1)) = 0.95
        _Emis ("Self Illumination", Range(0, 1)) = 0.0

        [NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
        [NoScaleOffset] _BumpMap2 ("Static Ripple Normal", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 1
        _BumpLerp ("Static Ripple Blend", Range(0, 1)) = 0.25
        _Tiling ("Metres Per Tile", Float) = 8
        _ScrollSpeed ("Scroll (tiles/s)", Float) = 0.03

        _WaveHeight ("Wobble Height (m)", Float) = 0.03
        _WaveSpeed ("Wobble Speed", Float) = 1.2
        _WaveLength ("Wobble Length (m)", Float) = 6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        LOD 300

        HLSLINCLUDE
        #define FG_TRANSPARENT
        #define FG_NORMAL_WORLD
        #define FG_VERTEX_HOOK
        #define _ALPHAPREMULTIPLY_ON 1
        #include "FG_Common.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        TEXTURE2D(_BumpMap);  SAMPLER(sampler_BumpMap);
        TEXTURE2D(_BumpMap2); SAMPLER(sampler_BumpMap2);

        CBUFFER_START(UnityPerMaterial)
            half4 _ShallowColor, _DeepColor;
            half _ShallowAlpha, _DeepAlpha, _DepthMax, _EdgeSoftness, _UseDepth;
            half _Smoothness, _Emis, _BumpScale, _BumpLerp;
            float _Tiling, _ScrollSpeed, _WaveHeight, _WaveSpeed, _WaveLength;
        CBUFFER_END

        float3 FGDisplaceWS(float3 p)
        {
            float phase = _Time.y * _WaveSpeed;
            float k = 6.2831853 / max(_WaveLength, 0.01);
            p.y += sin(phase + (p.x + p.z * 2.0) * k) * _WaveHeight;
            return p;
        }

        SurfaceData FGSurface(FGInput i)
        {
            float t = _Time.y * _ScrollSpeed;
            float2 uv = i.positionWS.xz / max(_Tiling, 0.01);

            half3 n1 = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv + float2(t, t * 0.5)), _BumpScale);
            half3 nTS = n1;
        #if !defined(_FG_LOW)
            half3 n2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv * 0.73 + float2(-t * 0.8, t * 0.3)), _BumpScale);
            half3 n3 = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap2, sampler_BumpMap2, uv * 2.1), _BumpScale);
            nTS = normalize(half3(n1.xy + n2.xy, n1.z * n2.z));
            nTS = normalize(lerp(nTS, half3(nTS.xy + n3.xy, nTS.z), _BumpLerp));
        #endif
            half3 nWS = normalize(half3(nTS.x, nTS.z, nTS.y));      // flat water: tangent space = world

            half depthT = 0.6h;     // Low tier (or no depth texture): a fixed mid-depth murk
            half edge = 1.0h;
            #if defined(_DEPTH_FADE) && !defined(_FG_LOW)
                float2 screenUV = i.positionCS.xy / _ScaledScreenParams.xy;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfEye = LinearEyeDepth(i.positionWS, GetWorldToViewMatrix());
                half thickness = max(sceneEye - surfEye, 0);
                depthT = saturate(thickness / max(_DepthMax, 0.001h));
                edge = saturate(thickness / max(_EdgeSoftness, 0.001h));
            #endif

            half3 colour = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthT);
            half alpha = lerp(_ShallowAlpha, _DeepAlpha, depthT) * edge;

            SurfaceData s = FGDefaultSurface();
            s.albedo = colour;
            s.metallic = 0;
            s.smoothness = _Smoothness;
            s.emission = colour * _Emis;
            s.alpha = alpha;
            s.normalTS = nWS;      // world space, see FG_NORMAL_WORLD
            return s;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FGForwardVertex
            #pragma fragment FGForwardFragment
            #pragma shader_feature_local _FG_LOW          // Low tier: fewer texture reads, no depth fade
            #pragma shader_feature_local_fragment _DEPTH_FADE

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            // Lightmap/shadowmask variants are dropped on purpose: the fairground is lit by realtime lights and
            // adaptive probe volumes, and every dropped keyword halves the variant count (WebGL/Android build size).
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing

            #include "FG_ForwardPass.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
