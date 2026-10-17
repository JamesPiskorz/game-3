using System.Collections.Generic;
using System.Linq;
using Game.Characters.Rng;
using Game.Sim.Relationships;
using Game.Sim.Residents;

namespace Game.Sim.Sandbox
{
    /// <summary>
    /// Things residents say to no one in particular, which make a place feel inhabited: the weather, their work, a
    /// night out, the person they're with, something they love. A few people at a place speak each hour; who and
    /// what is fixed for that hour (seeded), so the scene doesn't chatter differently every redraw.
    /// </summary>
    public static class Barks
    {
        public const float Chance = 0.35f;
        public const int MaxPerScene = 2;

        public static List<(Resident Resident, string Line)> At(SimWorld world, string location, IEnumerable<Resident> present, Resident except = null)
        {
            var dialogue = new Dialogue(world.Catalog);
            var said = new List<(Resident, string)>();
            foreach (Resident resident in present)
            {
                if (resident == except || said.Count >= MaxPerScene) continue;
                Pcg32 rng = SeedStreams.For(StableHash.SplitMix64(world.Seed ^ resident.Dna.Seed ^ (ulong)(world.Now.Day * 24 + world.Now.Hour)), "bark");
                if (!rng.Chance(Chance)) continue;
                (string key, Dictionary<string, string> tokens) = Topic(world, resident, location, rng);
                if (key == null) continue;
                Relationship rel = world.Find(resident.Index);
                said.Add((resident, dialogue.Line(key, resident.Profile.Tone, rel?.Stage ?? Stage.Stranger, rng, tokens)));
            }
            return said;
        }

        static (string, Dictionary<string, string>) Topic(SimWorld world, Resident resident, string location, Pcg32 rng)
        {
            ResidentProfile profile = resident.Profile;
            LocationEntry place = world.Catalog.Location(location);
            var tokens = new Dictionary<string, string> { ["name"] = profile.FirstName, ["location"] = place?.Name ?? location };
            var options = new List<string>();

            int partner = world.Social.PartnerOf(resident.Index);
            if (partner >= 0 && world.WhereIs(world.Residents[partner]) == location)
            {
                tokens["partner"] = world.Residents[partner].Profile.FirstName;
                options.Add("bark.couple");
            }
            Weather weather = world.Weather;
            bool outdoors = place != null && place.Tags.Contains("outdoors");
            if (weather == Weather.Rainy) options.Add(outdoors ? "bark.rain.out" : "bark.rain");
            else if (weather == Weather.Sunny && outdoors && world.Now.Hour >= 8 && world.Now.Hour < 18) options.Add("bark.sunny");
            if (world.Catalog.Job(profile.JobId ?? "")?.Location == location) options.Add("bark.work");
            if (world.Now.Hour >= 20 && place != null && place.Tags.Contains("social")) options.Add("bark.night");
            string love = profile.Interests.Where(i => i.Value >= ProfileGenerator.LikeThreshold).Select(i => i.Key).OrderBy(k => k, System.StringComparer.Ordinal).FirstOrDefault();
            if (love != null)
            {
                tokens["topic"] = world.Catalog.Topic(love)?.Name ?? love;
                options.Add("bark.interest");
            }
            return options.Count == 0 ? (null, tokens) : (rng.Pick(options), tokens);
        }
    }
}
