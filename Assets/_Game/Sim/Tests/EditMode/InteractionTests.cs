using System.Linq;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class InteractionTests
    {
        static (SimWorld World, Resident Resident, Interactions Act) Setup()
        {
            (SimWorld world, Resident resident) = Fixtures.OneResident();
            return (world, resident, new Interactions(world));
        }

        [Test]
        public void FirstContactIsAMeeting()
        {
            (SimWorld world, Resident resident, Interactions act) = Setup();
            InteractionResult hello = act.Greet(resident);
            Assert.That(hello.FirstMeeting);
            Assert.That(hello.Reaction, Is.EqualTo("greet.stranger"));
            Assert.That(hello.Line, Does.Contain(resident.Profile.FirstName));
            Assert.That(world.RelationshipWith(resident).Met);

            InteractionResult again = act.Greet(resident);
            Assert.That(again.FirstMeeting, Is.False);
        }

        [Test]
        public void LovedTopicsBeatDislikedOnes()
        {
            (SimWorld _, Resident resident, Interactions act) = Setup();
            act.Greet(resident);
            InteractionResult loved = act.Talk(resident, "music");
            InteractionResult disliked = act.Talk(resident, "sports");
            InteractionResult neutral = act.Talk(resident, "food");

            Assert.That(loved.Reaction, Is.EqualTo("talk.love"));
            Assert.That(disliked.Reaction, Is.EqualTo("talk.dislike"));
            Assert.That(neutral.Reaction, Is.EqualTo("talk.neutral"));
            Assert.That(loved.Affection, Is.GreaterThan(neutral.Affection));
            Assert.That(neutral.Affection, Is.GreaterThan(0f));
            Assert.That(disliked.Affection, Is.LessThan(0f));
            Assert.That(loved.Learnt, Does.Contain("loves music"));
            Assert.That(disliked.Learnt, Is.Not.Null);
            Assert.That(loved.Line, Does.Contain("music").IgnoreCase);
        }

        [Test]
        public void RepeatingATopicTheSameDayBores()
        {
            (SimWorld world, Resident resident, Interactions act) = Setup();
            act.Talk(resident, "music");
            InteractionResult repeat = act.Talk(resident, "music");
            Assert.That(repeat.Reaction, Is.EqualTo("talk.bored"));
            Assert.That(repeat.Affection, Is.LessThan(0f));

            world.Sleep();
            Assert.That(act.Talk(resident, "music").Reaction, Is.EqualTo("talk.love"), "A new day, a fresh conversation.");
        }

        [Test]
        public void InteractionsTakeTime()
        {
            (SimWorld world, Resident resident, Interactions act) = Setup();
            long start = world.Now.Minutes;
            act.Talk(resident, "music");
            Assert.That(world.Now.Minutes - start, Is.EqualTo(Interactions.TalkMinutes));
        }

        [Test]
        public void StrangersFindGiftsAwkward()
        {
            (SimWorld _, Resident resident, Interactions act) = Setup();
            Assert.That(act.Gift(resident, "record").Reaction, Is.EqualTo("gift.awkward"), "A gift from someone you've just met is odd.");
        }

        [Test]
        public void GiftsLandByTaste()
        {
            (SimWorld world, Resident friend, Interactions act) = Setup();
            Relationship rel = world.RelationshipWith(friend);
            rel.Met = true;
            rel.Familiarity = 40f;
            rel.Affection = 35f;

            InteractionResult loved = act.Gift(friend, "record");
            Assert.That(loved.Reaction, Is.EqualTo("gift.love"));
            Assert.That(loved.Affection, Is.GreaterThan(act.Gift(friend, "novel").Affection), "A loved gift beats a second gift the same day.");
            Assert.That(act.Gift(friend, "plant").Reaction, Is.EqualTo("gift.again"), "One gift a day.");

            world.Sleep();
            InteractionResult disliked = act.Gift(friend, "sports_bottle");
            Assert.That(disliked.Reaction, Is.EqualTo("gift.dislike"));
            Assert.That(disliked.Affection, Is.LessThan(0f));
            Assert.That(rel.KnownDislikes, Does.Contain("sports"));
        }

        [Test]
        public void ComplimentsWorkOncePerDay()
        {
            (SimWorld _, Resident resident, Interactions act) = Setup();
            act.Greet(resident);
            InteractionResult first = act.Compliment(resident);
            Assert.That(first.Reaction, Is.EqualTo("compliment.pleased"));
            Assert.That(first.Attraction, Is.GreaterThan(0f));
            Assert.That(act.Compliment(resident).Reaction, Is.EqualTo("compliment.awkward"));
        }

        [Test]
        public void StagesFollowTheMeters()
        {
            var rel = new Relationship();
            Assert.That(rel.Stage, Is.EqualTo(Stage.Stranger));
            rel.Met = true;
            rel.Familiarity = Relationship.AcquaintanceFamiliarity;
            Assert.That(rel.Stage, Is.EqualTo(Stage.Acquaintance));
            rel.Familiarity = Relationship.FriendFamiliarity;
            rel.Affection = Relationship.FriendAffection;
            Assert.That(rel.Stage, Is.EqualTo(Stage.Friend));
            rel.Familiarity = Relationship.CloseFamiliarity;
            rel.Affection = Relationship.CloseAffection;
            Assert.That(rel.Stage, Is.EqualTo(Stage.Friend), "Close takes trust too.");
            rel.Trust = Relationship.CloseTrust;
            Assert.That(rel.Stage, Is.EqualTo(Stage.Close));
            rel.Dating = true;
            Assert.That(rel.Stage, Is.EqualTo(Stage.Dating));
        }

        [Test]
        public void DatesNeedFriendshipFirst()
        {
            (SimWorld _, Resident resident, Interactions act) = Setup();
            act.Greet(resident);
            InteractionResult early = act.AskOnDate(resident, "park");
            Assert.That(early.Reaction, Is.EqualTo("date.notyet"));
            Assert.That(early.After, Is.Not.EqualTo(Stage.Dating));
        }

        [Test]
        public void AFondFriendSaysYesAndAGoodDateHelps()
        {
            (SimWorld world, Resident resident, Interactions act) = Setup(); // loves music: the bar suits them
            Relationship rel = world.RelationshipWith(resident);
            rel.Met = true;
            rel.Familiarity = 60f;
            rel.Affection = 70f;
            rel.Attraction = 60f;
            rel.Trust = 40f;
            world.Now = SimTime.At(0, 19);

            InteractionResult date = act.AskOnDate(resident, "bar");
            Assert.That(date.Reaction, Is.EqualTo("date.yes"));
            Assert.That(date.FollowUpReaction, Is.EqualTo("date.great"));
            Assert.That(date.After, Is.EqualTo(Stage.Dating));
            Assert.That(date.StageChanged);
            Assert.That(date.Line, Does.Contain("The Night Owl"));
            Assert.That(world.Now.Hour, Is.EqualTo(21));
        }

        [Test]
        public void ALukewarmFriendSaysNoThenNotYet()
        {
            (SimWorld world, Resident resident, Interactions act) = Setup();
            Relationship rel = world.RelationshipWith(resident);
            rel.Met = true;
            rel.Familiarity = 35f;
            rel.Affection = 31f;
            rel.Attraction = 10f;

            Assert.That(act.AskOnDate(resident, "park").Reaction, Is.EqualTo("date.no"));
            Assert.That(act.AskOnDate(resident, "park").Reaction, Is.EqualTo("date.notyet"), "Asking again straight away.");
        }

        [Test]
        public void ThreeGoodDatesMakeAPartner()
        {
            (SimWorld world, Resident resident, Interactions act) = Setup();
            Relationship rel = world.RelationshipWith(resident);
            rel.Met = true;
            rel.Familiarity = 70f;
            rel.Affection = 70f;
            rel.Attraction = 70f;
            rel.Trust = 50f;
            for (int i = 0; i < 3; i++)
            {
                world.AdvanceTo(19);
                act.AskOnDate(resident, "bar");
            }
            Assert.That(rel.Dates, Is.EqualTo(3));
            Assert.That(rel.Stage, Is.EqualTo(Stage.Partner));
        }

        [Test]
        public void SameActionsReplayTheSameWay()
        {
            string Play()
            {
                SimWorld world = Fixtures.Town(77, 6);
                var act = new Interactions(world);
                var lines = new System.Text.StringBuilder();
                foreach (Resident resident in world.Residents)
                {
                    lines.AppendLine(act.Greet(resident).Line);
                    foreach (string topic in new[] { "music", "books", "food", "sports" })
                    {
                        InteractionResult r = act.Talk(resident, topic);
                        lines.AppendLine($"{r.Line} {r.Affection:R} {r.Familiarity:R}");
                    }
                    lines.AppendLine(act.Compliment(resident).Line);
                }
                return lines.ToString();
            }
            Assert.That(Play(), Is.EqualTo(Play()));
        }

        /// <summary>
        /// The design goal behind the numbers: paying attention (talking about what they love, giving gifts that
        /// suit them, picking date spots they'd enjoy) should get a player there clearly faster than chatting at random.
        /// </summary>
        [Test]
        public void ListeningPaysOff()
        {
            float DaysToDating(bool attentive)
            {
                SimWorld world = Fixtures.Town(0x51DE, 10);
                var act = new Interactions(world);
                var rng = new Game.Characters.Rng.Pcg32(1, 1);
                var daysTaken = new int[world.Residents.Count];
                for (int day = 0; day < 40; day++)
                    foreach (Resident resident in world.Residents.Where(r => world.Social.PartnerOf(r.Index) < 0))
                    {
                        Relationship rel = world.RelationshipWith(resident);
                        if (rel.Dating) continue;
                        world.Now = SimTime.At(day, 18);
                        act.Greet(resident);
                        for (int t = 0; t < 3; t++)
                        {
                            string topic = attentive && rel.KnownLikes.Count > t ? rel.KnownLikes.ElementAt(t) : rng.Pick(world.Catalog.Topics).Id;
                            act.Talk(resident, topic);
                        }
                        act.Compliment(resident);
                        if (day % 2 == 0)
                        {
                            GiftEntry gift = attentive && rel.KnownLikes.Count > 0
                                ? world.Catalog.Gifts.First(g => g.Topics.Any(rel.KnownLikes.Contains))
                                : rng.Pick(world.Catalog.Gifts);
                            act.Gift(resident, gift.Id);
                        }
                        if (rel.Stage >= Stage.Friend && act.AskOnDate(resident, "park").Reaction == "date.yes") daysTaken[resident.Index] = day + 1;
                    }
                return (float)world.Residents.Where(r => world.Social.PartnerOf(r.Index) < 0).Select(r => daysTaken[r.Index] == 0 ? 40 : daysTaken[r.Index]).Average();
            }

            float attentive = DaysToDating(true), random = DaysToDating(false);
            Assert.That(attentive, Is.LessThan(random * 0.8f), $"Attentive: {attentive:F1} days, random: {random:F1} days.");
            Assert.That(attentive, Is.InRange(3f, 15f), "A first date should take days of attention, not one visit or a whole season.");
        }

        [Test]
        public void DressingToTheirTasteMakesABetterFirstImpression()
        {
            SimWorld town = Fixtures.Town(31, 80);
            var generator = new CharacterGenerator(town.Characters);
            float Average(string playerStyle, string residentStyle)
            {
                float total = 0f;
                int count = 0;
                for (ulong seed = 1; seed <= 12; seed++)
                {
                    CharacterDna player = generator.Generate(seed, new GenerationRequest { Archetype = playerStyle });
                    foreach (Resident r in town.Residents.Where(r => r.Profile.Archetype == residentStyle))
                    {
                        total += FirstImpression.Score(r.Profile, player, "casual", town.Characters);
                        count++;
                    }
                }
                Assume.That(count, Is.GreaterThan(0), $"The town has no {residentStyle} residents.");
                return total / count;
            }

            Assert.That(Average("gothic", "gothic"), Is.GreaterThan(Average("sporty", "gothic")));
            Assert.That(Average("sporty", "sporty"), Is.GreaterThan(Average("gothic", "sporty")));
        }
    }
}
