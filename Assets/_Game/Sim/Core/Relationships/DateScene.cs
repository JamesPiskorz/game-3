using System.Collections.Generic;
using System.Linq;

namespace Game.Sim.Relationships
{
    public enum DateOptionKind
    {
        Topic,
        Compliment,
    }

    /// <summary>One thing the player can do in a round of a date.</summary>
    public sealed class DateOption
    {
        public DateOptionKind Kind;
        public string Topic;
        public string Label;
    }

    /// <summary>
    /// A date in progress: a few rounds where the player picks what to talk about (or pays a compliment), each landing
    /// well or badly with the person they're out with. How it went overall comes from those choices plus how much
    /// the place suits them. Run by <see cref="Interactions.DateChoose"/> and <see cref="Interactions.FinishDate"/>.
    /// </summary>
    public sealed class DateScene
    {
        public const int Rounds = 3;

        public Resident With { get; internal set; }
        public LocationEntry Place { get; internal set; }
        public int Round { get; internal set; }

        /// <summary>Sum of how each round landed: about +1 for a loved topic, -1 for a disliked one.</summary>
        public float Score { get; internal set; }
        public List<DateOption> Options { get; internal set; } = new List<DateOption>();

        /// <summary>The outcome's reaction key once finished ("date.great"...), else null.</summary>
        public string Outcome { get; internal set; }

        public bool Done => Round >= Rounds;
        public bool Finished => Outcome != null;

        /// <summary>Topics already raised on this date.</summary>
        public IReadOnlyCollection<string> TopicsRaised => Talked;

        internal readonly HashSet<string> Talked = new HashSet<string>();
        internal bool Complimented;

        /// <summary>The pick an attentive player would make: something they love, else a first compliment, else the least bad topic.</summary>
        public int BestOption()
        {
            int best = 0;
            float bestValue = float.MinValue;
            for (int i = 0; i < Options.Count; i++)
            {
                DateOption option = Options[i];
                float value = option.Kind == DateOptionKind.Compliment
                    ? (Complimented ? -0.2f : 0.6f)
                    : Talked.Contains(option.Topic) ? -0.5f : With.Profile.Interest(option.Topic);
                if (value > bestValue)
                {
                    bestValue = value;
                    best = i;
                }
            }
            return best;
        }
    }
}
