using System.Linq;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    /// <summary>The newer things get a gentle tip too.</summary>
    public class HintTests
    {
        static TownSession Busy(out SimWorld world)
        {
            world = Fixtures.Town(0x5EED, 24);
            world.Player.Dna = new CharacterGenerator(world.Characters).Generate(99);
            world.Now = SimTime.At(5, 12);
            var session = new TownSession(world);
            SimWorld w = world;
            session.Travel(w.Catalog.Locations.Where(l => session.IsOpen(l.Id)).OrderByDescending(l => w.At(l.Id).Count()).First().Id);
            return session;
        }

        [Test]
        public void TheFirstDatePhotoIsPointedOut()
        {
            TownSession session = Busy(out SimWorld world);
            Assert.That(session.Hint() ?? "", Does.Not.Contain("Photos"));
            Resident someone = world.Residents[0];
            world.Photos.Add(new Photo { Resident = someone.Index, Day = world.Now.Day, Location = "park", Name = someone.Profile.FirstName, Place = "Willow Park",
                PlayerLook = "{}", TheirLook = "{}" });
            Assert.That(session.Hint(), Does.Contain("Photos"));
            session.Sleep();
            Assert.That(session.Hint() ?? "", Does.Not.Contain("Photos"), "Only on the day.");
        }

        [Test]
        public void AChatWaitingForAnAnswerSaysSo()
        {
            TownSession session = Busy(out SimWorld _);
            session.Select(session.Present().First());
            session.StartChat();
            Assert.That(session.Hint(), Does.Contain("Pick a reply"));
        }

        [Test]
        public void OnceYouKnowWhatTheyLoveItSuggestsAChat()
        {
            TownSession session = Busy(out SimWorld world);
            Resident someone = session.Present().First();
            Relationship rel = world.RelationshipWith(someone);
            rel.Met = true;
            rel.Familiarity = 20f;
            rel.KnownLikes.Add(someone.Profile.Interests.First().Key);
            rel.LastSeenDay = world.Now.Day - 1;
            session.Select(someone);
            Assert.That(session.Hint(), Does.Contain("Try Chat"));
        }

        [Test]
        public void TheSalonIntroducesItself()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Player.Dna = new CharacterGenerator(world.Characters).Generate(99);
            world.RelationshipWith(world.Residents[0]).Met = true; // not a brand-new player
            world.Now = SimTime.At(2, 12);
            world.Player.Location = Salon.Location;
            world.Player.Money = 200;
            var session = new TownSession(world);
            Assert.That(session.Hint(), Does.Contain("salon"));
        }
    }
}
