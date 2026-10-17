using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Generation;
using NUnit.Framework;

namespace Game.Characters.Tests.Generation
{
    public class CharacterGeneratorTests
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
        public void SameSeed_GivesIdenticalDna()
        {
            for (ulong seed = 0; seed < 200; seed++)
                Assert.That(Json(_generator.Generate(seed)), Is.EqualTo(Json(new CharacterGenerator(Catalogs.Project()).Generate(seed))), $"seed {seed}");
        }

        [Test]
        public void DifferentSeeds_GiveDifferentCharacters()
        {
            var seen = new HashSet<string>();
            for (ulong seed = 0; seed < 500; seed++)
                Assert.That(seen.Add(Json(_generator.Generate(seed))), $"seed {seed} repeated an earlier character");
        }

        [Test]
        public void TenThousandCharacters_BreakNoRules()
        {
            var failures = new List<string>();
            for (ulong seed = 0; seed < 10000 && failures.Count < 10; seed++)
            {
                List<string> errors = DnaValidator.Validate(_catalog, _generator.Generate(seed * 7919UL + 13UL));
                if (errors.Count > 0) failures.Add($"seed {seed}: {string.Join("; ", errors)}");
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void Generation_TakesUnderFiveMillisecondsPerCharacter()
        {
            for (ulong seed = 0; seed < 50; seed++) _generator.Generate(seed); // warm up
            var clock = Stopwatch.StartNew();
            const int Count = 2000;
            for (ulong seed = 0; seed < Count; seed++) _generator.Generate(seed + 1000);
            double perCharacter = clock.Elapsed.TotalMilliseconds / Count;

            Assert.That(perCharacter, Is.LessThan(5.0), $"{perCharacter:0.000} ms per character");
        }

        [Test]
        public void SkinTones_CoverTheWholeRampEvenly()
        {
            var buckets = new int[10];
            const int Count = 3000;
            for (ulong seed = 0; seed < Count; seed++)
                buckets[Math.Min(9, (int)(_generator.Generate(seed).Skin.Tone * 10f))]++;

            foreach (int bucket in buckets)
                Assert.That(bucket, Is.InRange(Count / 10 * 0.75, Count / 10 * 1.25), string.Join(", ", buckets));
        }

        [Test]
        public void Presentation_FormsTwoClustersAndAnAndrogynousMiddle()
        {
            int feminine = 0, masculine = 0, middle = 0;
            const int Count = 3000;
            for (ulong seed = 0; seed < Count; seed++)
            {
                float femininity = _generator.Generate(seed).Body.Femininity;
                if (femininity > 0.65f) feminine++;
                else if (femininity < 0.35f) masculine++;
                else middle++;
            }

            Assert.That(feminine / (double)Count, Is.InRange(0.38, 0.56));
            Assert.That(masculine / (double)Count, Is.InRange(0.36, 0.54));
            Assert.That(middle / (double)Count, Is.InRange(0.04, 0.18));
        }

        [Test]
        public void EveryArchetypeAndHairPiece_ShowsUp()
        {
            var archetypes = new HashSet<string>();
            var hair = new HashSet<string>();
            for (ulong seed = 0; seed < 3000; seed++)
            {
                CharacterDna dna = _generator.Generate(seed);
                archetypes.Add(dna.Style);
                hair.UnionWith(dna.Hair.PieceIds);
            }

            Assert.That(archetypes, Is.EquivalentTo(_catalog.Archetypes.Select(a => a.Id)));
            Assert.That(hair, Is.EquivalentTo(_catalog.Parts.Where(p => p.Slot.StartsWith("hair.")).Select(p => p.Id)));
        }

        [Test]
        public void Request_IsHonoured()
        {
            var request = new GenerationRequest
            {
                Archetype = "gothic", FemininityMin = 0.7f, FemininityMax = 0.9f, ToneMin = 0.8f, ToneMax = 1f,
                FantasyHair = true, Contexts = new List<string> { "date" },
            };
            List<HsvColor> fantasy = _catalog.Palette("hair.fantasy").Colours.Select(c => HsvColor.FromHex(c.Hex)).ToList();

            for (ulong seed = 0; seed < 300; seed++)
            {
                CharacterDna dna = _generator.Generate(seed, request);
                Assert.That(dna.Style, Is.EqualTo("gothic"));
                Assert.That(dna.Body.Femininity, Is.InRange(0.7f, 0.9f));
                Assert.That(dna.Skin.Tone, Is.InRange(0.8f, 1f));
                Assert.That(dna.Outfits.Select(o => o.Context), Is.EqualTo(new[] { "date" }));
                Assert.That(fantasy.Min(c => HsvColor.Distance(c, dna.Hair.Base)), Is.LessThan(0.12f), "hair should come from the fantasy palette");
            }
        }

        [Test]
        public void TopAndBottom_AlwaysContrast()
        {
            for (ulong seed = 0; seed < 2000; seed++)
                foreach (OutfitDna outfit in _generator.Generate(seed).Outfits)
                {
                    ItemDna top = outfit.Items.FirstOrDefault(i => _catalog.Part(i.ItemId).Slot == Slots.Top);
                    ItemDna bottom = outfit.Items.FirstOrDefault(i => _catalog.Part(i.ItemId).Slot == Slots.Bottom);
                    if (top == null || bottom == null) continue;
                    // The colourer guarantees 0.15 before per-item jitter, which can take back a little.
                    Assert.That(HsvColor.Distance(top.RegionColors[0], bottom.RegionColors[0]), Is.GreaterThan(0.08f), $"seed {seed}");
                }
        }

        [Test]
        public void AddingASlider_DoesNotChangeTheOthers()
        {
            ContentCatalog extended = Catalogs.Project();
            extended.BodySliders.Insert(0, new SliderEntry { Id = "body.new", Min = -1f, Max = 1f, Spread = 0.3f });
            var other = new CharacterGenerator(extended);

            for (ulong seed = 0; seed < 50; seed++)
            {
                CharacterDna before = _generator.Generate(seed), after = other.Generate(seed);
                foreach (SliderEntry slider in _catalog.BodySliders)
                    Assert.That(after.Body.Sliders[slider.Id], Is.EqualTo(before.Body.Sliders[slider.Id]), $"{slider.Id}, seed {seed}");
                Assert.That(Json(after.Hair), Is.EqualTo(Json(before.Hair)));
            }
        }

        [Test]
        public void Reroll_ChangesOnlyTheChosenCategories_AndStaysValid()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                CharacterDna original = _generator.Generate(seed);
                CharacterDna rerolled = _generator.Reroll(original, DnaCategories.Outfits | DnaCategories.Hair, seed + 99999);

                Assert.That(Json(rerolled.Body), Is.EqualTo(Json(original.Body)));
                Assert.That(Json(rerolled.Face), Is.EqualTo(Json(original.Face)));
                Assert.That(Json(rerolled.Skin), Is.EqualTo(Json(original.Skin)));
                Assert.That(rerolled.Style, Is.EqualTo(original.Style));
                Assert.That(DnaValidator.Validate(_catalog, rerolled), Is.Empty, $"seed {seed}");
            }
        }

        [Test]
        public void Reroll_DropsAHatThatNewHairCannotTake()
        {
            CharacterDna dna = Enumerable.Range(0, 5000).Select(s => _generator.Generate((ulong)s, new GenerationRequest { Archetype = "street" }))
                .First(d => d.Outfits.Any(o => o.Items.Any(i => i.ItemId == "acc.hat.beanie_01")));
            dna.Hair.PieceIds[0] = "hair.back.bun_01"; // a bun can't go under a beanie

            CharacterDna repaired = _generator.Reroll(dna, DnaCategories.None, 1);

            Assert.That(repaired.Outfits.SelectMany(o => o.Items).Any(i => i.ItemId == "acc.hat.beanie_01"), Is.False);
            Assert.That(DnaValidator.Validate(_catalog, repaired), Is.Empty);
        }

        [Test]
        public void Population_HasNoLookalikes_AndIsDeterministic()
        {
            var population = new PopulationGenerator(_generator) { MinDistance = 0.9f };
            List<CharacterDna> town = population.Generate(2026, 40);

            for (int i = 0; i < town.Count; i++)
                for (int j = i + 1; j < town.Count; j++)
                    Assert.That(AppearanceDistance.Between(town[i], town[j]), Is.GreaterThanOrEqualTo(0.9f), $"residents {i} and {j}");
            Assert.That(population.Generate(2026, 40).Select(Json), Is.EqualTo(town.Select(Json)));
        }

        static string Json(CharacterDna dna) => DnaSerializer.ToJson(dna, indented: false);

        static string Json(object section) => Newtonsoft.Json.JsonConvert.SerializeObject(section);
    }
}
