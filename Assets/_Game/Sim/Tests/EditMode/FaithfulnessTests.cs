using System.Linq;
using Game.Sim.Relationships;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    /// <summary>Seeing more than one person: the town talks, and partners don't stand for it twice.</summary>
    public class FaithfulnessTests
    {
        static (SimWorld World, Interactions Act, Resident Partner, Resident Other) TwoRomances()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(3, 19);
            Resident partner = world.Residents[0];
            Resident other = world.Residents.First(r => r != partner && world.SiblingsOf(partner).All(s => s != r)
                && !world.Social.AreFriends(r.Index, partner.Index) && world.Social.PartnerOf(r.Index) < 0);
            Relationship p = world.RelationshipWith(partner);
            p.Met = true; p.Familiarity = 70f; p.Affection = 85f; p.Attraction = 80f; p.Trust = 65f; p.Dating = true; p.Partner = true; p.Dates = 4; p.LastSeenDay = 2;
            Relationship o = world.RelationshipWith(other);
            o.Met = true; o.Familiarity = 60f; o.Affection = 75f; o.Attraction = 75f; o.Trust = 50f; o.LastSeenDay = 2;
            return (world, new Interactions(world), partner, other);
        }

        static void DateWith(SimWorld world, Interactions act, Resident who)
        {
            string place = world.Catalog.Locations.First(l => l.IsOpen(world.Now.Hour) && l.Tags.Contains("romantic")).Id;
            InteractionResult answer = act.AskOnDate(who, place);
            Assert.That(world.RelationshipWith(who).Dates, Is.GreaterThan(0), answer.Reaction);
        }

        [Test]
        public void APartnerHearsAboutADateWithSomeoneElse()
        {
            (SimWorld world, Interactions act, Resident partner, Resident other) = TwoRomances();
            DateWith(world, act, other);
            float affection = world.RelationshipWith(partner).Affection;
            InteractionResult hello = act.Greet(partner);
            Assert.That(hello.Reaction, Is.EqualTo("greet.cheated"));
            Assert.That(hello.Line, Does.Contain(other.Profile.FirstName));
            Assert.That(world.RelationshipWith(partner).Affection, Is.LessThan(affection));
            Assert.That(world.RelationshipWith(partner).Partner, "Once is forgiven, just.");
            Assert.That(act.Greet(partner).Reaction, Is.Not.EqualTo("greet.cheated"), "They bring it up once.");
        }

        [Test]
        public void TwiceAndTheyLeave()
        {
            (SimWorld world, Interactions act, Resident partner, Resident other) = TwoRomances();
            DateWith(world, act, other);
            act.Greet(partner);
            world.Now = SimTime.At(4, 19);
            DateWith(world, act, other);
            InteractionResult hello = act.Greet(partner);
            Assert.That(hello.Reaction, Is.EqualTo("greet.breakup"));
            Relationship rel = world.RelationshipWith(partner);
            Assert.That(rel.Partner, Is.False);
            Assert.That(rel.Dating, Is.False);
            Assert.That(hello.Learnt, Does.Contain("ended things"));
        }

        [Test]
        public void DatingOnePersonUpsetsNobody()
        {
            (SimWorld world, Interactions act, Resident partner, Resident _) = TwoRomances();
            DateWith(world, act, partner);
            Assert.That(world.Relationships.All(r => r.HeardAboutDatesWith.Count == 0));
            Assert.That(act.Greet(partner).Reaction, Is.Not.EqualTo("greet.cheated"));
        }
    }
}
