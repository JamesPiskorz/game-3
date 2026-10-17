using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Creator;
using Game.Characters.Dna;
using Game.Characters.Generation;
using NUnit.Framework;

namespace Game.Characters.Tests.Generation
{
    /// <summary>Looks made with other content load anyway, keeping what still fits.</summary>
    public class DnaRepairTests
    {
        static string Json(object o) => Newtonsoft.Json.JsonConvert.SerializeObject(o);

        [Test]
        public void ValidDnaIsLeftAlone()
        {
            var generator = new CharacterGenerator(Catalogs.Project());
            CharacterDna dna = generator.Generate(5);
            Assert.That(DnaRepair.Repair(generator, dna, out List<string> fixes), Is.SameAs(dna));
            Assert.That(fixes, Is.Empty);
        }

        [Test]
        public void ARemovedPartIsReplacedAndTheRestKept()
        {
            CharacterDna dna = new CharacterGenerator(Catalogs.Project()).Generate(5);
            OutfitDna casual = dna.Outfits.First(o => o.Context == "casual");
            ContentCatalog older = Catalogs.Project();
            string removed = casual.Items.Select(i => older.Part(i.ItemId)).First(p => p.Slot == Slots.Shoes || p.Slot == Slots.Top || p.Slot == Slots.OnePiece).Id;
            // A build without that part (from the JSON: the catalog indexes its parts when it loads).
            var json = Newtonsoft.Json.Linq.JObject.Parse(Catalogs.Json);
            ((Newtonsoft.Json.Linq.JArray)json["parts"]).Where(t => (string)t["id"] == removed).ToList().ForEach(t => t.Remove());
            ContentCatalog newer = CatalogSerializer.FromJson(json.ToString());
            var generator = new CharacterGenerator(newer);

            Assert.That(newer.Part(removed), Is.Null, "The newer build doesn't have " + removed);
            Assert.That(DnaValidator.Validate(newer, dna), Is.Not.Empty, "so the old look isn't valid there");
            CharacterDna repaired = DnaRepair.Repair(generator, dna, out List<string> fixes);
            Assert.That(DnaValidator.Validate(newer, repaired), Is.Empty);
            Assert.That(fixes, Is.Not.Empty);
            Assert.That(repaired.Outfits.SelectMany(o => o.Items).Any(i => i.ItemId == removed), Is.False);
            Assert.That(Json(repaired.Hair), Is.EqualTo(Json(dna.Hair)), "Hair untouched.");
            Assert.That(Json(repaired.Face), Is.EqualTo(Json(dna.Face)), "Face untouched.");
            Assert.That(repaired.Skin.Tone, Is.EqualTo(dna.Skin.Tone));
            var kept = casual.Items.Where(i => i.ItemId != removed).Select(i => i.ItemId);
            Assert.That(repaired.Outfits.First(o => o.Context == "casual").Items.Select(i => i.ItemId), Is.SupersetOf(kept.Where(id => newer.Part(id).Slot != Slots.Neck)),
                "The rest of the outfit stays.");
        }

        [Test]
        public void UnknownPresetsStylesAndMissingSlidersAreFilledIn()
        {
            var generator = new CharacterGenerator(Catalogs.Project());
            CharacterDna dna = generator.Generate(8);
            dna.Face.PresetId = "face.preset.gone";
            dna.Face.EyeStyleId = "eye.style.gone";
            string slider = dna.Body.Sliders.Keys.First();
            dna.Body.Sliders.Remove(slider);
            dna.Body.Sliders["body.old_slider"] = 0.5f;
            dna.Style = "retro";

            CharacterDna repaired = DnaRepair.Repair(generator, dna, out List<string> fixes);
            Assert.That(DnaValidator.Validate(generator.Catalog, repaired), Is.Empty);
            Assert.That(repaired.Body.Sliders.ContainsKey(slider));
            Assert.That(repaired.Body.Sliders.ContainsKey("body.old_slider"), Is.False);
            Assert.That(generator.Catalog.Archetype(repaired.Style), Is.Not.Null);
            Assert.That(fixes, Has.Some.Contains("face preset"));
        }

        [Test]
        public void OutfitsForRemovedContextsGoAndNewContextsArrive()
        {
            var generator = new CharacterGenerator(Catalogs.Project());
            CharacterDna dna = generator.Generate(13);
            dna.Outfits.RemoveAll(o => o.Context == "rain");
            dna.Outfits.Add(new OutfitDna { Context = "formal", Items = dna.Outfits[0].Items.ToList() });

            CharacterDna repaired = DnaRepair.Repair(generator, dna, out _);
            Assert.That(repaired.Outfits.Select(o => o.Context), Is.EquivalentTo(generator.Catalog.Contexts.Select(c => c.Id)));
            Assert.That(DnaValidator.Validate(generator.Catalog, repaired), Is.Empty);
        }

        [Test]
        public void TheCreatorLoadsAnOutOfDateShareCode()
        {
            var generator = new CharacterGenerator(Catalogs.Project());
            CharacterDna old = generator.Generate(21);
            old.Hair.PieceIds = new List<string> { "hair.back.gone_01" };
            var session = new CreatorSession(generator, generator.Generate(1));
            Assert.That(session.TryLoad(DnaSerializer.ToShareCode(old), out string note), Is.True);
            Assert.That(note, Does.Contain("Brought up to date"));
            Assert.That(DnaValidator.Validate(generator.Catalog, session.Dna), Is.Empty);
            Assert.That(session.Dna.Skin.Tone, Is.EqualTo(old.Skin.Tone), "Still the same person.");
        }
    }
}
