using System.Linq;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    /// <summary>A stranger taken with the player's freckles (more than with any piece of clothing) says so.</summary>
    public class FeatureGreetTests
    {
        [Test]
        public void AStrangerTakenWithYourFrecklesSaysSo()
        {
            // Who is taken with what shifts whenever the catalog grows, so look across a few towns.
            foreach (ulong town in new ulong[] { 0x5EED, 0xBEEF, 0xF00D, 0xC0FFEE })
            {
                SimWorld world = Fixtures.Town(town, 24);
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
                        && FirstImpression.Noticed(r.Profile, dna, world.Player.Outfit, world.Characters)?.Id == "face.mark.freckles_01"
                        && act.Impression(r) >= Interactions.ImpressedAt
                        && !world.SiblingsOf(r).Any());
                    if (taken == null) continue;
                    InteractionResult hello = act.Greet(taken);
                    Assert.That(hello.Reaction, Is.EqualTo("greet.stranger.impressed.mark"));
                    Assert.That(hello.Line, Does.Contain("freckles"));
                    return;
                }
            }
            Assert.Inconclusive("No stranger in these towns was taken with freckles.");
        }

        [Test]
        public void SomeoneTakenWithYourFrecklesComplimentsThemBack()
        {
            foreach (ulong town in new ulong[] { 0x5EED, 0xBEEF, 0xF00D, 0xC0FFEE })
            {
                SimWorld world = Fixtures.Town(town, 24);
                world.Now = SimTime.At(2, 12);
                var generator = new CharacterGenerator(world.Characters);
                var act = new Interactions(world);
                for (ulong seed = 0; seed < 300; seed++)
                {
                    CharacterDna dna = generator.Generate(seed);
                    dna.Face.Makeup.RemoveAll(m => m.OverlayId == "face.mark.freckles_01");
                    dna.Face.Makeup.Add(new OverlayDna { OverlayId = "face.mark.freckles_01", Opacity = 1f });
                    world.Player.Dna = dna;
                    Resident taken = world.Residents.FirstOrDefault(r =>
                        FirstImpression.Noticed(r.Profile, dna, world.Player.Outfit, world.Characters)?.Id == "face.mark.freckles_01"
                        && act.Impression(r) >= Interactions.ImpressedAt
                        && r.Profile.Personality.Neuroticism <= 0.6f && r.Profile.Personality.Agreeableness >= 0.4f);
                    if (taken == null) continue;
                    InteractionResult reply = act.Compliment(taken);
                    Assert.That(reply.Reaction, Is.EqualTo("compliment.style.mark"));
                    Assert.That(reply.Line, Does.Contain("freckles"));
                    return;
                }
            }
            Assert.Inconclusive("Nobody in these towns was taken with freckles.");
        }
    }
}
