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
    /// <summary>The salon: new cuts, bangs and colours for the player.</summary>
    public sealed partial class TownSession
    {
        /// <summary>Whether the salon (at Arcade Lane, while it's open) can see the player now.</summary>
        public bool AtSalon => Location == Salon.Location && IsOpen(Location) && DateWith == null && World.Player.Dna != null;

        /// <summary>A new cut: the back piece of the hair. Bangs that don't go with it come off.</summary>
        public void Haircut(string backId) => Restyle(Salon.CutPrice, MakeoverKind.Haircut, "haircut", dna =>
        {
            PartEntry cut = World.Characters.Part(backId);
            if (cut == null || cut.Slot != Slots.HairBack) return false;
            List<string> pieces = new List<string> { cut.Id };
            pieces.AddRange(dna.Hair.PieceIds.Select(World.Characters.Part)
                .Where(p => p != null && p.Slot == Slots.HairFront && CharacterGenerator.Compatible(p, new[] { cut })).Select(p => p.Id));
            if (pieces.SequenceEqual(dna.Hair.PieceIds)) return false;
            dna.Hair.PieceIds = pieces;
            return true;
        });

        /// <summary>New bangs, or none (<paramref name="frontId"/> null).</summary>
        public void Bangs(string frontId) => Restyle(Salon.BangsPrice, MakeoverKind.Haircut, frontId == null ? "look without bangs" : "set of bangs", dna =>
        {
            List<PartEntry> back = dna.Hair.PieceIds.Select(World.Characters.Part).Where(p => p != null && p.Slot == Slots.HairBack).ToList();
            PartEntry front = frontId == null ? null : World.Characters.Part(frontId);
            if (frontId != null && (front == null || front.Slot != Slots.HairFront || !CharacterGenerator.Compatible(front, back))) return false;
            List<string> pieces = back.Select(p => p.Id).ToList();
            if (front != null) pieces.Add(front.Id);
            if (pieces.SequenceEqual(dna.Hair.PieceIds)) return false;
            dna.Hair.PieceIds = pieces;
            return true;
        });

        /// <summary>A new colour, with the highlight to match; any dip-dye goes.</summary>
        public void Dye(HsvColor colour) => Restyle(Salon.DyePrice, MakeoverKind.Colour, "hair colour", dna =>
        {
            if (HsvColor.Distance(colour, dna.Hair.Base) < 0.02f) return false;
            dna.Hair.Base = colour;
            dna.Hair.Highlight = new HsvColor(colour.H, System.Math.Max(0f, colour.S - 0.1f), System.Math.Min(1f, colour.V + 0.18f));
            dna.Hair.Tip = colour;
            dna.Hair.Gradient = 0f;
            return true;
        });

        void Restyle(int price, MakeoverKind kind, string what, Func<CharacterDna, bool> change)
        {
            Notice = null;
            PlayerCharacter player = World.Player;
            if (!AtSalon) Notice = "The salon is at Arcade Lane, while it's open.";
            else if (player.Money < price) Notice = $"That's {price} coins, and you have {player.Money}.";
            else
            {
                CharacterDna next = DnaSerializer.FromJson(DnaSerializer.ToJson(player.Dna, indented: false));
                if (!change(next)) Notice = "That's what you have already.";
                else
                {
                    // A hat the new hair can't take comes off.
                    new CharacterGenerator(World.Characters).DropIncompatibleParts(next);
                    player.Dna = next;
                    player.Money -= price;
                    _today.Spent += price;
                    player.LookVersion++;
                    player.LastMakeover = new Makeover { Day = World.Now.Day, Kind = kind };
                    World.Advance(Salon.Minutes);
                    AfterTimePasses();
                    Notice ??= $"A new {what}, for {price} coins. People who know you will notice.";
                }
            }
            Raise();
        }
    }
}
