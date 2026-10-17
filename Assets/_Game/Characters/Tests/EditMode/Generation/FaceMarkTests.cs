using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using NUnit.Framework;

namespace Game.Characters.Tests.Generation
{
    /// <summary>Freckles, beauty marks, blush and lipstick: how often, on whom, and that bad ones get caught.</summary>
    public class FaceMarkTests
    {
        CharacterGenerator _generator;
        ContentCatalog _catalog;

        [SetUp]
        public void CreateGenerator()
        {
            _catalog = Catalogs.Project();
            _generator = new CharacterGenerator(_catalog);
        }

        bool Has(CharacterDna dna, string slot) => dna.Face.Makeup.Any(m => Makeup.SlotOf(_catalog, m.OverlayId) == slot);

        [Test]
        public void EachStyleWearsItsOwnMarks()
        {
            var counts = new Dictionary<string, (int People, int Freckles, int Lipstick, int Blush)>();
            for (ulong seed = 0; seed < 4000; seed++)
            {
                CharacterDna dna = _generator.Generate(seed);
                counts.TryGetValue(dna.Style, out var c);
                counts[dna.Style] = (c.People + 1, c.Freckles + (Has(dna, Slots.FaceFreckles) ? 1 : 0),
                    c.Lipstick + (Has(dna, Makeup.Lipstick) ? 1 : 0), c.Blush + (Has(dna, Slots.FaceBlush) ? 1 : 0));
            }
            float Share(string style, System.Func<(int People, int Freckles, int Lipstick, int Blush), int> pick) => pick(counts[style]) / (float)counts[style].People;
            Assert.That(Share("gothic", c => c.Lipstick), Is.GreaterThan(Share("sporty", c => c.Lipstick) * 4), "Gothic looks wear lipstick far more than sporty ones.");
            Assert.That(Share("cute", c => c.Blush), Is.GreaterThan(Share("street", c => c.Blush) * 3));
            Assert.That(Share("sporty", c => c.Freckles), Is.GreaterThan(Share("elegant", c => c.Freckles) * 2));
            foreach (string style in counts.Keys)
                Assert.That(Share(style, c => c.Freckles) + Share(style, c => c.Lipstick), Is.LessThan(0.8f), $"{style}: marks are a detail, not a uniform.");
        }

        [Test]
        public void MakeupLeansFeminineButIsntOnlyForWomen()
        {
            int masc = 0, mascMade = 0, fem = 0, femMade = 0;
            for (ulong seed = 0; seed < 4000; seed++)
            {
                CharacterDna dna = _generator.Generate(seed);
                bool madeUp = Has(dna, Makeup.Lipstick) || Has(dna, Slots.FaceBlush);
                if (dna.Body.Femininity < 0.3f) { masc++; if (madeUp) mascMade++; }
                if (dna.Body.Femininity > 0.7f) { fem++; if (madeUp) femMade++; }
            }
            Assert.That(mascMade, Is.GreaterThan(0), "Some masculine characters wear makeup.");
            Assert.That(femMade / (float)fem, Is.GreaterThan(2.5f * mascMade / masc));
        }

        [Test]
        public void LipstickComesFromTheStylesPalette()
        {
            List<HsvColor> dark = _catalog.Palette("makeup.lips.dark").Colours.Select(c => HsvColor.FromHex(c.Hex)).ToList();
            List<HsvColor> usual = _catalog.Palette(Makeup.LipPalette).Colours.Select(c => HsvColor.FromHex(c.Hex)).ToList();
            int checkedLips = 0;
            for (ulong seed = 0; seed < 3000; seed++)
            {
                CharacterDna dna = _generator.Generate(seed);
                OverlayDna lips = dna.Face.Makeup.FirstOrDefault(m => m.OverlayId == Makeup.Lipstick);
                if (lips == null) continue;
                checkedLips++;
                List<HsvColor> palette = dna.Style == "gothic" ? dark : usual;
                Assert.That(palette.Min(c => HsvColor.Distance(c, lips.Tint)), Is.LessThan(0.1f), $"seed {seed} ({dna.Style})");
            }
            Assert.That(checkedLips, Is.GreaterThan(300));
        }

        [Test]
        public void TheValidatorAndRepairCatchBadMarks()
        {
            CharacterDna dna = _generator.Generate(7);
            dna.Face.Makeup = new List<OverlayDna>
            {
                new OverlayDna { OverlayId = "face.mark.freckles_01" },
                new OverlayDna { OverlayId = "face.mark.freckles_01" },
                new OverlayDna { OverlayId = "face.mark.tattoo_99" },
                new OverlayDna { OverlayId = "cloth.top.tshirt_01" },
                new OverlayDna { OverlayId = Makeup.Lipstick, Tint = new HsvColor(0.98f, 0.7f, 0.6f) },
            };
            List<string> errors = DnaValidator.Validate(_catalog, dna);
            Assert.That(errors.Count(e => e.Contains("face mark") || e.Contains("Two face marks")), Is.EqualTo(3), string.Join("\n", errors));

            CharacterDna repaired = DnaRepair.Repair(_generator, dna, out List<string> fixes);
            Assert.That(DnaValidator.Validate(_catalog, repaired), Is.Empty);
            Assert.That(repaired.Face.Makeup.Select(m => m.OverlayId), Is.EquivalentTo(new[] { "face.mark.freckles_01", Makeup.Lipstick }));
            Assert.That(fixes.Count(f => f.Contains("face mark")), Is.EqualTo(3));
        }

        [Test]
        public void RerollingTheFaceRerollsTheMarksAndNothingElse()
        {
            CharacterDna dna = _generator.Generate(11);
            for (ulong seed = 100; seed < 160; seed++)
            {
                CharacterDna rerolled = _generator.Reroll(dna, DnaCategories.Face, seed);
                Assert.That(rerolled.Hair.PieceIds, Is.EqualTo(dna.Hair.PieceIds));
                Assert.That(rerolled.Hair.Base, Is.EqualTo(dna.Hair.Base));
                Assert.That(DnaValidator.Validate(_catalog, rerolled), Is.Empty);
            }
            Assert.That(Enumerable.Range(100, 60).Select(s => _generator.Reroll(dna, DnaCategories.Face, (ulong)s).Face.Makeup.Count).Distinct().Count(), Is.GreaterThan(1),
                "Different rolls give different marks.");
        }
    }
}
