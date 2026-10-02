// ShadowCaster, DepthOnly and DepthNormals for the FG_*_URP opaque shaders.
// Select the pass with FG_PASS_SHADOW / FG_PASS_DEPTH / FG_PASS_DEPTHNORMALS.
// With FG_ALPHATEST the shader must provide: float FGAlpha(float2 uv, float3 positionOS)
#ifndef FG_DEPTH_PASSES_INCLUDED
#define FG_DEPTH_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#if defined(FG_PASS_SHADOW)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#endif
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

#if defined(FG_PASS_SHADOW)
float3 _LightDirection;
float3 _LightPosition;
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float2 texcoord   : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
#if defined(FG_ALPHATEST)
    float2 uv         : TEXCOORD0;
    float3 positionOS : TEXCOORD1;
#endif
#if defined(FG_PASS_DEPTHNORMALS)
    half3 normalWS    : TEXCOORD2;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings FGDepthVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

#if defined(FG_ALPHATEST)
    output.uv = input.texcoord;
    output.positionOS = input.positionOS.xyz;
#endif

#if defined(FG_PASS_SHADOW)
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 lightDirectionWS = normalize(_LightPosition - positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    output.positionCS = ApplyShadowClamping(positionCS);
#else
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
#endif

#if defined(FG_PASS_DEPTHNORMALS)
    output.normalWS = half3(TransformObjectToWorldNormal(input.normalOS));
#endif
    return output;
}

#if defined(FG_PASS_DEPTHNORMALS)
half4 FGDepthFragment(Varyings input) : SV_Target
#else
half FGDepthFragment(Varyings input) : SV_Target
#endif
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

#if defined(FG_ALPHATEST)
    clip(FGAlpha(input.uv, input.positionOS) - _Cutoff);
#endif
#if defined(LOD_FADE_CROSSFADE)
    LODFadeCrossFade(input.positionCS);
#endif

#if defined(FG_PASS_DEPTHNORMALS)
    float3 normalWS = normalize(input.normalWS);
    #if defined(_GBUFFER_NORMALS_OCT)
        float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
        float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
        half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
        return half4(packedNormalWS, 0.0);
    #else
        return half4(normalWS, 0.0);
    #endif
#elif defined(FG_PASS_SHADOW)
    return 0;
#else
    return input.positionCS.z;
#endif
}

#endif
