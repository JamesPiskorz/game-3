using System;
using System.Collections.Generic;
using System.Linq;
using Game.Sim.Relationships;

namespace Game.Sim.Sandbox
{
    /// <summary>
    /// The playable loop over a <see cref="SimWorld"/>: go somewhere, see who's there, pick someone and interact, let
    /// time pass. Keeps the rules a UI shouldn't have to know: closed places, people leaving when their schedule
    /// moves them on, dates taking you both somewhere, dressed up. A UI calls the verbs and redraws on
    /// <see cref="Changed"/>.
    /// </summary>
    public sealed class TownSession
    {
        public const int TravelMinutes = 20;
        public const string DateOutfit = "date";
        public const string DayOutfit = "casual";

        public SimWorld World { get; private set; }
        public Interactions Act { get; private set; }
        public Resident Selected { get; private set; }

        /// <summary>Who's on a date with the player; while set, it's just the two of them.</summary>
        public Resident DateWith { get; private set; }

        /// <summary>The latest interaction and who it was with, for the dialogue box.</summary>
        public InteractionResult Last { get; private set; }
        public Resident Speaker { get; private set; }

        /// <summary>A one-off message: "The library is closed", "Mei headed off".</summary>
        public string Notice { get; private set; }

        public event Action Changed;

        public TownSession(SimWorld world) => Load(world);

        public string Location => World.Player.Location;
        public bool AtHome => Location == SimCatalog.Home;

        public string LocationName(string id) => id == SimCatalog.Home ? "Home" : World.Catalog.Location(id)?.Name ?? id;

        /// <summary>Who the player can see and talk to here.</summary>
        public List<Resident> Present() => DateWith != null ? new List<Resident> { DateWith } : AtHome ? new List<Resident>() : World.At(Location).ToList();

        public bool IsOpen(string id) => id == SimCatalog.Home || (World.Catalog.Location(id)?.IsOpen(World.Now.Hour) ?? false);

        public void Load(SimWorld world)
        {
            World = world;
            Act = new Interactions(world);
            Selected = DateWith = Speaker = null;
            Last = null;
            Notice = null;
            Raise();
        }

        public void Travel(string location)
        {
            Notice = null;
            if (location == Location && DateWith == null) return;
            if (!IsOpen(location))
            {
                LocationEntry entry = World.Catalog.Location(location);
                Notice = entry == null ? $"There's no '{location}' in town." : $"{entry.Name} is closed. It opens at {entry.Opens}:00.";
                Raise();
                return;
            }
            EndDateQuietly();
            World.Player.Location = location;
            Selected = null;
            Last = null;
            World.Advance(TravelMinutes);
            AfterTimePasses();
            Raise();
        }

        public void Wait(int minutes = 60)
        {
            Notice = null;
            EndDateQuietly();
            World.Advance(minutes);
            AfterTimePasses();
            Raise();
        }

        public void Sleep()
        {
            EndDateQuietly();
            World.Sleep();
            Selected = null;
            Last = null;
            Notice = "A new day: " + World.Now + ".";
            Raise();
        }

        /// <summary>Shows a message from outside the sim ("Game saved").</summary>
        public void Tell(string message)
        {
            Notice = message;
            Raise();
        }

        public void Select(Resident resident)
        {
            Notice = null;
            Selected = resident != null && Present().Contains(resident) ? resident : null;
            Raise();
        }

        public InteractionResult Greet() => Do(r => Act.Greet(r));
        public InteractionResult Talk(string topic) => Do(r => Act.Talk(r, topic));
        public InteractionResult Compliment() => Do(r => Act.Compliment(r));
        public InteractionResult Gift(string gift) => Do(r => Act.Gift(r, gift));

        /// <summary>Asks the selected resident out to <paramref name="location"/>. A yes goes there now, both dressed for a date.</summary>
        public InteractionResult AskOnDate(string location)
        {
            if (Selected != null && !IsOpen(location))
            {
                Notice = $"{LocationName(location)} is closed now.";
                Raise();
                return null;
            }
            Resident partner = Selected;
            InteractionResult result = Do(r => Act.AskOnDate(r, location), keepTime: true);
            if (result?.Reaction == "date.yes")
            {
                DateWith = partner;
                World.Player.Location = location;
                World.Player.Outfit = DateOutfit;
                Selected = partner;
                Raise();
            }
            else if (result != null)
            {
                AfterTimePasses();
                Raise();
            }
            return result;
        }

        /// <summary>Says goodbye after a date; the player stays where the date was.</summary>
        public void EndDate()
        {
            if (DateWith == null) return;
            Notice = $"You say goodbye to {DateWith.Profile.FirstName}.";
            EndDateQuietly();
            AfterTimePasses();
            Raise();
        }

        InteractionResult Do(Func<Resident, InteractionResult> verb, bool keepTime = false)
        {
            Notice = null;
            if (Selected == null)
            {
                Notice = "Pick someone to talk to first.";
                Raise();
                return null;
            }
            Resident resident = Selected;
            InteractionResult result = verb(resident);
            Last = result;
            Speaker = resident;
            if (!keepTime) AfterTimePasses();
            Raise();
            return result;
        }

        void EndDateQuietly()
        {
            if (DateWith == null) return;
            DateWith = null;
            World.Player.Outfit = DayOutfit;
        }

        /// <summary>Places close and people move on with their day.</summary>
        void AfterTimePasses()
        {
            if (DateWith != null) return;
            if (!IsOpen(Location))
            {
                Notice = $"{LocationName(Location)} is closing, so you head home.";
                World.Player.Location = SimCatalog.Home;
            }
            if (Selected != null && !Present().Contains(Selected))
            {
                string where = World.WhereIs(Selected);
                Notice = $"{Selected.Profile.FirstName} headed off" + (where == SimCatalog.Home ? " home." : $" to {LocationName(where)}.");
                Selected = null;
            }
        }

        void Raise() => Changed?.Invoke();
    }
}
