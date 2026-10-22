using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;

namespace Game.Sim.Residents
{
    /// <summary>A piece of clothing the player gave a resident, who wears it in their everyday outfit from then on.</summary>
    public sealed class GiftedClothes
    {
        public int Resident;
        public int Day;
        public ItemDna Item;
    }

    /// <summary>
    /// Clothes the player gives to residents. A piece they love and can wear goes into their everyday outfit, and
    /// stays there: it's re-applied after each week's makeovers and when a game loads (the town itself is
    /// regenerated from the seed, so the gifts are saved).
    /// </summary>
    public static class ClothesGifts
    {
        /// <summary>The resident's look wearing <paramref name="item"/> in their everyday outfit, or null if it can't be worn there.</summary>
        public static CharacterDna Wearing(SimWorld world, Resident resident, ItemDna item)
        {
            ContentCatalog catalog = world.Characters;
            PartEntry part = catalog.Part(item.ItemId);
            if (part == null) return null;
            CharacterDna dna = DnaSerializer.FromJson(DnaSerializer.ToJson(resident.Dna, indented: false));
            OutfitDna outfit = dna.Outfits.FirstOrDefault(o => o.Context == Sandbox.TownSession.DayOutfit);
            if (outfit == null) return null;
            outfit.Items.RemoveAll(worn =>
            {
                PartEntry other = catalog.Part(worn.ItemId);
                return other == null || other.Slot == part.Slot || part.BlocksSlots.Contains(other.Slot) || other.BlocksSlots.Contains(part.Slot);
            });
            outfit.Items.Add(new ItemDna { ItemId = item.ItemId, RegionColors = item.RegionColors.ToList(), FabricId = item.FabricId, PatternId = item.PatternId, PatternScale = item.PatternScale, PatternColor = item.PatternColor });
            new CharacterGenerator(catalog).DropIncompatibleParts(dna);
            bool kept = outfit.Items.Any(i => i.ItemId == item.ItemId);
            return kept && DnaValidator.Validate(catalog, dna).Count == 0 ? dna : null;
        }

        /// <summary>The names of the player's gifts the resident has on in their everyday outfit right now.</summary>
        public static List<string> WornGifts(SimWorld world, Resident resident)
        {
            OutfitDna everyday = resident.Dna.Outfits.FirstOrDefault(o => o.Context == Sandbox.TownSession.DayOutfit);
            if (everyday == null) return new List<string>();
            return world.GiftedClothes.Where(g => g.Resident == resident.Index && everyday.Items.Any(i => i.ItemId == g.Item.ItemId))
                .Select(g => world.Characters.Part(g.Item.ItemId)?.Name?.ToLowerInvariant()).Where(n => n != null).Distinct().ToList();
        }

        /// <summary>Puts every gift back on its wearer, oldest first (after makeovers, and after loading).</summary>
        public static void Apply(SimWorld world)
        {
            foreach (GiftedClothes gift in world.GiftedClothes.OrderBy(g => g.Day))
            {
                if (gift.Resident < 0 || gift.Resident >= world.Residents.Count) continue;
                Resident resident = world.Residents[gift.Resident];
                CharacterDna dressed = Wearing(world, resident, gift.Item);
                if (dressed != null) resident.Dna = dressed;
            }
        }
    }
}
