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
                        bool dressed = slots.Contains(Slots.OnePiece) || (slots.Contains(Slots.Top) && slots.Contains(Slots.Bottom));
                        Assert.That(dressed, $"{outfit.Context}, seed {seed}: {string.Join(", ", slots)}");
                        if (catalog.Context(outfit.Context).Shoes) Assert.That(slots, Does.Contain(Slots.Shoes), $"{outfit.Context}, seed {seed}");
                        // Swimwear and lingerie only where they belong.
                        foreach (ItemDna item in outfit.Items)
                        {
                            PartEntry part = catalog.Part(item.ItemId);
                            if (part.Tags.Contains("swim") && part.Tags.All(t => t == "swim")) Assert.That(outfit.Context, Is.EqualTo("swim"), part.Id);
                            if (part.Tags.Contains("sleep") && part.Tags.All(t => t == "sleep")) Assert.That(outfit.Context, Is.EqualTo("sleep"), part.Id);
                        }
                    }
                }
        }

        [Test]
        public void GownsHaveAnOccasionAndUniformsGoToWork()
        {
            ContentCatalog catalog = Catalogs.Generated();
            OutfitContext formal = catalog.Context("formal");
            Assert.That(formal, Is.Not.Null, "Gowns and the wedding dress need an occasion to be worn to.");
            Assert.That(formal.OnlyTags, Is.EquivalentTo(new[] { "formal" }));
            Assert.That(catalog.Context("work").OnlyTags, Does.Contain("uniform"));

            // Scrub trousers come only with a scrub top.
            var generator = new CharacterGenerator(catalog);
            for (ulong seed = 1; seed <= 300; seed++)
                foreach (OutfitDna outfit in generator.Generate(seed * 104729, GenerationRequest.Any).Outfits)
                {
                    List<PartEntry> worn = outfit.Items.Select(i => catalog.Part(i.ItemId)).ToList();
                    foreach (PartEntry part in worn)
                        Assert.That(part.RequiresTags.All(tag => worn.Any(other => other != part && other.Tags.Contains(tag))),
                                    $"{part.Id} in {outfit.Context}, seed {seed}");
                }
        }

        [Test]
        public void TallBootsAreNeverWornWithAnkleLengthTrousers()
        {
            ContentCatalog catalog = Catalogs.Generated();
            List<PartEntry> tall = catalog.PartsInSlot(Slots.Shoes).Where(p => p.ExcludesTags.Contains("long_legs")).ToList();
            Assert.That(tall, Is.Not.Empty, "Tall boots exclude ankle-length trousers (their shafts came through them).");
            var generator = new CharacterGenerator(catalog);
            for (ulong seed = 1; seed <= 400; seed++)
                foreach (OutfitDna outfit in generator.Generate(seed * 6151, GenerationRequest.Any).Outfits)
                {
                    List<PartEntry> worn = outfit.Items.Select(i => catalog.Part(i.ItemId)).ToList();
                    if (worn.Any(tall.Contains))
                        Assert.That(worn.SelectMany(p => p.Tags), Has.None.EqualTo("long_legs"), $"{outfit.Context}, seed {seed}");
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
