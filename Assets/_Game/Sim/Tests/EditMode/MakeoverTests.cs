using System.Linq;
using Game.Characters.Dna;
using Game.Sim.Relationships;
using Game.Sim.Residents;
using Game.Sim.Sandbox;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class MakeoverTests
    {
        static string Json(CharacterDna dna) => DnaSerializer.ToJson(dna, indented: false);

        static string Json(OutfitDna outfit) => Newtonsoft.Json.JsonConvert.SerializeObject(outfit);

        static SimWorld TownOnDay(int day)
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            world.Now = SimTime.At(day, 9);
            Makeovers.CatchUp(world);
            return world;
        }

        [Test]
        public void LooksChangeOverTheWeeksTheSameWayEveryTime()
        {
            SimWorld before = Fixtures.Town(0x5EED, 24);
            SimWorld after = TownOnDay(56);
            var changed = after.Residents.Where(r => r.LastMakeover != null).ToList();
            Assert.That(changed.Count, Is.InRange(5, 20), "Over eight weeks, some people change their look.");
            Assert.That(changed.Select(r => r.LastMakeover.Kind).Distinct().Count(), Is.GreaterThanOrEqualTo(2));
            foreach (Resident r in changed)
                Assert.That(Json(r.Dna), Is.Not.EqualTo(Json(before.Residents[r.Index].Dna)), $"{r} looks different");

            SimWorld again = TownOnDay(56);
            Assert.That(again.Residents.Select(r => Json(r.Dna)), Is.EqualTo(after.Residents.Select(r => Json(r.Dna))));
        }

        [Test]
        public void EachKindChangesOnlyWhatItSays()
        {
            SimWorld before = Fixtures.Town(0x5EED, 24);
            SimWorld town = Fixtures.Town(0x5EED, 24);
            // Week by week, so each change is compared with the look just before it.
            for (int week = 1; week <= 20; week++)
            {
                var looks = town.Residents.Select(r => Json(r.Dna)).ToList();
                town.Now = SimTime.At(week * 7, 9);
                Makeovers.CatchUp(town);
                foreach (Resident r in town.Residents.Where(r => r.LastMakeover?.Day == week * 7))
                {
                    CharacterDna was = DnaSerializer.FromJson(looks[r.Index]);
                    switch (r.LastMakeover.Kind)
                    {
                        case MakeoverKind.Haircut:
                            Assert.That(r.Dna.Hair.PieceIds, Is.Not.EqualTo(was.Hair.PieceIds));
                            Assert.That(r.Dna.Hair.Base.ToString(), Is.EqualTo(was.Hair.Base.ToString()), "Same colour, new cut.");
                            break;
                        case MakeoverKind.Colour:
                            Assert.That(r.Dna.Hair.PieceIds, Is.EqualTo(was.Hair.PieceIds), "Same cut, new colour.");
                            Assert.That(HsvColor.Distance(r.Dna.Hair.Base, was.Hair.Base), Is.GreaterThanOrEqualTo(0.15f));
                            break;
                        default:
                            Assert.That(Json(r.Dna.Outfits.Single(o => o.Context == TownSession.DayOutfit)),
                                Is.Not.EqualTo(Json(was.Outfits.Single(o => o.Context == TownSession.DayOutfit))));
                            Assert.That(Json(r.Dna.Outfits.Single(o => o.Context == TownSession.DateOutfit)),
                                Is.EqualTo(Json(was.Outfits.Single(o => o.Context == TownSession.DateOutfit))), "Only everyday clothes change.");
                            break;
                    }
                    Assert.That(r.Dna.Style, Is.EqualTo(before.Residents[r.Index].Dna.Style), "Still their own style.");
                    Assert.That(Game.Characters.Generation.DnaValidator.Validate(town.Characters, r.Dna), Is.Empty);
                }
            }
        }

        [Test]
        public void ALoadedGameHasTheSameLooks()
        {
            SimWorld world = TownOnDay(30);
            SimWorld loaded = SimSave.FromJson(SimSave.Capture(world).ToJson()).Restore(Fixtures.Sim(), Fixtures.Characters());
            Assert.That(loaded.Residents.Select(r => Json(r.Dna)), Is.EqualTo(world.Residents.Select(r => Json(r.Dna))));
        }

        [Test]
        public void SomeoneYouKnewMentionsTheirNewLookAndAComplimentCounts()
        {
            SimWorld world = Fixtures.Town(0x5EED, 24);
            SimWorld later = TownOnDay(56);
            Resident changed = later.Residents.First(r => r.LastMakeover != null);
            int day = changed.LastMakeover.Day;

            // The player knew them before the change, and sees them the day after it.
            world.Now = SimTime.At(day - 2, 10);
            Relationship rel = world.RelationshipWith(world.Residents[changed.Index]);
            rel.Met = true;
            rel.Familiarity = 30f;
            rel.Affection = 20f;
            rel.LastSeenDay = day - 2;
            world.Now = SimTime.At(day + 1, 10);
            Makeovers.CatchUp(world);
            Resident resident = world.Residents[changed.Index];
            Assert.That(resident.LastMakeover?.Day, Is.EqualTo(day));

            var act = new Interactions(world);
            InteractionResult hello = act.Greet(resident);
            Assert.That(hello.Reaction, Is.EqualTo("greet.makeover." + Makeovers.Key(resident.LastMakeover.Kind)));
            InteractionResult compliment = act.Compliment(resident);
            Assert.That(compliment.Reaction, Does.StartWith("compliment.makeover."));
            Assert.That(compliment.Learnt, Does.Contain("notice"));

            world.Now = SimTime.At(day + 2, 10);
            Assert.That(act.Greet(resident).Reaction, Does.Not.StartWith("greet.makeover"), "Only once.");
            Assert.That(act.Compliment(resident).Reaction, Does.Not.StartWith("compliment.makeover"));
        }

        [Test]
        public void StrangersDontMentionAChangeYouNeverSaw()
        {
            SimWorld world = TownOnDay(56);
            Resident changed = world.Residents.First(r => r.LastMakeover != null);
            world.Now = SimTime.At(changed.LastMakeover.Day + 1, 10);
            InteractionResult hello = new Interactions(world).Greet(changed);
            Assert.That(hello.Reaction, Does.Not.StartWith("greet.makeover"));
        }
    }
}
