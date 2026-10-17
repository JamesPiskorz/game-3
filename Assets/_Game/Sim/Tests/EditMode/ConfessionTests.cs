using System.Linq;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    /// <summary>Close friends who've fallen for the player say so, and the player answers.</summary>
    public class ConfessionTests
    {
        /// <summary>A session at noon on day 2, standing next to a close friend (single, or not) who last saw the player yesterday.</summary>
        static (TownSession Session, Resident Resident, Relationship Rel) Smitten(bool single = true, float attraction = 85f)
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(2, 12);
            Resident resident = world.Residents.First(r => (world.Social.PartnerOf(r.Index) < 0) == single
                && world.WhereIs(r) != SimCatalog.Home && world.Catalog.Location(world.WhereIs(r)).IsOpen(12));
            world.Player.Location = world.WhereIs(resident);
            Relationship rel = world.RelationshipWith(resident);
            rel.Met = true;
            rel.Familiarity = 65f;
            rel.Affection = 70f;
            rel.Trust = 40f;
            rel.Attraction = attraction;
            rel.LastSeenDay = 1;
            var session = new TownSession(world);
            session.Select(resident);
            Assume.That(session.Selected, Is.EqualTo(resident));
            return (session, resident, rel);
        }

        [Test]
        public void ACloseFriendWithASparkConfessesAndYesMeansDating()
        {
            (TownSession session, Resident resident, Relationship rel) = Smitten();
            InteractionResult hello = session.Greet();
            Assert.That(hello.Reaction, Is.EqualTo("greet.confess"));
            Assert.That(session.Confession?.With, Is.EqualTo(resident));
            Assert.That(session.Hint(), Does.Contain("feelings"));

            InteractionResult answer = session.AnswerConfession(true);
            Assert.That(answer.Reaction, Is.EqualTo("confess.yes"));
            Assert.That(rel.Dating, Is.True);
            Assert.That(rel.Stage, Is.EqualTo(Stage.Dating));
            Assert.That(session.Confession, Is.Null);
            Assert.That(answer.Line, Does.Not.Contain("{"));
        }

        [Test]
        public void NoCoolsTheSparkAndTheyDontAskAgainForAWhile()
        {
            (TownSession session, Resident resident, Relationship rel) = Smitten();
            Assume.That(session.Greet().Reaction, Is.EqualTo("greet.confess"));
            float spark = rel.Attraction;
            Assert.That(session.AnswerConfession(false).Reaction, Is.EqualTo("confess.no"));
            Assert.That(rel.Dating, Is.False);
            Assert.That(rel.Attraction, Is.LessThan(spark - 10f));

            // Even with the spark back, the next weeks' hellos are just hellos.
            var act = new Interactions(session.World);
            for (int day = 3; day < 2 + Interactions.ConfessGapDays; day++)
            {
                session.World.Now = SimTime.At(day, 12);
                rel.Attraction = 90f;
                Assert.That(act.Greet(resident).Reaction, Does.Not.StartWith("greet.confess"), $"day {day}");
            }
            session.World.Now = SimTime.At(2 + Interactions.ConfessGapDays, 12);
            Assert.That(act.Greet(resident).Reaction, Is.EqualTo("greet.confess"));
        }

        [Test]
        public void NobodyConfessesWithoutASparkOrToSomeoneSpokenFor()
        {
            (TownSession session, _, _) = Smitten(attraction: 50f);
            Assert.That(session.Greet().Reaction, Does.Not.StartWith("greet.confess"));
            Assert.That(session.Confession, Is.Null);

            (TownSession taken, Resident other, _) = Smitten();
            Relationship partner = taken.World.RelationshipWith(taken.World.Residents.First(r => r != other));
            partner.Met = true;
            partner.Dating = partner.Partner = true;
            Assert.That(taken.Greet().Reaction, Does.Not.StartWith("greet.confess"));
        }

        [Test]
        public void SomeoneSeeingAnotherResidentOnlyConfessesIfTheydLeaveThem()
        {
            (TownSession session, Resident resident, Relationship rel) = Smitten(single: false);
            Assert.That(session.Greet().Reaction, Does.Not.StartWith("greet.confess"), "Fond, but not enough to leave.");

            session.World.Now = SimTime.At(3, 12);
            rel.Affection = Interactions.LeaveForYouAffection + 2f;
            rel.Attraction = Interactions.LeaveForYouAttraction + 5f;
            session.Select(null);
            session.Select(resident);
            Assume.That(session.Selected, Is.EqualTo(resident));
            Assert.That(session.Greet().Reaction, Is.EqualTo("greet.confess.taken"));
            Assert.That(session.Confession.Partner, Is.GreaterThanOrEqualTo(0));
            Assert.That(session.AnswerConfession(true).Reaction, Is.EqualTo("confess.yes.leaves"));
            Assert.That(session.World.Social.PartnerOf(resident.Index), Is.LessThan(0), "They've left their partner.");
        }

        [Test]
        public void DoingSomethingElseLeavesItUnanswered()
        {
            (TownSession session, _, Relationship rel) = Smitten();
            Assume.That(session.Greet().Reaction, Is.EqualTo("greet.confess"));
            session.Compliment();
            Assert.That(session.Confession, Is.Null);
            Assert.That(session.AnswerConfession(true), Is.Null);
            Assert.That(session.Notice, Does.Contain("Nobody"));
            Assert.That(rel.Dating, Is.False);
        }

        [Test]
        public void ItSurvivesSavingWhileTheyWait()
        {
            (TownSession session, Resident resident, _) = Smitten();
            Assume.That(session.Greet().Reaction, Is.EqualTo("greet.confess"));
            SimWorld loaded = SimSave.FromJson(SimSave.Capture(session.World).ToJson()).Restore(Fixtures.Sim(), Fixtures.Characters());
            Assert.That(new Interactions(loaded).AwaitingAnswer(loaded.Residents[resident.Index]), Is.True);
        }
    }
}
