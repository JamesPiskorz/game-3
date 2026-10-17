using System.Linq;
using Game.Characters.Rng;

namespace Game.Sim.Relationships
{
    /// <summary>A resident who has just told the player how they feel, waiting for an answer.</summary>
    public sealed class Confession
    {
        public Resident With;
        public int Day;

        /// <summary>The resident they're seeing, whom they'd leave for the player, or -1 if they're single.</summary>
        public int Partner = -1;
    }

    /// <summary>
    /// Confessions: the town asking the player out, not only the other way round. A close friend with a strong spark
    /// says so at the first hello of a day, and the player answers yes (they're dating) or no (they take it as well as
    /// they can, and the spark cools).
    /// </summary>
    public sealed partial class Interactions
    {
        public const float ConfessAttraction = 75f;
        public const float ConfessAffection = 60f;

        /// <summary>After confessing they wait this long before trying again (if they were left without an answer).</summary>
        public const int ConfessGapDays = 14;

        public const float TurnedDownAttraction = 15f;

        /// <summary>
        /// Whether they'd confess now: close but not dating, smitten, not asked in the last couple of weeks, single (or
        /// keen enough to leave their partner), and the player isn't spoken for (in a town this size, everyone knows).
        /// </summary>
        public bool WouldConfess(Resident resident, Relationship rel)
        {
            if (rel.Dating || rel.Stage != Stage.Close) return false;
            if (rel.Attraction < ConfessAttraction || rel.Affection < ConfessAffection) return false;
            if (Today - rel.ConfessedDay < ConfessGapDays) return false;
            if (_world.Relationships.Any(r => r.Partner)) return false;
            int partner = _world.Social.PartnerOf(resident.Index);
            return partner < 0 || rel.Affection >= LeaveForYouAffection && rel.Attraction >= LeaveForYouAttraction;
        }

        /// <summary>Whether they confessed today and are still waiting for the player's answer.</summary>
        public bool AwaitingAnswer(Resident resident)
        {
            Relationship rel = _world.Find(resident.Index);
            return rel != null && rel.ConfessedDay == Today && !rel.ConfessionAnswered && !rel.Dating;
        }

        /// <summary>
        /// The player's answer to today's confession. Yes: they're dating now (and someone seeing another resident
        /// leaves them for the player). No: a little hurt, and the spark cools; being honest earns some trust.
        /// </summary>
        public InteractionResult AnswerConfession(Resident resident, bool yes)
        {
            if (!AwaitingAnswer(resident)) throw new System.InvalidOperationException($"{resident.Profile.FirstName} hasn't confessed today.");
            (Relationship rel, Pcg32 rng, InteractionResult result, Meters before) = Begin(resident, InteractionKind.Confession);
            rel.ConfessionAnswered = true;
            string name = resident.Profile.FirstName;
            if (!yes)
            {
                Add(ref rel.Affection, -3f);
                Add(ref rel.Attraction, -TurnedDownAttraction);
                Add(ref rel.Trust, 2f);
                result.Learnt = $"You told {name} you'd rather stay friends.";
                return Finish(resident, rel, rng, result, before, "confess.no", GreetMinutes);
            }

            rel.Dating = true;
            Add(ref rel.Affection, 6f);
            Add(ref rel.Attraction, 3f);
            Add(ref rel.Trust, 4f);
            int partner = _world.Social.PartnerOf(resident.Index);
            if (partner < 0)
            {
                result.Learnt = $"You and {name} are dating now.";
                return Finish(resident, rel, rng, result, before, "confess.yes", GreetMinutes);
            }
            var partnerName = ("partner", _world.Residents[partner].Profile.FirstName);
            _world.Social.LeaveForPlayer(resident.Index, Today);
            Relationship ex = _world.Find(partner);
            if (ex != null && ex.Met) ex.Affection = Clamp(ex.Affection - ExResentment);
            result.Learnt = $"{name} is leaving {partnerName.Item2} for you.";
            return Finish(resident, rel, rng, result, before, "confess.yes.leaves", GreetMinutes, partnerName);
        }
    }
}
