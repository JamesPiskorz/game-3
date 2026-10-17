using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Game.Sim.Relationships
{
    public enum Stage
    {
        Stranger,
        Acquaintance,
        Friend,
        Close,
        Dating,
        Partner,
    }

    /// <summary>
    /// How one resident feels about the player. Four meters, 0..100: familiarity (how well they know you), affection
    /// (how much they like you), attraction (romantic spark) and trust (built slowly, across days). The stage follows
    /// from the meters, except dating and partnership, which take the resident saying yes.
    /// </summary>
    public sealed class Relationship
    {
        public const float FriendAffection = 30f;
        public const float FriendFamiliarity = 35f;
        public const float CloseAffection = 55f;
        public const float CloseFamiliarity = 60f;
        public const float CloseTrust = 30f;
        public const float AcquaintanceFamiliarity = 10f;

        public int Resident;
        public float Familiarity;
        public float Affection;
        public float Attraction;
        public float Trust;

        /// <summary>First impression taken; until then they're a stranger whatever the meters say.</summary>
        public bool Met;
        public bool Dating;
        public bool Partner;
        public int Dates;
        public int GreatDates;

        /// <summary>Day of the last refused date request, so asking again straight away is a "not yet".</summary>
        public int LastRefusalDay = -100;
        public int LastComplimentDay = -1;
        public int LastGiftDay = -1;
        public int LastSeenDay = -1;
        public int TalksToday;

        /// <summary>Topic ID → day it last came up, for "we already covered that".</summary>
        public SortedDictionary<string, int> TopicDays = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>What the player has learnt about their tastes, shown in the contact book.</summary>
        public SortedSet<string> KnownLikes = new SortedSet<string>(StringComparer.Ordinal);
        public SortedSet<string> KnownDislikes = new SortedSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Recent moments worth bringing up when they next meet: the latest loved topic, loved gift and great date.
        /// Each is mentioned once, then forgotten.
        /// </summary>
        public Memory LovedTopic, LovedGift, GreatDate;

        /// <summary>The day of the makeover the player last mentioned or complimented, so each change counts once.</summary>
        public int MentionedMakeoverDay = -1;
        public int NoticedMakeoverDay = -1;

        /// <summary>Chat replies that landed well, by style; two of a kind and the player has learnt what they like.</summary>
        public SortedDictionary<string, int> ChatWins = new SortedDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>The way of talking they respond to best, once the player has seen it work (null until then).</summary>
        public string KnownChatStyle;

        /// <summary>Questions asked today (by kind), so asking twice is noticed.</summary>
        public SortedSet<string> AskedToday = new SortedSet<string>(StringComparer.Ordinal);
        public int AskedDay = -1;

        /// <summary>They've told the player about their friends and whether they're seeing anyone.</summary>
        public bool KnowsTheirLife;

        /// <summary>Where they said they'd be later today, if asked.</summary>
        public Memory Plan;
        public int PlanHour;

        /// <summary>Friends of theirs the player is also dating, already found out about.</summary>
        public SortedSet<int> JealousOf = new SortedSet<int>();

        /// <summary>Town news (social event indices) they've already told the player.</summary>
        public SortedSet<int> HeardNews = new SortedSet<int>();

        /// <summary>Friends of theirs who've spoken well of the player to them (each only counts once).</summary>
        public SortedSet<int> HeardFrom = new SortedSet<int>();

        /// <summary>Reaction key → the line said last time, so nobody repeats themselves back to back.</summary>
        public SortedDictionary<string, string> LastLines = new SortedDictionary<string, string>(StringComparer.Ordinal);

        [JsonIgnore]
        public Stage Stage
        {
            get
            {
                if (Partner) return Stage.Partner;
                if (Dating) return Stage.Dating;
                if (!Met || Familiarity < AcquaintanceFamiliarity) return Stage.Stranger;
                if (Affection >= CloseAffection && Familiarity >= CloseFamiliarity && Trust >= CloseTrust) return Stage.Close;
                if (Affection >= FriendAffection && Familiarity >= FriendFamiliarity) return Stage.Friend;
                return Stage.Acquaintance;
            }
        }
    }

    /// <summary>Something that happened between the player and a resident: what (a topic, gift or place ID) and when.</summary>
    public sealed class Memory
    {
        public string Id;
        public int Day;
    }
}
