using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Rng;
using Game.Sim.Relationships;

namespace Game.Sim.Sandbox
{
    /// <summary>
    /// A plausible attentive player, for balancing and for checking that different towns tell different stories.
    /// Each day it works when money is short, visits a few places, says hi to new faces, spends time with whoever
    /// it likes most (talking about what they love, chatting, giving fitting gifts, asking them out somewhere they'd enjoy),
    /// shops on Saturdays, and sleeps. It writes a diary of what happened.
    /// </summary>
    public sealed class AutoPlayer
    {
        readonly TownSession _session;
        readonly Pcg32 _rng;
        readonly List<string> _diary = new List<string>();
        readonly List<(string ItemId, int For)> _presents = new List<(string, int)>(); // bought for someone, not yet given

        public AutoPlayer(TownSession session, ulong seed)
        {
            _session = session;
            _rng = new Pcg32(seed, 7);
        }

        public IReadOnlyList<string> Diary => _diary;

        SimWorld World => _session.World;

        public void PlayDays(int days)
        {
            for (int i = 0; i < days; i++) PlayDay();
        }

        public void PlayDay()
        {
            int day = World.Now.Day;
            if (World.Player.Money < 60 && !World.Now.IsWeekend) WorkSomewhere();
            if (World.Now.DayOfWeek == 5) Shop();

            foreach (int hour in new[] { 11, 15, 19 })
            {
                if (World.Now.Hour >= hour + 2) continue;
                if (KeepInvitation(before: hour + 4)) continue;
                if (World.Now.Hour < hour) _session.Wait((hour - World.Now.Hour) * 60 - World.Now.Minute);
                string place = PickPlace();
                if (place == null) continue;
                _session.Travel(place);
                Socialise(evening: hour >= 19);
            }

            _session.Sleep();
            Note(day, _session.Notice);
        }

        /// <summary>Answers today's invitation if it falls before <paramref name="before"/>: goes, and enjoys the date.</summary>
        bool KeepInvitation(int before)
        {
            Invitation invitation = World.Invitations.FirstOrDefault(i => i.Pending && i.Day == World.Now.Day && i.Hour < before
                && World.Now.Hour < i.Hour + Invitations.WindowHours);
            if (invitation == null) return false;
            if (Taken && World.Find(invitation.From)?.Partner != true) return false; // loyal: only their partner's invitations
            _session.Answer(invitation);
            if (_session.Date == null) return false;
            Record(_session.Last);
            Note(World.Now.Day, $"Met {World.Residents[invitation.From].Profile.FirstName} at {_session.LocationName(invitation.Location)}, as invited.");
            while (!_session.Date.Finished) Record(_session.DateChoose(_session.Date.BestOption()));
            _session.EndDate();
            return true;
        }

        /// <summary>Whoever it's keenest on, whom it shops for.</summary>
        Resident Crush()
        {
            Relationship keenest = World.Relationships.Where(r => r.Met).OrderByDescending(r => r.Affection + r.Attraction).ThenBy(r => r.Resident).FirstOrDefault();
            return keenest == null ? null : World.Residents[keenest.Resident];
        }

        /// <summary>Once it has a partner, it stops dating anyone else.</summary>
        bool Taken => World.Relationships.Any(r => r.Partner);

        void WorkSomewhere()
        {
            string place = World.Catalog.Jobs.Select(j => j.Location).Where(l => l != SimCatalog.Home && _session.IsOpen(l)).FirstOrDefault();
            if (place == null) return;
            _session.Travel(place);
            _session.Work();
        }

        /// <summary>Wherever the favourite is (if known), else the busiest open place.</summary>
        string PickPlace()
        {
            Contact favourite = _session.Contacts().FirstOrDefault(c => c.KnownLocation != null && c.KnownLocation != SimCatalog.Home && _session.IsOpen(c.KnownLocation));
            if (favourite != null && _rng.Chance(0.7f)) return favourite.KnownLocation;
            return World.Catalog.Locations.Where(l => _session.IsOpen(l.Id)).OrderByDescending(l => World.At(l.Id).Count()).ThenBy(l => l.Id).Select(l => l.Id).FirstOrDefault();
        }

        void Socialise(bool evening)
        {
            List<Resident> present = _session.Present();
            if (present.Count == 0) return;

            // Hello to a couple of new faces.
            foreach (Resident stranger in present.Where(r => World.Find(r.Index)?.Met != true).Take(2))
            {
                _session.Select(stranger);
                Greet();
            }

            // Time with whoever they like most here.
            Resident favourite = present.Where(r => _session.Present().Contains(r))
                .OrderByDescending(r => World.Find(r.Index)?.Affection ?? 0f).ThenBy(r => r.Index).FirstOrDefault();
            if (favourite == null) return;
            _session.Select(favourite);
            if (_session.Selected == null) return;
            Relationship rel = World.RelationshipWith(favourite);
            if (rel.LastSeenDay != World.Now.Day) Greet();

            for (int i = 0; i < 2 && _session.Selected != null; i++)
            {
                string topic = rel.KnownLikes.Count > i ? rel.KnownLikes.ElementAt(i)
                    : _rng.Pick(World.Catalog.Topics.Where(t => !rel.KnownDislikes.Contains(t.Id) && !rel.TopicDays.ContainsKey(t.Id)).DefaultIfEmpty(World.Catalog.Topics[0]).ToList()).Id;
                Record(_session.Talk(topic));
            }
            if (_session.Selected == null) return;
            if (rel.Stage >= Stage.Acquaintance)
            {
                Record(_session.StartChat());
                while (_session.Chat != null) Record(_session.ChatReply(Reply(_session.Chat, rel)));
            }
            if (_session.Selected == null) return;
            if (rel.LastComplimentDay != World.Now.Day) Record(_session.Compliment());

            // The present bought for them, if it's still spare and still fits their look.
            if (rel.Stage >= Stage.Friend && rel.LastGiftDay < World.Now.Day - 1 && _session.Selected != null)
            {
                ItemDna spare = _session.Giveable().FirstOrDefault(item => _presents.Contains((item.ItemId, favourite.Index))
                    && Residents.ClothesGifts.Wearing(World, favourite, item) != null);
                if (spare != null)
                {
                    _presents.Remove((spare.ItemId, favourite.Index));
                    Record(_session.GiveClothes(spare));
                }
            }

            if (rel.Stage >= Stage.Acquaintance && rel.KnownLikes.Count > 0 && rel.LastGiftDay < World.Now.Day - 1 && _session.Selected != null)
            {
                GiftEntry gift = World.Catalog.Gifts.FirstOrDefault(g => g.Topics.Any(rel.KnownLikes.Contains) && g.Price <= World.Player.Money);
                if (gift != null) Record(_session.Gift(gift.Id));
            }

            if (evening && rel.Stage >= Stage.Friend && _session.Selected != null && (!Taken || rel.Partner))
            {
                LocationEntry place = World.Catalog.Locations.Where(l => _session.IsOpen(l.Id))
                    .OrderByDescending(l => l.Tags.Count(rel.KnownLikes.Contains) + (l.Tags.Contains("romantic") ? 0.5f : 0f)).ThenBy(l => l.Id).FirstOrDefault();
                if (place == null) return;
                InteractionResult answer = _session.AskOnDate(place.Id);
                Record(answer);
                if (_session.Date != null)
                {
                    while (!_session.Date.Finished) Record(_session.DateChoose(_session.Date.BestOption()));
                    _session.EndDate();
                }
            }
        }

        /// <summary>Hello, and a yes to anyone who confesses (unless the player already has a partner).</summary>
        void Greet()
        {
            Record(_session.Greet());
            if (_session.Confession != null) Record(_session.AnswerConfession(!Taken));
        }

        /// <summary>
        /// What an attentive player would say: the way they've seen work, comfort for a worry, otherwise a guess.
        /// It can't see personality, only what the card tells it.
        /// </summary>
        int Reply(ChatScene chat, Relationship rel)
        {
            int known = chat.Replies.FindIndex(r => r.Style.ToString() == rel.KnownChatStyle);
            if (known >= 0) return known;
            if (chat.Opener == "worry") return chat.Replies.FindIndex(r => r.Style == ChatStyle.Warm);
            return _rng.NextInt(chat.Replies.Count);
        }

        void Shop()
        {
            if (World.Player.Money < 50) return;
            // It's the morning: wait for the boutique to open (it used to give up, and never shopped).
            for (int waited = 0; waited < 6 && !_session.IsOpen(Boutique.Location); waited++) _session.Wait(60);
            if (!_session.IsOpen(Boutique.Location)) return;
            _session.Travel(Boutique.Location);
            // Dress for whoever they're keenest on.
            Resident crush = Crush();
            ShopItem pick = _session.Shop().Where(s => !_session.Owns(s.Item) && s.Price <= World.Player.Money)
                .OrderByDescending(s => crush == null ? 0f : s.Part.Tags.Max(t => crush.Profile.StyleTaste.TryGetValue(t, out float v) ? v : 0f))
                .ThenBy(s => s.Price).FirstOrDefault();
            // A friend's very kind of thing they could wear is bought as a present, and kept for when they next meet.
            bool present = pick != null && crush != null && World.RelationshipWith(crush).Stage >= Stage.Friend
                && Interactions.ClothesTaste(crush.Profile, pick.Part, World.Characters) >= Interactions.LovesClothesAt
                && Residents.ClothesGifts.Wearing(World, crush, pick.Item) != null;
            if (pick != null)
            {
                OutfitDna wearing = World.Player.Dna?.Outfits.FirstOrDefault(o => o.Context == World.Player.Outfit);
                ItemDna before = wearing?.Items.FirstOrDefault(i => World.Characters.Part(i.ItemId)?.Slot == pick.Part.Slot);
                _session.Buy(pick);
                if (present && before != null) _session.Wear(before, quiet: true); // keep it spare, to give
                if (present) _presents.Add((pick.Item.ItemId, crush.Index));
                Note(World.Now.Day, $"Bought the {pick.Name.ToLowerInvariant()}" + (crush == null ? "." : present ? $" as a present for {crush.Profile.FirstName}." : $" with {crush.Profile.FirstName}'s taste in mind."));
            }
            Restyle(crush);
        }

        /// <summary>A new cut at the salon next door when the current one isn't their crush's kind of thing.</summary>
        void Restyle(Resident crush)
        {
            if (crush == null || !_session.AtSalon || World.Player.Money < Salon.CutPrice) return;
            float Taste(CharacterDna dna) => FirstImpression.HairTaste(crush.Profile, dna, World.Characters);
            if (Taste(World.Player.Dna) >= Interactions.LovesHairAt) return;
            PartEntry best = Salon.Cuts(World.Characters).OrderByDescending(cut =>
            {
                CharacterDna trial = DnaSerializer.FromJson(DnaSerializer.ToJson(World.Player.Dna, indented: false));
                trial.Hair.PieceIds = new List<string> { cut.Id };
                return Taste(trial);
            }).ThenBy(cut => cut.Id, System.StringComparer.Ordinal).First();
            if (World.Player.Dna.Hair.PieceIds.Contains(best.Id)) return;
            _session.Haircut(best.Id);
            if (World.Player.LastMakeover?.Day == World.Now.Day)
                Note(World.Now.Day, $"Got a new cut ({(best.Name ?? best.Id).ToLowerInvariant()}) with {crush.Profile.FirstName} in mind.");
        }

        void Record(InteractionResult result)
        {
            if (result == null || _session.Speaker == null) return;
            Residents.ResidentProfile who = _session.Speaker.Profile;
            string name = who.FirstName;
            string theyAre = who.Pronouns == Residents.Pronouns.They ? "they're" : who.Subject + "'s";
            int day = World.Now.Day;
            if (result.FirstMeeting) Note(day, $"Met {_session.Speaker.Name}, {World.Catalog.Job(_session.Speaker.Profile.JobId)?.Name.ToLowerInvariant()} ({_session.Speaker.Profile.Archetype}).");
            if (result.StageChanged && result.After >= Stage.Friend) Note(day, $"{name} is now: {result.After}.");
            string outcome = result.FollowUpReaction ?? result.Reaction;
            switch (outcome)
            {
                case "date.great": Note(day, $"A great date with {name}."); break;
                case "date.partner": Note(day, $"A great date with {name}, who asked to be together. Partners now."); break;
                case "date.fine": Note(day, $"A fine date with {name}."); break;
                case "date.awkward": Note(day, $"An awkward date with {name}."); break;
            }
            switch (result.Reaction)
            {
                case "date.no": Note(day, $"{name} said no to a date."); break;
                case "date.taken": Note(day, $"{name} turned down a date: {theyAre} seeing someone."); break;
                case "date.leaves": Note(day, $"{name} left {who.Possessive} partner to date you."); break;
                case "greet.jealous": Note(day, $"{name} found out about another of your dates, and is hurt."); break;
                case "greet.gossip": Note(day, $"{name} had heard good things about you from a friend."); break;
                case "greet.cheated": Note(day, $"{name} heard about another of your dates."); break;
                case "greet.breakup": Note(day, $"{name} broke up with you."); break;
                case "gift.clothes.love": Note(day, _session.Last?.Learnt ?? $"{name} loved the clothes you gave."); break;
                case "greet.confess":
                case "greet.confess.taken": Note(day, $"{name} confessed {who.Possessive} feelings."); break;
                case "confess.yes": Note(day, $"Said yes to {name}: dating now."); break;
                case "confess.yes.leaves": Note(day, $"Said yes to {name}, who is leaving {who.Possessive} partner."); break;
                case "confess.no": Note(day, $"Let {name} down gently."); break;
                case "greet.stranger.family": Note(day, $"{name}'s family had already told them all about you."); break;
                case "greet.player.makeover.love": Note(day, $"{name} loved the new hair."); break;
                case "greet.player.makeover.meh": Note(day, $"{name} wasn't sure about the new hair."); break;
                case "greet.player.makeup.love": Note(day, $"{name} loved the makeup."); break;
                case "greet.player.makeup.meh": Note(day, $"{name} wasn't sure about the makeup."); break;
                case "compliment.makeover.haircut":
                case "compliment.makeover.colour":
                case "compliment.makeover.clothes": Note(day, $"Noticed {name}'s new look, which meant a lot."); break;
            }
        }

        void Note(int day, string text)
        {
            if (!string.IsNullOrEmpty(text)) _diary.Add($"Day {day + 1}: {text}");
        }
    }
}
