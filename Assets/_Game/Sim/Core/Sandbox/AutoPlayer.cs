using System.Collections.Generic;
using System.Linq;
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
            _session.Answer(invitation);
            if (_session.Date == null) return false;
            Record(_session.Last);
            Note(World.Now.Day, $"Met {World.Residents[invitation.From].Profile.FirstName} at {_session.LocationName(invitation.Location)}, as invited.");
            while (!_session.Date.Finished) Record(_session.DateChoose(_session.Date.BestOption()));
            _session.EndDate();
            return true;
        }

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
                Record(_session.Greet());
            }

            // Time with whoever they like most here.
            Resident favourite = present.Where(r => _session.Present().Contains(r))
                .OrderByDescending(r => World.Find(r.Index)?.Affection ?? 0f).ThenBy(r => r.Index).FirstOrDefault();
            if (favourite == null) return;
            _session.Select(favourite);
            if (_session.Selected == null) return;
            Relationship rel = World.RelationshipWith(favourite);
            if (rel.LastSeenDay != World.Now.Day) Record(_session.Greet());

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

            if (rel.Stage >= Stage.Acquaintance && rel.KnownLikes.Count > 0 && rel.LastGiftDay < World.Now.Day - 1 && _session.Selected != null)
            {
                GiftEntry gift = World.Catalog.Gifts.FirstOrDefault(g => g.Topics.Any(rel.KnownLikes.Contains) && g.Price <= World.Player.Money);
                if (gift != null) Record(_session.Gift(gift.Id));
            }

            if (evening && rel.Stage >= Stage.Friend && _session.Selected != null)
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
            if (!_session.IsOpen(Boutique.Location) || World.Player.Money < 50) return;
            _session.Travel(Boutique.Location);
            // Dress for whoever they're keenest on.
            Relationship keenest = World.Relationships.Where(r => r.Met).OrderByDescending(r => r.Affection + r.Attraction).FirstOrDefault();
            Resident crush = keenest == null ? null : World.Residents[keenest.Resident];
            ShopItem pick = _session.Shop().Where(s => !_session.Owns(s.Item) && s.Price <= World.Player.Money)
                .OrderByDescending(s => crush == null ? 0f : s.Part.Tags.Max(t => crush.Profile.StyleTaste.TryGetValue(t, out float v) ? v : 0f))
                .ThenBy(s => s.Price).FirstOrDefault();
            if (pick == null) return;
            _session.Buy(pick);
            Note(World.Now.Day, $"Bought the {pick.Name.ToLowerInvariant()}" + (crush == null ? "." : $" with {crush.Profile.FirstName}'s taste in mind."));
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
            }
        }

        void Note(int day, string text)
        {
            if (!string.IsNullOrEmpty(text)) _diary.Add($"Day {day + 1}: {text}");
        }
    }
}
