using System.Linq;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class TownSessionTests
    {
        static TownSession Saturday(int hour, string location)
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(5, hour);
            world.Player.Location = location;
            return new TownSession(world);
        }

        /// <summary>The busiest open place right now, so tests have people to talk to.</summary>
        static string Busiest(TownSession session) =>
            session.World.Catalog.Locations.Where(l => session.IsOpen(l.Id)).OrderByDescending(l => session.World.At(l.Id).Count()).First().Id;

        [Test]
        public void TravelTakesTimeAndShowsWhoIsThere()
        {
            TownSession session = Saturday(11, SimCatalog.Home);
            Assert.That(session.Present(), Is.Empty, "Nobody visits the player at home.");

            string busiest = Busiest(session);
            long before = session.World.Now.Minutes;
            session.Travel(busiest);
            Assert.That(session.Location, Is.EqualTo(busiest));
            Assert.That(session.World.Now.Minutes - before, Is.EqualTo(TownSession.TravelMinutes));
            Assert.That(session.Present(), Is.Not.Empty);
            Assert.That(session.Present().All(r => session.World.WhereIs(r) == busiest));
        }

        [Test]
        public void ClosedPlacesTurnYouAway()
        {
            TownSession session = Saturday(7, "park");
            session.Travel("bar");
            Assert.That(session.Location, Is.EqualTo("park"));
            Assert.That(session.Notice, Does.Contain("closed"));
        }

        [Test]
        public void ClosingTimeSendsYouHome()
        {
            TownSession session = Saturday(19, "library");
            session.Wait(60);
            Assert.That(session.Location, Is.EqualTo(SimCatalog.Home));
            Assert.That(session.Notice, Does.Contain("closing"));
        }

        [Test]
        public void YouNeedSomeoneSelectedToTalk()
        {
            TownSession session = Saturday(11, "park");
            Assert.That(session.Talk("music"), Is.Null);
            Assert.That(session.Notice, Is.Not.Null);
        }

        [Test]
        public void TalkingUpdatesTheDialogue()
        {
            TownSession session = Saturday(14, "park");
            session.Travel(Busiest(session));
            Resident someone = session.Present()[0];
            session.Select(someone);
            InteractionResult hello = session.Greet();
            Assert.That(session.Last, Is.SameAs(hello));
            Assert.That(session.Speaker, Is.SameAs(someone));
            Assert.That(hello.FirstMeeting);
        }

        [Test]
        public void PeopleLeaveWhenTheirScheduleMovesOn()
        {
            TownSession session = Saturday(14, "park");
            session.Travel(Busiest(session));
            // Find someone whose next few hours take them elsewhere, then wait them out.
            Resident leaver = session.Present().FirstOrDefault(r => Enumerable.Range(1, 4).Any(h => r.Schedule.At(session.World.Now.Plus(h * 60)) != session.Location));
            Assume.That(leaver, Is.Not.Null);
            session.Select(leaver);
            for (int i = 0; i < 4 && session.Selected != null; i++) session.Wait(60);
            Assert.That(session.Selected, Is.Null);
            Assert.That(session.Notice, Does.Contain(leaver.Profile.FirstName));
        }

        [Test]
        public void AYesTakesYouBothOnADate()
        {
            TownSession session = Saturday(14, "park");
            session.Travel(Busiest(session));
            Resident partner = session.Present()[0];
            Relationship rel = session.World.RelationshipWith(partner);
            rel.Met = true;
            rel.Familiarity = 70f;
            rel.Affection = 80f;
            rel.Attraction = 70f;
            session.Select(partner);

            InteractionResult result = session.AskOnDate("park");
            Assert.That(result.Reaction, Is.EqualTo("date.yes"));
            Assert.That(session.DateWith, Is.SameAs(partner));
            Assert.That(session.Location, Is.EqualTo("park"));
            Assert.That(session.World.Player.Outfit, Is.EqualTo(TownSession.DateOutfit));
            Assert.That(session.Present(), Is.EqualTo(new[] { partner }), "Just the two of you.");

            session.EndDate();
            Assert.That(session.DateWith, Is.Null);
            Assert.That(session.World.Player.Outfit, Is.EqualTo(TownSession.DayOutfit));
        }

        [Test]
        public void SleepingStartsTheNextMorningAtHome()
        {
            TownSession session = Saturday(22, "bar");
            session.Sleep();
            Assert.That(session.World.Now.Day, Is.EqualTo(6));
            Assert.That(session.World.Now.Hour, Is.EqualTo(8));
            Assert.That(session.AtHome);
        }

        [Test]
        public void ChangedFiresForEveryVerb()
        {
            TownSession session = Saturday(11, "park");
            int changes = 0;
            session.Changed += () => changes++;
            session.Wait();
            session.Travel("cafe");
            session.Sleep();
            Assert.That(changes, Is.EqualTo(3));
        }
    }
}
