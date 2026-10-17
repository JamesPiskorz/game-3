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
    public class SalonTests
    {
        static TownSession AtTheSalon(out SimWorld world)
        {
            world = Fixtures.Town(0x5EED, 24);
            world.Player.Dna = new CharacterGenerator(world.Characters).Generate(99);
            world.Now = SimTime.At(2, 11);
            world.Player.Location = Salon.Location;
            world.Player.Money = 500;
            return new TownSession(world);
        }

        static string OtherCut(SimWorld world) =>
            Salon.Cuts(world.Characters).First(c => !world.Player.Dna.Hair.PieceIds.Contains(c.Id) && !c.RequiresTags.Any()).Id;

        [Test]
        public void ACutCostsCoinsAndAnHourAndChangesOnlyTheHair()
        {
            TownSession session = AtTheSalon(out SimWorld world);
            string outfits = Newtonsoft.Json.JsonConvert.SerializeObject(world.Player.Dna.Outfits.Select(o => o.Items.Select(i => i.ItemId)));
            int version = world.Player.LookVersion;
            SimTime before = world.Now;
            string cut = OtherCut(world);

            session.Haircut(cut);
            Assert.That(world.Player.Dna.Hair.PieceIds[0], Is.EqualTo(cut));
            Assert.That(world.Player.Money, Is.EqualTo(500 - Salon.CutPrice));
            Assert.That(world.Now.Minutes - before.Minutes, Is.EqualTo(Salon.Minutes));
            Assert.That(world.Player.LookVersion, Is.GreaterThan(version));
            Assert.That(world.Player.LastMakeover.Day, Is.EqualTo(2));
            Assert.That(DnaValidator.Validate(world.Characters, world.Player.Dna), Is.Empty);
            foreach (OutfitDna outfit in world.Player.Dna.Outfits)
                Assert.That(outfit.Items.All(i => world.Characters.Part(i.ItemId).Slot != Slots.Hat
                    || CharacterGenerator.Compatible(world.Characters.Part(i.ItemId), world.Player.Dna.Hair.PieceIds.Select(world.Characters.Part).ToList())),
                    "A hat the new cut can't take comes off.");
            _ = outfits;
        }

        [Test]
        public void TheSalonIsOnlyAtArcadeLaneAndNeedsTheMoney()
        {
            TownSession session = AtTheSalon(out SimWorld world);
            world.Player.Money = 5;
            session.Haircut(OtherCut(world));
            Assert.That(session.Notice, Does.Contain("coins"));
            world.Player.Money = 500;
            session.Travel("park");
            session.Dye(Salon.Colours(world.Characters).Last());
            Assert.That(session.Notice, Does.Contain("Arcade Lane"));
            Assert.That(world.Player.LastMakeover, Is.Null);
        }

        [Test]
        public void DyeAndBangsChangeWhatTheySay()
        {
            TownSession session = AtTheSalon(out SimWorld world);
            HsvColor colour = Salon.Colours(world.Characters).OrderByDescending(c => HsvColor.Distance(c, world.Player.Dna.Hair.Base)).First();
            session.Dye(colour);
            Assert.That(HsvColor.Distance(world.Player.Dna.Hair.Base, colour), Is.LessThan(1e-4f));
            Assert.That(world.Player.LastMakeover.Kind, Is.EqualTo(MakeoverKind.Colour));

            session.Bangs(null);
            Assert.That(world.Player.Dna.Hair.PieceIds.Select(world.Characters.Part).Any(p => p.Slot == Slots.HairFront), Is.False);
            session.Bangs(null);
            Assert.That(session.Notice, Does.Contain("already"));
        }

        [Test]
        public void PeopleWhoKnewTheOldHairReactByTheirTaste()
        {
            TownSession session = AtTheSalon(out SimWorld world);
            // Two residents the player met yesterday: one who loves the cut's style, one who doesn't care for it.
            PartEntry cut = world.Characters.Part(OtherCut(world));
            string tag = cut.Tags.First(t => world.Characters.Archetypes.Any(a => a.TagWeights.ContainsKey(t)));
            Resident fan = world.Residents.OrderByDescending(r => r.Profile.StyleTaste.TryGetValue(tag, out float v) ? v : 0f).First();
            Resident other = world.Residents.Where(r => r != fan)
                .OrderBy(r => FirstImpression.HairTaste(r.Profile, Cut(world, cut), world.Characters)).First();
            foreach (Resident r in new[] { fan, other })
            {
                Relationship rel = world.RelationshipWith(r);
                rel.Met = true;
                rel.Familiarity = 20f;
                rel.LastSeenDay = 1;
            }
            session.Haircut(cut.Id);

            var act = new Interactions(world);
            Assert.That(act.Greet(fan).Reaction, Is.EqualTo("greet.player.makeover.love"));
            float taste = FirstImpression.HairTaste(other.Profile, world.Player.Dna, world.Characters);
            string expected = taste >= Interactions.LovesHairAt ? "love" : taste < Interactions.MehHairBelow ? "meh" : "nice";
            Assert.That(act.Greet(other).Reaction, Is.EqualTo("greet.player.makeover." + expected), "Their taste decides.");
            Assert.That(expected, Is.Not.EqualTo("love"), "Not everyone loves it.");

            Resident stranger = world.Residents.First(r => r != fan && r != other);
            Assert.That(act.Greet(stranger).Reaction, Does.Not.StartWith("greet.player.makeover"), "A stranger never saw the old hair.");
            world.Now = SimTime.At(3, 11);
            Assert.That(act.Greet(fan).Reaction, Does.Not.StartWith("greet.player.makeover"), "Once.");
        }

        [Test]
        public void AnAfroTakesTheBangsOffAndTheSalonSaysWhyItWontAddThem()
        {
            TownSession session = AtTheSalon(out SimWorld world);
            session.Haircut("hair.back.afro_01");
            Assert.That(world.Player.Dna.Hair.PieceIds, Is.EqualTo(new[] { "hair.back.afro_01" }));
            int money = world.Player.Money;
            session.Bangs("hair.front.blunt_01");
            Assert.That(session.Notice, Does.Contain("don't go with your afro"));
            Assert.That(world.Player.Money, Is.EqualTo(money));
        }

        [Test]
        public void LipstickAndBlushCostLittleAndComeOffFree()
        {
            TownSession session = AtTheSalon(out SimWorld world);
            world.Player.Dna.Face.Makeup.Clear();
            HsvColor red = Salon.Lipsticks(world.Characters)[1];
            session.Lipstick(red);
            session.Blush(true);
            Assert.That(world.Player.Money, Is.EqualTo(500 - 2 * Salon.MakeupPrice));
            Assert.That(world.Player.Dna.Face.Makeup.Select(m => m.OverlayId), Is.EquivalentTo(new[] { Makeup.Lipstick, Salon.BlushId }));
            Assert.That(world.Player.LastMakeover.Kind, Is.EqualTo(MakeoverKind.Makeup));
            Assert.That(DnaValidator.Validate(world.Characters, world.Player.Dna), Is.Empty);

            session.Lipstick(red);
            Assert.That(session.Notice, Does.Contain("already"));
            int money = world.Player.Money;
            world.Player.LastMakeover = null;
            session.Lipstick(null);
            session.Blush(false);
            Assert.That(world.Player.Money, Is.EqualTo(money), "Taking it off is free.");
            Assert.That(world.Player.Dna.Face.Makeup, Is.Empty);
            Assert.That(world.Player.LastMakeover, Is.Null, "And nobody remarks on it.");
        }

        [Test]
        public void PeopleReactToMakeupByHowMuchTheirStyleWearsIt()
        {
            TownSession session = AtTheSalon(out SimWorld world);
            world.Player.Dna.Face.Makeup.Clear();
            Resident fan = world.Residents.OrderByDescending(r => FirstImpression.MakeupTaste(r.Profile, world.Characters)).First();
            Resident sceptic = world.Residents.OrderBy(r => FirstImpression.MakeupTaste(r.Profile, world.Characters)).First();
            foreach (Resident r in new[] { fan, sceptic })
            {
                Relationship rel = world.RelationshipWith(r);
                rel.Met = true;
                rel.Familiarity = 20f;
                rel.LastSeenDay = 1;
            }
            session.Lipstick(Salon.Lipsticks(world.Characters)[0]);

            var act = new Interactions(world);
            Assert.That(act.Greet(fan).Reaction, Is.EqualTo("greet.player.makeup.love"));
            Assert.That(act.Greet(sceptic).Reaction, Is.EqualTo("greet.player.makeup.meh"));
        }

        static CharacterDna Cut(SimWorld world, PartEntry cut)
        {
            CharacterDna dna = DnaSerializer.FromJson(DnaSerializer.ToJson(world.Player.Dna, indented: false));
            dna.Hair.PieceIds = new System.Collections.Generic.List<string> { cut.Id };
            return dna;
        }

        [Test]
        public void HairCountsInFirstImpressions()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            CharacterDna player = new CharacterGenerator(world.Characters).Generate(99);
            CharacterDna bald = DnaSerializer.FromJson(DnaSerializer.ToJson(player, indented: false));
            bald.Hair.PieceIds.Clear();
            int differ = world.Residents.Count(r => System.Math.Abs(FirstImpression.Score(r.Profile, player, "casual", world.Characters)
                - FirstImpression.Score(r.Profile, bald, "casual", world.Characters)) > 1e-4f);
            Assert.That(differ, Is.GreaterThan(world.Residents.Count / 2), "Hairstyles carry style tags, and those count.");
        }

        [Test]
        public void TheLastSalonVisitIsSaved()
        {
            TownSession session = AtTheSalon(out SimWorld world);
            session.Haircut(OtherCut(world));
            SimWorld loaded = SimSave.FromJson(SimSave.Capture(world).ToJson()).Restore(Fixtures.Sim(), Fixtures.Characters());
            Assert.That(loaded.Player.LastMakeover?.Day, Is.EqualTo(world.Player.LastMakeover.Day));
            Assert.That(loaded.Player.Dna.Hair.PieceIds, Is.EqualTo(world.Player.Dna.Hair.PieceIds));
        }
    }
}
