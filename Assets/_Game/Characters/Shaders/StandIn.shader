// Development shader for stand-in character parts until CharacterToon (Spike A) exists.
// Body meshes carry a region ID in vertex colour red (ID / 255); regions whose bit is set in
// _HiddenRegions are discarded, which hides skin under clothing without touching the mesh.
Shader "Game/Characters/StandIn"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.8, 0.8, 0.8, 1)
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
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                int _HiddenRegions;
                float _Cull;
            CBUFFER_END

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

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.region = (uint)round(input.color.r * 255.0);
                return output;
            }

            half4 Frag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                if (input.region < 32u && (((uint)_HiddenRegions >> input.region) & 1u) != 0u)
                    discard;

                float3 normalWS = normalize(input.normalWS) * (isFrontFace ? 1.0 : -1.0);
                Light light = GetMainLight();
                half diffuse = saturate(dot(normalWS, light.direction));
                half3 colour = _BaseColor.rgb * (0.35 + 0.65 * diffuse * light.color);
                return half4(colour, 1.0);
            }
            ENDHLSL
        }
    }
}
