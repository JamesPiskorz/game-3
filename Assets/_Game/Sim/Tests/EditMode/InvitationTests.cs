using System.Linq;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class InvitationTests
    {
        /// <summary>A town where a few residents are close to the player and the rest merely friends.</summary>
        static TownSession CloseFriends(out Resident[] close)
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(0, 8);
            world.Player.Location = SimCatalog.Home;
            close = world.Residents.Take(4).ToArray();
            foreach (Resident r in world.Residents.Take(8))
            {
                Relationship rel = world.RelationshipWith(r);
                rel.Met = true;
                rel.Familiarity = 65f;
                rel.Affection = close.Contains(r) ? 70f : 40f;
                rel.Trust = 40f;
                rel.LastSeenDay = 0;
            }
            return new TownSession(world);
        }

        static Invitation FirstInvitation(TownSession session)
        {
            for (int day = 0; day < 20 && session.World.Invitations.Count == 0; day++) session.Sleep();
            Assume.That(session.World.Invitations, Is.Not.Empty, "Nobody invited the player in 20 days.");
            return session.World.Invitations[0];
        }

        [Test]
        public void OnlyClosePeopleInviteAndOnlyWhereTheyllBe()
        {
            TownSession session = CloseFriends(out Resident[] close);
            for (int day = 0; day < 14; day++) session.Sleep();
            SimWorld world = session.World;
            Assert.That(world.Invitations, Is.Not.Empty);
            foreach (Invitation invitation in world.Invitations)
            {
                Resident host = world.Residents[invitation.From];
                Assert.That(close, Does.Contain(host), "Only close friends invite.");
                Assert.That(world.WhereIs(host, SimTime.At(invitation.Day, invitation.Hour)), Is.EqualTo(invitation.Location), "They'll be there.");
                Assert.That(world.Catalog.Location(invitation.Location).IsOpen(invitation.Hour));
                Assert.That(invitation.Message, Does.Not.Contain("{"));
            }
        }

        [Test]
        public void TurningUpStartsADate()
        {
            TownSession session = CloseFriends(out _);
            Invitation invitation = FirstInvitation(session);
            Resident host = session.World.Residents[invitation.From];

            session.Answer(invitation);
            Assert.That(invitation.Kept);
            Assert.That(session.DateWith, Is.SameAs(host));
            Assert.That(session.Last.Reaction, Is.EqualTo("invite.met"));
            Assert.That(session.Date, Is.Not.Null);
        }

        [Test]
        public void StandingSomeoneUpCostsYou()
        {
            TownSession session = CloseFriends(out _);
            Invitation invitation = FirstInvitation(session);
            Relationship rel = session.World.RelationshipWith(session.World.Residents[invitation.From]);
            float affection = rel.Affection;

            session.Sleep();
            Assert.That(invitation.Missed);
            Assert.That(rel.Affection, Is.LessThan(affection));
            Assert.That(session.Notice, Does.Contain("stood"));
        }

        [Test]
        public void InvitationsAreSaved()
        {
            TownSession session = CloseFriends(out _);
            FirstInvitation(session);
            SimWorld loaded = SimSave.FromJson(SimSave.Capture(session.World).ToJson()).Restore(Fixtures.Sim(), Fixtures.Characters());
            Assert.That(loaded.Invitations.Count, Is.EqualTo(session.World.Invitations.Count));
            Assert.That(loaded.Invitations[0].Location, Is.EqualTo(session.World.Invitations[0].Location));
        }
    }
}
