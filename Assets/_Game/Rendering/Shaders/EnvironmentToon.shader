// Toon shading for sets (graphics plan G0.6): the same two-tone banding as the characters, a cooler, slightly
// desaturated shade colour, no specular, per-pixel shadows from characters and props, emission for lit windows and
// lamp bulbs, fog, and an inverted-hull outline from the outline normals baked into the tangents (OutlineNormals).
Shader "Game/Environment/Toon"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Colour", Color) = (0.8, 0.8, 0.8, 1)
        _ShadeTint ("Shade Tint", Color) = (0.68, 0.7, 0.82, 1)
        _Desaturate ("Desaturate (so characters stand out)", Range(0, 1)) = 0.12
        _ShadeThreshold ("Shade Threshold", Range(0, 1)) = 0.5
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.5)) = 0.04
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1
        [HDR] _EmissionColor ("Emission (lit windows, bulbs)", Color) = (0, 0, 0, 1)
        _OutlineColor ("Outline Colour (× shade albedo)", Color) = (0.42, 0.4, 0.45, 1)
        _OutlineWidth ("Outline Width (px at 1080p)", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadeTint;
            half _Desaturate;
            half _ShadeThreshold;
            half _ShadeSoftness;
            half _AmbientStrength;
            half4 _EmissionColor;
            half4 _OutlineColor;
            half _OutlineWidth;
        CBUFFER_END

        // The set's colour, pulled a little toward grey, and its art-directed shade.
        void Albedo(out half3 lit, out half3 shade)
        {
            half3 base = _BaseColor.rgb;
            lit = lerp(base, dot(base, half3(0.2126, 0.7152, 0.0722)).xxx, _Desaturate);
            shade = lit * _ShadeTint.rgb;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half ToonStep(float3 normalWS, float3 lightDirection)
            {
                half halfLambert = dot(normalWS, lightDirection) * 0.5 + 0.5;
                return smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, halfLambert);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 lit, shade;
                Albedo(lit, shade);
                float3 normalWS = normalize(input.normalWS);

                // Cast shadows (characters, props) snap to the shade colour, softly at the edge.
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half mainLit = ToonStep(normalWS, mainLight.direction) * smoothstep(0.3, 0.7, mainLight.shadowAttenuation);
                half3 colour = lerp(shade, lit, mainLit) * mainLight.color;
                colour += shade * SampleSH(normalWS) * _AmbientStrength;

            #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                    half attenuation = light.distanceAttenuation * smoothstep(0.3, 0.7, light.shadowAttenuation);
                    colour += lit * light.color * (ToonStep(normalWS, light.direction) * attenuation);
                LIGHT_LOOP_END
            #endif

                colour += _EmissionColor.rgb;
                return half4(MixFog(colour, input.fogFactor), 1.0);
            }
            ENDHLSL
        }

        // Inverted hull a constant number of pixels wide, along the outline normals so it stays closed at hard corners.
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; };
            struct Varyings { float4 positionCS : SV_POSITION; half fogFactor : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 smoothed = dot(input.tangentOS.xyz, input.tangentOS.xyz) > 0.01 ? input.tangentOS.xyz : input.normalOS;
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalCS = TransformWorldToHClipDir(TransformObjectToWorldNormal(smoothed));
                float pixels = _OutlineWidth * (_ScreenParams.y / 1080.0);
                float2 offset = normalize(normalCS.xy + 1e-5) * pixels * 2.0 / _ScreenParams.xy;
                positionCS.xy += offset * positionCS.w;
                output.positionCS = positionCS;
                output.fogFactor = ComputeFogFactor(positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (_OutlineWidth <= 0.0) discard;
                half3 lit, shade;
                Albedo(lit, shade);
                return half4(MixFog(shade * _OutlineColor.rgb, input.fogFactor), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirection = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirection = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
                output.positionCS = ApplyShadowClamping(positionCS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half Frag(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
    }
}
