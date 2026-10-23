using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using NUnit.Framework;

namespace Game.Characters.Tests.Generation
{
    /// <summary>
    /// Each occasion dresses people in what suits it (decision 0008), on the generated content: gym clothes in their own
    /// fabric or a sportswear one, swimwear in swim fabrics, nobody in tights or socks barefoot (in bed, at the pool),
    /// and a gala in formal fabrics and prints.
    /// </summary>
    public class OccasionTests
    {
        ContentCatalog _catalog;
        List<CharacterDna> _people;

        [OneTimeSetUp]
        public void GenerateTown()
        {
            _catalog = Catalogs.Generated();
            var generator = new CharacterGenerator(_catalog);
            _people = Enumerable.Range(1, 1500).Select(seed => generator.Generate((ulong)seed * 7919, GenerationRequest.Any)).ToList();
        }

        IEnumerable<(PartEntry Part, ItemDna Item)> Worn(string context) =>
            _people.SelectMany(dna => dna.Outfits.Where(o => o.Context == context)).SelectMany(o => o.Items).Select(i => (_catalog.Part(i.ItemId), i));

        FabricEntry Fabric((PartEntry Part, ItemDna Item) worn) => _catalog.FabricOf(worn.Part, worn.Item.FabricId);

        [Test]
        public void GymClothesAreInTheirOwnFabricOrASportyOne()
        {
            int varied = 0;
            foreach (var worn in Worn("sport").Where(w => w.Item.FabricId != null))
            {
                varied++;
                Assert.That(Fabric(worn).Tags, Does.Contain("sporty"), $"{worn.Part.Name} in {worn.Item.FabricId} at the gym");
            }
            Assert.That(varied, Is.GreaterThan(100));
            var notForTheGym = new[] { "faux_fur", "sherpa", "velvet", "quilted", "gloss_puffer", "diamond_quilt" };
            Assert.That(Worn("sport").Where(w => notForTheGym.Contains(Fabric(w)?.Id)).Select(w => $"{w.Part.Name} in {Fabric(w).Id}"), Is.Empty);
        }

        [Test]
        public void SwimwearIsInSwimFabrics()
        {
            var notForThePool = new[] { "velvet", "satin", "vinyl", "patent", "leather", "rubber" };
            List<string> wrong = Worn("swim").Where(w => notForThePool.Contains(Fabric(w)?.Id)).Select(w => $"{w.Part.Name} in {Fabric(w).Id}").ToList();
            Assert.That(wrong, Is.Empty);
        }

        [Test]
        public void NoTightsOrSocksBarefoot()
        {
            List<OutfitContext> barefoot = _catalog.Contexts.Where(c => !c.Shoes).ToList();
            Assert.That(barefoot.Select(c => c.Id), Is.SupersetOf(new[] { "sleep", "swim" }));
            foreach (OutfitContext context in barefoot)
                Assert.That(Worn(context.Id).Where(w => w.Part.Slot == Slots.Legwear).Select(w => w.Part.Name), Is.Empty, context.Id);
        }

        [Test]
        public void GalaClothesAreInFormalFabricsWhereTheyCanBe()
        {
            int checkedItems = 0, ownShoes = 0;
            foreach (var worn in Worn("formal"))
            {
                if (!worn.Part.Fabrics.Select(_catalog.Fabric).Any(f => f != null && f.Tags.Contains("formal"))) continue;
                checkedItems++;
                // Shoes keep their own leather or patent, or are made in a formal fabric.
                if (worn.Part.Slot == Slots.Shoes && worn.Item.FabricId == null)
                {
                    ownShoes++;
                    continue;
                }
                Assert.That(Fabric(worn).Tags, Does.Contain("formal"), $"{worn.Part.Name} in {Fabric(worn).Id} at a gala");
            }
            Assert.That(checkedItems, Is.GreaterThan(1000));
            Assert.That(ownShoes, Is.GreaterThan(100), "Shoes often keep their own leather or patent at a gala.");
        }

        [Test]
        public void GalaPrintsAreFormal()
        {
            List<ItemDna> printed = Worn("formal").Select(w => w.Item).Where(i => i.PatternId != null).ToList();
            Assert.That(printed.Count, Is.GreaterThan(10));
            foreach (ItemDna item in printed)
                Assert.That(_catalog.Patterns.First(p => p.Id == item.PatternId).Tags, Does.Contain("formal"), item.PatternId);
        }
    }
}
