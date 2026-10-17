using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Sim.Relationships;
using Game.Sim.Social;
using Newtonsoft.Json;

namespace Game.Sim
{
    /// <summary>
    /// A save game. Small on purpose: the town is regenerated from its seed and population, so only what the player
    /// changed is stored, namely the clock, the player and the relationships.
    /// </summary>
    public sealed class SimSave
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        [JsonConverter(typeof(HexSeedConverter))]
        public ulong WorldSeed;
        public int Population;
        public long Minutes;
        public long Actions;
        public string PlayerName;

        /// <summary>The player's look as a DNA share code.</summary>
        public string PlayerDna;
        public string PlayerOutfit;
        public string PlayerLocation;
        public int PlayerMoney;
        public List<ItemDna> PlayerWardrobe = new List<ItemDna>();
        public List<Relationship> Relationships = new List<Relationship>();

        public List<Sandbox.Invitation> Invitations = new List<Sandbox.Invitation>();

        /// <summary>How the town's couples changed during play, replayed over the seeded web on load.</summary>
        public List<SocialEvent> SocialHistory = new List<SocialEvent>();

        public static SimSave Capture(SimWorld world) => new SimSave
        {
            WorldSeed = world.Seed,
            Population = world.Residents.Count,
            Minutes = world.Now.Minutes,
            Actions = world.Actions,
            PlayerName = world.Player.Name,
            PlayerDna = world.Player.Dna == null ? null : DnaSerializer.ToShareCode(world.Player.Dna),
            PlayerOutfit = world.Player.Outfit,
            PlayerLocation = world.Player.Location,
            PlayerMoney = world.Player.Money,
            PlayerWardrobe = world.Player.Wardrobe.ToList(),
            Relationships = world.Relationships.ToList(),
            SocialHistory = world.Social.Events.ToList(),
            Invitations = world.Invitations.ToList(),
        };

        /// <summary>Rebuilds the town from the seed, then puts the saved state back.</summary>
        public SimWorld Restore(SimCatalog sim, ContentCatalog characters)
        {
            if (Version > CurrentVersion) throw new FormatException($"Save version {Version} is newer than this game ({CurrentVersion}).");

            SimWorld world = new TownGenerator(sim, characters).Create(WorldSeed, Population);
            world.Now = new SimTime(Minutes);
            world.Actions = Actions;
            world.Player = new PlayerCharacter
            {
                Name = PlayerName,
                Dna = PlayerDna == null ? null : DnaSerializer.FromShareCode(PlayerDna),
                Outfit = PlayerOutfit,
                Location = PlayerLocation,
                Money = PlayerMoney,
                Wardrobe = PlayerWardrobe ?? new List<ItemDna>(),
            };
            world.Social = SocialWeb.Build(world.Residents, WorldSeed, SocialHistory);
            world.Invitations = Invitations ?? new List<Sandbox.Invitation>();
            foreach (Relationship relationship in Relationships)
            {
                if (relationship.Resident < 0 || relationship.Resident >= world.Residents.Count)
                    throw new FormatException($"Save has a relationship with resident {relationship.Resident}, but the town has {world.Residents.Count}.");
                world.Restore(relationship);
            }
            return world;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented, SimCatalog.Settings).Replace("\r\n", "\n");

        public static SimSave FromJson(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<SimSave>(json, SimCatalog.Settings) ?? throw new FormatException("Save is empty.");
            }
            catch (JsonException e)
            {
                throw new FormatException("Save is not valid: " + e.Message, e);
            }
        }

        /// <summary>Seeds as hex strings: 64-bit integers don't survive JSON readers that use doubles.</summary>
        sealed class HexSeedConverter : JsonConverter<ulong>
        {
            public override void WriteJson(JsonWriter writer, ulong value, JsonSerializer serializer) => writer.WriteValue(value.ToString("x16"));

            public override ulong ReadJson(JsonReader reader, Type objectType, ulong existingValue, bool hasExistingValue, JsonSerializer serializer) =>
                Convert.ToUInt64((string)reader.Value, 16);
        }
    }
}
