// FPSKitRealismAudit's object-id pass: every surface drawn flat in its object's id colour (no light, no fog), so the share
// of the screen each object takes along the player's route can be counted exactly. Alpha-tested leaves keep their holes.
Shader "Hidden/FPSKit/RealismId"
{
    Properties
    {
        _Id ("Id", Vector) = (0, 0, 0, 1)   // a vector, not a colour: a colour is converted to linear on its way in
        _MainTex ("Alpha source", 2D) = "white" {}
        _Cutoff ("Cutoff", Float) = 0.5
        _Clip ("Clip", Float) = 0
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
            float4 _Id;
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Cutoff, _Clip;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.uv, _MainTex); return o; }
            float4 frag (v2f i) : SV_Target
            {
                if (_Clip > 0.5) clip(tex2D(_MainTex, i.uv).a - _Cutoff);
                return _Id;
            }
            ENDCG
        }
    }
}
