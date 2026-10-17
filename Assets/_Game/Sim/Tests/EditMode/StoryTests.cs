using System.Linq;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    /// <summary>
    /// The Phase 5 exit test in miniature (PLAN.md §7): an attentive player's month should go somewhere, a date at
    /// least, and different towns should tell different stories.
    /// </summary>
    public class StoryTests
    {
        static (SimWorld World, AutoPlayer Player) Play(ulong seed, int days)
        {
            SimWorld world = Fixtures.Town(seed, 24);
            world.Player.Dna = new CharacterGenerator(world.Characters).Generate(seed + 1);
            world.Player.Location = SimCatalog.Home;
            var player = new AutoPlayer(new TownSession(world), seed);
            player.PlayDays(days);
            return (world, player);
        }

        [Test]
        public void AMonthOfAttentivePlayGoesSomewhere()
        {
            (SimWorld world, AutoPlayer player) = Play(0x5EED, 28);
            Assert.That(world.Relationships.Count(r => r.Met), Is.GreaterThanOrEqualTo(8), "Meets a good part of town.");
            Assert.That(world.Relationships.Count(r => r.Stage >= Stage.Friend), Is.GreaterThanOrEqualTo(2), "Makes friends.");
            Assert.That(world.Relationships.Any(r => r.Dates > 0), "Goes on dates. Diary:\n" + string.Join("\n", player.Diary));
        }

        [Test]
        public void DifferentTownsTellDifferentStories()
        {
            var stories = new[] { 11UL, 22UL, 33UL }.Select(seed => Play(seed, 21)).ToList();
            var firstDates = stories.Select(s => s.World.Relationships.Where(r => r.Dates > 0).Select(r => s.World.Residents[r.Resident].Name).FirstOrDefault()).ToList();
            var diaries = stories.Select(s => string.Join("\n", s.Player.Diary)).ToList();
            Assert.That(diaries.Distinct().Count(), Is.EqualTo(3));
            Assert.That(firstDates.Distinct().Count(), Is.GreaterThan(1), "Different towns, different first dates: " + string.Join(", ", firstDates));
        }
    }
}
