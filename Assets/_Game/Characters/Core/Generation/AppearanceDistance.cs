using System;
using System.Linq;
using Game.Characters.Dna;

namespace Game.Characters.Generation
{
    /// <summary>
    /// How different two characters look at a glance: skin, hair (shape and colour), presentation, face and the
    /// first outfit's colours, roughly in order of how much they register across a street. 0 means identical.
    /// </summary>
    public static class AppearanceDistance
    {
        public static float Between(CharacterDna a, CharacterDna b)
        {
            float distance = 0f;
            distance += 1.5f * Math.Abs(a.Skin.Tone - b.Skin.Tone);
            distance += 1.0f * Math.Abs(a.Body.Femininity - b.Body.Femininity);
            distance += 1.2f * HsvColor.Distance(a.Hair.Base, b.Hair.Base);
            distance += 0.5f * (Piece(a, "hair.back") == Piece(b, "hair.back") ? 0f : 1f);
            distance += 0.25f * (Piece(a, "hair.front") == Piece(b, "hair.front") ? 0f : 1f);
            distance += 0.3f * (a.Face.PresetId == b.Face.PresetId ? 0f : 1f);
            distance += 0.6f * HsvColor.Distance(a.Face.IrisLeft, b.Face.IrisLeft);
            distance += 0.6f * OutfitDistance(a, b);
            return distance;
        }

        static string Piece(CharacterDna dna, string slotPrefix) =>
            dna.Hair.PieceIds.FirstOrDefault(id => id.StartsWith(slotPrefix, StringComparison.Ordinal));

        static float OutfitDistance(CharacterDna a, CharacterDna b)
        {
            if (a.Outfits.Count == 0 || b.Outfits.Count == 0) return 0f;
            OutfitDna x = a.Outfits[0], y = b.Outfits[0];
            int count = Math.Min(x.Items.Count, y.Items.Count);
            if (count == 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                sum += x.Items[i].ItemId == y.Items[i].ItemId ? 0f : 0.5f;
                if (x.Items[i].RegionColors.Count > 0 && y.Items[i].RegionColors.Count > 0)
                    sum += HsvColor.Distance(x.Items[i].RegionColors[0], y.Items[i].RegionColors[0]);
            }
            return sum / count;
        }
    }
}
