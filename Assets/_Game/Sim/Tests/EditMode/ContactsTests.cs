using System.Linq;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class ContactsTests
    {
        static (TownSession Session, Resident Someone) Saturday()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(5, 14);
            Resident someone = world.Residents.First(r => r.Schedule.At(world.Now) != SimCatalog.Home && world.Catalog.Location(r.Schedule.At(world.Now)).IsOpen(14));
            world.Player.Location = SimCatalog.Home;
            return (new TownSession(world), someone);
        }

        [Test]
        public void OnlyPeopleYouveMetAreListed()
        {
            (TownSession session, Resident someone) = Saturday();
            Assert.That(session.Contacts(), Is.Empty);
            session.World.RelationshipWith(someone).Met = true;
            Assert.That(session.Contacts().Select(c => c.Resident), Is.EqualTo(new[] { someone }));
        }

        [Test]
        public void KnowingSomeoneBetterTellsYouWhereTheyAre()
        {
            (TownSession session, Resident someone) = Saturday();
            Relationship rel = session.World.RelationshipWith(someone);
            rel.Met = true;

            rel.Familiarity = 5f;
            Contact contact = session.Contacts().Single();
            Assert.That(contact.KnownLocation, Is.Null);
            Assert.That(contact.Hint, Does.Contain("don't know"));

            rel.Familiarity = Contacts.KnowsHaunt;
            Assert.That(session.Contacts().Single().Hint, Does.StartWith("Often at"));

            rel.Familiarity = Contacts.KnowsRoutine;
            contact = session.Contacts().Single();
            Assert.That(contact.KnownLocation, Is.EqualTo(session.World.WhereIs(someone)));
            Assert.That(contact.Hint, Does.Contain("right now"));
        }

        [Test]
        public void FindTakesYouToThem()
        {
            (TownSession session, Resident someone) = Saturday();
            Relationship rel = session.World.RelationshipWith(someone);
            rel.Met = true;
            rel.Familiarity = Contacts.KnowsRoutine;

            session.Find(session.Contacts().Single());
            Assert.That(session.Location, Is.EqualTo(session.World.WhereIs(someone)));
            Assert.That(session.Selected, Is.SameAs(someone));
        }

        [Test]
        public void YouCantFindSomeoneYouBarelyKnow()
        {
            (TownSession session, Resident someone) = Saturday();
            session.World.RelationshipWith(someone).Met = true;
            session.Find(session.Contacts().Single());
            Assert.That(session.AtHome);
            Assert.That(session.Notice, Does.Contain("not sure"));
        }
    }
}
