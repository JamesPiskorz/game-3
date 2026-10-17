using System.Linq;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class StatsTests
    {
        static TownSession At(string location, int hour = 12)
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(1, hour);
            world.Player.Location = location;
            world.Player.Money = 100;
            return new TownSession(world);
        }

        [Test]
        public void EveryActivityRaisesARealStat()
        {
            foreach (LocationEntry place in Fixtures.Sim().Locations)
                foreach (ActivityEntry activity in place.Activities)
                {
                    Assert.That(SimCatalog.Stats, Does.Contain(activity.Stat), $"{place.Id}/{activity.Id}");
                    Assert.That(activity.Gain, Is.GreaterThan(0f));
                    Assert.That(activity.Hours, Is.GreaterThan(0));
                }
            Assert.That(SimCatalog.Stats.All(stat => Fixtures.Sim().Locations.Any(l => l.Activities.Any(a => a.Stat == stat))), "Every stat can be trained somewhere.");
        }

        [Test]
        public void WorkingOutTakesTimeAndCoinsAndTapersOff()
        {
            TownSession session = At("gym", 8);
            ActivityEntry workout = session.Activities().Single();
            float first = session.World.Player.Stat("fitness");
            session.DoActivity(workout.Id);
            float gainOne = session.World.Player.Stat("fitness") - first;
            Assert.That(gainOne, Is.GreaterThan(0f));
            Assert.That(session.World.Player.Money, Is.EqualTo(100 - workout.Cost));
            Assert.That(session.World.Now.Hour, Is.EqualTo(8 + workout.Hours));

            session.World.Player.Stats["fitness"] = 90f;
            session.DoActivity(workout.Id);
            Assert.That(session.World.Player.Stat("fitness") - 90f, Is.LessThan(gainOne), "Gains taper as you get better.");
            Assert.That(session.World.Player.Stat("fitness"), Is.LessThanOrEqualTo(100f));
        }

        [Test]
        public void ActivitiesNeedTheRightPlaceAndMoney()
        {
            TownSession park = At("park");
            park.DoActivity("workout");
            Assert.That(park.Notice, Does.Contain("can't"));

            TownSession bar = At("bar", 20);
            bar.World.Player.Money = 0;
            bar.DoActivity("mingle");
            Assert.That(bar.Notice, Does.Contain("coins"));
            Assert.That(bar.World.Player.Stat("charm"), Is.EqualTo(10f));
        }

        [Test]
        public void PeopleAdmireWhatTheirStyleValues()
        {
            SimWorld world = Fixtures.Town(0x5EED, 40);
            Resident sporty = world.Residents.First(r => r.Profile.Archetype == "sporty");
            var act = new Interactions(world);
            float before = act.Admiration(sporty);
            world.Player.Stats["fitness"] = 90f;
            Assert.That(act.Admiration(sporty), Is.GreaterThan(before + 0.15f));
            world.Player.Stats["smarts"] = 90f;
            Resident bookish = world.Residents.First(r => r.Profile.Archetype == "bookish");
            Assert.That(act.Admiration(bookish), Is.GreaterThan(0.2f));
        }

        [Test]
        public void StatsAreSaved()
        {
            TownSession session = At("library");
            session.DoActivity("read");
            SimWorld loaded = SimSave.FromJson(SimSave.Capture(session.World).ToJson()).Restore(Fixtures.Sim(), Fixtures.Characters());
            Assert.That(loaded.Player.Stat("smarts"), Is.EqualTo(session.World.Player.Stat("smarts")));
        }
    }
}
