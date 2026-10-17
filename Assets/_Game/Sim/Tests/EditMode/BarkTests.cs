using System.Linq;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class BarkTests
    {
        static TownSession Busy()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(5, 14);
            world.Player.Location = world.Catalog.Locations.OrderByDescending(l => world.At(l.Id).Count()).First().Id;
            return new TownSession(world);
        }

        [Test]
        public void APlaceHasAFewVoicesThatHoldForTheHour()
        {
            TownSession session = Busy();
            var first = session.Overheard();
            Assert.That(first.Count, Is.InRange(0, Barks.MaxPerScene));
            Assert.That(first.Select(b => b.Line), Is.EqualTo(session.Overheard().Select(b => b.Line)), "Same hour, same chatter.");
            Assert.That(first.All(b => session.Present().Contains(b.Resident)));
            Assert.That(first.All(b => !b.Line.Contains("{")));
        }

        [Test]
        public void ThePersonYoureTalkingToIsntBarking()
        {
            TownSession session = Busy();
            for (int hour = 10; hour < 20; hour++)
            {
                session.World.Now = SimTime.At(5, hour);
                var present = session.Present();
                if (present.Count == 0) continue;
                session.Select(present[0]);
                Assert.That(session.Overheard().Any(b => b.Resident == session.Selected), Is.False);
            }
        }

        [Test]
        public void OverAWeekTheTownSaysPlentyOfDifferentThings()
        {
            TownSession session = Busy();
            var lines = new System.Collections.Generic.HashSet<string>();
            for (int day = 0; day < 7; day++)
                for (int hour = 9; hour < 22; hour += 2)
                {
                    session.World.Now = SimTime.At(day, hour);
                    foreach (var place in session.World.Catalog.Locations)
                    {
                        session.World.Player.Location = place.Id;
                        foreach (var bark in session.Overheard()) lines.Add(bark.Line);
                    }
                }
            Assert.That(lines.Count, Is.GreaterThan(25));
        }
    }
}
