using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using NUnit.Framework;

namespace Game.Characters.Tests.Generation
{
    public class CatalogTests
    {
        [Test]
        public void ProjectCatalog_LoadsAndValidates()
        {
            ContentCatalog catalog = Catalogs.Project();

            Assert.That(catalog.Validate(), Is.Empty);
            Assert.That(catalog.Parts, Is.Not.Empty);
            Assert.That(catalog.Archetypes, Is.Not.Empty);
        }

        [Test]
        public void Catalog_RoundTripsThroughJson()
        {
            ContentCatalog catalog = Catalogs.Project();
            string json = CatalogSerializer.ToJson(catalog);

            Assert.That(CatalogSerializer.ToJson(CatalogSerializer.FromJson(json)), Is.EqualTo(json));
        }

        [Test]
        public void UnknownFields_AreErrors()
        {
            Assert.Throws<FormatException>(() => CatalogSerializer.FromJson("{ \"partz\": [] }"));
        }

        [Test]
        public void Validate_ReportsDanglingReferencesAndDuplicates()
        {
            ContentCatalog catalog = Catalogs.Project();
            catalog.Archetypes[0].OutfitPalette = "outfit.missing";
            catalog.Parts.Add(new PartEntry { Id = catalog.Parts[0].Id, Slot = "top" });
            catalog.FacePresets[0].Values["no.such.slider"] = 1f;

            string errors = string.Join("\n", catalog.Validate());

            StringAssert.Contains("unknown palette 'outfit.missing'", errors);
            StringAssert.Contains($"Duplicate part id '{catalog.Parts[0].Id}'", errors);
            StringAssert.Contains("unknown slider 'no.such.slider'", errors);
        }

        [Test]
        public void AFabricTakesTheKindOfPrintItNames()
        {
            var catalog = new ContentCatalog();
            catalog.Patterns.Add(new PatternEntry { Id = "floral", Tags = new List<string> { "cute" } });
            catalog.Patterns.Add(new PatternEntry { Id = "fairisle", Tags = new List<string> { "cute", "knitted" } });
            catalog.Fabrics.Add(new FabricEntry { Id = "jersey" });
            catalog.Fabrics.Add(new FabricEntry { Id = "knit", PrintTags = new List<string> { "knitted" } });
            catalog.Fabrics.Add(new FabricEntry { Id = "lace", Printable = false });
            var sweater = new PartEntry { Id = "sweater", Slot = "top", Patternable = true, Fabrics = new List<string> { "knit", "jersey", "lace" } };

            Assert.That(catalog.PrintsFor(sweater, null).Select(p => p.Id), Is.EqualTo(new[] { "fairisle" }), "A knit takes only knitted prints.");
            Assert.That(catalog.PrintsFor(sweater, "jersey").Select(p => p.Id), Is.EqualTo(new[] { "floral", "fairisle" }));
            Assert.That(catalog.PrintsFor(sweater, "lace"), Is.Empty);
            Assert.That(catalog.TakesPrint(sweater, null, "floral"), Is.False);
            sweater.Patternable = false;
            Assert.That(catalog.PrintsFor(sweater, "jersey"), Is.Empty, "Only parts that take a pattern take a print.");
        }
    }
}
