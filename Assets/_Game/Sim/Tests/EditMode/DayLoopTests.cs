using System.Linq;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class DayLoopTests
    {
        static (TownSession Session, Resident Friend) WithAFriend(int money)
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(5, 14);
            Resident friend = world.Residents.First(r => r.Schedule.At(world.Now) != SimCatalog.Home);
            world.Player.Location = world.WhereIs(friend);
            world.Player.Money = money;
            Relationship rel = world.RelationshipWith(friend);
            rel.Met = true;
            rel.Familiarity = 40f;
            rel.Affection = 40f;
            var session = new TownSession(world);
            session.Select(friend);
            return (session, friend);
        }

        [Test]
        public void GiftsCostCoins()
        {
            (TownSession session, Resident _) = WithAFriend(money: 10);
            Assert.That(session.Gift("gadget"), Is.Null, "Can't afford it.");
            Assert.That(session.Notice, Does.Contain("coins"));
            Assert.That(session.World.Player.Money, Is.EqualTo(10));

            session.World.Player.Money = 100;
            Assert.That(session.Gift("gadget"), Is.Not.Null);
            Assert.That(session.World.Player.Money, Is.EqualTo(100 - session.World.Catalog.Gift("gadget").Price));
        }

        [Test]
        public void EveryGiftHasAPrice()
        {
            Assert.That(Fixtures.Sim().Gifts.All(g => g.Price > 0));
        }

        [Test]
        public void SleepingSumsUpTheDayAndFiresSlept()
        {
            (TownSession session, Resident _) = WithAFriend(money: 0);
            Resident stranger = session.Present().First(r => !session.World.RelationshipWith(r).Met);
            session.Select(stranger);
            session.Greet();
            session.Work();
            int slept = 0;
            session.Slept += () => slept++;

            session.Sleep();
            Assert.That(slept, Is.EqualTo(1));
            Assert.That(session.Notice, Does.Contain("met someone new").And.Contain("earned"));

            session.Sleep();
            Assert.That(session.Notice, Does.StartWith("A new day"), "A quiet day has nothing to sum up.");
        }

        [Test]
        public void NeglectedFriendsCoolOffButNotAtOnce()
        {
            (TownSession session, Resident friend) = WithAFriend(money: 0);
            session.Greet(); // seen today
            Relationship rel = session.World.RelationshipWith(friend);
            float start = rel.Affection;

            for (int day = 0; day < Decay.GraceDays; day++) session.Sleep();
            Assert.That(rel.Affection, Is.EqualTo(start), "A few days away is fine.");

            session.Sleep();
            session.Sleep();
            Assert.That(rel.Affection, Is.EqualTo(start - 2 * Decay.AffectionPerDay).Within(1e-4f));
        }

        [Test]
        public void SomeoneYoureDatingMissesYou()
        {
            (TownSession session, Resident friend) = WithAFriend(money: 0);
            session.Greet();
            session.World.RelationshipWith(friend).Dating = true;
            for (int day = 0; day < Decay.GraceDays; day++) session.Sleep();
            session.Sleep();
            Assert.That(session.Notice, Does.Contain(friend.Profile.FirstName + " misses you"));
        }
    }
}
