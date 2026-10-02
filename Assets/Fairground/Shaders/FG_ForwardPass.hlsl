// Forward pass shared by the FG_*_URP shaders (see FG_Common.hlsl).
// Optional switches, defined by the shader before including this file:
//   FG_ALPHATEST        clip(surface.alpha - _Cutoff)
//   FG_TRANSPARENT      keep the surface alpha (otherwise output alpha is 1)
//   FG_NORMAL_WORLD     surface.normalTS is a WORLD-space normal (flat water)
//   FG_VERTEX_HOOK      shader provides float3 FGDisplaceWS(float3 positionWS)
#ifndef FG_FORWARD_PASS_INCLUDED
#define FG_FORWARD_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
    float2 texcoord     : TEXCOORD0;
    float2 staticLightmapUV  : TEXCOORD1;
    float2 dynamicLightmapUV : TEXCOORD2;
    half4  color        : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv           : TEXCOORD0;
    float3 positionWS   : TEXCOORD1;
    float3 normalWS     : TEXCOORD2;
    half4  tangentWS    : TEXCOORD3;    // xyz tangent, w sign
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half4 fogFactorAndVertexLight : TEXCOORD5;
#else
    half  fogFactor     : TEXCOORD5;
#endif
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord  : TEXCOORD6;
#endif
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
#ifdef DYNAMICLIGHTMAP_ON
    float2 dynamicLightmapUV : TEXCOORD9;
#endif
#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD10;
#endif
    float3 positionOS   : TEXCOORD11;
    half4  color        : TEXCOORD12;
    float4 positionCS   : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings FGForwardVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
#if defined(FG_VERTEX_HOOK)
    positionWS = FGDisplaceWS(positionWS);
#endif
    VertexPositionInputs vertexInput;
    vertexInput.positionWS = positionWS;
    vertexInput.positionVS = TransformWorldToView(positionWS);
    vertexInput.positionCS = TransformWorldToHClip(positionWS);
    float4 ndc = vertexInput.positionCS * 0.5f;
    vertexInput.positionNDC.xy = float2(ndc.x, ndc.y * _ProjectionParams.x) + ndc.w;
    vertexInput.positionNDC.zw = vertexInput.positionCS.zw;

    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);

    half fogFactor = 0;
    #if !defined(_FOG_FRAGMENT)
        fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
    #endif

    output.uv = input.texcoord;
    output.positionWS = vertexInput.positionWS;
    output.positionOS = input.positionOS.xyz;
    output.color = input.color;
    output.normalWS = normalInput.normalWS;
    real sign = input.tangentOS.w * GetOddNegativeScale();
    output.tangentWS = half4(normalInput.tangentWS.xyz, sign);

    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
#ifdef DYNAMICLIGHTMAP_ON
    output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif
    OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
#else
    output.fogFactor = fogFactor;
#endif
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(vertexInput);
#endif
    output.positionCS = vertexInput.positionCS;
    return output;
}

half4 FGForwardFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
    float sgn = input.tangentWS.w;
    float3 bitangent = sgn * cross(normalWS, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, normalWS);

    FGInput fi;
    fi.uv = input.uv;
    fi.positionOS = input.positionOS;
    fi.positionWS = input.positionWS;
    fi.normalWS = normalWS;
    fi.viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    fi.positionCS = input.positionCS;
    fi.color = input.color;
    fi.tbn = tangentToWorld;

    SurfaceData surfaceData = FGSurface(fi);
#if defined(FG_ALPHATEST)
    clip(surfaceData.alpha - _Cutoff);
#endif
#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
#if defined(FG_NORMAL_WORLD)
    inputData.normalWS = normalize(surfaceData.normalTS);
#else
    inputData.tangentToWorld = tangentToWorld;
    inputData.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(surfaceData.normalTS, tangentToWorld));
#endif
    inputData.viewDirectionWS = fi.viewDirWS;
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
    inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
#else
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
#endif
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

#if defined(DYNAMICLIGHTMAP_ON)
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(input.vertexSH,
        GetAbsolutePositionWS(inputData.positionWS),
        inputData.normalWS,
        inputData.viewDirectionWS,
        input.positionCS.xy,
        input.probeOcclusion,
        inputData.shadowMask);
#else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#endif

    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
#if !defined(FG_TRANSPARENT)
    color.a = 1;
#endif
    return color;
}

#endif
