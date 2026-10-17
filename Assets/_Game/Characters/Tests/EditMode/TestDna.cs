using Game.Characters.Dna;
using Game.Characters.Rng;

namespace Game.Characters.Tests
{
    /// <summary>
    /// Builds random but well-formed DNA from a seed, for serialization and determinism tests.
    /// Not the real generator: no taste, no content catalog.
    /// </summary>
    static class TestDna
    {
        static readonly string[] BodySliders = { "height", "shoulders", "chest", "waist", "hips", "legs" };
        static readonly string[] FaceSliders = { "eye.scale", "eye.tilt", "eye.spacing", "nose.size", "mouth.width", "jaw.width" };
        static readonly string[] HairPieces = { "hair.back.long_01", "hair.back.bob_02", "hair.front.blunt_01", "hair.front.side_02", "hair.extra.ahoge_01" };
        static readonly string[] Overlays = { "overlay.blush_01", "overlay.freckles_01", "overlay.mole_01", "overlay.lipstick_01" };
        static readonly string[] Items = { "cloth.top.hoodie_01", "cloth.bottom.jeans_01", "cloth.shoes.sneakers_01", "cloth.onepiece.sundress_01" };
        static readonly string[] Patterns = { "pattern.stripes_01", "pattern.plaid_01", "pattern.dots_01" };
        static readonly string[] Contexts = { "casual", "date", "work", "sleep" };

        public static CharacterDna Create(ulong seed)
        {
            var dna = new CharacterDna { Seed = seed };

            Pcg32 body = SeedStreams.For(seed, "body");
            dna.Body.Femininity = body.NextFloat();
            foreach (string id in BodySliders) dna.Body.Sliders[id] = body.Range(-1f, 1f);

            Pcg32 face = SeedStreams.For(seed, "face");
            dna.Face.PresetId = "face.preset.test_" + face.NextInt(10);
            foreach (string id in FaceSliders) dna.Face.Sliders[id] = face.Range(-1f, 1f);
            dna.Face.EyeStyleId = "eye.style.test_" + face.NextInt(10);
            dna.Face.BrowStyleId = "brow.style.test_" + face.NextInt(8);
            dna.Face.LashStyleId = "lash.style.test_" + face.NextInt(6);
            dna.Face.IrisLeft = Colour(face);
            dna.Face.IrisRight = face.Chance(0.1f) ? Colour(face) : dna.Face.IrisLeft;
            int overlays = face.NextInt(3);
            for (int i = 0; i < overlays; i++)
                dna.Face.Makeup.Add(new OverlayDna { OverlayId = face.Pick(Overlays), Tint = Colour(face), Opacity = face.NextFloat() });

            Pcg32 skin = SeedStreams.For(seed, "skin");
            dna.Skin.Tone = skin.NextFloat();
            dna.Skin.Undertone = skin.NextFloat();

            Pcg32 hair = SeedStreams.For(seed, "hair");
            int pieces = 1 + hair.NextInt(3);
            for (int i = 0; i < pieces; i++) dna.Hair.PieceIds.Add(hair.Pick(HairPieces));
            dna.Hair.Base = Colour(hair);
            dna.Hair.Tip = Colour(hair);
            dna.Hair.Highlight = Colour(hair);
            dna.Hair.Gradient = hair.NextFloat();
            if (hair.Chance(0.2f)) dna.Hair.BrowOverride = Colour(hair);

            Pcg32 outfits = SeedStreams.For(seed, "outfits");
            foreach (string context in Contexts)
            {
                var outfit = new OutfitDna { Context = context };
                int items = 1 + outfits.NextInt(3);
                for (int i = 0; i < items; i++)
                {
                    var item = new ItemDna { ItemId = outfits.Pick(Items) };
                    int regions = 1 + outfits.NextInt(4);
                    for (int r = 0; r < regions; r++) item.RegionColors.Add(Colour(outfits));
                    if (outfits.Chance(0.3f))
                    {
                        item.PatternId = outfits.Pick(Patterns);
                        item.PatternScale = outfits.Range(0.5f, 2f);
                    }
                    outfit.Items.Add(item);
                }
                dna.Outfits.Add(outfit);
            }

            return dna;
        }

        static HsvColor Colour(Pcg32 rng) => new HsvColor(rng.NextFloat(), rng.NextFloat(), rng.NextFloat());
    }
}
