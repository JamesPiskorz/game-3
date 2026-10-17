using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using NUnit.Framework;

namespace Game.Characters.Tests.Generation
{
    public class OuterwearTests
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
        public void EveryRainyDayOutfitHasACoatOverSomethingThatFitsUnderIt()
        {
            for (ulong seed = 0; seed < 1500; seed++)
            {
                CharacterDna dna = _generator.Generate(seed);
                OutfitDna rain = dna.Outfits.Single(o => o.Context == "rain");
                List<PartEntry> worn = Parts(rain);
                Assert.That(worn.Count(p => p.Slot == Slots.Outer), Is.EqualTo(1), $"seed {seed}");
                Assert.That(worn.Any(p => p.Tags.Contains("hooded") || p.Tags.Contains("tailored")), Is.False, $"seed {seed}: nothing bulky under the coat");
                Assert.That(worn.Any(p => p.Slot == Slots.Top || p.Slot == Slots.OnePiece), $"seed {seed}: something under it");
            }
        }

        [Test]
        public void SomeEverydayOutfitsAddAJacketAndStyleDecidesWho()
        {
            var casual = Enumerable.Range(0, 2000).Select(s => _generator.Generate((ulong)s)).ToList();
            float Share(string style) =>
                casual.Where(d => d.Style == style).Select(d => Parts(d.Outfits.Single(o => o.Context == "casual")).Any(p => p.Slot == Slots.Outer) ? 1f : 0f).DefaultIfEmpty(0f).Average();
            float overall = casual.Average(d => Parts(d.Outfits.Single(o => o.Context == "casual")).Any(p => p.Slot == Slots.Outer) ? 1f : 0f);
            Assert.That(overall, Is.InRange(0.05f, 0.3f));
            Assert.That(Share("street"), Is.GreaterThan(Share("cute")), "Street style layers up more than cute.");
        }

        [Test]
        public void ContextsThatNeedACoatMustHaveOne()
        {
            ContentCatalog catalog = Catalogs.Project();
            catalog.Parts.RemoveAll(p => p.Slot == Slots.Outer);
            Assert.That(catalog.Validate(), Has.Some.Contains("needs an outer layer"));
        }
    }
}
