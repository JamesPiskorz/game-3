using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;

namespace Game.Sim.Sandbox
{
    /// <summary>
    /// The salon at Arcade Lane, next to the boutique: a new cut, new bangs (or none) or a new colour, for coins and
    /// an hour. What's on offer comes from the character catalog, so new hair content shows up here without UI work.
    /// Residents who knew the player before notice, and react by their own taste in styles.
    /// </summary>
    public static class Salon
    {
        public const string Location = Boutique.Location;
        public const int CutPrice = 40, BangsPrice = 20, DyePrice = 35;
        public const int Minutes = 60;

        public static List<PartEntry> Cuts(ContentCatalog catalog) => catalog.PartsInSlot(Slots.HairBack).ToList();

        public static List<PartEntry> Bangs(ContentCatalog catalog) => catalog.PartsInSlot(Slots.HairFront).ToList();

        /// <summary>Natural shades first, then the fantasy ones.</summary>
        public static List<HsvColor> Colours(ContentCatalog catalog) =>
            new[] { catalog.Hair.NaturalPalette, catalog.Hair.FantasyPalette }
                .SelectMany(id => catalog.Palette(id)?.Colours ?? new List<PaletteColour>())
                .Select(c => HsvColor.FromHex(c.Hex)).ToList();
    }
}
