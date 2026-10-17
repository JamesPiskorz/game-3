using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Creator;
using Game.Characters.Dna;
using Game.Characters.Generation;
using NUnit.Framework;

namespace Game.Characters.Tests.Generation
{
    /// <summary>Textured styles (an afro, puffs): how often, and what they don't go with.</summary>
    public class TexturedHairTests
    {
        CharacterGenerator _generator;
        ContentCatalog _catalog;

        [SetUp]
        public void CreateGenerator()
        {
            _catalog = Catalogs.Project();
            _generator = new CharacterGenerator(_catalog);
        }

        [Test]
        public void TexturedStylesAreAboutOneInEightAndTakeNoBangsHatsOrClips()
        {
            int textured = 0, afros = 0;
            const int Count = 3000;
            for (ulong seed = 0; seed < Count; seed++)
            {
                CharacterDna dna = _generator.Generate(seed);
                List<PartEntry> hair = dna.Hair.PieceIds.Select(_catalog.Part).ToList();
                if (!hair.Any(p => p.Tags.Contains(HairTags.Textured))) continue;
                textured++;
                if (hair.Any(p => p.Tags.Contains(HairTags.Big)))
                {
                    afros++;
                    Assert.That(hair.Any(p => p.Slot == Slots.HairFront), Is.False, $"seed {seed}: an afro has no bangs");
                    foreach (OutfitDna outfit in dna.Outfits)
                        Assert.That(outfit.Items.Select(i => _catalog.Part(i.ItemId).Slot), Has.None.EqualTo(Slots.Hat).And.None.EqualTo(Slots.HairAccessory), $"seed {seed}");
                }
                Assert.That(DnaValidator.Validate(_catalog, dna), Is.Empty, $"seed {seed}");
            }
            Assert.That(textured / (float)Count, Is.InRange(0.08f, 0.16f));
            Assert.That(afros, Is.GreaterThan(textured / 4));
        }

        [Test]
        public void ChoosingAnAfroInTheCreatorTakesTheBangsOff()
        {
            CharacterDna start = Enumerable.Range(0, 200).Select(s => _generator.Generate((ulong)s)).First(d => d.Hair.PieceIds.Count == 2);
            var session = new CreatorSession(_generator, start);
            Assert.That(session.SetHairPiece(Slots.HairBack, "hair.back.afro_01", out string problem), Is.True, problem);
            Assert.That(session.Dna.Hair.PieceIds, Is.EqualTo(new[] { "hair.back.afro_01" }));
            Assert.That(session.SetHairPiece(Slots.HairFront, "hair.front.blunt_01", out problem), Is.False, "Bangs don't go on an afro.");
        }
    }
}
