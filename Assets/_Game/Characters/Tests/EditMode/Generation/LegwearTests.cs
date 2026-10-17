using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using NUnit.Framework;

namespace Game.Characters.Tests.Generation
{
    public class LegwearTests
    {
        CharacterGenerator _generator;
        ContentCatalog _catalog;

        [SetUp]
        public void CreateGenerator()
        {
            _catalog = Catalogs.Project();
            _generator = new CharacterGenerator(_catalog);
        }

        List<PartEntry> Parts(OutfitDna outfit) => outfit.Items.Select(i => _catalog.Part(i.ItemId)).ToList();

        [Test]
        public void TightsAndSocksOnlyWhereLegsShow()
        {
            int withLegwear = 0;
            for (ulong seed = 0; seed < 1500; seed++)
                foreach (OutfitDna outfit in _generator.Generate(seed).Outfits)
                {
                    List<PartEntry> worn = Parts(outfit);
                    if (!worn.Any(p => p.Slot == Slots.Legwear)) continue;
                    withLegwear++;
                    Assert.That(worn.Any(p => p.Tags.Contains("bare_legs")), $"seed {seed} {outfit.Context}: legwear under something that hides the legs");
                }
            Assert.That(withLegwear, Is.GreaterThan(100));
        }

        [Test]
        public void GothicAndCuteStylesWearThemMostSportyLeast()
        {
            var people = Enumerable.Range(0, 2500).Select(s => _generator.Generate((ulong)s)).ToList();
            float Share(string style) => people.Where(d => d.Style == style)
                .Select(d => Parts(d.Outfits.Single(o => o.Context == "casual")).Any(p => p.Slot == Slots.Legwear) ? 1f : 0f).DefaultIfEmpty(0f).Average();
            Assert.That(Share("gothic"), Is.GreaterThan(Share("sporty")));
            Assert.That(Share("cute"), Is.GreaterThan(Share("sporty")));
        }

        [Test]
        public void WorkoutClothesAreAllSporty()
        {
            for (ulong seed = 0; seed < 800; seed++)
            {
                OutfitDna sport = _generator.Generate(seed).Outfits.Single(o => o.Context == "sport");
                foreach (PartEntry part in Parts(sport))
                    Assert.That(part.Tags.Contains("sporty") || part.Slot == Slots.Glasses, $"seed {seed}: {part.Id}");
            }
        }
    }
}
