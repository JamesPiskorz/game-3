#ifndef GAME_CHARACTER_TOON_INPUT_INCLUDED
#define GAME_CHARACTER_TOON_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Shared by every pass so the material stays SRP Batcher compatible.
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half _BaseGain;
    half _BaseLift;
    half _Gloss;
    half _Sheen;
    half _Sheer;
    half4 _SkinLit;
    half4 _SkinShade;
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
    half4 _PatternAccent;
    float4 _PatternPlace;
    float4 _PatternOffset;
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
TEXTURE2D(_LightMap);   SAMPLER(sampler_LightMap);

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
// shows; its green channel is ink (outlines and shading drawn in the print), darkening by up to _PatternInk; its blue
// channel is how much of its second colour, _PatternAccent, shows (a flower's leaves, a daisy's heart). A placed
// print (_PatternPlace.z > 0: a T-shirt's chest graphic, a polo's badge) is drawn once, .z by .w metres either side of
// the point (u, v) = .xy, rather than repeated: the generated garments' UVs are metres of cloth, u from the centre front.
// A band (.z < 0: a hem's border, an embroidered yoke) repeats round the garment at _PatternScale tiles per metre and
// is placed up it, .w metres either side of the height .y.
// The base map's alpha marks an open fabric's holes, which show the skin (_Sheer, _SkinLit, _SkinShade).
// _BaseLift adds the fabric's structure as light too, the more the darker the cloth, since darkening alone can't show
// on black: the stitches of a black knit, the flecks of a charcoal tweed, the glint of black patent.
void ToonAlbedo(float2 uv, float2 uv1, half4 vertexColour, out half3 lit, out half3 shade)
{
    half gain = _BaseGain > 0.0 ? _BaseGain : 1.0; // shaders and materials without the property keep the texture as is
    half4 surface = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(uv, _BaseMap));
    half3 texel = surface.rgb * gain;
    half4 mask = saturate(ToonMask(uv, uv1, vertexColour));
    half baseWeight = saturate(1.0 - mask.r - mask.g - mask.b - mask.a);
    half3 regions = _Color1.rgb * mask.r + _Color2.rgb * mask.g + _Color3.rgb * mask.b + _Color4.rgb * mask.a;

    float2 printUV = uv * _PatternScale + _PatternOffset.xy; // each wearer's repeat starts somewhere else
    half placed = 1.0;
    if (_PatternPlace.z > 0.0)
    {
        printUV = (uv - _PatternPlace.xy) / (2.0 * _PatternPlace.zw) + 0.5;
        placed = step(0.0, printUV.x) * step(printUV.x, 1.0) * step(0.0, printUV.y) * step(printUV.y, 1.0);
    }
    else if (_PatternPlace.z < 0.0)
    {
        printUV.y = (uv.y - _PatternPlace.y) / (2.0 * _PatternPlace.w) + 0.5; // a band: offset round the garment only
        placed = step(0.0, printUV.y) * step(printUV.y, 1.0);
    }
    half3 print = SAMPLE_TEXTURE2D(_PatternMap, sampler_PatternMap, printUV).rgb * placed;
    half strength = _PatternColor.a * dot(mask, _PatternRegions);
    half pattern = print.r * strength;
    half accent = print.b * strength;
    half ink = 1.0 - print.g * strength * _PatternInk;

    half3 litColour = ink * lerp(lerp(_BaseColor.rgb * baseWeight + regions, _PatternColor.rgb, pattern), _PatternAccent.rgb, accent);
    half3 shadeColour = ink * lerp(lerp(_BaseShade.rgb * baseWeight + regions * _ShadeTint.rgb, _PatternColor.rgb * _ShadeTint.rgb, pattern),
                                   _PatternAccent.rgb * _ShadeTint.rgb, accent);
    half3 lift = (texel - 1.0) * _BaseLift;
    const half3 luma = half3(0.2126, 0.7152, 0.0722);
    lit = max(texel * litColour + lift * (1.0 - saturate(dot(litColour, luma))), 0.0);
    shade = max(texel * shadeColour + lift * 0.6 * (1.0 - saturate(dot(shadeColour, luma))), 0.0);

    // An open fabric's holes (its base map's alpha: lace's net, a crochet's gaps, eyelets) show the wearer's skin, by
    // _Sheer: all of it on lingerie and swimwear, part of it on a lined dress or blouse.
    half open = (1.0 - surface.a) * _Sheer;
    lit = lerp(lit, _SkinLit.rgb, open);
    shade = lerp(shade, _SkinShade.rgb, open);
}

// The part's lightmap (graphics plan G1.1), linear RGBA on UV0:
//   R  shadow bias: 0.5 neutral; lower falls into shade sooner, higher stays lit longer (R1, painted cel shadows)
//   G  specular mask (G1.5)
//   B  metal mask (G1.6)
//   A  material type, which picks the ramp row (G1.2): 0 skin, 32 hair, 64 cloth, 128 metal, out of 255
// Only R is read so far. A part without a lightmap gets Unity's linearGrey (every channel about 0.5): neutral in R,
// but not in G-A, so the steps that read those must not take the default at face value.
half4 ToonLightMap(float2 uv)
{
    return SAMPLE_TEXTURE2D(_LightMap, sampler_LightMap, uv);
}

// How far the bias moves the toon step's threshold: 0 at neutral, up to ±0.5 (0 never lit, 1 always lit). Anything
// within one 8-bit step of 0.5 is neutral: linearGrey reads just under 0.5 and our textures write 0.5 as 128/255, and
// both must leave the shading exactly as it was without a lightmap.
half ToonShadowBias(half4 lightMap)
{
    half bias = lightMap.r - 0.5;
    return abs(bias) < 1.0 / 255.0 ? 0.0 : bias;
}

#endif
