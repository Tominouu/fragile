// Liquide dans une bouteille : le volume est coupé par un plan dans le repère du monde (_FillPoint / _FillNormal,
// mis à jour par LiquidFill), donc la surface reste horizontale quand on penche la bouteille et peut onduler.
// Les faces intérieures du volume, vues du dessus, dessinent la surface du liquide (pas de géométrie en plus).
Shader "Fragile/Liquide"
{
    Properties
    {
        _Color("Liquide (alpha = opacité)", Color) = (0.8, 0.45, 0.1, 0.85)
        _SurfaceColor("Surface", Color) = (0.95, 0.65, 0.25, 0.9)
        _RimStrength("Lumière sur les bords", Range(0, 2)) = 0.6
        _FillPoint("Point du niveau (monde)", Vector) = (0, 0, 0, 0)
        _FillNormal("Normale du niveau (monde)", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Liquide"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _SurfaceColor;
                half _RimStrength;
                float4 _FillPoint;
                float4 _FillNormal;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   fog        : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_TRANSFER_INSTANCE_ID(i, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i, bool isFront : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // Au-dessus du niveau : pas de liquide.
                float3 n = normalize(_FillNormal.xyz);
                clip(-dot(i.positionWS - _FillPoint.xyz, n));

                Light mainLight = GetMainLight();
                half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half4 col;
                if (isFront)
                {
                    // Corps du liquide : éclairage doux + bords plus lumineux (lumière qui traverse).
                    half3 N = normalize(i.normalWS);
                    half3 light = SampleSH(N) + mainLight.color * saturate(dot(N, mainLight.direction)) * 0.5h;
                    half rim = pow(1.0h - saturate(dot(N, V)), 2.0h) * _RimStrength;
                    col = half4(_Color.rgb * light + _Color.rgb * rim, _Color.a);
                }
                else
                {
                    bool cameraAbove = dot(_WorldSpaceCameraPos - _FillPoint.xyz, n) > 0;
                    if (cameraAbove)
                    {
                        // Surface (face intérieure vue d'en haut), éclairée comme un plan horizontal.
                        // Comme une vraie surface de liquide : transparente vue d'en haut, plus visible en vue rasante.
                        half fres = pow(1.0h - abs(dot(V, n)), 3.0h);
                        half3 light = SampleSH(n) + mainLight.color * saturate(dot(n, mainLight.direction)) * 0.6h;
                        half3 H = SafeNormalize(mainLight.direction + V);
                        half spec = pow(saturate(dot(n, H)), 200.0h) * 0.6h;
                        // Liquide dense (vin, whisky) : surface opaque. Liquide clair (eau, gin) : surface discrète, surtout des reflets.
                        half surfAlpha = lerp(_SurfaceColor.a * lerp(0.35h, 1.0h, fres), _SurfaceColor.a, _Color.a);
                        col = half4(_SurfaceColor.rgb * light + spec * mainLight.color, saturate(surfAlpha + spec));
                    }
                    else
                    {
                        // Vu d'en dessous du niveau : c'est la paroi arrière du liquide.
                        half3 N = -normalize(i.normalWS);
                        half3 light = SampleSH(N) + mainLight.color * saturate(dot(N, mainLight.direction)) * 0.5h;
                        col = half4(_Color.rgb * light, _Color.a);
                    }
                }
                col.rgb = MixFog(col.rgb, i.fog);
                return col;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
