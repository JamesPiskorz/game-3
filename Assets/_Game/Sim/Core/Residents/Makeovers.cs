using System.Collections.Generic;
using System.Linq;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Rng;
using Game.Sim.Relationships;

namespace Game.Sim.Residents
{
    public enum MakeoverKind
    {
        Haircut,
        Colour,
        Clothes,
    }

    /// <summary>A change to how a resident looks, and the day it happened.</summary>
    public sealed class Makeover
    {
        public int Day;
        public MakeoverKind Kind;
    }

    /// <summary>
    /// Residents change how they look now and then: a new haircut, a new hair colour or new everyday clothes, made by
    /// the character generator in their own style. Each week's changes are a function of the town seed and the week,
    /// so saves don't store them: a loaded town catches up by replaying the weeks.
    /// Someone the player knew before the change mentions it, and a compliment while it's new counts for more.
    /// </summary>
    public static class Makeovers
    {
        public const float WeeklyChance = 0.12f;

        /// <summary>How long a change counts as new.</summary>
        public const int NewForDays = 7;

        /// <summary>Applies every week's makeovers up to today that haven't been applied yet.</summary>
        public static void CatchUp(SimWorld world)
        {
            int week = world.Now.Day / 7;
            if (world.MakeoverWeek >= week) return;
            var generator = new CharacterGenerator(world.Characters);
            while (world.MakeoverWeek < week)
            {
                world.MakeoverWeek++;
                ForWeek(world, generator, world.MakeoverWeek);
            }
        }

        static void ForWeek(SimWorld world, CharacterGenerator generator, int week)
        {
            int day = week * 7;
            foreach (Resident resident in world.Residents)
            {
                Pcg32 rng = SeedStreams.For(StableHash.SplitMix64(world.Seed ^ StableHash.SplitMix64((ulong)week)) ^ resident.Dna.Seed, "makeover");
                if (!rng.Chance(WeeklyChance)) continue;
                MakeoverKind kind = rng.PickWeighted(new[] { MakeoverKind.Haircut, MakeoverKind.Colour, MakeoverKind.Clothes },
                    k => k == MakeoverKind.Haircut ? 0.4f : k == MakeoverKind.Colour ? 0.25f : 0.35f);
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    ulong seed = rng.NextUInt() | (ulong)rng.NextUInt() << 32;
                    CharacterDna changed = Change(generator, resident.Dna, kind, seed);
                    if (changed == null) continue;
                    resident.Dna = changed;
                    resident.LastMakeover = new Makeover { Day = day, Kind = kind };
                    break;
                }
            }
        }

        /// <summary>The resident after one change, or null if this seed didn't change anything noticeable.</summary>
        static CharacterDna Change(CharacterGenerator generator, CharacterDna dna, MakeoverKind kind, ulong seed)
        {
            switch (kind)
            {
                case MakeoverKind.Haircut:
                {
                    // A new style in the same colour. Rerolling the hair also fixes up hats the new style can't take.
                    CharacterDna cut = generator.Reroll(dna, DnaCategories.Hair, seed);
                    if (cut.Hair.PieceIds.SequenceEqual(dna.Hair.PieceIds)) return null;
                    cut.Hair.Base = dna.Hair.Base;
                    cut.Hair.Highlight = dna.Hair.Highlight;
                    cut.Hair.Tip = dna.Hair.Tip;
                    cut.Hair.Gradient = dna.Hair.Gradient;
                    return cut;
                }
                case MakeoverKind.Colour:
                {
                    CharacterDna fresh = generator.Reroll(dna, DnaCategories.Hair, seed);
                    if (HsvColor.Distance(fresh.Hair.Base, dna.Hair.Base) < 0.15f) return null;
                    CharacterDna dyed = Clone(dna);
                    dyed.Hair.Base = fresh.Hair.Base;
                    dyed.Hair.Highlight = fresh.Hair.Highlight;
                    dyed.Hair.Tip = fresh.Hair.Tip;
                    dyed.Hair.Gradient = fresh.Hair.Gradient;
                    return dyed;
                }
                default:
                {
                    // New everyday clothes; going-out and rainy-day outfits stay as they were.
                    CharacterDna fresh = generator.Reroll(dna, DnaCategories.Outfits, seed);
                    CharacterDna dressed = Clone(dna);
                    int casual = dressed.Outfits.FindIndex(o => o.Context == Sandbox.TownSession.DayOutfit);
                    OutfitDna outfit = fresh.Outfits.FirstOrDefault(o => o.Context == Sandbox.TownSession.DayOutfit);
                    if (casual < 0 || outfit == null) return null;
                    dressed.Outfits[casual] = outfit;
                    return dressed;
                }
            }
        }

        /// <summary>
        /// The change the player would notice: recent, made since they last saw this person (whom they'd met before),
        /// and not yet complimented. <paramref name="lastSeen"/> is the day they last saw them before now.
        /// </summary>
        public static Makeover Noticeable(SimWorld world, Resident resident, Relationship rel, int lastSeen)
        {
            Makeover change = resident.LastMakeover;
            if (change == null || rel == null || !rel.Met || rel.NoticedMakeoverDay >= change.Day) return null;
            if (world.Now.Day - change.Day > NewForDays) return null;
            bool sawBefore = lastSeen >= 0 && lastSeen < change.Day;
            return sawBefore || rel.MentionedMakeoverDay == change.Day ? change : null;
        }

        /// <summary>"a new haircut", "a new hair colour" or "new clothes".</summary>
        public static string Describe(MakeoverKind kind) => kind switch
        {
            MakeoverKind.Haircut => "a new haircut",
            MakeoverKind.Colour => "a new hair colour",
            _ => "new clothes",
        };

        public static string Key(MakeoverKind kind) => kind switch
        {
            MakeoverKind.Haircut => "haircut",
            MakeoverKind.Colour => "colour",
            _ => "clothes",
        };

        static CharacterDna Clone(CharacterDna dna) => DnaSerializer.FromJson(DnaSerializer.ToJson(dna, indented: false));
    }
}
