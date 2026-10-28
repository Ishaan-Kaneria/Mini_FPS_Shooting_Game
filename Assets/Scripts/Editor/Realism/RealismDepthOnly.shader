// FPSKitRealismAudit: a surface that writes depth and no colour (with _ZWrite 0, nothing at all). Solid surfaces drawn this
// way hide the transparent ones behind them in the transparent id pass; transparent overlays drawn with _ZWrite 0 are left
// out of the albedo and texel views so the wall under a weathering strip is what gets checked.
Shader "Hidden/FPSKit/RealismDepthOnly"
{
    Properties
    {
        _ZWrite ("Write depth", Float) = 1
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            ZWrite [_ZWrite]
            ColorMask 0
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert (float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
            float4 frag () : SV_Target { return 0; }
            ENDCG
        }
    }
}
