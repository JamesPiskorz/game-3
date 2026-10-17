using System.Linq;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class FamilyTests
    {
        static SimWorld Town() => Fixtures.Town(0x5EED, 24);

        static Resident WithSibling(SimWorld world) => world.Residents.First(r => world.SiblingsOf(r).Any());

        [Test]
        public void TownsHaveFamiliesWhoShareANameAndAnAge()
        {
            foreach (ulong seed in new[] { 0x5EEDUL, 11UL, 22UL, 33UL })
            {
                SimWorld world = Fixtures.Town(seed, 24);
                var families = world.Residents.GroupBy(r => r.Family).Where(g => g.Count() > 1).ToList();
                Assert.That(families.Count, Is.GreaterThanOrEqualTo(2), $"town {seed}");
                foreach (var family in families)
                {
                    Assert.That(family.Select(r => r.Profile.FamilyName).Distinct().Count(), Is.EqualTo(1), "Siblings share a name.");
                    Assert.That(family.Max(r => r.Profile.Age) - family.Min(r => r.Profile.Age), Is.LessThanOrEqualTo(2 * TownGenerator.SiblingAgeGap));
                    Assert.That(family.Select(r => r.Profile.FirstName).Distinct().Count(), Is.EqualTo(family.Count()));
                }
                var surnames = world.Residents.GroupBy(r => r.Profile.FamilyName);
                Assert.That(surnames.All(g => g.Select(r => r.Family).Distinct().Count() == 1), "A shared surname always means family.");
            }
        }

        [Test]
        public void SiblingsLookRelated()
        {
            SimWorld world = Town();
            var families = world.Residents.GroupBy(r => r.Family).Where(g => g.Count() > 1).ToList();
            float related = families.Average(g => AppearanceDistance.Between(g.First().Dna, g.Last().Dna));
            float town = world.Residents.Average(a => world.Residents.Where(b => b.Family != a.Family).Average(b => AppearanceDistance.Between(a.Dna, b.Dna)));
            Assert.That(related, Is.LessThan(town), $"siblings {related:0.00}, everyone else {town:0.00}");
        }

        [Test]
        public void SiblingsArentFriendsOrCouplesInTheWeb()
        {
            foreach (ulong seed in new[] { 0x5EEDUL, 11UL, 22UL, 33UL })
            {
                SimWorld world = Fixtures.Town(seed, 24);
                foreach (Resident r in world.Residents)
                    foreach (Resident s in world.SiblingsOf(r))
                    {
                        Assert.That(world.Social.AreFriends(r.Index, s.Index), Is.False);
                        Assert.That(world.Social.AreCouple(r.Index, s.Index), Is.False);
                    }
            }
        }

        [Test]
        public void AFondSiblingHasToldThemAboutYou()
        {
            SimWorld world = Town();
            world.Now = SimTime.At(1, 10);
            Resident sibling = WithSibling(world);
            Resident fan = world.SiblingsOf(sibling).First();
            Relationship fond = world.RelationshipWith(fan);
            fond.Met = true;
            fond.Affection = Interactions.GoodWordAffection + 5f;

            InteractionResult hello = new Interactions(world).Greet(sibling);
            Assert.That(hello.Reaction, Is.EqualTo("greet.stranger.family"));
            Assert.That(hello.Line, Does.Contain(fan.Profile.FirstName));
            Assert.That(hello.Line, Does.Contain(fan.Profile.SiblingWord));
        }

        [Test]
        public void AskingAboutTheirFriendsTellsYouAboutFamily()
        {
            SimWorld world = Town();
            world.Now = SimTime.At(1, 10);
            Resident resident = WithSibling(world);
            Relationship rel = world.RelationshipWith(resident);
            rel.Met = true;
            rel.Familiarity = 15f;
            rel.LastSeenDay = 1;
            InteractionResult answer = new Interactions(world).Ask(resident, "friends");
            Assert.That(answer.Learnt, Does.Contain(world.SiblingsOf(resident).First().Profile.FirstName));
            Assert.That(answer.Learnt, Does.Contain(resident.Profile.SiblingWord));
        }

        [Test]
        public void DatingTwoSiblingsIsNoticed()
        {
            SimWorld world = Town();
            world.Now = SimTime.At(3, 10);
            Resident a = WithSibling(world);
            Resident b = world.SiblingsOf(a).First();
            foreach (Resident r in new[] { a, b })
            {
                Relationship rel = world.RelationshipWith(r);
                rel.Met = true;
                rel.Familiarity = 50f;
                rel.Affection = 60f;
                rel.Attraction = 60f;
                rel.Dating = true;
                rel.LastSeenDay = 1;
            }
            var act = new Interactions(world);
            Assert.That(act.Greet(a).Reaction, Is.EqualTo("greet.jealous"));
        }
    }
}
