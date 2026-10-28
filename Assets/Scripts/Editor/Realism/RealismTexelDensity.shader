// FPSKitRealismAudit's texel density view: each surface coloured by how many of its base map's texels fall on a metre of it
// (screen-space derivatives of the texel coordinate against the world position), with a checker of 64-texel cells over it.
//   red under 128 px/m, orange 256, green 512 (the near-surface target), cyan 1024, blue 2048 and over.
Shader "Hidden/FPSKit/RealismTexelDensity"
{
    Properties
    {
        _TexSize ("Base map size (texels)", Vector) = (1024, 1024, 0, 0)
        _ST ("Base map tiling and offset", Vector) = (1, 1, 0, 0)
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            ZWrite On
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _TexSize, _ST;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 texel : TEXCOORD0; float3 world : TEXCOORD1; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.texel = (v.uv * _ST.xy + _ST.zw) * _TexSize.xy;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }
            float3 Ramp (float t)   // t: log2(density / 512), -2..+2
            {
                t = saturate((t + 2.0) / 4.0);
                float3 c0 = float3(0.85, 0.10, 0.08), c1 = float3(0.95, 0.55, 0.10), c2 = float3(0.20, 0.75, 0.25), c3 = float3(0.15, 0.75, 0.85), c4 = float3(0.15, 0.25, 0.90);
                return t < 0.25 ? lerp(c0, c1, t * 4.0) : t < 0.5 ? lerp(c1, c2, (t - 0.25) * 4.0) : t < 0.75 ? lerp(c2, c3, (t - 0.5) * 4.0) : lerp(c3, c4, (t - 0.75) * 4.0);
            }
            float4 frag (v2f i) : SV_Target
            {
                float2 tx = ddx(i.texel), ty = ddy(i.texel);
                float3 px = ddx(i.world), py = ddy(i.world);
                float texArea = abs(tx.x * ty.y - tx.y * ty.x);
                float worldArea = length(cross(px, py));
                float density = sqrt(texArea / max(worldArea, 1e-10));
                float3 c = Ramp(log2(max(density, 1.0) / 512.0));
                float2 cell = floor(i.texel / 64.0);
                float checker = fmod(abs(cell.x + cell.y), 2.0);
                return float4(c * (0.78 + 0.22 * checker), 1);
            }
            ENDCG
        }
    }
}
