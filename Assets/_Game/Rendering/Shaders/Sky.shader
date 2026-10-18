// The sky behind outdoor sets (graphics plan G0.5): the horizon colour easing into the top colour overhead, with soft
// stylised clouds when the preset or the weather asks for them. Colours come from the lighting preset (SceneLighting).
Shader "Game/Environment/Sky"
{
    Properties
    {
        _SkyTop ("Top", Color) = (0.38, 0.58, 0.86, 1)
        _SkyHorizon ("Horizon", Color) = (0.78, 0.86, 0.92, 1)
        _Clouds ("Cloud Cover", Range(0, 1)) = 0
        _CloudColour ("Cloud Colour", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SkyTop;
                half4 _SkyHorizon;
                half _Clouds;
                half4 _CloudColour;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz; // the skybox mesh is centred on the camera
                return output;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(127.1, 311.7));
                p += dot(p, p + 19.19);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                // Mostly horizon colour near the horizon, the top colour from about 35° up; below the horizon stays horizon.
                half3 sky = lerp(_SkyHorizon.rgb, _SkyTop.rgb, smoothstep(0.0, 0.6, d.y));

                if (_Clouds > 0.001)
                {
                    // Flat cloud layer seen from below: noise on a plane above, squashed toward the horizon.
                    float2 uv = d.xz / max(d.y + 0.18, 0.05) * 0.9;
                    float n = Noise(uv) * 0.55 + Noise(uv * 2.1 + 7.3) * 0.3 + Noise(uv * 4.3 + 3.1) * 0.15;
                    half cover = smoothstep(1.0 - _Clouds, 1.25 - 0.7 * _Clouds, n) * smoothstep(-0.02, 0.18, d.y);
                    sky = lerp(sky, _CloudColour.rgb, cover * 0.85);
                }
                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
}
