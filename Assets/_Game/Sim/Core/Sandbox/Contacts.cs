using System.Collections.Generic;
using System.Linq;
using Game.Sim.Relationships;

namespace Game.Sim.Sandbox
{
    /// <summary>Someone the player has met, and what the player knows about where to find them.</summary>
    public sealed class Contact
    {
        public Resident Resident;
        public Relationship Relationship;

        /// <summary>"Probably at the library right now", "Often at Willow Park"...</summary>
        public string Hint;

        /// <summary>Where they are now, if the player knows them well enough to guess; else null.</summary>
        public string KnownLocation;
    }

    /// <summary>
    /// The player's contact book. What you know about someone's whereabouts grows with familiarity: first nothing,
    /// then their favourite haunt, then where they'd be right now. That turns schedules into something to learn.
    /// </summary>
    public static class Contacts
    {
        public const float KnowsHaunt = 15f;
        public const float KnowsRoutine = 30f;

        public static List<Contact> For(TownSession session)
        {
            SimWorld world = session.World;
            var contacts = new List<Contact>();
            foreach (Relationship rel in world.Relationships.Where(r => r.Met))
            {
                Resident resident = world.Residents[rel.Resident];
                var contact = new Contact { Resident = resident, Relationship = rel };
                bool toldPlan = rel.Plan != null && rel.Plan.Day == world.Now.Day && world.Now.Hour >= rel.PlanHour && world.WhereIs(resident) == rel.Plan.Id;
                if (toldPlan)
                {
                    contact.KnownLocation = rel.Plan.Id;
                    contact.Hint = $"Said they'd be at {session.LocationName(rel.Plan.Id)} from {rel.PlanHour}:00";
                }
                else if (rel.Familiarity >= KnowsRoutine)
                {
                    string now = world.WhereIs(resident);
                    contact.KnownLocation = now;
                    contact.Hint = now == SimCatalog.Home ? "Probably at home right now" : $"Probably at {session.LocationName(now)} right now";
                }
                else if (rel.Familiarity >= KnowsHaunt && resident.Profile.Hangouts.Count > 0)
                {
                    contact.Hint = $"Often at {session.LocationName(resident.Profile.Hangouts[0])}";
                }
                else
                {
                    contact.Hint = "You don't know their routine yet";
                }
                contacts.Add(contact);
            }
            return contacts.OrderByDescending(c => c.Relationship.Stage).ThenByDescending(c => c.Relationship.Affection).ToList();
        }
    }
}
