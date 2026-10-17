using System.Collections.Generic;
using System.Linq;
using Game.Sim.Residents;
using NUnit.Framework;

namespace Game.Sim.Tests
{
    public class TownTests
    {
        [Test]
        public void ProjectSimCatalogIsValid()
        {
            List<string> errors = Fixtures.Sim().Validate();
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void EveryCharacterArchetypeHasSimTendencies()
        {
            SimCatalog sim = Fixtures.Sim();
            foreach (var archetype in Fixtures.Characters().Archetypes)
                Assert.That(sim.Archetypes.ContainsKey(archetype.Id), $"No personality or interests for archetype '{archetype.Id}'.");
        }

        [Test]
        public void EveryReactionHasDialogue()
        {
            SimCatalog sim = Fixtures.Sim();
            string[] reactions =
            {
                "greet.stranger", "greet.stranger.impressed", "greet.acquaintance", "greet.friend", "greet.close", "greet.dating",
                "greet.remember.date", "greet.remember.gift", "greet.remember.topic", "greet.gossip", "date.taken", "date.leaves", "greet.jealous",
                "invite", "invite.met", "greet.stranger.family", "greet.makeover.haircut", "greet.makeover.colour", "greet.makeover.clothes", "greet.player.makeover.love",
                "greet.player.makeover.nice", "greet.player.makeover.meh", "greet.cheated", "greet.breakup",
                "compliment.makeover.haircut", "compliment.makeover.colour", "compliment.makeover.clothes", "bark.makeover.haircut", "bark.makeover.colour", "bark.makeover.clothes", "chat.open.interest", "chat.open.day", "chat.open.worry", "chat.open.dream",
                "chat.react.good", "chat.react.ok", "chat.react.bad", "chat.more.interest", "chat.more.day", "chat.more.worry",
                "chat.more.dream", "chat.close.heard", "bark.couple", "bark.rain", "bark.rain.out", "bark.sunny", "bark.work", "bark.night", "bark.interest", "ask.likes", "ask.dislikes", "ask.nothing", "ask.plans", "ask.plans.none",
                "ask.friends", "ask.friends.partner", "ask.friends.none", "ask.nosy", "ask.again", "greet.news.together", "greet.news.apart", "greet.news.mine.together", "greet.news.mine.apart",
                "talk.love", "talk.neutral", "talk.dislike", "talk.bored", "compliment.pleased", "compliment.style", "compliment.awkward",
                "gift.love", "gift.ok", "gift.dislike", "gift.awkward", "gift.again",
                "date.yes", "date.no", "date.notyet", "date.great", "date.fine", "date.awkward", "date.partner",
                "date.activity.love", "date.activity.ok", "date.activity.dislike",
                "date.talk.love", "date.talk.neutral", "date.talk.dislike", "date.repeat", "date.compliment", "date.compliment.again",
            };
            foreach (string key in reactions)
                Assert.That(sim.Dialogue.TryGetValue(key, out var lines) && lines.Count > 0, $"No lines for '{key}'.");
        }

        [Test]
        public void SameSeedMakesTheSameTown()
        {
            SimWorld a = Fixtures.Town(42), b = Fixtures.Town(42);
            for (int i = 0; i < a.Residents.Count; i++)
            {
                Assert.That(b.Residents[i].Name, Is.EqualTo(a.Residents[i].Name));
                Assert.That(b.Residents[i].Profile.JobId, Is.EqualTo(a.Residents[i].Profile.JobId));
                for (int day = 0; day < 7; day++)
                    for (int hour = 0; hour < 24; hour++)
                        Assert.That(b.Residents[i].Schedule.At(day, hour), Is.EqualTo(a.Residents[i].Schedule.At(day, hour)));
            }
        }

        [Test]
        public void DifferentSeedsMakeDifferentTowns()
        {
            var a = Fixtures.Town(1).Residents.Select(r => r.Name);
            var b = Fixtures.Town(2).Residents.Select(r => r.Name);
            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void FullNamesAreUnique()
        {
            SimWorld town = Fixtures.Town(7, 60);
            Assert.That(town.Residents.Select(r => r.Name), Is.Unique);
        }

        [Test]
        public void FirstNamesAreUniqueInATypicalTown()
        {
            foreach (ulong seed in new[] { 1UL, 2UL, 0x5EEDUL })
                Assert.That(Fixtures.Town(seed, TownGenerator.DefaultPopulation).Residents.Select(r => r.Profile.FirstName), Is.Unique, $"town {seed}");
        }

        [Test]
        public void ProfilesHaveThreeLovesAndOneOrTwoDislikes()
        {
            foreach (Resident resident in Fixtures.Town(3, 40).Residents)
            {
                ResidentProfile p = resident.Profile;
                Assert.That(p.Interests.Count(i => i.Value >= ProfileGenerator.LikeThreshold), Is.EqualTo(ProfileGenerator.Likes), resident.ToString());
                Assert.That(p.Interests.Count(i => i.Value <= -ProfileGenerator.LikeThreshold), Is.InRange(1, 2), resident.ToString());
                Assert.That(p.Age, Is.InRange(ProfileGenerator.MinAge, ProfileGenerator.MaxAge));
                Assert.That(p.Hangouts, Is.Not.Empty);
                Assert.That(p.JobId, Is.Not.Null);
                Assert.That(p.StyleTaste.Values.Max(), Is.EqualTo(1f).Within(1e-5f));
            }
        }

        [Test]
        public void ArchetypesShapePersonality()
        {
            List<Resident> residents = Fixtures.Town(11, 150).Residents;
            float Mean(string archetype) => residents.Where(r => r.Profile.Archetype == archetype).Average(r => r.Profile.Personality.Extraversion);
            Assert.That(Mean("sporty"), Is.GreaterThan(Mean("bookish") + 0.2f), "Sporty residents should be more outgoing than bookish ones.");
        }

        [Test]
        public void PresentationTiltsNamesWithoutDecidingThem()
        {
            SimCatalog sim = Fixtures.Sim();
            List<Resident> residents = Fixtures.Town(5, 200).Residents;
            List<Resident> feminine = residents.Where(r => r.Dna.Body.Femininity >= 0.65f).ToList();
            float share = feminine.Count(r => sim.Names.Feminine.Contains(r.Profile.FirstName)) / (float)feminine.Count;
            Assert.That(share, Is.InRange(0.7f, 0.97f));
            Assert.That(residents.Any(r => r.Profile.Pronouns == Pronouns.They), "Some residents should use they/them.");
        }

        [Test]
        public void EveryoneIsHomeAtThreeInTheMorning()
        {
            foreach (Resident resident in Fixtures.Town(9, 60).Residents)
                for (int day = 0; day < 7; day++)
                    Assert.That(resident.Schedule.At(day, 3), Is.EqualTo(SimCatalog.Home), $"{resident} on day {day}");
        }

        [Test]
        public void WorkersAreAtWorkDuringTheirShifts()
        {
            SimCatalog sim = Fixtures.Sim();
            foreach (Resident resident in Fixtures.Town(9, 60).Residents)
            {
                JobEntry job = sim.Job(resident.Profile.JobId);
                foreach (int day in job.Days)
                    for (int hour = job.Start; hour < job.End; hour++)
                        Assert.That(resident.Schedule.At(day, hour), Is.EqualTo(job.Location), $"{resident} ({job.Id}) day {day} {hour}:00");
            }
        }

        [Test]
        public void NobodyIsSomewhereClosed()
        {
            SimCatalog sim = Fixtures.Sim();
            foreach (Resident resident in Fixtures.Town(13, 60).Residents)
                for (int day = 0; day < 7; day++)
                    for (int hour = 0; hour < 24; hour++)
                    {
                        string place = resident.Schedule.At(day, hour);
                        if (place == SimCatalog.Home) continue;
                        LocationEntry location = sim.Location(place);
                        bool works = sim.Job(resident.Profile.JobId).Location == place;
                        Assert.That(works || location.IsOpen(hour), $"{resident} at {place} on day {day} at {hour}:00, which is closed.");
                    }
        }

        [Test]
        public void TheTownIsLivelyInTheEvening()
        {
            SimWorld town = Fixtures.Town(21, 40);
            town.Now = SimTime.At(5, 19); // Saturday evening
            int out_ = town.Residents.Count(r => town.WhereIs(r) != SimCatalog.Home);
            Assert.That(out_, Is.GreaterThan(town.Residents.Count / 4), "A Saturday evening should have plenty of people out.");
            Assert.That(town.Catalog.Locations.Count(l => town.At(l.Id).Any()), Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void WeatherIsSeededAndMostlyFine()
        {
            Assert.That(Forecast.For(5, 3), Is.EqualTo(Forecast.For(5, 3)));
            var days = Enumerable.Range(0, 400).Select(d => Forecast.For(0x5EED, d)).ToList();
            float rain = days.Count(w => w == Weather.Rainy) / 400f;
            Assert.That(rain, Is.InRange(Forecast.RainChance - 0.06f, Forecast.RainChance + 0.06f));
            Assert.That(days.Count(w => w == Weather.Sunny), Is.GreaterThan(days.Count(w => w == Weather.Rainy)));
        }

        [Test]
        public void AtTheGymEveryoneIsInWorkoutClothes()
        {
            SimWorld town = Fixtures.Town(0x5EED, 24);
            town.Now = SimTime.At(2, 18);
            Assert.That(town.OutfitAt("gym"), Is.EqualTo(Sandbox.TownSession.SportOutfit));
            foreach (Resident r in town.Residents)
            {
                var sport = r.Dna.Outfits.Single(o => o.Context == Sandbox.TownSession.SportOutfit);
                foreach (var item in sport.Items)
                {
                    var part = town.Characters.Part(item.ItemId);
                    Assert.That(part.Tags.Contains("sporty") || part.Slot == Game.Characters.Content.Slots.Glasses, $"{r}: {part.Id} at the gym");
                }
            }
        }

        [Test]
        public void OnRainyDaysPeopleWearTheirCoats()
        {
            SimWorld town = Fixtures.Town(0x5EED, 24);
            int rainy = Enumerable.Range(0, 60).First(d => Forecast.For(town.Seed, d) == Weather.Rainy);
            int dry = Enumerable.Range(0, 60).First(d => Forecast.For(town.Seed, d) == Weather.Sunny);
            town.Now = SimTime.At(rainy, 11);
            Assert.That(town.OutfitAt("cafe"), Is.EqualTo(Sandbox.TownSession.RainOutfit));
            foreach (Resident r in town.Residents)
            {
                var rain = r.Dna.Outfits.Single(o => o.Context == Sandbox.TownSession.RainOutfit);
                Assert.That(rain.Items.Any(i => town.Characters.Part(i.ItemId).Slot == Game.Characters.Content.Slots.Outer), $"{r} has a coat for the rain");
            }
            town.Now = SimTime.At(dry, 11);
            Assert.That(town.OutfitAt("cafe"), Is.EqualTo(Sandbox.TownSession.DayOutfit));
            town.Now = SimTime.At(rainy, 20);
            Assert.That(town.OutfitAt("bar"), Is.EqualTo(Sandbox.TownSession.DateOutfit), "Going out, they dress up whatever the weather.");
        }

        [Test]
        public void RainKeepsPeopleOutOfThePark()
        {
            SimWorld town = Fixtures.Town(0x5EED, 40);
            int rainy = Enumerable.Range(0, 60).First(d => Forecast.For(town.Seed, d) == Weather.Rainy);
            int planned = 0;
            for (int hour = 8; hour < 22; hour++)
            {
                town.Now = SimTime.At(rainy, hour);
                foreach (Resident r in town.Residents)
                {
                    if (r.Schedule.At(town.Now) == "park") planned++;
                    string where = town.WhereIs(r);
                    bool works = town.Catalog.Job(r.Profile.JobId).Location == where;
                    Assert.That(where == "park" && !works, Is.False, $"{r} is in the park in the rain at {hour}:00.");
                }
            }
            Assert.That(planned, Is.GreaterThan(0), "Someone had park plans that day.");
        }

        [Test]
        public void PeopleDressUpForAnEveningOut()
        {
            SimWorld town = Fixtures.Town(1, 1);
            town.Now = SimTime.At(2, 14);
            Assert.That(town.OutfitAt("bar"), Is.EqualTo("casual"), "Afternoons are casual.");
            town.Now = SimTime.At(2, 20);
            Assert.That(town.OutfitAt("bar"), Is.EqualTo("date"), "The bar in the evening.");
            Assert.That(town.OutfitAt("library"), Is.EqualTo("casual"));
            Assert.That(town.OutfitAt("cafe"), Is.EqualTo("casual"), "A social place on a weeknight.");
            town.Now = SimTime.At(5, 19);
            Assert.That(town.OutfitAt("cafe"), Is.EqualTo("date"), "A social place on Saturday night.");
            Assert.That(town.OutfitAt(SimCatalog.Home), Is.EqualTo("casual"));
        }

        [Test]
        public void ClockKnowsDaysAndAdvancesToHours()
        {
            var time = SimTime.At(6, 23, 30);
            Assert.That(time.DayOfWeek, Is.EqualTo(6));
            Assert.That(time.IsWeekend);
            Assert.That(time.Plus(30).DayOfWeek, Is.EqualTo(0));
            Assert.That(time.Plus(30).Day, Is.EqualTo(7));

            SimWorld world = Fixtures.Town(1, 1);
            world.Now = SimTime.At(2, 10);
            world.AdvanceTo(9);
            Assert.That(world.Now.Day, Is.EqualTo(3));
            Assert.That(world.Now.Hour, Is.EqualTo(9));
        }
    }
}
