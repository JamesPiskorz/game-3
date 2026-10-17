using System.Linq;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class AskTests
    {
        static (SimWorld World, Interactions Act, Resident Resident) Acquainted()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(1, 10);
            Resident resident = world.Residents.First(r => world.Social.FriendsOf(r.Index).Count > 0);
            Relationship rel = world.RelationshipWith(resident);
            rel.Met = true;
            rel.Familiarity = 15f;
            rel.LastSeenDay = 1;
            return (world, new Interactions(world), resident);
        }

        [Test]
        public void AskingWhatTheyreIntoRevealsALove()
        {
            (SimWorld world, Interactions act, Resident resident) = Acquainted();
            InteractionResult answer = act.Ask(resident, "likes");
            Assert.That(answer.Reaction, Is.EqualTo("ask.likes"));
            string love = world.RelationshipWith(resident).KnownLikes.Single();
            Assert.That(resident.Profile.Interest(love), Is.GreaterThanOrEqualTo(0.5f));
            Assert.That(answer.Line, Does.Contain(world.Catalog.Topic(love).Name).IgnoreCase);
        }

        [Test]
        public void AskingTwiceInADayIsNoticed()
        {
            (SimWorld _, Interactions act, Resident resident) = Acquainted();
            act.Ask(resident, "likes");
            InteractionResult again = act.Ask(resident, "likes");
            Assert.That(again.Reaction, Is.EqualTo("ask.again"));
            Assert.That(again.Affection, Is.LessThan(0f));
        }

        [Test]
        public void StrangersFindPersonalQuestionsNosy()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            var act = new Interactions(world);
            Assert.That(act.Ask(world.Residents[0], "friends").Reaction, Is.EqualTo("ask.nosy"));
        }

        [Test]
        public void AskingAboutFriendsRevealsTheirLife()
        {
            (SimWorld world, Interactions act, Resident resident) = Acquainted();
            InteractionResult answer = act.Ask(resident, "friends");
            Assert.That(answer.Reaction, Does.StartWith("ask.friends"));
            Assert.That(world.RelationshipWith(resident).KnowsTheirLife);
        }

        [Test]
        public void AskingAboutPlansPutsThemInTheContactBook()
        {
            (SimWorld world, Interactions act, Resident resident) = Acquainted();
            InteractionResult answer = act.Ask(resident, "plans");
            Relationship rel = world.RelationshipWith(resident);
            if (answer.Reaction == "ask.plans.none")
            {
                Assert.That(rel.Plan, Is.Null);
                return;
            }
            Assert.That(answer.Reaction, Is.EqualTo("ask.plans"));
            Assert.That(world.WhereIs(resident, SimTime.At(rel.Plan.Day, rel.PlanHour)), Is.EqualTo(rel.Plan.Id), "They'll really be there.");

            world.Now = SimTime.At(rel.Plan.Day, rel.PlanHour);
            var session = new TownSession(world);
            Contact contact = session.Contacts().Single(c => c.Resident == resident);
            Assert.That(contact.KnownLocation, Is.EqualTo(rel.Plan.Id));
            Assert.That(contact.Hint, Does.Contain("Said they'd be"));
        }
    }
}
