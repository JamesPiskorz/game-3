using System;
using System.Collections.Generic;
using Game.Characters.Rng;

namespace Game.Sim.Residents
{
    /// <summary>
    /// Where a resident is each hour of a typical week. Regenerated from their seed rather than saved; the town
    /// repeats weekly, which is what lets the player learn "Mei is at the library on Thursday evenings".
    /// </summary>
    public sealed class Schedule
    {
        readonly string[][] _hours;

        public Schedule(string[][] hours)
        {
            if (hours.Length != 7) throw new ArgumentException("A schedule covers seven days.", nameof(hours));
            foreach (string[] day in hours)
                if (day.Length != 24) throw new ArgumentException("Each schedule day has 24 hours.", nameof(hours));
            _hours = hours;
        }

        public string At(int dayOfWeek, int hour) => _hours[dayOfWeek][hour];
        public string At(SimTime time) => _hours[time.DayOfWeek][time.Hour];

        /// <summary>Consecutive hours at the same place on a day, for the UI's "usually at..." hints.</summary>
        public IEnumerable<(int Start, int End, string Location)> Blocks(int dayOfWeek)
        {
            string[] day = _hours[dayOfWeek];
            int start = 0;
            for (int hour = 1; hour <= 24; hour++)
            {
                if (hour < 24 && day[hour] == day[start]) continue;
                yield return (start, hour, day[start]);
                start = hour;
            }
        }
    }

    /// <summary>
    /// Builds a week: home at night, at work on working days, and free hours split between home and favourite
    /// haunts that are open. Outgoing residents go out more and stay up later.
    /// </summary>
    public sealed class ScheduleGenerator
    {
        readonly SimCatalog _sim;

        public ScheduleGenerator(SimCatalog sim) => _sim = sim;

        public Schedule Generate(ulong seed, ResidentProfile profile)
        {
            Pcg32 rng = SeedStreams.For(seed, "schedule");
            JobEntry job = profile.JobId == null ? null : _sim.Job(profile.JobId);
            var hours = new string[7][];
            for (int day = 0; day < 7; day++) hours[day] = Day(day, profile, job, rng);
            return new Schedule(hours);
        }

        string[] Day(int day, ResidentProfile profile, JobEntry job, Pcg32 rng)
        {
            var hours = new string[24];
            for (int h = 0; h < 24; h++) hours[h] = SimCatalog.Home;

            bool working = job != null && job.Days.Contains(day);
            Personality p = profile.Personality;
            int wake = working ? Math.Max(5, Math.Min(job.Start - 1, 8)) : 8 + rng.NextInt(3);
            int bed = 22 + (p.Extraversion > 0.6f ? 1 : 0) + (day == 4 || day == 5 ? 1 : 0);
            if (working) bed = Math.Max(bed, Math.Min(24, job.End));
            bed = Math.Min(bed, 24);

            // Busy hours are spoken for, even when the job is at home (freelancers).
            var busy = new bool[24];
            if (working)
                for (int h = job.Start; h < Math.Min(job.End, 24); h++)
                {
                    hours[h] = job.Location;
                    busy[h] = true;
                }

            float goOut = 0.3f + 0.5f * p.Extraversion;
            int hour = wake;
            while (hour < bed)
            {
                if (busy[hour]) { hour++; continue; }

                int stay = 1 + rng.NextInt(3);
                if (rng.Chance(goOut))
                {
                    string place = Hangout(profile, hour, rng);
                    if (place != null)
                    {
                        LocationEntry location = _sim.Location(place);
                        for (int h = hour; h < hour + stay && h < bed && !busy[h] && location.IsOpen(h); h++)
                            hours[h] = place;
                    }
                }
                hour += stay;
            }
            return hours;
        }

        /// <summary>A favourite open place: earlier hangouts in the list are likelier.</summary>
        string Hangout(ResidentProfile profile, int hour, Pcg32 rng)
        {
            var open = new List<(string Id, float Weight)>();
            for (int i = 0; i < profile.Hangouts.Count; i++)
            {
                LocationEntry location = _sim.Location(profile.Hangouts[i]);
                if (location != null && location.IsOpen(hour)) open.Add((location.Id, Math.Max(1f, 3f - i)));
            }
            return open.Count == 0 ? null : rng.PickWeighted(open, o => o.Weight).Id;
        }
    }
}
