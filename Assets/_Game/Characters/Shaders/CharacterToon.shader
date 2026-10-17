// CharacterToon v0 (Spike A): two-tone lighting with art-directed shade colours, rim light, inverted-hull
// outline, mask/vertex-colour recolouring, pattern overlay and body-region hiding. See PLAN.md §3.7.
Shader "Game/Characters/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map (grayscale-authored)", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Colour", Color) = (1, 1, 1, 1)
        _BaseShade ("Base Shade Colour", Color) = (0.7, 0.62, 0.68, 1)

        [Header(Recolour regions)]
        [NoScaleOffset] _MaskMap ("Mask (RGBA = regions 1-4)", 2D) = "black" {}
        [Toggle(_MASK_VERTEX_COLOUR)] _MaskFromVertexColour ("Mask From Vertex Colour GBA", Float) = 0
        _Color1 ("Region 1", Color) = (1, 1, 1, 1)
        _Color2 ("Region 2", Color) = (1, 1, 1, 1)
        _Color3 ("Region 3", Color) = (1, 1, 1, 1)
        _Color4 ("Region 4", Color) = (1, 1, 1, 1)

        [Header(Shading)]
        _ShadeTint ("Shade Tint (recoloured regions)", Color) = (0.72, 0.66, 0.8, 1)
        _ShadeThreshold ("Shade Threshold", Range(0, 1)) = 0.5
        _ShadeSoftness ("Shade Softness", Range(0.001, 0.5)) = 0.03
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1
        _RimColor ("Rim Colour", Color) = (0.25, 0.22, 0.2, 1)
        _RimWidth ("Rim Width", Range(0, 1)) = 0.25

        [Header(Pattern)]
        _PatternMap ("Pattern (R)", 2D) = "white" {}
        _PatternColor ("Pattern Colour (A = strength)", Color) = (1, 1, 1, 0)
        _PatternRegions ("Pattern Regions (weights for regions 1-4)", Vector) = (1, 0, 0, 0)
        _PatternScale ("Pattern Tiles Per Metre", Float) = 8

        [Header(Outline)]
        _OutlineColor ("Outline Colour (× shade albedo)", Color) = (0.35, 0.3, 0.32, 1)
        _OutlineWidth ("Outline Width (px at 1080p)", Range(0, 8)) = 1.5

        [Header(Body)]
        _HiddenRegions ("Hidden Regions (bitmask)", Integer) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _MASK_VERTEX_COLOUR
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "CharacterToonInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 uv : TEXCOORD2; // xy = UV0, zw = UV1
                half4 color : TEXCOORD3;
                nointerpolation uint region : TEXCOORD4;
                half fogFactor : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = float4(input.uv, input.uv1);
                output.color = input.color;
                output.region = ToonRegionId(input.color);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            // 0 in shade, 1 lit: a narrow smoothstep around the threshold on half-Lambert.
            half ToonStep(float3 normalWS, float3 lightDirection)
            {
                half halfLambert = dot(normalWS, lightDirection) * 0.5 + 0.5;
                return smoothstep(_ShadeThreshold - _ShadeSoftness, _ShadeThreshold + _ShadeSoftness, halfLambert);
            }

            half4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                if (ToonIsHidden(input.region)) discard;

                half3 lit, shade;
                ToonAlbedo(input.uv.xy, input.uv.zw, input.color, lit, shade);

                float3 normalWS = normalize(input.normalWS) * (isFrontFace ? 1.0 : -1.0);
                float3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // Main light: shade colour where unlit, base colour where lit; cast shadows snap to the shade colour.
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half mainShadow = smoothstep(0.35, 0.65, mainLight.shadowAttenuation);
                half mainLit = ToonStep(normalWS, mainLight.direction) * mainShadow;
                half3 colour = lerp(shade, lit, mainLit) * mainLight.color;

                // Ambient fills from the shade colour, so dark scenes keep the art-directed hues.
                colour += shade * SampleSH(normalWS) * _AmbientStrength;

            #if defined(_ADDITIONAL_LIGHTS)
                // Point and spot lights add their toon-stepped lit colour.
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                uint lightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                    half attenuation = light.distanceAttenuation * smoothstep(0.35, 0.65, light.shadowAttenuation);
                    colour += lit * light.color * (ToonStep(normalWS, light.direction) * attenuation);
                LIGHT_LOOP_END
            #endif

                // Rim on the lit side only.
                half fresnel = 1.0 - saturate(dot(normalWS, viewWS));
                half rim = smoothstep(1.0 - _RimWidth, 1.0 - _RimWidth + 0.06, fresnel);
                colour += _RimColor.rgb * mainLight.color * (rim * mainLit);

                return half4(MixFog(colour, input.fogFactor), 1.0);
            }
            ENDHLSL
        }

        // Inverted hull, extruded a constant number of pixels in clip space along smoothed normals so hard
        // edges don't split the outline. Smoothed normals live in the tangent channel (baked by OutlineNormals),
        // because skinning transforms tangents but not UVs.
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma shader_feature_local _MASK_VERTEX_COLOUR
            #pragma multi_compile_fog

            #include "CharacterToonInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float4 smoothNormalOS : TANGENT;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0; // xy = UV0, zw = UV1
                half4 color : TEXCOORD1;
                nointerpolation uint region : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings OutlineVert(Attributes input)
            {
                Varyings output;
                float3 normalOS = dot(input.smoothNormalOS.xyz, input.smoothNormalOS.xyz) > 0.01 ? input.smoothNormalOS.xyz : input.normalOS;
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalCS = TransformWorldToHClipDir(TransformObjectToWorldNormal(normalOS));
                float2 direction = normalize(normalCS.xy + float2(1e-5, 1e-5));
                float pixels = _OutlineWidth * (_ScreenParams.y / 1080.0);
                positionCS.xy += direction * (pixels * 2.0 / _ScreenParams.xy) * positionCS.w;

                output.positionCS = positionCS;
                output.uv = float4(input.uv, input.uv1);
                output.color = input.color;
                output.region = ToonRegionId(input.color);
                output.fogFactor = ComputeFogFactor(positionCS.z);
                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                if (ToonIsHidden(input.region)) discard;
                half3 lit, shade;
                ToonAlbedo(input.uv.xy, input.uv.zw, input.color, lit, shade);
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
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "CharacterToonInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation uint region : TEXCOORD0;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
                output.region = ToonRegionId(input.color);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                if (ToonIsHidden(input.region)) discard;
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "CharacterToonInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                nointerpolation uint region : TEXCOORD0;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.region = ToonRegionId(input.color);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                if (ToonIsHidden(input.region)) discard;
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Lets SSAO see characters (without it they leave halos); the forward pass itself ignores SSAO.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "CharacterToonInput.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                nointerpolation uint region : TEXCOORD1;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.region = ToonRegionId(input.color);
                return output;
            }

            half4 DepthNormalsFrag(Varyings input) : SV_Target
            {
                if (ToonIsHidden(input.region)) discard;
                float3 normalWS = normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                return half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 0.0);
            #else
                return half4(normalWS, 0.0);
            #endif
            }
            ENDHLSL
        }
    }
}
