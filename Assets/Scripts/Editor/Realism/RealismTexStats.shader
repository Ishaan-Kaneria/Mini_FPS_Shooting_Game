// FPSKitRealismAudit's texture reader: copies a texture as the shader sees it (sRGB colour decoded to linear), or a normal map
// unpacked to its tangent-space normal (x, y, z mapped to 0..1), into a float target small enough to read back and measure.
Shader "Hidden/FPSKit/RealismTexStats"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Mode ("0 colour, 1 normal map", Float) = 0
    }
    SubShader
    {
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _Mode;
            float4 frag (v2f_img i) : SV_Target
            {
                float4 c = tex2Dlod(_MainTex, float4(i.uv, 0, 0));   // the full-size level: a lower one averages relief away
                if (_Mode > 0.5) return float4(normalize(UnpackNormal(c)) * 0.5 + 0.5, 1);
                return c;
            }
            ENDCG
        }
    }
}
