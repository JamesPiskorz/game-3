using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Rng;

namespace Game.Sim.Sandbox
{
    /// <summary>Something on sale: a catalog part in a particular colourway, and the style it was made for.</summary>
    public sealed class ShopItem
    {
        public ItemDna Item;
        public PartEntry Part;
        public string Style;
        public int Price;

        public string Name => (Part.Name ?? ContentCatalog.Humanize(Part.Id));
    }

    /// <summary>
    /// The clothes shop at the mall. Its stock changes daily and is made by the character generator: each piece
    /// comes from a generated outfit in some archetype's style, so it arrives in colours that were chosen to work
    /// together and with that style. This is where the player shapes how they come across.
    /// </summary>
    public static class Boutique
    {
        public const string Location = "mall";
        public const int StockSize = 6;

        static readonly string[] ClothingSlots = { Slots.Top, Slots.Bottom, Slots.OnePiece, Slots.Shoes, Slots.Hat, Slots.Glasses, Slots.Neck };

        /// <summary>Today's stock: the same for everyone in this town on this day.</summary>
        public static List<ShopItem> Stock(SimWorld world, int day)
        {
            var generator = new CharacterGenerator(world.Characters);
            Pcg32 rng = SeedStreams.For(StableHash.SplitMix64(world.Seed ^ (ulong)day), "boutique");
            var stock = new List<ShopItem>();
            for (int attempt = 0; attempt < 60 && stock.Count < StockSize; attempt++)
            {
                StyleArchetype style = rng.Pick(world.Characters.Archetypes);
                ulong seed = rng.NextUInt() | (ulong)rng.NextUInt() << 32;
                CharacterDna model = generator.Generate(seed, new GenerationRequest { Archetype = style.Id });
                OutfitDna outfit = model.Outfits.FirstOrDefault(o => o.Context == TownSession.DayOutfit) ?? model.Outfits.First();
                List<ItemDna> clothes = outfit.Items.Where(i => ClothingSlots.Contains(world.Characters.Part(i.ItemId)?.Slot)).ToList();
                if (clothes.Count == 0) continue;

                ItemDna item = rng.Pick(clothes);
                if (stock.Any(s => s.Item.ItemId == item.ItemId)) continue; // one of each piece a day
                PartEntry part = world.Characters.Part(item.ItemId);
                stock.Add(new ShopItem { Item = item, Part = part, Style = style.Id, Price = Price(part) });
            }
            return stock;
        }

        /// <summary>Bigger pieces cost more; so does detail (colour regions, a pattern).</summary>
        public static int Price(PartEntry part)
        {
            int price = part.Slot switch
            {
                Slots.OnePiece => 90,
                Slots.Top => 55,
                Slots.Bottom => 50,
                Slots.Shoes => 45,
                Slots.Glasses => 35,
                Slots.Neck => 30,
                _ => 30,
            };
            return price + 5 * (part.Regions - 1) + (part.Patternable ? 5 : 0);
        }
    }
}
