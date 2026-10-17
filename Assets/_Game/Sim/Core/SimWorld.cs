using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Sim.Relationships;
using Game.Sim.Residents;
using Game.Sim.Social;

namespace Game.Sim
{
    public sealed class Resident
    {
        public int Index;
        public CharacterDna Dna;
        public ResidentProfile Profile;
        public Schedule Schedule;

        public string Name => Profile.Name;
        public override string ToString() => $"#{Index} {Name}";
    }

    public sealed class PlayerCharacter
    {
        public string Name = "You";
        public CharacterDna Dna;

        /// <summary>Which of their outfits they're wearing (an outfit context ID).</summary>
        public string Outfit = "casual";
        public string Location = "park";
        public int Money = 150;

        /// <summary>Every piece of clothing they own, worn or not.</summary>
        public List<ItemDna> Wardrobe = new List<ItemDna>();

        /// <summary>Goes up whenever what they're wearing changes, so views know to redress them.</summary>
        public int LookVersion;
    }

    /// <summary>
    /// The whole sandbox state: a generated town, the clock, the player, and how every resident feels about them.
    /// Residents are regenerated from <see cref="Seed"/> on load; only the clock, player and relationships are saved.
    /// </summary>
    public sealed class SimWorld
    {
        public ulong Seed;
        public SimCatalog Catalog;
        public ContentCatalog Characters;
        public List<Resident> Residents = new List<Resident>();
        public SimTime Now = SimTime.At(0, 9);
        public PlayerCharacter Player = new PlayerCharacter();

        /// <summary>Friendships and couples among the residents.</summary>
        public SocialWeb Social = new SocialWeb();

        /// <summary>Interactions so far: part of every interaction's random seed, so replays from a save match.</summary>
        public long Actions;

        readonly Dictionary<int, Relationship> _relationships = new Dictionary<int, Relationship>();

        public string WhereIs(Resident resident) => resident.Schedule.At(Now);

        /// <summary>Residents at a location right now, in index order.</summary>
        public IEnumerable<Resident> At(string location) => Residents.Where(r => r.Schedule.At(Now) == location);

        /// <summary>The relationship with resident <paramref name="index"/> if the player has one, else null (doesn't create one).</summary>
        public Relationship Find(int index) => _relationships.TryGetValue(index, out Relationship relationship) ? relationship : null;

        public Relationship RelationshipWith(Resident resident)
        {
            if (!_relationships.TryGetValue(resident.Index, out Relationship relationship))
                _relationships[resident.Index] = relationship = new Relationship { Resident = resident.Index };
            return relationship;
        }

        /// <summary>Relationships the player has started, in resident order.</summary>
        public IEnumerable<Relationship> Relationships => _relationships.Values.OrderBy(r => r.Resident);

        internal void Restore(Relationship relationship) => _relationships[relationship.Resident] = relationship;

        public void Advance(int minutes) => Now = Now.Plus(minutes);

        /// <summary>Goes home and sleeps until <paramref name="wakeHour"/>:00 the next day.</summary>
        public void Sleep(int wakeHour = 8)
        {
            Now = SimTime.At(Now.Day + 1, wakeHour);
            Player.Location = SimCatalog.Home;
        }

        /// <summary>Skips to the next time the clock reads <paramref name="hour"/>:00.</summary>
        public void AdvanceTo(int hour)
        {
            long target = (long)Now.Day * SimTime.MinutesPerDay + hour * 60;
            if (target <= Now.Minutes) target += SimTime.MinutesPerDay;
            Now = new SimTime(target);
        }
    }
}
