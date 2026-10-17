using System.Linq;
using Game.Characters.Content;
using Game.Characters.Generation;
using NUnit.Framework;

namespace Game.Characters.Tests.Generation
{
    /// <summary>The population-level side of the quality bar (PLAN.md §3.8): what a big batch looks like.</summary>
    public class GeneratorStatsTests
    {
        static GeneratorStats _stats;
        static ContentCatalog _catalog;

        [OneTimeSetUp]
        public void MeasureOnce()
        {
            _catalog = Catalogs.Project();
            _stats = GeneratorStats.Measure(new CharacterGenerator(_catalog), 3000);
        }

        [Test]
        public void EveryPartIsWornBySomeone()
        {
            var unused = _catalog.Parts.Where(p => _stats.Worn(p.Id) == 0).Select(p => p.Id).ToList();
            Assert.That(unused, Is.Empty, "Content nobody wears: check its tags, weight, lean and contexts.");
        }

        [Test]
        public void NoRuleViolationsAndQuickEnough()
        {
            Assert.That(_stats.Violations, Is.Zero, string.Join("\n", _stats.ViolationExamples));
            Assert.That(_stats.MillisecondsPerCharacter, Is.LessThan(5.0), "The quality bar's budget per character.");
        }

        [Test]
        public void SkinTonesCoverTheWholeRamp()
        {
            foreach (int n in _stats.SkinTone)
                Assert.That(n / (float)_stats.Count, Is.InRange(0.06f, 0.14f), "Every tenth of the ramp is about equally common.");
        }

        [Test]
        public void StylesFollowTheirCatalogWeights()
        {
            float total = _catalog.Archetypes.Sum(a => a.Weight);
            foreach (StyleArchetype archetype in _catalog.Archetypes)
            {
                float share = (_stats.Styles.TryGetValue(archetype.Id, out int n) ? n : 0) / (float)_stats.Count;
                Assert.That(share, Is.EqualTo(archetype.Weight / total).Within(0.03f), archetype.Id);
            }
        }

        [Test]
        public void TheReportListsEveryPart()
        {
            string report = _stats.ToMarkdown(_catalog);
            foreach (PartEntry part in _catalog.Parts) Assert.That(report, Does.Contain(part.Id));
            Assert.That(report, Does.Contain("Rule violations: **0**"));
        }
    }
}
