using System.Linq;
using Game.Sim.Relationships;
using Game.Sim.Social;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class SocialTests
    {
        [Test]
        public void TheSameTownHasTheSameFriendships()
        {
            SimWorld a = Fixtures.Town(0x5EED, 24), b = Fixtures.Town(0x5EED, 24);
            for (int i = 0; i < 24; i++)
            {
                Assert.That(b.Social.FriendsOf(i), Is.EqualTo(a.Social.FriendsOf(i)));
                Assert.That(b.Social.PartnerOf(i), Is.EqualTo(a.Social.PartnerOf(i)));
            }
        }

        [Test]
        public void FriendshipsAreMutualAndMostPeopleHaveSome()
        {
            SimWorld town = Fixtures.Town(0x5EED, 40);
            for (int i = 0; i < 40; i++)
            {
                Assert.That(town.Social.FriendsOf(i).Count, Is.LessThanOrEqualTo(SocialWeb.MaxFriends));
                foreach (int friend in town.Social.FriendsOf(i))
                {
                    Assert.That(friend, Is.Not.EqualTo(i));
                    Assert.That(town.Social.AreFriends(friend, i), $"{i} and {friend}");
                }
            }
            Assert.That(Enumerable.Range(0, 40).Count(i => town.Social.FriendsOf(i).Count > 0), Is.GreaterThan(24), "Most residents have friends.");
        }

        [Test]
        public void CouplesArePairsAndAboutAQuarterOfTown()
        {
            SimWorld town = Fixtures.Town(0x5EED, 40);
            var coupled = Enumerable.Range(0, 40).Where(i => town.Social.PartnerOf(i) >= 0).ToList();
            foreach (int i in coupled)
            {
                int partner = town.Social.PartnerOf(i);
                Assert.That(partner, Is.Not.EqualTo(i));
                Assert.That(town.Social.PartnerOf(partner), Is.EqualTo(i));
            }
            Assert.That(coupled.Count, Is.EqualTo((int)(40 * SocialWeb.CoupleShare / 2f) * 2));
        }

        static (SimWorld World, Interactions Act, Resident Taken) TakenResident(float affection, float attraction)
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            Resident taken = world.Residents.First(r => world.Social.PartnerOf(r.Index) >= 0);
            Relationship rel = world.RelationshipWith(taken);
            rel.Met = true;
            rel.Familiarity = 70f;
            rel.Affection = affection;
            rel.Attraction = attraction;
            return (world, new Interactions(world), taken);
        }

        [Test]
        public void SomeoneSeeingSomebodyTurnsYouDown()
        {
            (SimWorld world, Interactions act, Resident taken) = TakenResident(70f, 60f);
            string partner = world.Residents[world.Social.PartnerOf(taken.Index)].Profile.FirstName;
            InteractionResult answer = act.Invite(taken, "park", out DateScene scene);
            Assert.That(scene, Is.Null);
            Assert.That(answer.Reaction, Is.EqualTo("date.taken"));
            Assert.That(answer.Line, Does.Contain(partner));
        }

        [Test]
        public void TheSmittenLeaveTheirPartnerForYou()
        {
            (SimWorld world, Interactions act, Resident taken) = TakenResident(90f, 80f);
            Resident ex = world.Residents[world.Social.PartnerOf(taken.Index)];
            Relationship exRel = world.RelationshipWith(ex);
            exRel.Met = true;
            exRel.Affection = 50f;

            InteractionResult answer = act.Invite(taken, "park", out DateScene scene);
            Assert.That(answer.Reaction, Is.EqualTo("date.leaves"));
            Assert.That(scene, Is.Not.Null, "The date goes ahead.");
            Assert.That(world.Social.PartnerOf(taken.Index), Is.EqualTo(-1));
            Assert.That(world.Social.PartnerOf(ex.Index), Is.EqualTo(-1));
            Assert.That(exRel.Affection, Is.EqualTo(50f - Interactions.ExResentment), "The ex takes it badly.");

            SimWorld loaded = SimSave.FromJson(SimSave.Capture(world).ToJson()).Restore(Fixtures.Sim(), Fixtures.Characters());
            Assert.That(loaded.Social.PartnerOf(taken.Index), Is.EqualTo(-1), "Breakups are saved.");
        }

        [Test]
        public void FriendsPutInAGoodWordOnce()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            Resident listener = world.Residents.First(r => world.Social.FriendsOf(r.Index).Count > 0);
            Resident friend = world.Residents[world.Social.FriendsOf(listener.Index).First()];
            Relationship fond = world.RelationshipWith(friend);
            fond.Met = true;
            fond.Affection = 60f;

            var act = new Interactions(world);
            act.Greet(listener); // first meeting
            world.Sleep();
            InteractionResult hello = act.Greet(listener);
            Assert.That(hello.Reaction, Is.EqualTo("greet.gossip"));
            Assert.That(hello.Line, Does.Contain(friend.Profile.FirstName));
            Assert.That(hello.Affection, Is.GreaterThan(0f));

            world.Sleep();
            Assert.That(act.Greet(listener).Reaction, Is.Not.EqualTo("greet.gossip"), "A good word only counts once.");
        }
    }
}
