using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using Game.Sim.Residents;

namespace Game.Sim.Sandbox
{
    /// <summary>The boutique, the wardrobe and its rules, and dressing for the occasion.</summary>
    public sealed partial class TownSession
    {
        /// <summary>Today's stock, if the player is at the boutique while it's open.</summary>
        public List<ShopItem> Shop() =>
            Location == Boutique.Location && IsOpen(Location) && DateWith == null ? Boutique.Stock(World, World.Now.Day) : new List<ShopItem>();

        public bool Owns(ItemDna item) => World.Player.Wardrobe.Any(owned => Same(owned, item));

        /// <summary>Buys a piece and puts it on.</summary>
        public void Buy(ShopItem item)
        {
            Notice = null;
            PlayerCharacter player = World.Player;
            if (Owns(item.Item)) Notice = $"You already have that {item.Name.ToLowerInvariant()}.";
            else if (player.Money < item.Price) Notice = $"That's {item.Price} coins, and you have {player.Money}.";
            else
            {
                player.Money -= item.Price;
                _today.Spent += item.Price;
                player.Wardrobe.Add(Copy(item.Item));
                Notice = $"You bought the {item.Name.ToLowerInvariant()}.";
                if (Suits(item.Item)) Wear(item.Item, quiet: true);
                else Notice += " It's in your wardrobe for another day.";
            }
            Raise();
        }

        /// <summary>The wardrobe pieces the player could change into now (owned, not already on, and right for the outfit).</summary>
        public List<ItemDna> Changeable()
        {
            OutfitDna outfit = CurrentOutfit();
            return World.Player.Wardrobe.Where(item => (outfit == null || !outfit.Items.Any(worn => Same(worn, item))) && Suits(item)).ToList();
        }

        /// <summary>
        /// Whether a piece belongs in the outfit being worn, by the same rules the generator follows: a part limited to
        /// some contexts stays out of the others (no tank top for a rainy day), and the workout outfit is sporty only.
        /// </summary>
        public bool Suits(ItemDna item)
        {
            PartEntry part = World.Characters.Part(item.ItemId);
            OutfitContext context = World.Characters.Context(CurrentOutfit()?.Context ?? World.Player.Outfit);
            if (part == null || context == null) return part != null;
            return (part.Contexts.Count == 0 || part.Contexts.Contains(context.Id))
                && (context.OnlyTags.Count == 0 || part.Tags.Any(context.OnlyTags.Contains));
        }

        /// <summary>
        /// Puts on a piece the player owns, in the outfit they're wearing now. Whatever it replaces or covers comes
        /// off (a dress replaces top and bottom); a top or bottom without its partner pulls one from the wardrobe.
        /// Fails, and changes nothing, if it doesn't go with their hair or the rest of the outfit.
        /// </summary>
        public bool Wear(ItemDna item, bool quiet = false)
        {
            if (!quiet) Notice = null;
            OutfitDna outfit = CurrentOutfit();
            PartEntry part = World.Characters.Part(item.ItemId);
            if (outfit == null || part == null || !Owns(item))
            {
                if (!quiet) Notice = "You don't have that.";
                Raise();
                return false;
            }
            if (!Suits(item))
            {
                string dressedFor = World.Characters.Context(outfit.Context)?.Name ?? outfit.Context;
                Notice = $"The {(part.Name ?? ContentCatalog.Humanize(part.Id)).ToLowerInvariant()} isn't for your {dressedFor.ToLowerInvariant()} outfit.";
                Raise();
                return false;
            }

            List<ItemDna> next = outfit.Items.Where(worn =>
            {
                PartEntry other = World.Characters.Part(worn.ItemId);
                return other != null && other.Slot != part.Slot && !part.BlocksSlots.Contains(other.Slot) && !other.BlocksSlots.Contains(part.Slot);
            }).ToList();
            next.Add(Copy(item));
            FillRequiredSlots(next);
            List<string> off = TakeOffWhatNoLongerFits(next, item);

            if (!Wearable(next, out string problem))
            {
                Notice = problem;
                Raise();
                return false;
            }
            outfit.Items = next;
            World.Player.LookVersion++;
            World.Advance(ChangeMinutes);
            AfterTimePasses();
            if (!quiet) Notice = $"You change into the {(part.Name ?? ContentCatalog.Humanize(part.Id)).ToLowerInvariant()}."
                + (off.Count == 0 ? "" : $" The {string.Join(" and the ", off)} come off.");
            Raise();
            return true;
        }

        /// <summary>
        /// Accessories and extras that don't go with the new piece come off (tights under trousers), rather than
        /// refusing the change. Returns their names.
        /// </summary>
        List<string> TakeOffWhatNoLongerFits(List<ItemDna> items, ItemDna added)
        {
            var off = new List<string>();
            List<PartEntry> hair = World.Player.Dna.Hair.PieceIds.Select(World.Characters.Part).Where(p => p != null).ToList();
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (ItemDna worn in items.ToList())
                {
                    PartEntry part = World.Characters.Part(worn.ItemId);
                    if (part == null || worn.ItemId == added.ItemId || part.Slot == Slots.Top || part.Slot == Slots.Bottom
                        || part.Slot == Slots.OnePiece || part.Slot == Slots.Shoes) continue;
                    List<PartEntry> others = hair.Concat(items.Where(i => i != worn).Select(i => World.Characters.Part(i.ItemId)).Where(p => p != null)).ToList();
                    if (CharacterGenerator.Compatible(part, others)) continue;
                    items.Remove(worn);
                    off.Add((part.Name ?? ContentCatalog.Humanize(part.Id)).ToLowerInvariant());
                    changed = true;
                }
            }
            return off;
        }

        OutfitDna CurrentOutfit()
        {
            CharacterDna dna = World.Player.Dna;
            if (dna == null) return null;
            return dna.Outfits.FirstOrDefault(o => o.Context == World.Player.Outfit) ?? dna.Outfits.FirstOrDefault();
        }

        /// <summary>Without a one-piece, a top and a bottom are needed: take them from the wardrobe if missing.</summary>
        void FillRequiredSlots(List<ItemDna> items)
        {
            string SlotOf(ItemDna i) => World.Characters.Part(i.ItemId)?.Slot;
            if (items.Any(i => SlotOf(i) == Slots.OnePiece)) return;
            foreach (string slot in new[] { Slots.Top, Slots.Bottom })
            {
                if (items.Any(i => SlotOf(i) == slot)) continue;
                ItemDna spare = World.Player.Wardrobe.FirstOrDefault(i => SlotOf(i) == slot && Suits(i));
                if (spare != null) items.Add(Copy(spare));
            }
        }

        bool Wearable(List<ItemDna> items, out string problem)
        {
            problem = null;
            string SlotOf(ItemDna i) => World.Characters.Part(i.ItemId)?.Slot;
            bool dressed = items.Any(i => SlotOf(i) == Slots.OnePiece) || (items.Any(i => SlotOf(i) == Slots.Top) && items.Any(i => SlotOf(i) == Slots.Bottom));
            if (!dressed)
            {
                problem = "You'd need something to wear with that.";
                return false;
            }
            List<PartEntry> hair = World.Player.Dna.Hair.PieceIds.Select(World.Characters.Part).Where(p => p != null).ToList();
            List<PartEntry> parts = items.Select(i => World.Characters.Part(i.ItemId)).ToList();
            foreach (PartEntry part in parts)
            {
                List<PartEntry> others = hair.Concat(parts.Where(p => p != part)).ToList();
                if (CharacterGenerator.Compatible(part, others)) continue;
                string name = (part.Name ?? ContentCatalog.Humanize(part.Id)).ToLowerInvariant();
                problem = part.RequiresTags.Count > 0 ? $"The {name} doesn't work with your hair." : $"The {name} doesn't go with the rest of your outfit.";
                return false;
            }
            return true;
        }

        static bool Same(ItemDna a, ItemDna b) =>
            a.ItemId == b.ItemId && a.FabricId == b.FabricId && a.PatternId == b.PatternId && a.RegionColors.Count == b.RegionColors.Count
            && a.RegionColors.Zip(b.RegionColors, (x, y) => HsvColor.Distance(x, y) < 1e-3f).All(same => same);

        static ItemDna Copy(ItemDna item) => new ItemDna
        {
            ItemId = item.ItemId,
            RegionColors = item.RegionColors.ToList(),
            FabricId = item.FabricId,
            PatternId = item.PatternId,
            PatternScale = item.PatternScale,
            PatternColor = item.PatternColor,
        };

        /// <summary>
        /// The player dresses by the same rule as everyone else (<see cref="SimWorld.OutfitAt"/>): the coat outfit on a
        /// rainy day, workout clothes at the gym, dressed up for a night out. The wardrobe edits whichever is on.
        /// </summary>
        void DressForTheOccasion()
        {
            CharacterDna dna = World.Player.Dna;
            if (dna == null) return;
            string occasion = World.OutfitAt(Location);
            if (occasion == World.Player.Outfit || dna.Outfits.All(o => o.Context != occasion)) return;
            World.Player.Outfit = occasion;
            World.Player.LookVersion++;
        }

        /// <summary>
        /// A look made before some outfit contexts existed (an old save, a share code) gets the missing ones, generated
        /// in its own style, and owns what's in them.
        /// </summary>
        void DressForEveryContext()
        {
            CharacterDna dna = World.Player.Dna;
            if (dna == null) return;
            List<OutfitContext> missing = World.Characters.Contexts.Where(c => dna.Outfits.All(o => o.Context != c.Id)).ToList();
            if (missing.Count == 0) return;
            CharacterDna dressed = new CharacterGenerator(World.Characters).Reroll(dna, DnaCategories.Outfits, dna.Seed ^ 0xD8E55UL);
            foreach (OutfitContext context in missing)
            {
                OutfitDna outfit = dressed.Outfits.FirstOrDefault(o => o.Context == context.Id);
                if (outfit == null) continue;
                dna.Outfits.Add(outfit);
                foreach (ItemDna item in outfit.Items)
                    if (!World.Player.Wardrobe.Any(owned => Same(owned, item))) World.Player.Wardrobe.Add(Copy(item));
            }
        }
    }
}
