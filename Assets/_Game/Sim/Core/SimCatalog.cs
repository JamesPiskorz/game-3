using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Game.Sim
{
    /// <summary>
    /// Sim content: town locations, jobs, conversation topics, gifts, per-archetype personality tendencies, names and
    /// dialogue lines. Pure data from Sim/Content/SimCatalog.json, like the character catalog.
    /// </summary>
    public sealed class SimCatalog
    {
        public List<LocationEntry> Locations = new List<LocationEntry>();
        public List<JobEntry> Jobs = new List<JobEntry>();
        public List<TopicEntry> Topics = new List<TopicEntry>();
        public List<GiftEntry> Gifts = new List<GiftEntry>();
        public Dictionary<string, ArchetypeTendencies> Archetypes = new Dictionary<string, ArchetypeTendencies>();
        public NamePool Names = new NamePool();
        public Dictionary<string, List<DialogueLine>> Dialogue = new Dictionary<string, List<DialogueLine>>();

        public LocationEntry Location(string id) => Locations.FirstOrDefault(l => l.Id == id);
        public JobEntry Job(string id) => Jobs.FirstOrDefault(j => j.Id == id);
        public TopicEntry Topic(string id) => Topics.FirstOrDefault(t => t.Id == id);
        public GiftEntry Gift(string id) => Gifts.FirstOrDefault(g => g.Id == id);

        public static SimCatalog FromJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<SimCatalog>(json, Settings) ?? throw new FormatException("Sim catalog JSON is empty.");
            }
            catch (JsonException e)
            {
                throw new FormatException("Sim catalog is not valid: " + e.Message, e);
            }
        }

        internal static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false } },
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MissingMemberHandling = MissingMemberHandling.Error,
            Culture = CultureInfo.InvariantCulture,
        };

        /// <summary>Content errors: dangling references. Empty when usable.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            foreach (JobEntry job in Jobs)
                if (job.Location != Home && Location(job.Location) == null) errors.Add($"Job '{job.Id}' works at unknown location '{job.Location}'.");
            foreach (GiftEntry gift in Gifts)
                foreach (string topic in gift.Topics)
                    if (Topic(topic) == null) errors.Add($"Gift '{gift.Id}' names unknown topic '{topic}'.");
            foreach (KeyValuePair<string, ArchetypeTendencies> pair in Archetypes)
            {
                foreach (string topic in pair.Value.Interests.Keys)
                    if (Topic(topic) == null) errors.Add($"Archetype '{pair.Key}' names unknown topic '{topic}'.");
                foreach (string hangout in pair.Value.Hangouts)
                    if (Location(hangout) == null) errors.Add($"Archetype '{pair.Key}' hangs out at unknown location '{hangout}'.");
            }
            if (Names.Feminine.Count == 0 || Names.Masculine.Count == 0 || Names.Neutral.Count == 0 || Names.Family.Count == 0)
                errors.Add("Every name list needs names.");
            return errors;
        }

        /// <summary>Residents' homes aren't town locations; schedules use this ID for them.</summary>
        public const string Home = "home";
    }

    public sealed class LocationEntry
    {
        public string Id;
        public string Name;
        public List<string> Tags = new List<string>();
        public int Opens;
        public int Closes = 24;

        public bool IsOpen(int hour) => hour >= Opens && hour < Closes;
    }

    public sealed class JobEntry
    {
        public string Id;
        public string Name;
        public string Location;
        public int Start;
        public int End;
        public List<int> Days = new List<int>();
    }

    public sealed class TopicEntry
    {
        public string Id;
        public string Name;
    }

    public sealed class GiftEntry
    {
        public string Id;
        public string Name;
        public List<string> Topics = new List<string>();
    }

    public sealed class ArchetypeTendencies
    {
        public Personality Traits = new Personality();
        public Dictionary<string, float> Interests = new Dictionary<string, float>();
        public List<string> Hangouts = new List<string>();
    }

    public sealed class NamePool
    {
        public List<string> Feminine = new List<string>();
        public List<string> Masculine = new List<string>();
        public List<string> Neutral = new List<string>();
        public List<string> Family = new List<string>();
    }

    public sealed class DialogueLine
    {
        public string Tone = "any";
        public string Text;
    }
}
