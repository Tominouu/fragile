// Verre réaliste pour URP (Quest), inspiré du rendu Cycles :
// - réfraction : on voit la scène derrière, déformée (image opaque de la scène, "Opaque Texture" d'URP) ;
// - absorption selon l'épaisseur traversée : bords et fond plus foncés et plus colorés (verre ambré, vert...) ;
// - bords assombris (réfraction / réflexion totale), reflets de l'environnement (Fresnel), éclats de lumière nets.
// Compatible rendu stéréo (VR). Si l'Opaque Texture est désactivée, la réfraction utilise la sonde de réflexion.
Shader "Fragile/Verre"
{
    Properties
    {
        [MainColor] _BaseColor("Teinte du verre", Color) = (0.85, 0.95, 0.9, 1)
        [MainTexture] _BaseMap("Texture (optionnelle)", 2D) = "white" {}
        _Density("Densité de la teinte", Range(0, 6)) = 1.2
        _Refraction("Réfraction (déformation)", Range(0, 0.15)) = 0.045
        _EdgeDarkness("Bords sombres", Range(0, 1)) = 0.55
        _Reflectivity("Reflets", Range(0, 1)) = 1
        _Smoothness("Lissé", Range(0, 1)) = 0.97
        _SpecularStrength("Éclats de lumière", Range(0, 8)) = 3
        _Clarity("Clarté", Range(0.5, 1.6)) = 1.15
        [Toggle(_USE_SCENE_COLOR)] _UseSceneColor("Réfraction de la scène (Opaque Texture)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Verre"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _USE_SCENE_COLOR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                half _Density, _Refraction, _EdgeDarkness, _Reflectivity, _Smoothness, _SpecularStrength, _UseSceneColor, _Clarity;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                half   fog        : TEXCOORD3;
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
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half Spec(half3 N, half3 V, half3 L, half shininess)
            {
                half3 H = SafeNormalize(L + V);
                return pow(saturate(dot(N, H)), shininess) * saturate(dot(N, L) * 4.0h);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half3 N = normalize(i.normalWS);
                half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half ndv = saturate(dot(N, V));
                half edge = 1.0h - ndv;

                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half3 tint = saturate(_BaseColor.rgb * tex.rgb);

                // 1) Ce qu'on voit à travers : la scène derrière, déformée par la courbure du verre.
                half3 viewN = TransformWorldToViewDir(N, true);
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float2 offset = viewN.xy * _Refraction * (0.35h + edge);
                half3 behind;
                #if defined(_USE_SCENE_COLOR)
                    behind = SampleSceneColor(uv - offset);
                #else
                    half3 refr = refract(-V, N, 1.0h / 1.5h);
                    behind = GlossyEnvironmentReflection(refr, i.positionWS, 0.05h, 1.0h);
                #endif

                // 2) Absorption : plus le trajet dans le verre est long (bords, vue rasante), plus c'est teinté et sombre.
                half path = 1.0h / max(ndv, 0.12h);
                half3 transmit = pow(max(tint, 0.001h), _Density * path * 0.5h);
                // Bords sombres (réfraction de l'environnement + réflexion totale), comme en Cycles.
                transmit *= lerp(1.0h, 1.0h - _EdgeDarkness, pow(edge, 2.0h));

                // 3) Reflets : environnement (Fresnel, verre n = 1.5) + éclats de lumière nets.
                half fresnel = 0.04h + 0.96h * pow(edge, 5.0h);
                half3 env = GlossyEnvironmentReflection(reflect(-V, N), i.positionWS, 1.0h - _Smoothness, 1.0h);
                half shininess = lerp(64.0h, 2000.0h, _Smoothness);
                Light mainLight = GetMainLight();
                half3 specular = mainLight.color * mainLight.distanceAttenuation * Spec(N, V, mainLight.direction, shininess);
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = uv;
                uint count = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS);
                    specular += l.color * l.distanceAttenuation * Spec(N, V, l.direction, shininess);
                LIGHT_LOOP_END
                #endif


                half3 color = behind * transmit * (1.0h - fresnel) * _Clarity
                            + env * fresnel * _Reflectivity
                            + specular * _SpecularStrength;
                color = MixFog(color, i.fog);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        // Ombre portée : ancre les bouteilles sur le comptoir.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                half _Density, _Refraction, _EdgeDarkness, _Reflectivity, _Smoothness, _SpecularStrength, _UseSceneColor, _Clarity;
            CBUFFER_END
            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };

            float4 Vert(Attributes i) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(i.normalOS);
                return ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(ws, n, _LightDirection)));
            }
            half4 Frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
