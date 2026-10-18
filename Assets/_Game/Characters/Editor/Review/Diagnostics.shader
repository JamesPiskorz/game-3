// Debug views for the diagnostic sheet (graphics plan V1): world-space mesh normals, the outline normals baked into
// the tangents, a flat colour (silhouettes, the base under a wireframe), or the stand-ins' lightmap (G1.1, from UV2:
// light grey is a neutral shadow bias, blue falls into shade sooner, orange stays lit longer; the shine mask is green
// and the metal mask gold). Hides body regions like the toon shader.
Shader "Hidden/Game/Diagnostics"
{
    Properties
    {
        _Mode ("Mode (0 mesh normals, 1 outline normals, 2 flat colour, 3 stand-in lightmap)", Float) = 0
        _FlatColor ("Flat Colour", Color) = (0, 0, 0, 1)
        _HiddenRegions ("Hidden Regions (bitmask)", Integer) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Diagnostics"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                half4 _FlatColor;
                int _HiddenRegions;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float4 uv2 : TEXCOORD2;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                nointerpolation uint region : TEXCOORD1;
                float4 lightMap : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Outline normals live in the tangents (OutlineNormals.cs); meshes without them fall back to normals.
                float3 outline = dot(input.tangentOS.xyz, input.tangentOS.xyz) > 0.01 ? input.tangentOS.xyz : input.normalOS;
                output.direction = _Mode < 0.5 ? TransformObjectToWorldNormal(input.normalOS) : TransformObjectToWorldNormal(outline);
                output.region = (uint)round(input.color.r * 255.0);
                output.lightMap = float4(input.uv2.x + 0.5, input.uv2.yzw); // stored with R offset, zero neutral
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (input.region < 32u && (((uint)_HiddenRegions >> input.region) & 1u) != 0u) discard;
                if (_Mode > 2.5)
                {
                    half bias = input.lightMap.r - 0.5;
                    half3 colour = lerp(half3(0.8, 0.8, 0.8), bias < 0.0 ? half3(0.12, 0.22, 0.8) : half3(0.95, 0.55, 0.15), saturate(abs(bias) / 0.25));
                    colour = lerp(colour, half3(0.2, 0.85, 0.3), saturate(input.lightMap.g) * 0.85);
                    return half4(lerp(colour, half3(0.95, 0.75, 0.2), saturate(input.lightMap.b) * 0.85), 1.0);
                }
                if (_Mode > 1.5) return _FlatColor;
                return half4(normalize(input.direction) * 0.5 + 0.5, 1.0);
            }
            ENDHLSL
        }
    }
}
