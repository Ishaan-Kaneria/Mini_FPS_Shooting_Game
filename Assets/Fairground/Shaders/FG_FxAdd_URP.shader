// Additive, unlit effects for the Abandoned Fairground (our own code): fire blobs, rain streaks, light shafts,
// low mist. One shader so the Low tier can swap a single material.
//   _ViewFade   fades the surface where it turns edge-on to the camera (soft cone shafts)
//   _SoftDepth  fades where the surface meets geometry, in metres (needs the camera depth texture; 0 = off)
//   _ScrollX/Y  scrolls the texture (drifting mist)
// Multiplies vertex colour so particle systems can fade it out.
Shader "Fairground/FG_FxAdd_URP"
{
    Properties
    {
        _MainTex ("Texture (grey = intensity)", 2D) = "white" {}
        [HDR] _Color ("Tint", Color) = (1, 1, 1, 1)
        _Tiling ("Tiling", Float) = 1
        _ScrollX ("Scroll X", Float) = 0
        _ScrollY ("Scroll Y", Float) = 0
        _ViewFade ("View-angle edge fade", Range(0, 6)) = 0
        _SoftDepth ("Soft depth (m)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Tiling, _ScrollX, _ScrollY, _ViewFade, _SoftDepth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                half4  color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs v = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = v.positionCS;
                o.positionWS = v.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                o.color = input.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv * _Tiling + float2(_ScrollX, _ScrollY) * _Time.y;
                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb * _Color.rgb * i.color.rgb * i.color.a;
                if (_ViewFade > 0.001h)
                {
                    float3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                    c *= pow(saturate(abs(dot(normalize(i.normalWS), viewDir))), _ViewFade);
                }
                if (_SoftDepth > 0.001h)
                {
                    float2 suv = i.positionCS.xy / _ScaledScreenParams.xy;
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    float surfEye = LinearEyeDepth(i.positionWS, GetWorldToViewMatrix());
                    c *= saturate((sceneEye - surfEye) / _SoftDepth);
                }
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
