using System.Linq;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    /// <summary>Great dates leave a photo of the two of them, as they looked that night.</summary>
    public class PhotoTests
    {
        [Test]
        public void ThePhotoBoothTakesOneWithSomeoneCloseOnceADay()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Player.Dna = new CharacterGenerator(world.Characters).Generate(3);
            world.Player.Money = 100;
            world.Now = SimTime.At(5, 12);
            var session = new TownSession(world);
            session.Travel(Boutique.Location);
            Resident someone = session.Present().FirstOrDefault();
            Assume.That(someone, Is.Not.Null, "Somebody is at Arcade Lane at noon.");
            session.Select(someone);
            Assert.That(session.CanUseBooth, Is.False, "Not with a stranger.");

            Relationship rel = world.RelationshipWith(someone);
            rel.Met = true;
            rel.Familiarity = 65f;
            rel.Affection = 70f;
            rel.Trust = 40f;
            Assert.That(session.CanUseBooth, Is.True);
            session.PhotoBooth();
            Assert.That(world.Photos.Count, Is.EqualTo(1));
            Assert.That(world.Photos[0].Name, Is.EqualTo(someone.Profile.FirstName));
            Assert.That(world.Player.Money, Is.EqualTo(100 - TownSession.BoothPrice));
            if (session.Selected == someone) Assert.That(session.CanUseBooth, Is.False, "Once a day.");
        }

        [Test]
        public void GreatDatesLeavePhotosThatKeepTheLookOfTheNight()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Player.Dna = new CharacterGenerator(world.Characters).Generate(3);
            world.Player.Location = SimCatalog.Home;
            var player = new AutoPlayer(new TownSession(world), 3);
            player.PlayDays(30);

            int great = world.Relationships.Sum(r => r.GreatDates);
            Assume.That(great, Is.GreaterThan(0), "The attentive player had a great date in a month.");
            Assert.That(world.Photos.Count, Is.EqualTo(great), "One photo per great date.");
            foreach (Photo photo in world.Photos)
            {
                Assert.That(DnaValidator.Validate(world.Characters, DnaSerializer.FromJson(photo.TheirLook)), Is.Empty);
                Assert.That(DnaValidator.Validate(world.Characters, DnaSerializer.FromJson(photo.PlayerLook)), Is.Empty);
                Assert.That(world.Catalog.Location(photo.Location), Is.Not.Null);
                Assert.That(photo.Day, Is.LessThanOrEqualTo(world.Now.Day));
            }

            // Looks change later; the photo doesn't. And it survives saving.
            Photo first = world.Photos[0];
            string then = first.TheirLook;
            world.Residents[first.Resident].Dna = new CharacterGenerator(world.Characters).Generate(999);
            Assert.That(world.Photos[0].TheirLook, Is.EqualTo(then));
            SimWorld loaded = SimSave.FromJson(SimSave.Capture(world).ToJson()).Restore(Fixtures.Sim(), Fixtures.Characters());
            Assert.That(loaded.Photos.Select(p => p.TheirLook), Is.EqualTo(world.Photos.Select(p => p.TheirLook)));
        }
    }
}
