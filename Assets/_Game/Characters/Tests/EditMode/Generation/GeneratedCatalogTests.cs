using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using NUnit.Framework;

namespace Game.Characters.Tests.Generation
{
    /// <summary>
    /// The game on generated content (Content/GeneratedCatalog.json, decision 0008): the catalog is valid, generating
    /// people from it never fails and dresses them only in generated parts, and a look made with the stand-ins loads.
    /// </summary>
    public class GeneratedCatalogTests
    {
        [Test]
        public void TheGeneratedCatalogIsValidAndHasOnlyGeneratedParts()
        {
            ContentCatalog catalog = Catalogs.Generated();
            Assert.That(catalog.Validate(), Is.Empty);
            Assert.That(catalog.Parts, Is.Not.Empty);
            Assert.That(catalog.Parts.Select(p => p.Id), Has.All.Contains("_gen"));
            Assert.That(catalog.Parts.Select(p => p.Name), Has.None.Null.And.None.Empty, "The menu and the town show part names.");
        }

        [Test]
        public void GeneratingFromItNeverFails()
        {
            ContentCatalog catalog = Catalogs.Generated();
            var generator = new CharacterGenerator(catalog);
            var requests = new List<GenerationRequest> { GenerationRequest.Any };
            requests.AddRange(catalog.Archetypes.Select(a => new GenerationRequest { Archetype = a.Id }));
            requests.Add(new GenerationRequest { FemininityMin = 0f, FemininityMax = 0f });
            requests.Add(new GenerationRequest { FemininityMin = 1f, FemininityMax = 1f });
            foreach (GenerationRequest request in requests)
                for (ulong seed = 1; seed <= 300; seed++)
                {
                    CharacterDna dna = generator.Generate(seed * 7919, request);
                    Assert.That(DnaValidator.Validate(catalog, dna), Is.Empty, $"seed {seed}, archetype {request.Archetype}");
                    Assert.That(dna.Hair.PieceIds.Select(catalog.Part).Any(p => p.Slot == Slots.HairBack));
                    foreach (OutfitDna outfit in dna.Outfits)
                    {
                        string[] slots = outfit.Items.Select(i => catalog.Part(i.ItemId).Slot).ToArray();
                        Assert.That(slots, Does.Contain(Slots.Top).And.Contain(Slots.Bottom).And.Contain(Slots.Shoes), $"{outfit.Context}, seed {seed}");
                    }
                }
        }

        [Test]
        public void ALookMadeWithTheStandInsLoads()
        {
            var standIn = new CharacterGenerator(Catalogs.Project());
            ContentCatalog catalog = Catalogs.Generated();
            var generator = new CharacterGenerator(catalog);
            for (ulong seed = 1; seed <= 50; seed++)
            {
                CharacterDna saved = standIn.Generate(seed);
                CharacterDna loaded = DnaRepair.Repair(generator, saved, out List<string> fixes);
                Assert.That(DnaValidator.Validate(catalog, loaded), Is.Empty, $"seed {seed}: " + string.Join("; ", fixes));
                Assert.That(loaded.Hair.PieceIds.Concat(loaded.Outfits.SelectMany(o => o.Items).Select(i => i.ItemId)).Select(catalog.Part), Has.None.Null);
                Assert.That(loaded.Seed, Is.EqualTo(saved.Seed));
                Assert.That(loaded.Body.Femininity, Is.EqualTo(saved.Body.Femininity), "Same presentation.");
                Assert.That(loaded.Skin.Tone, Is.EqualTo(saved.Skin.Tone), "Same skin.");
                Assert.That(loaded.Face.PresetId, Is.EqualTo(saved.Face.PresetId), "Same face.");
            }
        }
    }
}
