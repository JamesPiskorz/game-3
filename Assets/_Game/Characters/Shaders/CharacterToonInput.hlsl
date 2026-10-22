#ifndef GAME_CHARACTER_TOON_INPUT_INCLUDED
#define GAME_CHARACTER_TOON_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Shared by every pass so the material stays SRP Batcher compatible.
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half _BaseGain;
    half4 _BaseColor;
    half4 _BaseShade;
    half4 _Color1;
    half4 _Color2;
    half4 _Color3;
    half4 _Color4;
    half4 _ShadeTint;
    half _ShadeThreshold;
    half _ShadeSoftness;
    half _AmbientStrength;
    half _LitAmbient;
    half4 _RimColor;
    half _RimWidth;
    half4 _PatternColor;
    half4 _PatternRegions;
    half _PatternScale;
    half _PatternInk;
    half4 _OutlineColor;
    half _OutlineWidth;
    int _HiddenRegions;
    float _Cull;
    float4 _ShadowAnchor;
CBUFFER_END

TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
TEXTURE2D(_MaskMap);    SAMPLER(sampler_MaskMap);
TEXTURE2D(_PatternMap); SAMPLER(sampler_PatternMap);

// Body meshes carry their region ID in vertex colour red (ID / 255).
uint ToonRegionId(half4 vertexColour)
{
    return (uint)round(vertexColour.r * 255.0);
}

bool ToonIsHidden(uint region)
{
    return region < 32u && (((uint)_HiddenRegions >> region) & 1u) != 0u;
}

// Face features painted on a generated head (decision 0008) carry UV1.y = 1 and are drawn this much nearer the
// camera than they are, so a few millimetres of sculpted eye socket or brow can't hide them when a slider, a style or
// an expression moves them. The hidden blush and smiling-eye strokes carry 0, so they still only show when pushed out.
// Only in vertex-colour mask mode, where UV1 is a data channel; stand-ins leave it at zero.
#define TOON_FEATURE_LIFT 0.010

float4 ToonObjectToHClip(float3 positionOS, float2 uv1)
{
#if defined(_MASK_VERTEX_COLOUR)
    float3 positionVS = TransformWorldToView(TransformObjectToWorld(positionOS));
    positionVS.z += saturate(uv1.y) * TOON_FEATURE_LIFT; // view space looks down -Z: +Z is towards the camera
    return TransformWViewToHClip(positionVS);
#else
    return TransformObjectToHClip(positionOS);
#endif
}

// Recolour weights for regions 1-4. Authored content uses _MaskMap (RGBA); procedural stand-ins
// put regions 1-3 in vertex colour GBA and region 4 in UV1.x instead.
half4 ToonMask(float2 uv, float2 uv1, half4 vertexColour)
{
#if defined(_MASK_VERTEX_COLOUR)
    return half4(vertexColour.g, vertexColour.b, vertexColour.a, uv1.x);
#else
    return SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv);
#endif
}

// Lit and shaded albedo at a surface point: grayscale-authored texture × region colours, then the pattern.
// The base map is a fabric's surface (its weave, stitches or grain) as shading, brightened by _BaseGain so the cloth
// keeps its colour on average. The unmasked base region shades to _BaseShade (hand-picked, e.g. from the skin palette);
// recoloured regions shade to their colour × _ShadeTint. A pattern's red channel is how much of the pattern colour
// shows; its green channel is ink (outlines and shading drawn in the print), darkening by up to _PatternInk.
void ToonAlbedo(float2 uv, float2 uv1, half4 vertexColour, out half3 lit, out half3 shade)
{
    half gain = _BaseGain > 0.0 ? _BaseGain : 1.0; // shaders and materials without the property keep the texture as is
    half3 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(uv, _BaseMap)).rgb * gain;
    half4 mask = saturate(ToonMask(uv, uv1, vertexColour));
    half baseWeight = saturate(1.0 - mask.r - mask.g - mask.b - mask.a);
    half3 regions = _Color1.rgb * mask.r + _Color2.rgb * mask.g + _Color3.rgb * mask.b + _Color4.rgb * mask.a;

    half2 print = SAMPLE_TEXTURE2D(_PatternMap, sampler_PatternMap, uv * _PatternScale).rg;
    half strength = _PatternColor.a * dot(mask, _PatternRegions);
    half pattern = print.r * strength;
    half ink = 1.0 - print.g * strength * _PatternInk;

    lit = texel * ink * lerp(_BaseColor.rgb * baseWeight + regions, _PatternColor.rgb, pattern);
    shade = texel * ink * lerp(_BaseShade.rgb * baseWeight + regions * _ShadeTint.rgb, _PatternColor.rgb * _ShadeTint.rgb, pattern);
}

#endif
