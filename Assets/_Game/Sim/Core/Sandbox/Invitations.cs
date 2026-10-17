using System.Collections.Generic;
using System.Linq;
using Game.Characters.Rng;
using Game.Sim.Relationships;

namespace Game.Sim.Sandbox
{
    /// <summary>A resident asking the player to meet them somewhere later today.</summary>
    public sealed class Invitation
    {
        public int From;
        public string Location;
        public int Day;
        public int Hour;
        public string Message;
        public bool Kept;
        public bool Missed;

        public bool Pending => !Kept && !Missed;
    }

    /// <summary>
    /// The town reaching out to the player. Each morning, residents who are close to the player (close friends,
    /// or dating) may suggest meeting later that day, somewhere their own plans take them anyway. Turning up
    /// becomes a date; standing them up costs affection and trust.
    /// </summary>
    public static class Invitations
    {
        public const float Chance = 0.35f;
        public const int GapDays = 2;
        public const int WindowHours = 2;
        public const float StoodUpAffection = 6f;
        public const float StoodUpTrust = 4f;

        /// <summary>New invitations for today. Call once each morning.</summary>
        public static List<Invitation> NewDay(SimWorld world, Dialogue dialogue)
        {
            var sent = new List<Invitation>();
            int today = world.Now.Day;
            Pcg32 rng = SeedStreams.For(StableHash.SplitMix64(world.Seed ^ (ulong)today), "invitations");
            foreach (Relationship rel in world.Relationships.Where(r => r.Stage >= Stage.Close && r.Affection >= 60f).ToList())
            {
                if (world.Invitations.Any(i => i.From == rel.Resident && (i.Pending || today - i.Day < GapDays))) continue;
                if (!rng.Chance(Chance)) continue;

                Resident resident = world.Residents[rel.Resident];
                (string place, int hour)? slot = FreeTime(world, resident, today);
                if (slot == null) continue;

                LocationEntry location = world.Catalog.Location(slot.Value.place);
                var tokens = new Dictionary<string, string>
                {
                    ["name"] = resident.Profile.FirstName, ["player"] = world.Player.Name, ["location"] = location.Name, ["time"] = $"{slot.Value.hour}:00",
                };
                var invitation = new Invitation
                {
                    From = rel.Resident, Location = slot.Value.place, Day = today, Hour = slot.Value.hour,
                    Message = dialogue.Line("invite", resident.Profile.Tone, rel.Stage, rng, tokens),
                };
                world.Invitations.Add(invitation);
                sent.Add(invitation);
            }
            return sent;
        }

        /// <summary>The start of a two-hour stretch later today that they'll spend somewhere open that isn't their work.</summary>
        static (string, int)? FreeTime(SimWorld world, Resident resident, int day)
        {
            string work = world.Catalog.Job(resident.Profile.JobId ?? "")?.Location;
            for (int hour = 12; hour <= 20; hour++)
            {
                string place = resident.Schedule.At(day % 7, hour);
                LocationEntry location = world.Catalog.Location(place);
                if (location == null || place == work) continue;
                bool stays = Enumerable.Range(hour, WindowHours).All(h => h < 24 && resident.Schedule.At(day % 7, h) == place && location.IsOpen(h));
                if (stays) return (place, hour);
            }
            return null;
        }

        /// <summary>A pending invitation the player is keeping right now (right place, right time), or null.</summary>
        public static Invitation Due(SimWorld world, string location) =>
            world.Invitations.FirstOrDefault(i => i.Pending && i.Location == location && i.Day == world.Now.Day
                && world.Now.Hour >= i.Hour && world.Now.Hour < i.Hour + WindowHours);

        /// <summary>Marks invitations whose time has passed as missed, and applies the cost. Returns them.</summary>
        public static List<Invitation> Expire(SimWorld world)
        {
            var missed = new List<Invitation>();
            foreach (Invitation invitation in world.Invitations.Where(i => i.Pending))
            {
                bool over = world.Now.Day > invitation.Day || world.Now.Hour >= invitation.Hour + WindowHours;
                if (!over) continue;
                invitation.Missed = true;
                Relationship rel = world.RelationshipWith(world.Residents[invitation.From]);
                rel.Affection = System.Math.Max(0f, rel.Affection - StoodUpAffection);
                rel.Trust = System.Math.Max(0f, rel.Trust - StoodUpTrust);
                missed.Add(invitation);
            }
            return missed;
        }
    }
}
