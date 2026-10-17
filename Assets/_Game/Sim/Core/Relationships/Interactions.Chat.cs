using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Rng;
using Game.Sim.Residents;
using Game.Sim.Social;

namespace Game.Sim.Relationships
{
    /// <summary>Chats: the resident opens, the player picks a warm, playful or curious reply, and a good one draws them out.</summary>
    public sealed partial class Interactions
    {
        public const int ChatMinutes = 10;
        public const float WorryFamiliarity = 20f;

        /// <summary>The resident starts a conversation; the player answers with <see cref="ChatReply"/>.</summary>
        public InteractionResult StartChat(Resident resident, out ChatScene chat)
        {
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.Chat);
            ResidentProfile profile = resident.Profile;
            string opener = ChatTaste.Opener(profile, rng);
            if (opener == "worry" && rel.Familiarity < WorryFamiliarity) opener = "day"; // worries are for people they know
            string topic = null;
            if (opener == "interest")
            {
                List<string> loves = profile.Interests.Where(i => i.Value >= ProfileGenerator.LikeThreshold).Select(i => i.Key).ToList();
                topic = loves.Count == 0 ? null : rng.Pick(loves);
                if (topic == null) opener = "day";
            }
            string topicName = topic == null ? null : _world.Catalog.Topic(topic)?.Name;
            chat = new ChatScene { With = resident, Opener = opener, Topic = topic, Replies = ChatTaste.RepliesFor(opener, topicName) };
            if (topic != null && rel.KnownLikes.Add(topic)) result.Learnt = $"{profile.FirstName} loves {topicName}.";
            InteractionResult opened = Finish(resident, rel, rng, result, before, "chat.open." + opener, 5, ("topic", topicName ?? ""));
            chat.Line = opened.Line;
            return opened;
        }

        /// <summary>
        /// The player's answer lands well when it's the kind this person warms to (see <see cref="ChatTaste"/>), badly
        /// when it's the opposite. Seeing one kind land well twice teaches the player what they respond to.
        /// A good first reply draws them out: they say more, and the chat goes on to a second beat. Two good replies in
        /// one chat leave them feeling heard, which counts for more than either alone.
        /// </summary>
        public InteractionResult ChatReply(ChatScene chat, int reply)
        {
            if (chat.Answered) throw new InvalidOperationException("That chat has been answered.");
            Resident resident = chat.With;
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.Chat);
            Personality p = resident.Profile.Personality;
            ChatReply choice = chat.Replies[reply];
            float mine = ChatTaste.Preference(p, choice.Style, chat.Opener);
            float mean = chat.Replies.Average(r => ChatTaste.Preference(p, r.Style, chat.Opener));
            float lift = mine - mean;
            string outcome = lift > 0.08f ? "good" : lift < -0.08f ? "bad" : "ok";
            float fresh = Freshness(rel.TalksToday);
            switch (outcome)
            {
                case "good":
                    Add(ref rel.Affection, 2.5f * fresh);
                    Add(ref rel.Familiarity, 3f * fresh);
                    Add(ref rel.Trust, (chat.Opener == "worry" ? 3f : 1f) * fresh);
                    if (choice.Style == ChatStyle.Playful) Add(ref rel.Attraction, 2f);
                    string style = choice.Style.ToString();
                    if (chat.Landed.Add(choice.Style)) rel.ChatWins[style] = rel.ChatWins.TryGetValue(style, out int wins) ? wins + 1 : 1;
                    if (rel.ChatWins[style] >= 2 && rel.KnownChatStyle == null)
                    {
                        rel.KnownChatStyle = style;
                        result.Learnt = $"{resident.Profile.FirstName} responds well to {StyleWords(choice.Style)}.";
                    }
                    break;
                case "ok":
                    Add(ref rel.Affection, 1.2f * fresh);
                    Add(ref rel.Familiarity, 2f * fresh);
                    break;
                default:
                    Add(ref rel.Affection, -2.5f);
                    Add(ref rel.Familiarity, 1f);
                    break;
            }
            rel.TalksToday++;
            if (outcome == "good") chat.Good++;
            bool more = outcome == "good" && chat.Beat + 1 < ChatScene.Beats;
            bool heard = outcome == "good" && !more && chat.Good == ChatScene.Beats;
            if (heard)
            {
                Add(ref rel.Affection, 1.5f * fresh);
                Add(ref rel.Trust, 2f * fresh);
            }
            string topicName = chat.Topic == null ? "" : _world.Catalog.Topic(chat.Topic)?.Name ?? "";
            InteractionResult answer = Finish(resident, rel, rng, result, before, "chat.react." + outcome, ChatMinutes);
            // What they say next answers how the player replied, where there are lines for it.
            string styled = $"chat.more.{chat.Opener}.{choice.Style.ToString().ToLowerInvariant()}";
            string next = more ? (_world.Catalog.Dialogue.ContainsKey(styled) ? styled : "chat.more." + chat.Opener) : heard ? "chat.close.heard" : null;
            if (next != null)
            {
                answer.FollowUp = Say(resident, rel, rng, next, rel.Stage, Tokens(resident, ("topic", topicName)));
                answer.FollowUpReaction = next;
            }
            if (more)
            {
                chat.Beat++;
                chat.Line = answer.FollowUp;
                chat.Replies = ChatTaste.RepliesFor(chat.Opener, topicName, chat.Beat);
            }
            else
            {
                chat.Answered = true;
            }
            return answer;
        }

        public static string StyleWords(ChatStyle style) => style switch
        {
            ChatStyle.Warm => "warmth",
            ChatStyle.Playful => "playful teasing",
            _ => "curious questions",
        };
    }
}
