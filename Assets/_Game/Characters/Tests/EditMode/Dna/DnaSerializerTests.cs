using System;
using System.Text.RegularExpressions;
using Game.Characters.Dna;
using NUnit.Framework;

namespace Game.Characters.Tests.Dna
{
    public class DnaSerializerTests
    {
        [Test]
        public void SameSeed_ProducesIdenticalJson()
        {
            for (ulong seed = 0; seed < 50; seed++)
                Assert.That(DnaSerializer.ToJson(TestDna.Create(seed)), Is.EqualTo(DnaSerializer.ToJson(TestDna.Create(seed))));
        }

        [Test]
        public void JsonRoundTrip_IsLossless([Values(true, false)] bool indented)
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                string json = DnaSerializer.ToJson(TestDna.Create(seed), indented);
                string again = DnaSerializer.ToJson(DnaSerializer.FromJson(json), indented);
                Assert.That(again, Is.EqualTo(json), $"seed {seed}");
            }
        }

        [Test]
        public void Json_HasStableFormat()
        {
            // The on-disk format. A change here breaks old saves unless DnaMigrations handles it.
            var dna = new CharacterDna { Seed = 42 };
            dna.Body.Femininity = 0.5f;
            dna.Body.Sliders["height"] = 0.25f;
            dna.Skin.Tone = 0.75f;
            dna.Hair.PieceIds.Add("hair.front.bob_01");

            Assert.That(DnaSerializer.ToJson(dna, indented: false), Is.EqualTo(ExpectedCompactJson));
        }

        [Test]
        public void SliderKeys_AreWrittenInOrdinalOrder_AndStaySoAfterLoading()
        {
            var dna = new CharacterDna();
            foreach (string key in new[] { "b", "B", "a", "A", "eye_tilt", "eyeScale" })
                dna.Face.Sliders[key] = 0f;

            string json = DnaSerializer.ToJson(dna, indented: false);
            StringAssert.Contains("\"sliders\":{\"A\":0.0,\"B\":0.0,\"a\":0.0,\"b\":0.0,\"eyeScale\":0.0,\"eye_tilt\":0.0}", json);

            CharacterDna loaded = DnaSerializer.FromJson(json);
            Assert.That(loaded.Face.Sliders.Comparer, Is.SameAs(StringComparer.Ordinal));
            Assert.That(loaded.Body.Sliders.Comparer, Is.SameAs(StringComparer.Ordinal));
        }

        [Test]
        public void Seed_KeepsAll64Bits()
        {
            var dna = new CharacterDna { Seed = ulong.MaxValue - 1 };

            string json = DnaSerializer.ToJson(dna);

            StringAssert.Contains("\"seed\": \"fffffffffffffffe\"", json);
            Assert.That(DnaSerializer.FromJson(json).Seed, Is.EqualTo(ulong.MaxValue - 1));
        }

        [Test]
        public void IndentedJson_UsesLfLineEndings()
        {
            StringAssert.DoesNotContain("\r", DnaSerializer.ToJson(TestDna.Create(1)));
        }

        [Test]
        public void ShareCode_RoundTripsAndIsUrlSafe()
        {
            for (ulong seed = 0; seed < 50; seed++)
            {
                CharacterDna dna = TestDna.Create(seed);
                string code = DnaSerializer.ToShareCode(dna);

                Assert.That(Regex.IsMatch(code, "^dna1\\.[A-Za-z0-9_-]+$"), code);
                Assert.That(DnaSerializer.ToJson(DnaSerializer.FromShareCode("  " + code + "\n")), Is.EqualTo(DnaSerializer.ToJson(dna)));
            }
        }

        [TestCase(null)]
        [TestCase("hello")]
        [TestCase("dna1.!!!")]
        [TestCase("dna1.AAAA")]
        public void ShareCode_RejectsGarbage(string code)
        {
            Assert.Throws<FormatException>(() => DnaSerializer.FromShareCode(code));
        }

        [TestCase("not json")]
        [TestCase("[1, 2, 3]")]
        [TestCase("{\"seed\": \"2a\"}")]
        [TestCase("{\"version\": \"1\"}")]
        [TestCase("{\"version\": 1, \"skin\": {\"tone\": \"dark\"}}")]
        public void FromJson_RejectsMalformedDna(string json)
        {
            Assert.Throws<FormatException>(() => DnaSerializer.FromJson(json));
        }

        [TestCase(0)]
        [TestCase(DnaMigrations.CurrentVersion + 1)]
        public void FromJson_RejectsUnsupportedVersions(int version)
        {
            Assert.Throws<NotSupportedException>(() => DnaSerializer.FromJson("{\"version\": " + version + "}"));
        }

        [Test]
        public void FromJson_KeepsDateLikeStringsVerbatim()
        {
            var dna = new CharacterDna();
            dna.Outfits.Add(new OutfitDna { Context = "2026-10-17T09:00:00Z" });

            CharacterDna loaded = DnaSerializer.FromJson(DnaSerializer.ToJson(dna));

            Assert.That(loaded.Outfits[0].Context, Is.EqualTo("2026-10-17T09:00:00Z"));
        }

        const string ExpectedCompactJson =
            @"{""version"":1,""seed"":""000000000000002a""," +
            @"""body"":{""femininity"":0.5,""sliders"":{""height"":0.25}}," +
            @"""face"":{""sliders"":{},""irisLeft"":{""h"":0.0,""s"":0.0,""v"":0.0},""irisRight"":{""h"":0.0,""s"":0.0,""v"":0.0},""makeup"":[]}," +
            @"""skin"":{""tone"":0.75,""undertone"":0.0}," +
            @"""hair"":{""pieceIds"":[""hair.front.bob_01""],""base"":{""h"":0.0,""s"":0.0,""v"":0.0},""tip"":{""h"":0.0,""s"":0.0,""v"":0.0},""highlight"":{""h"":0.0,""s"":0.0,""v"":0.0},""gradient"":0.0}," +
            @"""outfits"":[]}";
    }
}
