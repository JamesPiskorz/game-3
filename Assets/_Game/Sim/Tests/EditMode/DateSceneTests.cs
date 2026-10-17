using System.Linq;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class DateSceneTests
    {
        static readonly string[] Outcomes = { "date.great", "date.fine", "date.awkward" };

        static void Fond(Relationship rel)
        {
            rel.Met = true;
            rel.Familiarity = 60f;
            rel.Affection = 70f;
            rel.Attraction = 60f;
            rel.Trust = 30f;
        }

        [Test]
        public void EachRoundOffersFreshTopicsAndACompliment()
        {
            (SimWorld world, Resident resident) = Fixtures.OneResident();
            Fond(world.RelationshipWith(resident));
            var act = new Interactions(world);
            act.Invite(resident, "park", out DateScene scene);
            Assert.That(scene, Is.Not.Null);

            while (!scene.Done)
            {
                Assert.That(scene.Options.Count(o => o.Kind == DateOptionKind.Compliment), Is.EqualTo(1));
                var topics = scene.Options.Where(o => o.Kind == DateOptionKind.Topic).Select(o => o.Topic).ToList();
                Assert.That(topics.Count, Is.EqualTo(3));
                Assert.That(topics, Is.Unique);
                Assert.That(topics, Has.None.Matches<string>(t => scene.TopicsRaised.Contains(t)), "Topics already raised on this date aren't offered again.");
                act.DateChoose(scene, scene.Options.FindIndex(o => o.Kind == DateOptionKind.Topic));
            }
            Assert.That(act.FinishDate(scene).Reaction, Does.StartWith("date."));
            Assert.That(world.RelationshipWith(resident).Dates, Is.EqualTo(1));
        }

        [Test]
        public void AttentionMakesBetterDates()
        {
            int Great(bool attentive)
            {
                SimWorld world = Fixtures.Town(0xDA7E, 30);
                var act = new Interactions(world);
                int great = 0;
                foreach (Resident resident in world.Residents.Where(r => world.Social.PartnerOf(r.Index) < 0))
                {
                    Fond(world.RelationshipWith(resident));
                    act.Invite(resident, "park", out DateScene scene);
                    while (!scene.Done)
                    {
                        // Careless: whatever they'd like least.
                        int worst = Enumerable.Range(0, scene.Options.Count)
                            .Where(i => scene.Options[i].Kind == DateOptionKind.Topic)
                            .OrderBy(i => resident.Profile.Interest(scene.Options[i].Topic)).First();
                        act.DateChoose(scene, attentive ? scene.BestOption() : worst);
                    }
                    if (act.FinishDate(scene).Reaction == "date.great") great++;
                }
                return great;
            }

            int attentiveGreat = Great(true), carelessGreat = Great(false);
            Assert.That(attentiveGreat, Is.GreaterThan(carelessGreat + 4), $"attentive {attentiveGreat}, careless {carelessGreat} (of the single residents)");
        }

        [Test]
        public void ADatePlaysOutRoundByRoundInTown()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(5, 14);
            world.Player.Location = "park";
            var session = new TownSession(world);
            Resident partner = world.Residents.First(r => r.Schedule.At(world.Now) != SimCatalog.Home && world.Social.PartnerOf(r.Index) < 0);
            session.Travel(world.WhereIs(partner));
            Fond(world.RelationshipWith(partner));
            session.Select(partner);

            Assert.That(session.AskOnDate("park").Reaction, Is.EqualTo("date.yes"));
            Assert.That(session.Date, Is.Not.Null);
            for (int round = 0; round < DateScene.Rounds; round++)
            {
                InteractionResult reply = session.DateChoose(0);
                Assert.That(reply.Reaction, Does.StartWith("date."));
                Assert.That(session.Speaker, Is.SameAs(partner));
            }
            Assert.That(session.Date.Finished);
            Assert.That(Outcomes, Does.Contain(session.Last.FollowUpReaction));
            Assert.That(session.DateChoose(0), Is.Null, "No rounds left.");

            session.EndDate();
            Assert.That(session.Date, Is.Null);
            Assert.That(world.RelationshipWith(partner).Dates, Is.EqualTo(1));
        }

        [Test]
        public void SayingGoodnightEarlyStillCountsTheDate()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(5, 14);
            Resident partner = world.Residents.First(r => r.Schedule.At(world.Now) != SimCatalog.Home && world.Social.PartnerOf(r.Index) < 0);
            world.Player.Location = world.WhereIs(partner);
            var session = new TownSession(world);
            Fond(world.RelationshipWith(partner));
            session.Select(partner);
            session.AskOnDate("park");
            session.DateChoose(session.Date.BestOption());

            session.EndDate();
            Assert.That(world.RelationshipWith(partner).Dates, Is.EqualTo(1));
            Assert.That(Outcomes, Does.Contain(session.Last.Reaction));
        }
    }
}
