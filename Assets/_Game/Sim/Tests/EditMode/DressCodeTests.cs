using System.Linq;
using Game.Characters.Generation;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    /// <summary>
    /// On the generated content, which has them: nightwear at home at night, swimwear at the beach, and formal wear
    /// at the town's occasional events. On the stand-ins, which don't, none of it happens.
    /// </summary>
    public class DressCodeTests
    {
        static TownSession Session(out SimWorld world, int day, int hour, string location)
        {
            world = Fixtures.GeneratedTown();
            world.Player.Dna = new CharacterGenerator(world.Characters).Generate(99);
            world.Now = SimTime.At(day, hour);
            world.Player.Location = location;
            return new TownSession(world);
        }

        static int Dry(SimWorld world, int from = 0) => Enumerable.Range(from, 200).First(d => Forecast.For(world.Seed, d) != Weather.Rainy);
        static int Rainy(SimWorld world) => Enumerable.Range(0, 200).First(d => Forecast.For(world.Seed, d) == Weather.Rainy);

        [Test]
        public void AtHomeAtNightItsNightwear()
        {
            SimWorld world = Fixtures.GeneratedTown();
            foreach (int hour in new[] { 22, 23, 0, 6, 8 })
            {
                world.Now = SimTime.At(3, hour);
                Assert.That(world.OutfitAt(SimCatalog.Home), Is.EqualTo(TownSession.SleepOutfit), $"{hour}:00");
            }
            world.Now = SimTime.At(Dry(world), 12);
            Assert.That(world.OutfitAt(SimCatalog.Home), Is.EqualTo(TownSession.DayOutfit), "Dressed by day.");

            SimWorld standIns = Fixtures.Town();
            standIns.Now = SimTime.At(3, 23);
            Assert.That(standIns.OutfitAt(SimCatalog.Home), Is.Not.EqualTo(TownSession.SleepOutfit), "The stand-ins have no nightwear.");
        }

        [Test]
        public void ThePlayerWakesInNightwearAndDressesToGoOut()
        {
            TownSession session = Session(out SimWorld world, 2, 21, "cafe");
            session.Sleep();
            Assert.That(session.AtHome);
            Assert.That(world.Player.Outfit, Is.EqualTo(TownSession.SleepOutfit));
            session.Travel("cafe");
            Assert.That(world.Player.Outfit, Is.EqualTo(world.OutfitAt("cafe")).And.Not.EqualTo(TownSession.SleepOutfit));
        }

        [Test]
        public void AtTheBeachItsSwimwearUnlessItRains()
        {
            SimWorld world = Fixtures.GeneratedTown();
            int dry = Dry(world);
            world.Now = SimTime.At(dry, 11);
            Assert.That(world.OutfitAt("beach"), Is.EqualTo(TownSession.SwimOutfit));
            // Someone goes to the beach on a dry day in the first fortnight, and wears their swimwear there.
            Resident swimmer = null;
            for (int day = 0; day < 14 && swimmer == null; day++)
                for (int hour = 9; hour < 19 && swimmer == null; hour++)
                {
                    if (Forecast.For(world.Seed, day) == Weather.Rainy) continue;
                    world.Now = SimTime.At(day, hour);
                    swimmer = world.At("beach").FirstOrDefault();
                }
            Assert.That(swimmer, Is.Not.Null, "The beach is somebody's haunt.");
            Assert.That(world.OutfitFor(swimmer, "beach"), Is.EqualTo(TownSession.SwimOutfit));

            world.Now = SimTime.At(Rainy(world), 11);
            Assert.That(world.At("beach"), Is.Empty, "Nobody goes to the beach in the rain.");
            Assert.That(world.OutfitAt("beach"), Is.Not.EqualTo(TownSession.SwimOutfit));

            TownSession session = Session(out SimWorld played, dry, 11, "cafe");
            session.Travel("beach");
            Assert.That(played.Player.Outfit, Is.EqualTo(TownSession.SwimOutfit), "The player changes for the beach too.");
        }

        [Test]
        public void SomeWeeksThereIsAGalaAndEveryoneThereDressesUp()
        {
            SimWorld world = Fixtures.GeneratedTown();
            int[] galaDays = Enumerable.Range(0, 7 * 20).Where(d => TownEvents.On(world, d).Any(e => e.Id == "gala")).ToArray();
            Assert.That(galaDays.Length, Is.InRange(3, 17), "Some weeks, not every week.");
            Assert.That(galaDays.Select(d => d / 7), Is.Unique, "At most one a week.");
            Assert.That(galaDays.All(d => d % 7 == 4 || d % 7 == 5), "On a Friday or Saturday.");

            int day = galaDays[0];
            world.Now = SimTime.At(day, 20);
            Assert.That(TownEvents.At(world, "bar", world.Now), Is.Not.Null);
            Assert.That(world.At("bar"), Is.Not.Empty, "People come.");
            foreach (Resident resident in world.Residents)
            {
                string where = world.WhereIs(resident);
                bool atWork = world.Catalog.Job(resident.Profile.JobId ?? "")?.Location == where;
                Assert.That(where == SimCatalog.Home || where == "bar" || atWork, $"{resident} is out at {where} instead of the gala.");
                if (where != SimCatalog.Home) Assert.That(world.Catalog.Location(where).IsOpen(20), $"{where} is closed.");
                if (where == "bar" && !atWork) Assert.That(world.OutfitFor(resident, "bar"), Is.EqualTo("formal"), resident.ToString());
            }

            world.Now = SimTime.At(day, 12);
            Assert.That(TownEvents.At(world, "bar", world.Now), Is.Null, "Only in the evening.");
        }

        [Test]
        public void ThePlayerHearsOfTheGalaAndDressesForIt()
        {
            SimWorld probe = Fixtures.GeneratedTown();
            int day = Enumerable.Range(1, 7 * 20).First(d => TownEvents.On(probe, d).Any(e => e.Id == "gala"));
            TownSession session = Session(out SimWorld world, day - 1, 22, SimCatalog.Home);
            session.Sleep();
            Assert.That(session.Notice, Does.Contain("Harbour Gala"));
            session.Wait(60 * (19 - world.Now.Hour) + 30);
            session.Travel("bar");
            Assert.That(world.Player.Outfit, Is.EqualTo("formal"));
            Assert.That(session.Notice, Does.Contain("Harbour Gala"));
        }

        [Test]
        public void TheGardenPartyIsCalledOffInTheRain()
        {
            SimWorld world = Fixtures.GeneratedTown();
            int[] parties = Enumerable.Range(0, 7 * 60).Where(d => TownEvents.On(world, d).Any(e => e.Id == "gardenparty")).ToArray();
            Assert.That(parties, Is.Not.Empty);
            Assert.That(parties.Select(d => Forecast.For(world.Seed, d)), Has.None.EqualTo(Weather.Rainy));
        }

        [Test]
        public void OnTheStandInsThereAreNoEvents()
        {
            SimWorld world = Fixtures.Town();
            Assert.That(Enumerable.Range(0, 7 * 20).SelectMany(d => TownEvents.On(world, d)), Is.Empty,
                "The stand-ins have no formal wear to dress for one.");
        }
    }
}
