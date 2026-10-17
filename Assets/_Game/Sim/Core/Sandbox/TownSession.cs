using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
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
            // Everyone starts out owning what they're wearing.
            if (world.Player.Dna != null && world.Player.Wardrobe.Count == 0)
                foreach (ItemDna item in world.Player.Dna.Outfits.SelectMany(o => o.Items))
                    if (!world.Player.Wardrobe.Any(owned => Same(owned, item))) world.Player.Wardrobe.Add(Copy(item));
            Act = new Interactions(world);
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

        // ---------------------------------------------------------------- work and clothes

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

        /// <summary>Today's stock, if the player is at the boutique while it's open.</summary>
        public List<ShopItem> Shop() =>
            Location == Boutique.Location && IsOpen(Location) && DateWith == null ? Boutique.Stock(World, World.Now.Day) : new List<ShopItem>();

        public bool Owns(ItemDna item) => World.Player.Wardrobe.Any(owned => Same(owned, item));

        /// <summary>Buys a piece and puts it on.</summary>
        public void Buy(ShopItem item)
        {
            Notice = null;
            PlayerCharacter player = World.Player;
            if (Owns(item.Item)) Notice = $"You already have that {item.Name.ToLowerInvariant()}.";
            else if (player.Money < item.Price) Notice = $"That's {item.Price} coins, and you have {player.Money}.";
            else
            {
                player.Money -= item.Price;
                _today.Spent += item.Price;
                player.Wardrobe.Add(Copy(item.Item));
                Notice = $"You bought the {item.Name.ToLowerInvariant()}.";
                Wear(item.Item, quiet: true);
            }
            Raise();
        }

        /// <summary>The wardrobe pieces the player could change into now (owned, not already on).</summary>
        public List<ItemDna> Changeable()
        {
            OutfitDna outfit = CurrentOutfit();
            return World.Player.Wardrobe.Where(item => outfit == null || !outfit.Items.Any(worn => Same(worn, item))).ToList();
        }

        /// <summary>
        /// Puts on a piece the player owns, in the outfit they're wearing now. Whatever it replaces or covers comes
        /// off (a dress replaces top and bottom); a top or bottom without its partner pulls one from the wardrobe.
        /// Fails, and changes nothing, if it doesn't go with their hair or the rest of the outfit.
        /// </summary>
        public bool Wear(ItemDna item, bool quiet = false)
        {
            if (!quiet) Notice = null;
            OutfitDna outfit = CurrentOutfit();
            PartEntry part = World.Characters.Part(item.ItemId);
            if (outfit == null || part == null || !Owns(item))
            {
                if (!quiet) Notice = "You don't have that.";
                Raise();
                return false;
            }

            List<ItemDna> next = outfit.Items.Where(worn =>
            {
                PartEntry other = World.Characters.Part(worn.ItemId);
                return other != null && other.Slot != part.Slot && !part.BlocksSlots.Contains(other.Slot) && !other.BlocksSlots.Contains(part.Slot);
            }).ToList();
            next.Add(Copy(item));
            FillRequiredSlots(next);

            if (!Wearable(next, out string problem))
            {
                Notice = problem;
                Raise();
                return false;
            }
            outfit.Items = next;
            World.Player.LookVersion++;
            World.Advance(ChangeMinutes);
            AfterTimePasses();
            if (!quiet) Notice = $"You change into the {(part.Name ?? ContentCatalog.Humanize(part.Id)).ToLowerInvariant()}.";
            Raise();
            return true;
        }

        OutfitDna CurrentOutfit()
        {
            CharacterDna dna = World.Player.Dna;
            if (dna == null) return null;
            return dna.Outfits.FirstOrDefault(o => o.Context == World.Player.Outfit) ?? dna.Outfits.FirstOrDefault();
        }

        /// <summary>Without a one-piece, a top and a bottom are needed: take them from the wardrobe if missing.</summary>
        void FillRequiredSlots(List<ItemDna> items)
        {
            string SlotOf(ItemDna i) => World.Characters.Part(i.ItemId)?.Slot;
            if (items.Any(i => SlotOf(i) == Slots.OnePiece)) return;
            foreach (string slot in new[] { Slots.Top, Slots.Bottom })
            {
                if (items.Any(i => SlotOf(i) == slot)) continue;
                ItemDna spare = World.Player.Wardrobe.FirstOrDefault(i => SlotOf(i) == slot);
                if (spare != null) items.Add(Copy(spare));
            }
        }

        bool Wearable(List<ItemDna> items, out string problem)
        {
            problem = null;
            string SlotOf(ItemDna i) => World.Characters.Part(i.ItemId)?.Slot;
            bool dressed = items.Any(i => SlotOf(i) == Slots.OnePiece) || (items.Any(i => SlotOf(i) == Slots.Top) && items.Any(i => SlotOf(i) == Slots.Bottom));
            if (!dressed)
            {
                problem = "You'd need something to wear with that.";
                return false;
            }
            List<PartEntry> hair = World.Player.Dna.Hair.PieceIds.Select(World.Characters.Part).Where(p => p != null).ToList();
            List<PartEntry> parts = items.Select(i => World.Characters.Part(i.ItemId)).ToList();
            foreach (PartEntry part in parts)
            {
                List<PartEntry> others = hair.Concat(parts.Where(p => p != part)).ToList();
                if (CharacterGenerator.Compatible(part, others)) continue;
                string name = (part.Name ?? ContentCatalog.Humanize(part.Id)).ToLowerInvariant();
                problem = part.RequiresTags.Count > 0 ? $"The {name} doesn't work with your hair." : $"The {name} doesn't go with the rest of your outfit.";
                return false;
            }
            return true;
        }

        static bool Same(ItemDna a, ItemDna b) =>
            a.ItemId == b.ItemId && a.PatternId == b.PatternId && a.RegionColors.Count == b.RegionColors.Count
            && a.RegionColors.Zip(b.RegionColors, (x, y) => HsvColor.Distance(x, y) < 1e-3f).All(same => same);

        static ItemDna Copy(ItemDna item) => new ItemDna
        {
            ItemId = item.ItemId,
            RegionColors = item.RegionColors.ToList(),
            PatternId = item.PatternId,
            PatternScale = item.PatternScale,
            PatternColor = item.PatternColor,
        };

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
            bool metAnyone = World.Relationships.Any(r => r.Met);
            if (AtHome && !metAnyone) return "Head out: pick a place under Go to. The numbers show how many people are there.";
            if (Selected == null)
                return Present().Count > 0 && !metAnyone ? "Pick someone under Here to say hi." : null;

            Relationship rel = World.RelationshipWith(Selected);
            if (!rel.Met) return "Say hi. Your outfit makes the first impression.";
            if (rel.KnownLikes.Count == 0) return "Try Talk about…: you'll learn what they love from how they react.";
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

        public void Select(Resident resident)
        {
            Notice = null;
            Selected = resident != null && Present().Contains(resident) ? resident : null;
            Raise();
        }

        public InteractionResult Greet() => Do(r => Act.Greet(r));
        public InteractionResult Talk(string topic) => Do(r => Act.Talk(r, topic));
        public InteractionResult Compliment() => Do(r => Act.Compliment(r));
        public InteractionResult Ask(string question) => Do(r => Act.Ask(r, question));
        /// <summary>Gives a gift, which costs its price in coins.</summary>
        public InteractionResult Gift(string giftId)
        {
            GiftEntry gift = World.Catalog.Gift(giftId);
            if (gift != null && Selected != null && World.Player.Money < gift.Price)
            {
                Notice = $"{Capitalise(gift.Name)} costs {gift.Price} coins, and you have {World.Player.Money}.";
                Raise();
                return null;
            }
            InteractionResult result = Do(r => Act.Gift(r, giftId));
            if (result != null && gift != null)
            {
                World.Player.Money -= gift.Price;
                _today.Spent += gift.Price;
                Raise();
            }
            return result;
        }

        static string Capitalise(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

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
            DateScene scene = null;
            InteractionResult result = Do(r => Act.Invite(r, location, out scene), keepTime: true);
            if (scene != null)
            {
                DateWith = partner;
                Date = scene;
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

        /// <summary>
        /// Plays a round of the date in progress. After the last round the date wraps up: the reply comes with how
        /// the whole date went as its follow-up.
        /// </summary>
        public InteractionResult DateChoose(int option)
        {
            Notice = null;
            if (Date == null || Date.Finished || option < 0 || option >= Date.Options.Count)
            {
                Notice = "There's nothing to choose right now.";
                Raise();
                return null;
            }
            InteractionResult result = Act.DateChoose(Date, option);
            if (Date.Done)
            {
                result = Combine(result, Act.FinishDate(Date));
                _today.Dates++;
            }
            Last = result;
            Speaker = DateWith;
            Raise();
            return result;
        }

        /// <summary>Says goodbye after a date (ending it early counts the rounds so far); the player stays where it was.</summary>
        public void EndDate()
        {
            if (DateWith == null) return;
            if (Date != null && !Date.Finished)
            {
                Last = Act.FinishDate(Date);
                Speaker = DateWith;
                _today.Dates++;
            }
            Notice = $"You say goodbye to {DateWith.Profile.FirstName}.";
            EndDateQuietly();
            AfterTimePasses();
            Raise();
        }

        /// <summary>A round's reply followed by the date's outcome, as one exchange.</summary>
        static InteractionResult Combine(InteractionResult round, InteractionResult outcome) => new InteractionResult
        {
            Kind = round.Kind,
            Reaction = round.Reaction,
            Line = round.Line,
            FollowUp = outcome.Line,
            FollowUpReaction = outcome.Reaction,
            Before = round.Before,
            After = outcome.After,
            Familiarity = round.Familiarity + outcome.Familiarity,
            Affection = round.Affection + outcome.Affection,
            Attraction = round.Attraction + outcome.Attraction,
            Trust = round.Trust + outcome.Trust,
            Learnt = round.Learnt ?? outcome.Learnt,
        };

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
            if (result.FirstMeeting) _today.Met++;
            Last = result;
            Speaker = resident;
            if (!keepTime) AfterTimePasses();
            Raise();
            return result;
        }

        void EndDateQuietly()
        {
            if (DateWith == null) return;
            if (Date != null && !Date.Finished) Act.FinishDate(Date);
            DateWith = null;
            Date = null;
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
            foreach (Invitation missed in Invitations.Expire(World))
                Notice = $"You missed meeting {World.Residents[missed.From].Profile.FirstName} at {LocationName(missed.Location)}.";
            KeepInvitation();
        }

        /// <summary>The player is where a resident asked to meet, when they asked: the date begins.</summary>
        void KeepInvitation()
        {
            Invitation due = Invitations.Due(World, Location);
            if (due == null) return;
            Resident host = World.Residents[due.From];
            if (World.WhereIs(host) != Location) return;
            due.Kept = true;
            InteractionResult hello = Act.MeetUp(host, Location, out DateScene scene);
            DateWith = host;
            Date = scene;
            Selected = host;
            World.Player.Outfit = DateOutfit;
            Last = hello;
            Speaker = host;
            Notice = $"{host.Profile.FirstName} was waiting for you.";
        }

        /// <summary>Messages from residents, newest first.</summary>
        public List<Invitation> Messages() => World.Invitations.AsEnumerable().Reverse().ToList();

        /// <summary>Heads to where an invitation is, if it's still on and the place is open.</summary>
        public void Answer(Invitation invitation)
        {
            Notice = null;
            if (!invitation.Pending)
            {
                Notice = invitation.Kept ? "You already met up." : "That's passed.";
                Raise();
                return;
            }
            int early = (invitation.Hour - World.Now.Hour) * 60 - World.Now.Minute - TravelMinutes;
            if (invitation.Day == World.Now.Day && early > 0) Wait(early); // arrive on the hour, not before
            Travel(invitation.Location);
        }

        void Raise() => Changed?.Invoke();
    }
}
