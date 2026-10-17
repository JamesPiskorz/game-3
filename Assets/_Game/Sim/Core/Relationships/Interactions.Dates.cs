using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Rng;
using Game.Sim.Residents;
using Game.Sim.Social;

namespace Game.Sim.Relationships
{
    /// <summary>Asking out, meeting up, the rounds of a date and how it ends, and what the others hear.</summary>
    public sealed partial class Interactions
    {
        /// <summary>
        /// Friends and up can ask. A yes starts a date at <paramref name="locationId"/> right away, returned in
        /// <paramref name="scene"/> (null on a no) to be played with <see cref="DateChoose"/> and
        /// <see cref="FinishDate"/>.
        /// </summary>
        public InteractionResult Invite(Resident resident, string locationId, out DateScene scene)
        {
            scene = null;
            LocationEntry location = _world.Catalog.Location(locationId) ?? throw new ArgumentException($"Unknown location '{locationId}'.", nameof(locationId));
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.AskOnDate);
            var place = ("location", location.Name);

            if (before.Stage < Stage.Friend || Today - rel.LastRefusalDay < RefusalCooldownDays)
            {
                if (before.Stage < Stage.Friend) Add(ref rel.Affection, -1f);
                return Finish(resident, rel, rng, result, before, "date.notyet", GreetMinutes, place);
            }

            // Someone already seeing a resident turns the player down, unless the player is who they really want.
            int partner = _world.Social.PartnerOf(resident.Index);
            string answer = "date.yes";
            if (partner >= 0 && !rel.Dating)
            {
                var partnerName = ("partner", _world.Residents[partner].Profile.FirstName);
                if (rel.Affection < LeaveForYouAffection || rel.Attraction < LeaveForYouAttraction)
                    return Finish(resident, rel, rng, result, before, "date.taken", GreetMinutes, place, partnerName);

                _world.Social.LeaveForPlayer(resident.Index, Today);
                Relationship ex = _world.Find(partner);
                if (ex != null && ex.Met) ex.Affection = Clamp(ex.Affection - ExResentment);
                result.Learnt = $"{resident.Profile.FirstName} is leaving {partnerName.Item2} for you.";
                answer = "date.leaves";
                scene = new DateScene { With = resident, Place = location };
                scene.Options = DateOptions(scene, rng);
                return Finish(resident, rel, rng, result, before, answer, DateStartMinutes, place, partnerName);
            }

            float willing = rel.Affection * 0.6f + rel.Attraction * 0.4f + rng.NextNormal() * 4f;
            if (!rel.Dating && willing < DateThreshold)
            {
                Add(ref rel.Affection, -3f);
                rel.LastRefusalDay = Today;
                return Finish(resident, rel, rng, result, before, "date.no", GreetMinutes, place);
            }

            scene = new DateScene { With = resident, Place = location };
            scene.Options = DateOptions(scene, rng);
            return Finish(resident, rel, rng, result, before, "date.yes", DateStartMinutes, place);
        }

        /// <summary>A partner whose trust falls below this after hearing about another date leaves.</summary>
        public const float BreakUpTrust = 25f;

        /// <summary>
        /// The player turns up where a resident invited them: no asking needed, the date starts. Returned in
        /// <paramref name="scene"/>, like <see cref="Invite"/>'s yes.
        /// </summary>
        public InteractionResult MeetUp(Resident resident, string locationId, out DateScene scene)
        {
            LocationEntry location = _world.Catalog.Location(locationId) ?? throw new ArgumentException($"Unknown location '{locationId}'.", nameof(locationId));
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.AskOnDate);
            Add(ref rel.Trust, 2f);
            scene = new DateScene { With = resident, Place = location };
            scene.Options = DateOptions(scene, rng);
            return Finish(resident, rel, rng, result, before, "invite.met", 0, ("location", location.Name));
        }

        /// <summary>
        /// Asks someone out and, on a yes, plays the whole date choosing as an attentive player would. The result is
        /// the answer, with how the date went as its follow-up and every meter change of the evening.
        /// </summary>
        public InteractionResult AskOnDate(Resident resident, string locationId)
        {
            Relationship rel = _world.RelationshipWith(resident);
            var before = new Meters(rel);
            InteractionResult answer = Invite(resident, locationId, out DateScene scene);
            if (scene == null) return answer;

            while (!scene.Done) DateChoose(scene, scene.BestOption());
            InteractionResult outcome = FinishDate(scene);
            answer.FollowUp = outcome.Line;
            answer.FollowUpReaction = outcome.Reaction;
            answer.Learnt ??= outcome.Learnt;
            answer.Familiarity = rel.Familiarity - before.Familiarity;
            answer.Affection = rel.Affection - before.Affection;
            answer.Attraction = rel.Attraction - before.Attraction;
            answer.Trust = rel.Trust - before.Trust;
            answer.After = rel.Stage;
            return answer;
        }

        /// <summary>Plays one round of a date: the player picks <paramref name="option"/> from the scene's options.</summary>
        public InteractionResult DateChoose(DateScene scene, int option)
        {
            if (scene.Done || scene.Finished) throw new InvalidOperationException("That date has no rounds left.");
            DateOption choice = scene.Options[option];
            Resident resident = scene.With;
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.DateRound);
            ResidentProfile profile = resident.Profile;

            string key;
            float score;
            var tokens = new List<(string, string)> { ("location", scene.Place.Name) };
            if (choice.Kind == DateOptionKind.Activity)
            {
                float value = scene.ActivitiesDone.Contains(choice.Activity.Id) ? -0.5f : scene.ActivityValue(choice.Activity);
                key = value >= ProfileGenerator.LikeThreshold ? "date.activity.love" : value <= -ProfileGenerator.LikeThreshold ? "date.activity.dislike" : "date.activity.ok";
                score = value >= ProfileGenerator.LikeThreshold ? 1.2f : value <= -ProfileGenerator.LikeThreshold ? -0.8f : 0.4f;
                if (value >= ProfileGenerator.LikeThreshold) Add(ref rel.Attraction, 1.5f);
                scene.ActivitiesDone.Add(choice.Activity.Id);
            }
            else if (choice.Kind == DateOptionKind.Compliment)
            {
                key = scene.Complimented ? "date.compliment.again" : "date.compliment";
                score = scene.Complimented ? -0.2f : 0.6f;
                if (!scene.Complimented) Add(ref rel.Attraction, 2f);
                scene.Complimented = true;
            }
            else
            {
                TopicEntry topic = _world.Catalog.Topic(choice.Topic);
                tokens.Add(("topic", topic.Name));
                float interest = profile.Interest(choice.Topic);
                if (scene.Talked.Contains(choice.Topic))
                {
                    key = "date.repeat";
                    score = -0.5f;
                }
                else if (interest >= ProfileGenerator.LikeThreshold)
                {
                    key = "date.talk.love";
                    score = 1f;
                    if (rel.KnownLikes.Add(choice.Topic)) result.Learnt = $"{profile.FirstName} loves {topic.Name}.";
                }
                else if (interest <= -ProfileGenerator.LikeThreshold)
                {
                    key = "date.talk.dislike";
                    score = -1f;
                    if (rel.KnownDislikes.Add(choice.Topic)) result.Learnt = $"{profile.FirstName} isn't into {topic.Name}.";
                }
                else
                {
                    key = "date.talk.neutral";
                    score = 0.3f;
                }
                scene.Talked.Add(choice.Topic);
            }

            scene.Score += score;
            scene.Round++;
            Add(ref rel.Familiarity, 1.5f);
            Add(ref rel.Affection, 2f * score);
            if (!scene.Done) scene.Options = DateOptions(scene, rng);
            return Finish(resident, rel, rng, result, before, key, DateRoundMinutes, tokens.ToArray());
        }

        /// <summary>
        /// Ends a date, early or after its last round, and works out how it went: the rounds' average plus how
        /// much the place suits them. A great date is remembered; three dates, two of them great, with enough
        /// affection and trust, make a partner.
        /// </summary>
        public InteractionResult FinishDate(DateScene scene)
        {
            if (scene.Finished) throw new InvalidOperationException("That date is already over.");
            Resident resident = scene.With;
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.DateEnd);
            LocationEntry location = scene.Place;

            float suits = location.Tags.Sum(t => resident.Profile.Interest(t)) + (location.Tags.Contains("romantic") ? 0.3f : 0f);
            float value = suits + scene.Score / DateScene.Rounds + rng.NextNormal() * 0.15f;
            string key;
            if (value >= GreatDateAt)
            {
                key = "date.great";
                Add(ref rel.Affection, 8f);
                Add(ref rel.Attraction, 6f);
                Add(ref rel.Trust, 5f);
                Add(ref rel.Familiarity, 6f);
                rel.GreatDates++;
                rel.GreatDate = new Memory { Id = location.Id, Day = Today };
            }
            else if (value >= FineDateAt)
            {
                key = "date.fine";
                Add(ref rel.Affection, 4f);
                Add(ref rel.Attraction, 2f);
                Add(ref rel.Trust, 3f);
                Add(ref rel.Familiarity, 5f);
            }
            else
            {
                key = "date.awkward";
                Add(ref rel.Affection, 1f);
                Add(ref rel.Attraction, -2f);
                Add(ref rel.Trust, 1f);
                Add(ref rel.Familiarity, 3f);
            }
            rel.Dates++;
            rel.Dating = true;
            // The town talks: anyone else the player is seeing will hear about this date.
            foreach (Relationship other in _world.Relationships.Where(r => r != rel && r.Dating))
                other.HeardAboutDatesWith.Add(resident.Index);
            // A great date that tips them over the line ends with them asking to be together.
            if (!rel.Partner && rel.Dates >= 3 && rel.GreatDates >= 2 && rel.Affection >= 80f && rel.Trust >= 60f)
            {
                rel.Partner = true;
                if (key == "date.great") key = "date.partner";
            }
            scene.Outcome = key;
            return Finish(resident, rel, rng, result, before, key, 0, ("location", location.Name));
        }

        /// <summary>
        /// Three topics not yet raised on this date (usually including one they love, so attention pays) and a
        /// compliment, in a shuffled order.
        /// </summary>
        List<DateOption> DateOptions(DateScene scene, Pcg32 rng)
        {
            ResidentProfile profile = scene.With.Profile;
            List<TopicEntry> fresh = _world.Catalog.Topics.Where(t => !scene.Talked.Contains(t.Id)).ToList();
            var picked = new List<TopicEntry>();
            void Take(IEnumerable<TopicEntry> from)
            {
                List<TopicEntry> left = from.Where(t => !picked.Contains(t)).ToList();
                if (left.Count > 0) picked.Add(rng.Pick(left));
            }

            if (rng.Chance(0.8f)) Take(fresh.Where(t => profile.Interest(t.Id) >= ProfileGenerator.LikeThreshold));
            Take(fresh.Where(t => Math.Abs(profile.Interest(t.Id)) < ProfileGenerator.LikeThreshold));
            while (picked.Count < 3 && fresh.Any(t => !picked.Contains(t))) Take(fresh);

            var options = picked.Select(t => new DateOption { Kind = DateOptionKind.Topic, Topic = t.Id, Label = "Talk about " + t.Name }).ToList();
            options.Add(new DateOption { Kind = DateOptionKind.Compliment, Label = "Pay a compliment" });

            // Something to do together, if the place offers anything new: it takes a topic's slot.
            List<DateActivityEntry> fresh2 = scene.Place.DateActivities.Where(a => !scene.ActivitiesDone.Contains(a.Id)).ToList();
            if (fresh2.Count > 0)
            {
                DateActivityEntry activity = rng.Pick(fresh2);
                options[options.Count - 2] = new DateOption { Kind = DateOptionKind.Activity, Activity = activity, Label = activity.Name };
            }
            for (int i = options.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                (options[i], options[j]) = (options[j], options[i]);
            }
            return options;
        }
    }
}
