// Voile posé autour de la tête pendant le Trailer (sphère vue de l'intérieur, enfant de la caméra XR) :
// - _Fade : fondu plein écran vers _FadeColor (noir au début et à la fin) ;
// - _Vignette : bord de vision rougi par la rage (0 = rien), plus fort sur les côtés qu'au centre.
// Dessiné par-dessus tout (ZTest Always, file Overlay). Compatible stéréo « single pass instanced » (Quest).
Shader "Fragile/Trailer/Voile"
{
    Properties
    {
        _FadeColor ("Couleur du fondu", Color) = (0, 0, 0, 1)
        _Fade ("Fondu", Range(0, 1)) = 1
        _VignetteColor ("Couleur de la rage", Color) = (0.55, 0, 0, 1)
        _Vignette ("Rage", Range(0, 1)) = 0
        _Inner ("Début du bord (cos)", Range(0, 1)) = 0.92
        _Outer ("Bord plein (cos)", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Voile"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FadeColor;
                half _Fade;
                half4 _VignetteColor;
                half _Vignette;
                half _Inner;
                half _Outer;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.dirOS = v.positionOS.xyz;   // la sphère suit la tête : +Z local = regard
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half c = normalize(i.dirOS).z;
                half edge = saturate((_Inner - c) / max(_Inner - _Outer, 1e-3));
                half vig = saturate(_Vignette * edge * edge * (3.0h - 2.0h * edge));
                half fade = saturate(_Fade);
                half a = 1.0h - (1.0h - vig) * (1.0h - fade);
                half3 rgb = (_VignetteColor.rgb * vig * (1.0h - fade) + _FadeColor.rgb * fade) / max(a, 1e-4);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
}
