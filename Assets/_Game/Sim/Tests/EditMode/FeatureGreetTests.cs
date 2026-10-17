using System.Linq;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    /// <summary>A stranger taken with the player's freckles (when no piece of clothing stands out) says so.</summary>
    public class FeatureGreetTests
    {
        [Test]
        public void AStrangerTakenWithYourFrecklesSaysSo()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(2, 12);
            var generator = new CharacterGenerator(world.Characters);
            var act = new Interactions(world);
            for (ulong seed = 0; seed < 300; seed++)
            {
                CharacterDna dna = generator.Generate(seed);
                dna.Face.Makeup.RemoveAll(m => m.OverlayId == "face.mark.freckles_01");
                dna.Face.Makeup.Add(new OverlayDna { OverlayId = "face.mark.freckles_01", Opacity = 1f });
                world.Player.Dna = dna;
                Resident taken = world.Residents.FirstOrDefault(r => world.Find(r.Index)?.Met != true
                    && FirstImpression.Favourite(r.Profile, dna, world.Player.Outfit, world.Characters) == null
                    && FirstImpression.FavouriteMark(r.Profile, dna, world.Characters) != null
                    && act.Impression(r) >= Interactions.ImpressedAt
                    && !world.SiblingsOf(r).Any());
                if (taken == null) continue;
                InteractionResult hello = act.Greet(taken);
                Assert.That(hello.Reaction, Is.EqualTo("greet.stranger.impressed.mark"));
                Assert.That(hello.Line, Does.Contain("freckles"));
                return;
            }
            Assert.Inconclusive("No stranger in this town was taken with freckles alone.");
        }
    }
}
