// Debug views for the diagnostic sheet (graphics plan V1): world-space mesh normals, the outline normals baked into
// the tangents, or a flat colour (silhouettes, the base under a wireframe). Hides body regions like the toon shader.
Shader "Hidden/Game/Diagnostics"
{
    Properties
    {
        _Mode ("Mode (0 mesh normals, 1 outline normals, 2 flat colour)", Float) = 0
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
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                nointerpolation uint region : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Outline normals live in the tangents (OutlineNormals.cs); meshes without them fall back to normals.
                float3 outline = dot(input.tangentOS.xyz, input.tangentOS.xyz) > 0.01 ? input.tangentOS.xyz : input.normalOS;
                output.direction = _Mode < 0.5 ? TransformObjectToWorldNormal(input.normalOS) : TransformObjectToWorldNormal(outline);
                output.region = (uint)round(input.color.r * 255.0);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (input.region < 32u && (((uint)_HiddenRegions >> input.region) & 1u) != 0u) discard;
                if (_Mode > 1.5) return _FlatColor;
                return half4(normalize(input.direction) * 0.5 + 0.5, 1.0);
            }
            ENDHLSL
        }
    }
}
