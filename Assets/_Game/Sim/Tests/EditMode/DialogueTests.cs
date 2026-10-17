using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Rng;
using Game.Sim.Relationships;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class DialogueTests
    {
        static readonly HashSet<string> Tokens = new HashSet<string> { "name", "player", "topic", "gift", "location", "item", "partner", "friend" };

        [Test]
        public void EveryLineUsesOnlyKnownTokens()
        {
            foreach (var pair in Fixtures.Sim().Dialogue)
                foreach (DialogueLine line in pair.Value)
                    foreach (Match token in Regex.Matches(line.Text, @"\{(\w+)\}"))
                        Assert.That(Tokens, Does.Contain(token.Groups[1].Value), $"'{pair.Key}': {line.Text}");
        }

        [Test]
        public void StageGatedLinesWaitForTheirStage()
        {
            var dialogue = new Dialogue(Fixtures.Sim());
            var rng = new Pcg32(3, 3);
            var dating = Enumerable.Range(0, 200).Select(_ => dialogue.Template("greet.dating", Tones.Warm, Stage.Dating, rng)).ToList();
            var partner = Enumerable.Range(0, 200).Select(_ => dialogue.Template("greet.dating", Tones.Warm, Stage.Partner, rng)).ToList();
            Assert.That(dating, Has.None.Contains("partner"));
            Assert.That(partner, Has.Some.Contains("partner"));
        }

        [Test]
        public void NobodyRepeatsTheSameLineTwiceInARow()
        {
            (SimWorld world, Resident resident) = Fixtures.OneResident();
            var act = new Interactions(world);
            string previous = null;
            for (int day = 0; day < 8; day++)
            {
                string line = act.Talk(resident, "food").Line;
                Assert.That(line, Is.Not.EqualTo(previous), $"day {day}");
                previous = line;
                world.Sleep();
            }
        }

        [Test]
        public void TheyBringUpWhatTheyLovedTalkingAbout()
        {
            (SimWorld world, Resident resident) = Fixtures.OneResident();
            var act = new Interactions(world);
            act.Greet(resident);
            act.Talk(resident, "music");
            world.Sleep();

            InteractionResult hello = act.Greet(resident);
            Assert.That(hello.Reaction, Is.EqualTo("greet.remember.topic"));
            Assert.That(hello.Line, Does.Contain("music").IgnoreCase);

            world.Sleep();
            Assert.That(act.Greet(resident).Reaction, Does.Not.StartWith("greet.remember"), "A memory is only brought up once.");
        }

        [Test]
        public void OldMemoriesFade()
        {
            (SimWorld world, Resident resident) = Fixtures.OneResident();
            var act = new Interactions(world);
            act.Talk(resident, "music");
            for (int i = 0; i <= Interactions.MemoryDays; i++) world.Sleep();
            Assert.That(act.Greet(resident).Reaction, Does.Not.StartWith("greet.remember"));
        }

        [Test]
        public void AGreatDateIsTheFirstThingTheyMention()
        {
            (SimWorld world, Resident resident) = Fixtures.OneResident(); // loves music: the bar suits them
            Relationship rel = world.RelationshipWith(resident);
            rel.Met = true;
            rel.Familiarity = 60f;
            rel.Affection = 70f;
            rel.Attraction = 60f;
            var act = new Interactions(world);
            world.Now = SimTime.At(0, 19);
            act.Talk(resident, "books");
            Assert.That(act.AskOnDate(resident, "bar").FollowUpReaction, Is.EqualTo("date.great"));
            world.Sleep();

            InteractionResult hello = act.Greet(resident);
            Assert.That(hello.Reaction, Is.EqualTo("greet.remember.date"));
            Assert.That(hello.Line, Does.Contain("The Night Owl"));
        }

        [Test]
        public void DressingToTheirTasteGetsNoticedOnFirstMeeting()
        {
            SimWorld town = Fixtures.Town(0xD7E55, 40);
            var generator = new CharacterGenerator(town.Characters);
            foreach (Resident resident in town.Residents)
                for (ulong seed = 1; seed <= 20; seed++)
                {
                    CharacterDna look = generator.Generate(seed, new GenerationRequest { Archetype = resident.Profile.Archetype });
                    if (FirstImpression.Favourite(resident.Profile, look, "casual", town.Characters) == null) continue;
                    if (FirstImpression.Score(resident.Profile, look, "casual", town.Characters) < Interactions.ImpressedAt) continue;

                    town.Player.Dna = look;
                    InteractionResult hello = new Interactions(town).Greet(resident);
                    Assert.That(hello.Reaction, Is.EqualTo("greet.stranger.impressed"));
                    Assert.That(hello.Line, Does.Not.Contain("{"));
                    return;
                }
            Assert.Inconclusive("No resident in this town was impressed by an outfit in their own style.");
        }
    }
}
