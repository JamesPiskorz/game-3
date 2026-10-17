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
        public void Bangs(string frontId)
        {
            List<PartEntry> cut = World.Player.Dna?.Hair.PieceIds.Select(World.Characters.Part).Where(p => p != null && p.Slot == Slots.HairBack).ToList();
            PartEntry wanted = frontId == null ? null : World.Characters.Part(frontId);
            if (AtSalon && wanted != null && cut != null && !CharacterGenerator.Compatible(wanted, cut))
            {
                Notice = $"{wanted.Name} don't go with your {cut.FirstOrDefault()?.Name?.ToLowerInvariant() ?? "cut"}.";
                Raise();
                return;
            }
            RestyleBangs(frontId);
        }

        void RestyleBangs(string frontId) => Restyle(Salon.BangsPrice, MakeoverKind.Haircut, frontId == null ? "look without bangs" : "set of bangs", dna =>
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

        /// <summary>Lipstick in a shade, or (null) none. Putting it on costs; taking it off is free and nobody remarks on it.</summary>
        public void Lipstick(HsvColor? colour) => Restyle(colour.HasValue ? Salon.MakeupPrice : 0, colour.HasValue ? MakeoverKind.Makeup : (MakeoverKind?)null,
            colour.HasValue ? "lipstick" : "bare-lipped look", dna =>
            {
                OverlayDna old = dna.Face.Makeup.FirstOrDefault(m => m.OverlayId == Makeup.Lipstick);
                if (colour == null ? old == null : old != null && HsvColor.Distance(old.Tint, colour.Value) < 0.02f) return false;
                dna.Face.Makeup.RemoveAll(m => m.OverlayId == Makeup.Lipstick);
                if (colour.HasValue) dna.Face.Makeup.Add(new OverlayDna { OverlayId = Makeup.Lipstick, Tint = colour.Value.Normalized(), Opacity = 1f });
                return true;
            });

        /// <summary>Blush on, or off (free, and unremarked).</summary>
        public void Blush(bool on) => Restyle(on ? Salon.MakeupPrice : 0, on ? MakeoverKind.Makeup : (MakeoverKind?)null, on ? "blush" : "look without blush", dna =>
        {
            bool has = dna.Face.Makeup.Any(m => Makeup.SlotOf(World.Characters, m.OverlayId) == Slots.FaceBlush);
            if (has == on || (on && World.Characters.Part(Salon.BlushId) == null)) return false;
            dna.Face.Makeup.RemoveAll(m => Makeup.SlotOf(World.Characters, m.OverlayId) == Slots.FaceBlush);
            ColourPalette palette = World.Characters.Palette(Makeup.BlushPalette);
            if (on) dna.Face.Makeup.Add(new OverlayDna { OverlayId = Salon.BlushId, Tint = palette == null ? new HsvColor(0.96f, 0.35f, 0.95f) : HsvColor.FromHex(palette.Colours[0].Hex), Opacity = 1f });
            return true;
        });

        void Restyle(int price, MakeoverKind? kind, string what, Func<CharacterDna, bool> change)
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
                    if (kind.HasValue) player.LastMakeover = new Makeover { Day = World.Now.Day, Kind = kind.Value };
                    World.Advance(kind == MakeoverKind.Makeup ? Salon.Minutes / 2 : kind == null ? 5 : Salon.Minutes);
                    AfterTimePasses();
                    Notice ??= price == 0 ? $"A {what}." : $"A new {what}, for {price} coins. People who know you will notice.";
                }
            }
            Raise();
        }
    }
}
