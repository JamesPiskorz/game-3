using System.Linq;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    /// <summary>The player dresses by the same rules as everyone: rain, the gym, a night out.</summary>
    public class OccasionTests
    {
        static TownSession Town(out SimWorld world, int day, int hour, string location)
        {
            world = Fixtures.Town(0x5EED, 24);
            world.Player.Dna = new CharacterGenerator(world.Characters).Generate(99);
            world.Now = SimTime.At(day, hour);
            world.Player.Location = location;
            var session = new TownSession(world);
            session.Wait(1);
            return session;
        }

        static int Rainy(SimWorld world) => Enumerable.Range(0, 60).First(d => Forecast.For(world.Seed, d) == Weather.Rainy);
        static int Dry(SimWorld world) => Enumerable.Range(0, 60).First(d => Forecast.For(world.Seed, d) == Weather.Sunny);

        [Test]
        public void ThePlayerWearsTheirCoatInTheRainAndWorkoutClothesAtTheGym()
        {
            SimWorld probe = Fixtures.Town(0x5EED, 24);
            Town(out SimWorld rainy, Rainy(probe), 11, "cafe");
            Assert.That(rainy.Player.Outfit, Is.EqualTo(TownSession.RainOutfit));
            Town(out SimWorld dry, Dry(probe), 11, "cafe");
            Assert.That(dry.Player.Outfit, Is.EqualTo(TownSession.DayOutfit));
            TownSession gym = Town(out SimWorld world, Dry(probe), 11, "cafe");
            gym.Travel("gym");
            Assert.That(world.Player.Outfit, Is.EqualTo(TownSession.SportOutfit));
            gym.Travel("cafe");
            Assert.That(world.Player.Outfit, Is.EqualTo(TownSession.DayOutfit), "And back into everyday clothes after.");
        }

        [Test]
        public void TheWardrobeKeepsToWhatSuitsTheOutfit()
        {
            SimWorld probe = Fixtures.Town(0x5EED, 24);
            TownSession session = Town(out SimWorld world, Rainy(probe), 9, SimCatalog.Home);
            Assert.That(world.Player.Outfit, Is.EqualTo(TownSession.RainOutfit));
            var tank = new ItemDna { ItemId = "cloth.top.tank_01", RegionColors = { new HsvColor(0.1f, 0.5f, 0.5f), new HsvColor(0.6f, 0.5f, 0.5f) } };
            world.Player.Wardrobe.Add(tank);
            Assert.That(session.Changeable().Any(i => i.ItemId == tank.ItemId), Is.False, "Not offered for a rainy day.");
            Assert.That(session.Wear(tank), Is.False);
            Assert.That(session.Notice, Does.Contain("rainy day"));
        }

        [Test]
        public void AnOldLookGetsTheOutfitsItWasMissing()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            CharacterDna old = new CharacterGenerator(world.Characters).Generate(99);
            old.Outfits.RemoveAll(o => o.Context == TownSession.RainOutfit || o.Context == TownSession.SportOutfit);
            world.Player.Dna = old;
            var session = new TownSession(world);
            Assert.That(world.Player.Dna.Outfits.Select(o => o.Context), Is.SupersetOf(new[] { TownSession.RainOutfit, TownSession.SportOutfit }));
            OutfitDna rain = world.Player.Dna.Outfits.Single(o => o.Context == TownSession.RainOutfit);
            Assert.That(rain.Items.All(i => session.Owns(i)), "They own what's in it.");
            Assert.That(DnaValidator.Validate(world.Characters, world.Player.Dna), Is.Empty);
        }
    }
}
