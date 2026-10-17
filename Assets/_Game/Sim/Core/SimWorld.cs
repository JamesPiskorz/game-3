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

        /// <summary>The index of their family's first member: their own index unless they have a sibling in town.</summary>
        public int Family;

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

        /// <summary>Charm, fitness, smarts and style, 0..100, raised by activities around town.</summary>
        public SortedDictionary<string, float> Stats = new SortedDictionary<string, float>(System.StringComparer.Ordinal)
        {
            ["charm"] = 10f, ["fitness"] = 10f, ["smarts"] = 10f, ["style"] = 10f,
        };

        public float Stat(string id) => Stats.TryGetValue(id ?? "", out float value) ? value : 0f;

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

        /// <summary>Residents' invitations to the player, newest last.</summary>
        public List<Sandbox.Invitation> Invitations = new List<Sandbox.Invitation>();

        /// <summary>Friendships and couples among the residents.</summary>
        public SocialWeb Social = new SocialWeb();

        /// <summary>Interactions so far: part of every interaction's random seed, so replays from a save match.</summary>
        public long Actions;

        readonly Dictionary<int, Relationship> _relationships = new Dictionary<int, Relationship>();

        public string WhereIs(Resident resident) => WhereIs(resident, Now);

        /// <summary>
        /// Where a resident is at <paramref name="time"/>: their weekly schedule, except that on a rainy day they skip
        /// outdoor plans (work aside) for an open indoor favourite, or stay home.
        /// </summary>
        public string WhereIs(Resident resident, SimTime time)
        {
            string planned = resident.Schedule.At(time);
            if (Forecast.For(Seed, time.Day) != Weather.Rainy) return planned;
            LocationEntry place = Catalog.Location(planned);
            if (place == null || !place.Tags.Contains("outdoors") || Catalog.Job(resident.Profile.JobId ?? "")?.Location == planned) return planned;
            foreach (string hangout in resident.Profile.Hangouts)
            {
                LocationEntry shelter = Catalog.Location(hangout);
                if (shelter != null && !shelter.Tags.Contains("outdoors") && shelter.IsOpen(time.Hour)) return hangout;
            }
            return SimCatalog.Home;
        }

        public Weather Weather => Forecast.For(Seed, Now.Day);

        /// <summary>
        /// What residents wear out right now: their going-out (date) outfit for evenings somewhere romantic, and for
        /// Friday and Saturday nights anywhere social; everyday clothes otherwise.
        /// </summary>
        public string OutfitAt(string location)
        {
            LocationEntry place = Catalog.Location(location);
            if (place == null || Now.Hour < 18) return Sandbox.TownSession.DayOutfit;
            bool weekendNight = Now.DayOfWeek == 4 || Now.DayOfWeek == 5;
            bool goingOut = place.Tags.Contains("romantic") || (weekendNight && place.Tags.Contains("social"));
            return goingOut ? Sandbox.TownSession.DateOutfit : Sandbox.TownSession.DayOutfit;
        }

        /// <summary>Residents at a location right now, in index order.</summary>
        public IEnumerable<Resident> At(string location) => Residents.Where(r => WhereIs(r) == location);

        /// <summary>The relationship with resident <paramref name="index"/> if the player has one, else null (doesn't create one).</summary>
        public Relationship Find(int index) => _relationships.TryGetValue(index, out Relationship relationship) ? relationship : null;

        /// <summary>Their brothers and sisters in town.</summary>
        public IEnumerable<Resident> SiblingsOf(Resident resident) =>
            Residents.Where(r => r != resident && r.Family == resident.Family);

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
