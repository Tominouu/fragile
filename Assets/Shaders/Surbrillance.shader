// Surbrillance d'un objet interactif survolé par la main : liseré lumineux (Fresnel) + léger voile, qui pulse doucement.
// Dessiné par-dessus l'objet (additif) : ne change pas ses matériaux, aucun mot-clé à activer.
Shader "Fragile/Surbrillance"
{
    Properties
    {
        [HDR] _Color("Couleur", Color) = (1.0, 0.82, 0.45, 1)
        _Rim("Liseré", Range(0.5, 6)) = 2.2
        _Fill("Voile", Range(0, 1)) = 0.12
        _Pulse("Pulsation", Range(0, 1)) = 0.35
        _Inflate("Débord (m)", Range(0, 0.01)) = 0.0015
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Surbrillance"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Rim, _Fill, _Pulse, _Inflate;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // Légèrement gonflé le long des normales (en mètres) : le liseré déborde de la silhouette.
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                float3 ws = TransformObjectToWorld(i.positionOS.xyz) + o.normalWS * _Inflate;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 n = normalize(i.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half rim = pow(1.0h - saturate(abs(dot(n, v))), _Rim);
                half pulse = 1.0h - _Pulse * 0.5h * (1.0h + sin(_Time.y * 5.0h));
                return half4(_Color.rgb * (rim + _Fill) * pulse, 1.0h);
            }
            ENDHLSL
        }
    }
}
