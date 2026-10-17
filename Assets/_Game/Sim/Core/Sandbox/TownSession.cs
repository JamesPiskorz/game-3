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
            if (Date.Done) result = Combine(result, Act.FinishDate(Date));
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
        }

        void Raise() => Changed?.Invoke();
    }
}
