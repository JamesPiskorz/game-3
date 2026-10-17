using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using Game.Sim.Residents;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    /// <summary>Clothes as gifts: worn if loved, and kept on.</summary>
    public class ClothesGiftTests
    {
        static (TownSession Session, SimWorld World, Resident Resident) Friendly(string tag)
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Player.Dna = new CharacterGenerator(world.Characters).Generate(99);
            world.Now = SimTime.At(2, 12);
            // Whoever would love a pair of boots most, or ("-") would like joggers least.
            bool least = tag.StartsWith("-");
            string style = tag.TrimStart('-');
            PartEntry probe = world.Characters.Part(least ? "cloth.bottom.joggers_01" : "cloth.shoes.boots_01");
            Resident resident = least
                ? world.Residents.OrderBy(r => Taste(world, r, probe)).First()
                : world.Residents.OrderByDescending(r => Taste(world, r, probe)).First();
            _ = style;
            Relationship rel = world.RelationshipWith(resident);
            rel.Met = true;
            rel.Familiarity = 30f;
            rel.Affection = 30f;
            rel.LastSeenDay = 1;
            world.Player.Location = world.WhereIs(resident) == SimCatalog.Home ? "cafe" : world.WhereIs(resident);
            var session = new TownSession(world);
            return (session, world, resident);
        }

        static ItemDna Spare(SimWorld world, string id)
        {
            PartEntry part = world.Characters.Part(id);
            var item = new ItemDna { ItemId = id, RegionColors = Enumerable.Range(0, part.Regions).Select(i => new HsvColor(0.9f, 0.4f, 0.8f)).ToList() };
            world.Player.Wardrobe.Add(item);
            return item;
        }

        /// <summary>Their taste for a piece, as the game judges a gift: the average of its two best-liked style tags.</summary>
        static float Taste(SimWorld world, Resident r, PartEntry part) =>
            part.Tags.Where(t => world.Characters.Archetypes.Any(a => a.TagWeights.ContainsKey(t))).Distinct()
                .Select(t => r.Profile.StyleTaste.TryGetValue(t, out float v) ? v : 0f).OrderByDescending(v => v).Take(2).DefaultIfEmpty(0f).Average();

        [Test]
        public void APieceInTheirStyleIsLovedAndWorn()
        {
            (TownSession session, SimWorld world, Resident resident) = Friendly("gothic");
            PartEntry bootsPart = world.Characters.Part("cloth.shoes.boots_01");
            Assume.That(Taste(world, resident, bootsPart), Is.GreaterThanOrEqualTo(Interactions.LovesClothesAt));
            ItemDna boots = Spare(world, "cloth.shoes.boots_01");
            InteractionResult result = world.Act(world, resident, boots);
            Assert.That(result.Reaction, Is.EqualTo("gift.clothes.love"));
            OutfitDna everyday = resident.Dna.Outfits.Single(o => o.Context == TownSession.DayOutfit);
            Assert.That(everyday.Items.Any(i => i.ItemId == boots.ItemId && i.RegionColors[0].H == boots.RegionColors[0].H), "They wear it, in its colours.");
            Assert.That(DnaValidator.Validate(world.Characters, resident.Dna), Is.Empty);
            Assert.That(result.Learnt, Does.Contain("wearing your"));
            _ = session;
        }

        [Test]
        public void GivingTakesItFromTheWardrobeButNotWhatYoureWearing()
        {
            (TownSession session, SimWorld world, Resident resident) = Friendly("gothic");
            ItemDna boots = Spare(world, "cloth.shoes.boots_01");
            session.Select(resident);
            Assert.That(session.Giveable(), Has.Some.Matches<ItemDna>(i => i.ItemId == boots.ItemId));
            ItemDna wearing = world.Player.Dna.Outfits.First(o => o.Context == world.Player.Outfit).Items.First();
            Assert.That(session.GiveClothes(wearing), Is.Null);
            Assert.That(session.Notice, Does.Contain("aren't wearing"));

            Assert.That(session.GiveClothes(boots), Is.Not.Null);
            Assert.That(world.Player.Wardrobe.Any(i => i.ItemId == boots.ItemId && i.RegionColors[0].H == boots.RegionColors[0].H), Is.False);
        }

        [Test]
        public void SomethingNotTheirStyleIsntWorn()
        {
            (TownSession _, SimWorld world, Resident resident) = Friendly("-sporty");
            ItemDna joggers = Spare(world, "cloth.bottom.joggers_01");
            string before = DnaSerializer.ToJson(resident.Dna, indented: false);
            InteractionResult result = world.Act(world, resident, joggers);
            Assert.That(result.Reaction, Is.Not.EqualTo("gift.clothes.love"));
            Assert.That(DnaSerializer.ToJson(resident.Dna, indented: false), Is.EqualTo(before));
        }

        [Test]
        public void TheyKeepWearingItThroughMakeoversAndLoading()
        {
            (TownSession _, SimWorld world, Resident resident) = Friendly("gothic");
            ItemDna boots = Spare(world, "cloth.shoes.boots_01");
            Assume.That(world.Act(world, resident, boots).Reaction, Is.EqualTo("gift.clothes.love"));
            world.Now = SimTime.At(40, 9);
            Makeovers.CatchUp(world);
            bool Wears(SimWorld w) => w.Residents[resident.Index].Dna.Outfits.Single(o => o.Context == TownSession.DayOutfit).Items.Any(i => i.ItemId == boots.ItemId);
            Assert.That(Wears(world), "Still on after weeks of makeovers.");

            SimWorld loaded = SimSave.FromJson(SimSave.Capture(world).ToJson()).Restore(Fixtures.Sim(), Fixtures.Characters());
            Assert.That(Wears(loaded), "And after loading.");
        }
    }

    static class GiftTestExtensions
    {
        public static InteractionResult Act(this SimWorld world, SimWorld _, Resident resident, ItemDna item) => new Interactions(world).GiveClothes(resident, item);
    }
}
