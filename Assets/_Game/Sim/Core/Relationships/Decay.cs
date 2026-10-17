using System.Collections.Generic;
using System.Linq;

namespace Game.Sim.Relationships
{
    /// <summary>
    /// Relationships need tending. Friends and up who haven't seen the player for a few days cool off a little each
    /// day, and someone they're dating starts to miss them. Gentle on purpose: a busy week shouldn't undo a month.
    /// </summary>
    public static class Decay
    {
        public const int GraceDays = 3;
        public const float AffectionPerDay = 1.5f;
        public const float AttractionPerDay = 0.5f;

        /// <summary>Applies one day of distance; returns the people dating the player who now miss them (said once).</summary>
        public static List<Resident> NewDay(SimWorld world)
        {
            var missing = new List<Resident>();
            int today = world.Now.Day;
            foreach (Relationship rel in world.Relationships.Where(r => r.Met && r.LastSeenDay >= 0).ToList())
            {
                int away = today - rel.LastSeenDay;
                if (away <= GraceDays || rel.Stage < Stage.Friend) continue;
                rel.Affection = System.Math.Max(0f, rel.Affection - AffectionPerDay);
                if (rel.Dating) rel.Attraction = System.Math.Max(0f, rel.Attraction - AttractionPerDay);
                if (rel.Dating && away == GraceDays + 1) missing.Add(world.Residents[rel.Resident]);
            }
            return missing;
        }
    }
}
