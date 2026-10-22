using UnityEngine;

namespace Game.Characters.Rendering
{
    /// <summary>Property IDs and setters for Game/Characters/Toon materials.</summary>
    public static class ToonMaterial
    {
        public const string ShaderName = "Game/Characters/Toon";

        public static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public static readonly int BaseShade = Shader.PropertyToID("_BaseShade");
        public static readonly int RimColor = Shader.PropertyToID("_RimColor");
        public static readonly int ShadowAnchor = Shader.PropertyToID("_ShadowAnchor");
        public static readonly int ShadeTint = Shader.PropertyToID("_ShadeTint");
        public static readonly int PatternMap = Shader.PropertyToID("_PatternMap");
        public static readonly int PatternColor = Shader.PropertyToID("_PatternColor");
        public static readonly int PatternAccent = Shader.PropertyToID("_PatternAccent");
        public static readonly int PatternPlace = Shader.PropertyToID("_PatternPlace");
        public static readonly int PatternOffset = Shader.PropertyToID("_PatternOffset");
        public static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        public static readonly int BaseGain = Shader.PropertyToID("_BaseGain");
        public static readonly int BaseLift = Shader.PropertyToID("_BaseLift");
        public static readonly int Gloss = Shader.PropertyToID("_Gloss");
        public static readonly int Sheen = Shader.PropertyToID("_Sheen");
        public static readonly int Sheer = Shader.PropertyToID("_Sheer");
        public static readonly int SkinLit = Shader.PropertyToID("_SkinLit");
        public static readonly int SkinShade = Shader.PropertyToID("_SkinShade");
        public static readonly int PatternRegions = Shader.PropertyToID("_PatternRegions");
        public static readonly int PatternScale = Shader.PropertyToID("_PatternScale");
        public static readonly int OutlineWidth = Shader.PropertyToID("_OutlineWidth");
        public static readonly int HiddenRegions = Shader.PropertyToID("_HiddenRegions");
        public static readonly int Cull = Shader.PropertyToID("_Cull");

        static readonly int[] RegionColours =
        {
            Shader.PropertyToID("_Color1"),
            Shader.PropertyToID("_Color2"),
            Shader.PropertyToID("_Color3"),
            Shader.PropertyToID("_Color4"),
        };

        public const string VertexColourMaskKeyword = "_MASK_VERTEX_COLOUR";

        // The palette rim colour is a tint; the shader adds rim light, so it is scaled down.
        const float SkinRimStrength = 0.3f;

        public static void SetSkin(Material material, SkinTonePalette.Stop skin)
        {
            material.SetColor(BaseColor, skin.Base);
            material.SetColor(BaseShade, skin.Shade);
            material.SetColor(RimColor, skin.Rim * SkinRimStrength);
        }

        /// <summary>
        /// Where the environment's shadow is sampled for the whole character: <paramref name="point"/>, moved
        /// <paramref name="towardLight"/> metres toward the main light. A negative distance (the default) means unshadowed.
        /// </summary>
        public static void SetShadowAnchor(Material material, Vector3 point, float towardLight) =>
            material.SetVector(ShadowAnchor, new Vector4(point.x, point.y, point.z, towardLight));

        /// <summary>Sets recolour region <paramref name="region"/> (0-3, i.e. mask channel R-A).</summary>
        public static void SetRegionColour(Material material, int region, Color colour) =>
            material.SetColor(RegionColours[region], colour);

        /// <summary>
        /// A print over the regions weighted by <paramref name="regions"/>: <paramref name="pattern"/>'s red in
        /// <paramref name="colour"/>, its blue in <paramref name="accent"/> (its second colour; the print colour if none).
        /// <paramref name="placed"/> draws it once rather than repeated (xy its centre, zw its half size, in metres of the
        /// garment's fabric UVs; zero to repeat it; z negative for a band repeating round the garment at y, w either side).
        /// <paramref name="offset"/> shifts a repeat (in tiles), so two people in one print don't wear it the same.
        /// </summary>
        public static void SetPattern(Material material, Texture pattern, Color colour, float strength, float tilesPerMetre, Vector4 regions,
                                      Color? accent = null, Vector4 placed = default, Vector2 offset = default)
        {
            material.SetTexture(PatternMap, pattern);
            material.SetVector(PatternOffset, offset);
            material.SetVector(PatternPlace, placed);
            material.SetColor(PatternAccent, accent ?? colour);
            colour.a = pattern == null ? 0f : strength;
            material.SetColor(PatternColor, colour);
            material.SetFloat(PatternScale, tilesPerMetre);
            material.SetVector(PatternRegions, regions);
        }

        /// <summary>
        /// Gives <paramref name="material"/> the cloth surface of <paramref name="fabric"/> (a fabric's material, or a
        /// part's own): its base map, tiling, gain, lift, gloss and sheen.
        /// </summary>
        public static void SetFabric(Material material, Material fabric)
        {
            if (fabric == null || material == fabric) return;
            material.SetTexture(BaseMap, fabric.GetTexture(BaseMap));
            material.SetTextureScale(BaseMap, fabric.GetTextureScale(BaseMap));
            material.SetFloat(BaseGain, fabric.HasFloat(BaseGain) ? fabric.GetFloat(BaseGain) : 1f);
            material.SetFloat(BaseLift, fabric.HasFloat(BaseLift) ? fabric.GetFloat(BaseLift) : 0f);
            material.SetFloat(Gloss, fabric.HasFloat(Gloss) ? fabric.GetFloat(Gloss) : 0f);
            material.SetFloat(Sheen, fabric.HasFloat(Sheen) ? fabric.GetFloat(Sheen) : 0f);
        }

        /// <summary>
        /// How much of the wearer's skin (<paramref name="skin"/>) shows through an open fabric's holes (lace, mesh,
        /// crochet, eyelets: its base map's alpha), from 0 (none) to 1.
        /// </summary>
        public static void SetSheer(Material material, float sheer, SkinTonePalette.Stop skin)
        {
            material.SetFloat(Sheer, sheer);
            material.SetColor(SkinLit, skin.Base);
            material.SetColor(SkinShade, skin.Shade);
        }

        public static void ClearPattern(Material material)
        {
            material.SetTexture(PatternMap, null);
            material.SetColor(PatternColor, new Color(1f, 1f, 1f, 0f));
            material.SetVector(PatternPlace, Vector4.zero);
        }
    }
}
