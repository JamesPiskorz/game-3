using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using Game.Sim.Residents;

namespace Game.Sim.Sandbox
{
    /// <summary>
    /// The playable loop over a <see cref="SimWorld"/>: go somewhere, see who's there, pick someone and interact, let
    /// time pass. Keeps the rules a UI shouldn't have to know: closed places, people leaving when their schedule
    /// moves them on, dates taking you both somewhere, dressed up. A UI calls the verbs and redraws on
    /// <see cref="Changed"/>.
    /// </summary>
    public sealed partial class TownSession
    {
        public const int TravelMinutes = 20;
        public const string DateOutfit = "date";
        public const string DayOutfit = "casual";
        public const string RainOutfit = "rain";
        public const string SportOutfit = "sport";
        public const string WorkOutfit = "work";
        public const int ShiftHours = 4;
        public const int PayPerHour = 15;
        public const int ChangeMinutes = 5;

        public SimWorld World { get; private set; }
        public Interactions Act { get; private set; }
        public Resident Selected { get; private set; }

        /// <summary>Who's on a date with the player; while set, it's just the two of them.</summary>
        public Resident DateWith { get; private set; }

        /// <summary>The date in progress (its rounds and options), while <see cref="DateWith"/> is set.</summary>
        public DateScene Date { get; private set; }

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
            _dialogue = new Dialogue(world.Catalog);
            // A look saved with other content (parts since removed, new sliders) is brought up to date first.
            if (world.Player.Dna != null) world.Player.Dna = DnaRepair.Repair(new CharacterGenerator(world.Characters), world.Player.Dna, out _);
            // Everyone owns what they're wearing (from the start, and whatever a repair dressed them in).
            if (world.Player.Dna != null)
                foreach (ItemDna item in world.Player.Dna.Outfits.SelectMany(o => o.Items))
                    if (!world.Player.Wardrobe.Any(owned => Same(owned, item))) world.Player.Wardrobe.Add(Copy(item));
            Act = new Interactions(world);
            Game.Sim.Residents.Makeovers.CatchUp(world);
            DressForEveryContext();
            Selected = DateWith = Speaker = null;
            Date = null;
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
            if (minutes < 0) minutes = 0; // time only goes forward
            Notice = null;
            EndDateQuietly();
            World.Advance(minutes);
            AfterTimePasses();
            Raise();
        }

        /// <summary>
        /// Ends the day: sums it up, sleeps till morning, and lets relationships the player neglected cool a little.
        /// Fires <see cref="Slept"/> so the game can autosave.
        /// </summary>
        public void Sleep()
        {
            EndDateQuietly();
            string summary = _today.Summary();
            World.Sleep();
            List<Invitation> stoodUp = Invitations.Expire(World);
            List<Resident> missing = Decay.NewDay(World);
            string news = World.Now.DayOfWeek == 0 ? WeekNews() : "";
            List<Invitation> invited = Invitations.NewDay(World, _dialogue);
            _today = new DayLog();
            Selected = null;
            Last = null;
            Notice = (summary.Length > 0 ? summary + " " : "") + "A new day: " + World.Now + "."
                + string.Concat(stoodUp.Select(i => $" You stood {World.Residents[i.From].Profile.FirstName} up."))
                + string.Concat(missing.Select(r => $" {r.Profile.FirstName} misses you.")) + news
                + (invited.Count == 0 ? "" : invited.Count == 1 ? $" A message from {World.Residents[invited[0].From].Profile.FirstName}." : $" {invited.Count} new messages.");
            Slept?.Invoke();
            Raise();
        }

        /// <summary>A new week: the town's couples move on. Only news about people the player has met is worth telling.</summary>
        string WeekNews()
        {
            List<Social.SocialEvent> events = World.Social.NewWeek(World.Now.Day, World.Seed, i => World.Find(i)?.Dating == true);
            var told = new List<string>();
            foreach (Social.SocialEvent e in events)
            {
                if (World.Find(e.A)?.Met != true && World.Find(e.B)?.Met != true) continue;
                string a = World.Residents[e.A].Profile.FirstName, b = World.Residents[e.B].Profile.FirstName;
                told.Add(e.Kind == Social.SocialEventKind.Together ? $"{a} and {b} are a couple now." : $"{a} and {b} have split up.");
            }
            return told.Count == 0 ? "" : " Word around town: " + string.Join(" ", told);
        }

        /// <summary>After every sleep, once the new day has begun.</summary>
        public event Action Slept;

        /// <summary>What happened today, for the end-of-day summary.</summary>
        sealed class DayLog
        {
            public int Met, Dates, Earned, Spent;

            public string Summary()
            {
                var parts = new List<string>();
                if (Met > 0) parts.Add(Met == 1 ? "met someone new" : $"met {Met} new people");
                if (Dates > 0) parts.Add(Dates == 1 ? "went on a date" : $"went on {Dates} dates");
                if (Earned > 0) parts.Add($"earned {Earned} coins");
                if (Spent > 0) parts.Add($"spent {Spent}");
                return parts.Count == 0 ? "" : "Today you " + Join(parts) + ".";
            }

            static string Join(List<string> parts) =>
                parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1];
        }

        DayLog _today = new DayLog();
        Dialogue _dialogue;

        // ---------------------------------------------------------------- work (clothes are in TownSession.Wardrobe.cs)

        /// <summary>Anywhere that employs people can give the player a shift, while it's open.</summary>
        public bool CanWork => DateWith == null && !AtHome && IsOpen(Location) && World.Catalog.Jobs.Any(j => j.Location == Location);

        /// <summary>Works up to <see cref="ShiftHours"/> hours here (less if it closes first), paid by the hour.</summary>
        public void Work()
        {
            Notice = null;
            if (!CanWork)
            {
                Notice = "There's no work for you here right now.";
                Raise();
                return;
            }
            LocationEntry place = World.Catalog.Location(Location);
            int hours = Math.Max(1, Math.Min(ShiftHours, place.Closes - World.Now.Hour));
            World.Advance(hours * 60);
            World.Player.Money += hours * PayPerHour;
            _today.Earned += hours * PayPerHour;
            Selected = null;
            Last = null;
            Notice = $"You worked {hours} hours at {place.Name}: +{hours * PayPerHour} coins.";
            AfterTimePasses();
            Raise();
        }

        // ---------------------------------------------------------------- activities

        /// <summary>Things to do here now (the place must be open, and not mid-date).</summary>
        public List<ActivityEntry> Activities()
        {
            LocationEntry place = World.Catalog.Location(Location);
            return place == null || DateWith != null || !IsOpen(Location) ? new List<ActivityEntry>() : place.Activities.ToList();
        }

        /// <summary>Gain from an activity: full at first, tapering off as the stat climbs.</summary>
        public static float Gain(ActivityEntry activity, float current) => activity.Gain * Math.Max(0.15f, 1f - current / 120f);

        /// <summary>Spends the time (and any coins) on an activity here, raising its stat.</summary>
        public void DoActivity(string id)
        {
            Notice = null;
            ActivityEntry activity = Activities().FirstOrDefault(a => a.Id == id);
            if (activity == null)
            {
                Notice = "You can't do that here right now.";
                Raise();
                return;
            }
            if (World.Player.Money < activity.Cost)
            {
                Notice = $"That costs {activity.Cost} coins, and you have {World.Player.Money}.";
                Raise();
                return;
            }
            float before = World.Player.Stat(activity.Stat);
            float after = Math.Min(100f, before + Gain(activity, before));
            World.Player.Stats[activity.Stat] = after;
            World.Player.Money -= activity.Cost;
            _today.Spent += activity.Cost;
            World.Advance(activity.Hours * 60);
            Selected = null;
            Last = null;
            Notice = $"{activity.Name}: {activity.Stat} {before:0} \u2192 {after:0}.";
            AfterTimePasses();
            Raise();
        }

        /// <summary>Who's saying what out loud here this hour (not the person the player is talking to).</summary>
        public List<(Resident Resident, string Line)> Overheard() =>
            DateWith != null || AtHome ? new List<(Resident, string)>() : Barks.At(World, Location, Present(), Selected);

        // ---------------------------------------------------------------- hints

        /// <summary>
        /// One gentle tip for what to try next, from the situation (or null when the player seems to be finding their
        /// way). Aimed at the first hour: meeting people, learning tastes, a first date, money, messages.
        /// </summary>
        public string Hint()
        {
            if (Date != null && !Date.Finished) return "Pick what to talk about. Topics they love make for a good date.";
            if (Date != null) return "Say goodnight when you're ready.";
            if (World.Invitations.Any(i => i.Pending && i.Day == World.Now.Day)) return "You have a message: someone wants to meet up today.";
            if (Chat != null) return "Pick a reply. Warmth, teasing or curiosity: different people warm to different things.";
            bool metAnyone = World.Relationships.Any(r => r.Met);
            if (AtHome && !metAnyone) return "Head out: pick a place under Go to. The numbers show how many people are there.";
            if (Selected == null)
            {
                if (Present().Count > 0 && !metAnyone) return "Pick someone under Here to say hi.";
                if (AtSalon && World.Player.LastMakeover == null && World.Player.Money >= Salon.CutPrice)
                    return "The salon here can change your hair. People who know you will notice, for better or worse.";
                return null;
            }

            Relationship rel = World.RelationshipWith(Selected);
            if (!rel.Met) return "Say hi. Your outfit makes the first impression.";
            if (Game.Sim.Residents.Makeovers.Noticeable(World, Selected, rel, rel.LastSeenDay == World.Now.Day ? -1 : rel.LastSeenDay) != null)
                return "Something's different about them. A compliment now would mean a lot.";
            if (rel.KnownLikes.Count == 0) return "Try Talk about…: you'll learn what they love from how they react.";
            if (rel.ChatWins.Count == 0 && rel.Stage >= Stage.Acquaintance && rel.TalksToday == 0)
                return "Try Chat: let them bring something up, and see which replies land.";
            if (rel.Stage >= Stage.Friend && rel.Dates == 0) return "You're friends now. Ask them out somewhere they'd enjoy.";
            if (rel.Stage < Stage.Friend && World.Player.Money < 15) return "Short on coins? Work a shift wherever people are employed.";
            return null;
        }

        // ---------------------------------------------------------------- contacts

        public List<Contact> Contacts() => Sandbox.Contacts.For(this);

        /// <summary>Goes to where a contact is, if the player knows and it's somewhere they can go.</summary>
        public void Find(Contact contact)
        {
            Notice = null;
            string place = contact.KnownLocation;
            if (place == null || place == SimCatalog.Home)
            {
                Notice = place == null ? $"You're not sure where {contact.Resident.Profile.FirstName} is." : $"{contact.Resident.Profile.FirstName} is at home.";
                Raise();
                return;
            }
            Travel(place);
            if (Location == place) Select(contact.Resident);
        }

        /// <summary>Shows a message from outside the sim ("Game saved").</summary>
        public void Tell(string message)
        {
            Notice = message;
            Raise();
        }

        static string Capitalise(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        InteractionResult Do(Func<Resident, InteractionResult> verb, bool keepTime = false, bool chatting = false)
        {
            Notice = null;
            if (!chatting) Chat = null; // any other action moves the conversation on
            if (Selected == null)
            {
                Notice = "Pick someone to talk to first.";
                Raise();
                return null;
            }
            Resident resident = Selected;
            InteractionResult result = verb(resident);
            if (result.FirstMeeting) _today.Met++;
            Last = result;
            Speaker = resident;
            if (!keepTime) AfterTimePasses();
            Raise();
            return result;
        }

        /// <summary>Places close and people move on with their day.</summary>
        void AfterTimePasses()
        {
            Game.Sim.Residents.Makeovers.CatchUp(World);
            if (DateWith != null) return;
            if (!IsOpen(Location))
            {
                Notice = $"{LocationName(Location)} is closing, so you head home.";
                World.Player.Location = SimCatalog.Home;
            }
            if (Chat != null && Selected != Chat.With) Chat = null;
            if (Selected != null && !Present().Contains(Selected))
            {
                Chat = null;
                string where = World.WhereIs(Selected);
                Notice = $"{Selected.Profile.FirstName} headed off" + (where == SimCatalog.Home ? " home." : $" to {LocationName(where)}.");
                Selected = null;
            }
            foreach (Invitation missed in Invitations.Expire(World))
                Notice = $"You missed meeting {World.Residents[missed.From].Profile.FirstName} at {LocationName(missed.Location)}.";
            KeepInvitation();
            if (DateWith == null) DressForTheOccasion();
        }

        void Raise() => Changed?.Invoke();
    }
}
