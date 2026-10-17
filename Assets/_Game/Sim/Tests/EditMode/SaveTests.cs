using System;
using System.Linq;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class SaveTests
    {
        static SimWorld PlayedWorld()
        {
            SimWorld world = Fixtures.Town(0xC0FFEE, 10);
            world.Player.Name = "Rin";
            world.Player.Dna = new CharacterGenerator(world.Characters).Generate(99);
            var act = new Interactions(world);
            foreach (Resident resident in world.Residents.Take(4))
            {
                act.Greet(resident);
                act.Talk(resident, "music");
                act.Gift(resident, "sweets");
            }
            world.AdvanceTo(18);
            act.Compliment(world.Residents[1]);
            return world;
        }

        [Test]
        public void SaveRoundTripsThroughJson()
        {
            SimWorld world = PlayedWorld();
            string json = SimSave.Capture(world).ToJson();
            SimWorld loaded = SimSave.FromJson(json).Restore(Fixtures.Sim(), Fixtures.Characters());

            Assert.That(loaded.Now.Minutes, Is.EqualTo(world.Now.Minutes));
            Assert.That(loaded.Actions, Is.EqualTo(world.Actions));
            Assert.That(loaded.Player.Name, Is.EqualTo("Rin"));
            Assert.That(loaded.Player.Dna.Seed, Is.EqualTo(world.Player.Dna.Seed));
            Assert.That(loaded.Residents.Select(r => r.Name), Is.EqualTo(world.Residents.Select(r => r.Name)));
            Assert.That(loaded.Relationships.Count(), Is.EqualTo(4));
            foreach ((Relationship a, Relationship b) in world.Relationships.Zip(loaded.Relationships, (a, b) => (a, b)))
            {
                Assert.That(b.Affection, Is.EqualTo(a.Affection));
                Assert.That(b.Familiarity, Is.EqualTo(a.Familiarity));
                Assert.That(b.Attraction, Is.EqualTo(a.Attraction));
                Assert.That(b.Trust, Is.EqualTo(a.Trust));
                Assert.That(b.Stage, Is.EqualTo(a.Stage));
                Assert.That(b.KnownLikes, Is.EqualTo(a.KnownLikes));
                Assert.That(b.TopicDays, Is.EqualTo(a.TopicDays));
            }
            Assert.That(SimSave.Capture(loaded).ToJson(), Is.EqualTo(json), "Saving a loaded game changes nothing.");
        }

        [Test]
        public void ALoadedGamePlaysOnIdentically()
        {
            SimWorld world = PlayedWorld();
            SimWorld loaded = SimSave.FromJson(SimSave.Capture(world).ToJson()).Restore(Fixtures.Sim(), Fixtures.Characters());

            InteractionResult a = new Interactions(world).Talk(world.Residents[2], "books");
            InteractionResult b = new Interactions(loaded).Talk(loaded.Residents[2], "books");
            Assert.That(b.Line, Is.EqualTo(a.Line));
            Assert.That(b.Affection, Is.EqualTo(a.Affection));
        }

        [Test]
        public void SeedsSurviveAsHex()
        {
            SimWorld world = Fixtures.Town(0xFFFF_FFFF_FFFF_FFF0, 1);
            string json = SimSave.Capture(world).ToJson();
            Assert.That(json, Does.Contain("\"worldSeed\": \"fffffffffffffff0\""));
            Assert.That(SimSave.FromJson(json).WorldSeed, Is.EqualTo(0xFFFF_FFFF_FFFF_FFF0));
        }

        [Test]
        public void BadSavesAreFormatErrors()
        {
            Assert.Throws<FormatException>(() => SimSave.FromJson("{ \"nonsense\": 1 }"));
            Assert.Throws<FormatException>(() => SimSave.FromJson("not json"));
            var future = new SimSave { Version = SimSave.CurrentVersion + 1, Population = 1 };
            Assert.Throws<FormatException>(() => future.Restore(Fixtures.Sim(), Fixtures.Characters()));
        }
    }
}
