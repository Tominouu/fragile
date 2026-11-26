// Marque d'impact posée sur une surface (marbre du comptoir) : cratère de pierre écrasée (clair), fissures et rebord sombres.
// Mélange alpha prémultiplié, éclairage simple (light probes + lumière principale) : léger sur Quest.
Shader "Fragile/Impact"
{
    Properties
    {
        [NoScaleOffset] _BaseMap("Impacts (couleur + alpha)", 2D) = "clear" {}
        _Strength("Intensité", Range(0, 1.5)) = 1
        _Brightness("Luminosité", Range(0, 3)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Impact"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Strength, _Brightness;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half3 light : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                // Éclairage par sommet : ambiance (light probes) + lumière principale.
                half3 n = TransformObjectToWorldNormal(i.normalOS);
                Light main = GetMainLight();
                o.light = SampleSH(n) + main.color * saturate(dot(n, main.direction)) * 0.5h;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half a = saturate(t.a * _Strength);
                half3 c = t.rgb * i.light * _Brightness;
                return half4(c * a, a);
            }
            ENDHLSL
        }
    }
}
