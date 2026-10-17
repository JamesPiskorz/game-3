using System;
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
    }
}
