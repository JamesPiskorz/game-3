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
