using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using NUnit.Framework;

namespace Game.Characters.Tests.Generation
{
    public class FamilyTests
    {
        CharacterGenerator _generator;
        ContentCatalog _catalog;

        [SetUp]
        public void CreateGenerator()
        {
            _catalog = Catalogs.Project();
            _generator = new CharacterGenerator(_catalog);
        }

        IEnumerable<(CharacterDna Relative, CharacterDna Sibling)> Pairs(int count) =>
            Enumerable.Range(0, count).Select(i =>
            {
                CharacterDna relative = _generator.Generate((ulong)i * 7919UL + 3UL);
                return (relative, _generator.GenerateRelative(relative, (ulong)i * 104729UL + 11UL));
            });

        [Test]
        public void SiblingsBreakNoRules()
        {
            foreach ((CharacterDna relative, CharacterDna sibling) in Pairs(400))
            {
                Assert.That(DnaValidator.Validate(_catalog, sibling), Is.Empty, $"sibling of {relative.Seed}");
                HsvColor skin = _generator.SkinColour(sibling.Skin.Tone);
                foreach (ItemDna item in sibling.Outfits.SelectMany(o => o.Items))
                {
                    string slot = _catalog.Part(item.ItemId).Slot;
                    if (slot != Slots.Top && slot != Slots.Bottom && slot != Slots.OnePiece) continue;
                    Assert.That(HsvColor.Distance(item.RegionColors[0], skin), Is.GreaterThanOrEqualTo(_catalog.Skin.MinClothingContrast),
                        "Clothes are coloured against the sibling's real skin tone.");
                }
            }
        }

        [Test]
        public void SiblingsLookRelated()
        {
            var pairs = Pairs(300).ToList();
            float related = pairs.Average(p => AppearanceDistance.Between(p.Relative, p.Sibling));
            float strangers = Enumerable.Range(0, pairs.Count - 1).Average(i => AppearanceDistance.Between(pairs[i].Relative, pairs[i + 1].Sibling));
            Assert.That(related, Is.LessThan(strangers * 0.7f), $"related {related:0.00} vs strangers {strangers:0.00}");

            foreach ((CharacterDna relative, CharacterDna sibling) in pairs)
                Assert.That(sibling.Skin.Tone, Is.EqualTo(relative.Skin.Tone).Within(FamilyResemblance.ToneSpread + 1e-4f));

            List<PaletteColour> natural = _catalog.Palette(_catalog.Hair.NaturalPalette).Colours;
            bool Natural(HsvColor c) => natural.Any(n => HsvColor.Distance(HsvColor.FromHex(n.Hex), c) < 0.12f);
            var bothNatural = pairs.Where(p => Natural(p.Relative.Hair.Base) && Natural(p.Sibling.Hair.Base)).ToList();
            Assert.That(bothNatural.Count, Is.GreaterThan(100));
            Assert.That(bothNatural.Average(p => HsvColor.Distance(p.Relative.Hair.Base, p.Sibling.Hair.Base)), Is.LessThan(0.1f), "Natural hair colour runs in the family.");
            Assert.That(pairs.Count(p => p.Relative.Face.PresetId == p.Sibling.Face.PresetId), Is.GreaterThan(pairs.Count * 0.6f));
        }

        [Test]
        public void SiblingsAreStillTheirOwnPeople()
        {
            var pairs = Pairs(300).ToList();
            int ownLook = pairs.Count(p => p.Relative.Style != p.Sibling.Style || p.Relative.Hair.PieceIds[0] != p.Sibling.Hair.PieceIds[0]);
            Assert.That(ownLook, Is.GreaterThan(pairs.Count * 0.7f), "Most siblings have their own style or hairstyle.");
            Assert.That(pairs.Count(p => System.Math.Abs(p.Relative.Body.Femininity - p.Sibling.Body.Femininity) > 0.3f), Is.GreaterThan(pairs.Count / 5),
                "Brothers and sisters, not just twins.");

            CharacterDna again = _generator.GenerateRelative(pairs[5].Relative, 5UL * 104729UL + 11UL);
            Assert.That(DnaSerializer.ToJson(again), Is.EqualTo(DnaSerializer.ToJson(pairs[5].Sibling)), "Deterministic.");
        }

        [Test]
        public void ATownWithFamiliesKeepsStrangersDistinct()
        {
            var generator = new PopulationGenerator(_generator) { FamilyShare = 0.3f };
            Population town = generator.GenerateTown(2026, 40);

            var families = town.Family.GroupBy(f => f).Where(g => g.Count() > 1).ToList();
            Assert.That(families.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(families.Max(g => g.Count()), Is.LessThanOrEqualTo(generator.MaxFamilySize));
            for (int i = 0; i < town.People.Count; i++)
                for (int j = i + 1; j < town.People.Count; j++)
                {
                    float needed = town.Family[i] == town.Family[j] ? generator.MinDistance * 0.5f : generator.MinDistance;
                    Assert.That(AppearanceDistance.Between(town.People[i], town.People[j]), Is.GreaterThanOrEqualTo(needed), $"residents {i} and {j}");
                }
            Assert.That(generator.GenerateTown(2026, 40).People.Select(p => DnaSerializer.ToJson(p)), Is.EqualTo(town.People.Select(p => DnaSerializer.ToJson(p))));

            List<CharacterDna> plain = new PopulationGenerator(_generator).Generate(2026, 40);
            Assert.That(new PopulationGenerator(_generator).GenerateTown(2026, 40).Family, Is.EqualTo(Enumerable.Range(0, 40)), "No families unless asked.");
            Assert.That(plain.Count, Is.EqualTo(40));
        }
    }
}
