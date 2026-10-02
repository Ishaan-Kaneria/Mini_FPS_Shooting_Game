// Shared by the FG_*_URP shaders. Mirrors the structure of URP's Lit pass so the shaders
// pick up lights, shadows, Forward+, fog, SSAO, lightmaps and adaptive probe volumes the
// same way Lit does. A shader supplies its properties and one function:
//     SurfaceData FGSurface(FGInput i)
#ifndef FG_COMMON_INCLUDED
#define FG_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceData.hlsl"

struct FGInput
{
    float2 uv;
    float3 positionOS;
    float3 positionWS;
    float3 normalWS;      // geometric, normalised
    float3 viewDirWS;
    float4 positionCS;    // xy = pixel coordinates
    half4  color;         // vertex colour
    float3x3 tbn;         // rows: tangent, bitangent, normal (world space)
};

SurfaceData FGDefaultSurface()
{
    SurfaceData s = (SurfaceData)0;
    s.albedo = half3(0.5, 0.5, 0.5);
    s.smoothness = 0.5;
    s.occlusion = 1;
    s.alpha = 1;
    s.normalTS = half3(0, 0, 1);
    return s;
}

#endif
