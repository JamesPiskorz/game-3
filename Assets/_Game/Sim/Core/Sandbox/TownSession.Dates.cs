using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Sim.Relationships;
using Game.Sim.Residents;

namespace Game.Sim.Sandbox
{
    /// <summary>Asking out, the rounds of a date, saying goodnight, and invitations.</summary>
    public sealed partial class TownSession
    {
        /// <summary>Asks the selected resident out to <paramref name="location"/>. A yes goes there now, both dressed for a date.</summary>
        public InteractionResult AskOnDate(string location)
        {
            if (Selected != null && !IsOpen(location))
            {
                Notice = $"{LocationName(location)} is closed now.";
                Raise();
                return null;
            }
            Resident partner = Selected;
            DateScene scene = null;
            InteractionResult result = Do(r => Act.Invite(r, location, out scene), keepTime: true);
            if (scene != null)
            {
                DateWith = partner;
                Date = scene;
                World.Player.Location = location;
                World.Player.Outfit = DateOutfit;
                Selected = partner;
                Raise();
            }
            else if (result != null)
            {
                AfterTimePasses();
                Raise();
            }
            return result;
        }

        /// <summary>
        /// Plays a round of the date in progress. After the last round the date wraps up: the reply comes with how
        /// the whole date went as its follow-up.
        /// </summary>
        public InteractionResult DateChoose(int option)
        {
            Notice = null;
            if (Date == null || Date.Finished || option < 0 || option >= Date.Options.Count)
            {
                Notice = "There's nothing to choose right now.";
                Raise();
                return null;
            }
            InteractionResult result = Act.DateChoose(Date, option);
            if (Date.Done)
            {
                result = Combine(result, Act.FinishDate(Date));
                _today.Dates++;
            }
            Last = result;
            Speaker = DateWith;
            Raise();
            return result;
        }

        /// <summary>Says goodbye after a date (ending it early counts the rounds so far); the player stays where it was.</summary>
        public void EndDate()
        {
            if (DateWith == null) return;
            if (Date != null && !Date.Finished)
            {
                Last = Act.FinishDate(Date);
                Speaker = DateWith;
                _today.Dates++;
            }
            Notice = $"You say goodbye to {DateWith.Profile.FirstName}.";
            EndDateQuietly();
            AfterTimePasses();
            Raise();
        }

        /// <summary>A round's reply followed by the date's outcome, as one exchange.</summary>
        static InteractionResult Combine(InteractionResult round, InteractionResult outcome) => new InteractionResult
        {
            Kind = round.Kind,
            Reaction = round.Reaction,
            Line = round.Line,
            FollowUp = outcome.Line,
            FollowUpReaction = outcome.Reaction,
            Before = round.Before,
            After = outcome.After,
            Familiarity = round.Familiarity + outcome.Familiarity,
            Affection = round.Affection + outcome.Affection,
            Attraction = round.Attraction + outcome.Attraction,
            Trust = round.Trust + outcome.Trust,
            Learnt = round.Learnt ?? outcome.Learnt,
        };

        void EndDateQuietly()
        {
            if (DateWith == null) return;
            if (Date != null && !Date.Finished) Act.FinishDate(Date);
            DateWith = null;
            Date = null;
            World.Player.Outfit = DayOutfit;
            DressForTheOccasion();
        }

        /// <summary>The player is where a resident asked to meet, when they asked: the date begins.</summary>
        void KeepInvitation()
        {
            Invitation due = Invitations.Due(World, Location);
            if (due == null) return;
            Resident host = World.Residents[due.From];
            if (World.WhereIs(host) != Location) return;
            due.Kept = true;
            InteractionResult hello = Act.MeetUp(host, Location, out DateScene scene);
            DateWith = host;
            Date = scene;
            Selected = host;
            World.Player.Outfit = DateOutfit;
            Last = hello;
            Speaker = host;
            Notice = $"{host.Profile.FirstName} was waiting for you.";
        }

        /// <summary>Messages from residents, newest first.</summary>
        public List<Invitation> Messages() => World.Invitations.AsEnumerable().Reverse().ToList();

        /// <summary>Keepsake photos from great dates, newest first.</summary>
        public List<Photo> Photos() => World.Photos.AsEnumerable().Reverse().ToList();

        /// <summary>Heads to where an invitation is, if it's still on and the place is open.</summary>
        public void Answer(Invitation invitation)
        {
            Notice = null;
            if (!invitation.Pending)
            {
                Notice = invitation.Kept ? "You already met up." : "That's passed.";
                Raise();
                return;
            }
            int early = (invitation.Hour - World.Now.Hour) * 60 - World.Now.Minute - TravelMinutes;
            if (invitation.Day == World.Now.Day && early > 0) Wait(early); // arrive on the hour, not before
            Travel(invitation.Location);
        }
    }
}
