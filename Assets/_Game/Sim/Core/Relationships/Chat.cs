using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Rng;
using Game.Sim.Residents;

namespace Game.Sim.Relationships
{
    /// <summary>How the player answers in a chat.</summary>
    public enum ChatStyle
    {
        Warm,
        Playful,
        Curious,
    }

    /// <summary>A reply the player can give: its style and the words.</summary>
    public sealed class ChatReply
    {
        public ChatStyle Style;
        public string Text;
    }

    /// <summary>
    /// A chat in progress: what the resident brought up, and the player's possible replies. A reply that lands
    /// draws them out, and they say more (a second beat); anything else ends it.
    /// </summary>
    public sealed class ChatScene
    {
        public const int Beats = 2;

        public Resident With;

        /// <summary>The kind of opener ("interest", "day", "worry", "dream", "looks"), and the resident's line.</summary>
        public string Opener;
        public string Line;
        public string Topic;
        public List<ChatReply> Replies = new List<ChatReply>();

        /// <summary>Which beat the player is answering (0 for the opener), and how many replies have landed well.</summary>
        public int Beat;
        public int Good;

        /// <summary>Styles that have landed in this chat; each counts once towards learning what they respond to.</summary>
        public HashSet<ChatStyle> Landed = new HashSet<ChatStyle>();

        /// <summary>True once the conversation is over.</summary>
        public bool Answered;
    }

    /// <summary>
    /// What makes a reply land. It comes from personality, not from a table of right answers:
    /// - warmth suits the agreeable, and anyone who's worried;
    /// - teasing suits the outgoing and open, but stings the anxious and lands badly on a worry;
    /// - curiosity suits the open, and draws out introverts.
    /// The best style for someone is discoverable, and the card shows it once the player has seen it work twice.
    /// </summary>
    public static class ChatTaste
    {
        public static float Preference(Personality p, ChatStyle style, string opener)
        {
            switch (style)
            {
                case ChatStyle.Warm:
                    return 0.35f + 0.6f * p.Agreeableness + 0.3f * p.Neuroticism + (opener == "worry" ? 0.35f : 0f);
                case ChatStyle.Playful:
                    return 0.15f + 0.7f * p.Extraversion + 0.3f * p.Openness - 0.55f * p.Neuroticism - (opener == "worry" ? 0.6f : 0f) - (opener == "looks" ? 0.2f * p.Neuroticism : 0f);
                default:
                    return 0.2f + 0.6f * p.Openness + 0.4f * (1f - p.Extraversion) + (opener == "dream" ? 0.15f : 0f);
            }
        }

        /// <summary>The style this person warms to most in general (not for any particular opener).</summary>
        public static ChatStyle Favourite(Personality p) =>
            Enum.GetValues(typeof(ChatStyle)).Cast<ChatStyle>().OrderByDescending(s => Preference(p, s, "day")).First();

        /// <summary>The opener a resident picks: worriers sometimes share a worry, dreamers a daydream, everyone talks about what they love.</summary>
        public static string Opener(ResidentProfile profile, Pcg32 rng)
        {
            var options = new List<(string, float)> { ("interest", 1.2f), ("day", 0.8f), ("worry", profile.Personality.Neuroticism * 1.2f), ("dream", profile.Personality.Openness * 1.0f),
                ("looks", 0.25f + 0.35f * profile.Personality.Extraversion) };
            return rng.PickWeighted(options, o => o.Item2).Item1;
        }

        static readonly Dictionary<string, (string Warm, string Playful, string Curious)> Replies = new Dictionary<string, (string, string, string)>
        {
            ["interest"] = ("You light up when you talk about {topic}. I love that.", "Okay, but is {topic} really as cool as you make it sound?", "What got you into {topic}?"),
            ["day"] = ("That sounds like a lot. Want to sit for a bit?", "Did you at least get to boss someone around?", "What happened?"),
            ["worry"] = ("I'm here. Tell me what's on your mind.", "Stressed? You? Never.", "Is it something I can help with?"),
            ["dream"] = ("Wherever it is, it would suit you.", "Somewhere with free snacks, obviously.", "Why there? What draws you to it?"),
            ["looks"] = ("It really suits you.", "It's bold. I respect it. Mostly.", "Where did you find it?"),
        };

        /// <summary>Replies to what they say next, once the first reply has drawn them out.</summary>
        static readonly Dictionary<string, (string Warm, string Playful, string Curious)> MoreReplies = new Dictionary<string, (string, string, string)>
        {
            ["interest"] = ("I could listen to you talk about {topic} all day.", "Okay, you've sold me. When's my first lesson?", "What's the best part of it, for you?"),
            ["day"] = ("I'm glad it was me you talked to.", "Lucky for you, I'm an excellent listener. And very modest.", "Is it like that a lot?"),
            ["worry"] = ("You are. Anyone can see how much you care.", "Want me to go and have words with whoever said that?", "Where's that coming from?"),
            ["dream"] = ("I can picture you there. It fits.", "Only if I get to visit. Often.", "What would you do first, once you got there?"),
            ["looks"] = ("You always look like yourself. That's the best kind of style.", "Next time, I'm picking your outfit. Fair's fair.", "What made you go for that look?"),
        };

        public static List<ChatReply> RepliesFor(string opener, string topic, int beat = 0)
        {
            (string Warm, string Playful, string Curious) lines = beat == 0 ? Replies[opener] : MoreReplies[opener];
            return new List<ChatReply>
            {
                new ChatReply { Style = ChatStyle.Warm, Text = lines.Warm.Replace("{topic}", topic ?? "") },
                new ChatReply { Style = ChatStyle.Playful, Text = lines.Playful.Replace("{topic}", topic ?? "") },
                new ChatReply { Style = ChatStyle.Curious, Text = lines.Curious.Replace("{topic}", topic ?? "") },
            };
        }
    }
}
