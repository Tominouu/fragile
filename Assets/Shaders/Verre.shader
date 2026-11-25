// Verre léger pour URP (Quest) : transparence avec effet Fresnel (bords plus opaques), reflets de l'environnement,
// éclats de lumière nets, épaisseur suggérée par le rendu des faces arrière puis avant. Compatible rendu stéréo (VR).
Shader "Fragile/Verre"
{
    Properties
    {
        [MainColor] _BaseColor("Teinte (alpha = opacité au centre)", Color) = (0.85, 0.95, 0.9, 0.12)
        [MainTexture] _BaseMap("Texture (optionnelle)", 2D) = "white" {}
        _EdgeOpacity("Opacité des bords", Range(0, 1)) = 0.75
        _FresnelPower("Netteté des bords", Range(0.5, 8)) = 3.5
        _Reflectivity("Reflets", Range(0, 1)) = 0.85
        _Smoothness("Lissé", Range(0, 1)) = 0.96
        _SpecularStrength("Éclats de lumière", Range(0, 6)) = 2.5
        _BackFaceStrength("Paroi arrière", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            half _EdgeOpacity, _FresnelPower, _Reflectivity, _Smoothness, _SpecularStrength, _BackFaceStrength;
        CBUFFER_END
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

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

        // Prémultiplié : rgb = lumière ajoutée par le verre, a = part du fond masquée.
        half4 ShadeGlass(Varyings i, half faceSign, half strength)
        {
            UNITY_SETUP_INSTANCE_ID(i);
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

            half3 N = normalize(i.normalWS) * faceSign;
            half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
            half ndv = saturate(dot(N, V));
            half fresnel = pow(1.0h - ndv, _FresnelPower);

            half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
            half3 tint = _BaseColor.rgb * tex.rgb;

            // Reflets de l'environnement (sonde de réflexion / ciel).
            half3 R = reflect(-V, N);
            half3 env = GlossyEnvironmentReflection(R, i.positionWS, 1.0h - _Smoothness, 1.0h);

            // Lumière : ambiance + lampe principale + lampes additionnelles.
            half shininess = lerp(64.0h, 1500.0h, _Smoothness);
            half3 diffuse = SampleSH(N);
            half3 specular = 0;

            Light mainLight = GetMainLight();
            half3 mainCol = mainLight.color * mainLight.distanceAttenuation;
            diffuse += mainCol * saturate(dot(N, mainLight.direction));
            specular += mainCol * Spec(N, V, mainLight.direction, shininess);

            #if defined(_ADDITIONAL_LIGHTS)
            InputData inputData = (InputData)0;
            inputData.positionWS = i.positionWS;
            inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
            uint count = GetAdditionalLightsCount();
            LIGHT_LOOP_BEGIN(count)
                Light l = GetAdditionalLight(lightIndex, i.positionWS);
                half3 c = l.color * l.distanceAttenuation;
                diffuse += c * saturate(dot(N, l.direction));
                specular += c * Spec(N, V, l.direction, shininess);
            LIGHT_LOOP_END
            #endif

            half bodyAlpha = _BaseColor.a * tex.a;
            half refl = lerp(0.04h, 1.0h, fresnel) * _Reflectivity;
            half3 color = tint * diffuse * bodyAlpha            // teinte du verre
                        + env * refl                          // reflets, surtout sur les bords
                        + specular * _SpecularStrength;          // éclats de lumière
            half alpha = saturate(bodyAlpha + fresnel * _EdgeOpacity + Luminance(specular) * 0.5h);

            color *= strength; alpha *= strength;
            color = MixFog(color, i.fog);
            return half4(color, alpha);
        }
        ENDHLSL

        // 1) Paroi arrière (vue à travers la bouteille), plus discrète.
        Pass
        {
            Name "VerreArriere"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fog
            half4 Frag(Varyings i) : SV_Target { return ShadeGlass(i, -1.0h, _BackFaceStrength); }
            ENDHLSL
        }

        // 2) Paroi avant.
        Pass
        {
            Name "VerreAvant"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite Off
            Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fog
            half4 Frag(Varyings i) : SV_Target { return ShadeGlass(i, 1.0h, 1.0h); }
            ENDHLSL
        }
    }
    FallBack Off
}
