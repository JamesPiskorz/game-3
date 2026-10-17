using System.Linq;
using Game.Characters.Content;
using Game.Characters.Creator;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Tests.Generation;
using NUnit.Framework;

namespace Game.Characters.Tests.Creator
{
    public class CreatorSessionTests
    {
        ContentCatalog _catalog;
        CharacterGenerator _generator;
        CreatorSession _session;
        int _changes;

        [SetUp]
        public void CreateSession()
        {
            _catalog = Catalogs.Project();
            _generator = new CharacterGenerator(_catalog);
            _session = new CreatorSession(_generator, _generator.Generate(42), sessionSeed: 7);
            _session.Changed += () => _changes++;
            _changes = 0;
        }

        [Test]
        public void BodySliders_ClampToTheSafeRange()
        {
            SliderEntry head = _catalog.BodySliders.First(s => s.Id == "body.head");

            _session.SetBodySlider("body.head", 5f);

            Assert.That(_session.BodySlider("body.head"), Is.EqualTo(head.Max));
            Assert.That(_changes, Is.EqualTo(1));
        }

        [Test]
        public void FaceSliders_AreEditedAsFinalValues_AndStoredAsPresetOffsets()
        {
            _session.SetFaceSlider("eye.scale", 0.5f);

            Assert.That(_session.FaceSlider("eye.scale"), Is.EqualTo(0.5f).Within(1e-5f));
            float preset = _catalog.FacePresets.First(p => p.Id == _session.Dna.Face.PresetId).Values.TryGetValue("eye.scale", out float v) ? v : 0f;
            Assert.That(_session.Dna.Face.Sliders["eye.scale"], Is.EqualTo(0.5f - preset).Within(1e-5f));
        }

        [Test]
        public void ChangingPreset_KeepsTweaksWithinRange()
        {
            foreach (FacePreset preset in _catalog.FacePresets)
            {
                _session.SetFaceSlider("eye.scale", 0.6f);
                _session.SetFacePreset(preset.Id);
                Assert.That(DnaValidator.Validate(_catalog, _session.Dna), Is.Empty, preset.Id);
            }
        }

        [Test]
        public void UndoAndRedo_WalkTheHistory()
        {
            string original = Json(_session.Dna);
            _session.SetSkin(0.9f, 0.1f);
            string edited = Json(_session.Dna);

            Assert.That(_session.Undo(), Is.True);
            Assert.That(Json(_session.Dna), Is.EqualTo(original));
            Assert.That(_session.Redo(), Is.True);
            Assert.That(Json(_session.Dna), Is.EqualTo(edited));
            Assert.That(_session.Redo(), Is.False);
        }

        [Test]
        public void ASliderDrag_IsOneUndoStep()
        {
            string original = Json(_session.Dna);
            for (int i = 1; i <= 20; i++) _session.SetBodySlider("body.weight", i / 20f);
            _session.EndGesture();
            _session.SetBodySlider("body.weight", 0.3f);

            Assert.That(_session.Undo(), Is.True);
            Assert.That(_session.BodySlider("body.weight"), Is.EqualTo(1f), "undo should step back over the second drag only");
            Assert.That(_session.Undo(), Is.True);
            Assert.That(Json(_session.Dna), Is.EqualTo(original));
            Assert.That(_session.CanUndo, Is.False);
        }

        [Test]
        public void NoOpEdits_LeaveNoHistory()
        {
            _session.SetSkin(_session.Dna.Skin.Tone, _session.Dna.Skin.Undertone);

            Assert.That(_session.CanUndo, Is.False);
            Assert.That(_changes, Is.EqualTo(0));
        }

        [Test]
        public void Randomize_LeavesLockedCategoriesAlone()
        {
            _session.Locks = DnaCategories.Hair | DnaCategories.Skin;
            string hair = Newtonsoft.Json.JsonConvert.SerializeObject(_session.Dna.Hair);
            float tone = _session.Dna.Skin.Tone;
            string outfits = Newtonsoft.Json.JsonConvert.SerializeObject(_session.Dna.Outfits);

            _session.Randomize();

            Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(_session.Dna.Hair), Is.EqualTo(hair));
            Assert.That(_session.Dna.Skin.Tone, Is.EqualTo(tone));
            Assert.That(Newtonsoft.Json.JsonConvert.SerializeObject(_session.Dna.Outfits), Is.Not.EqualTo(outfits));
            Assert.That(DnaValidator.Validate(_catalog, _session.Dna), Is.Empty);
        }

        [Test]
        public void Randomize_IsRepeatableForTheSameSession()
        {
            var other = new CreatorSession(_generator, _generator.Generate(42), sessionSeed: 7);
            for (int i = 0; i < 5; i++)
            {
                _session.Randomize();
                other.Randomize();
                Assert.That(Json(_session.Dna), Is.EqualTo(Json(other.Dna)));
            }
        }

        [Test]
        public void SwappingSeparatesForADress_AndBack_StaysWearable()
        {
            _session.SetContext("date");

            Assert.That(_session.SetItem(Slots.OnePiece, "cloth.onepiece.dress_01", out string problem), Is.True, problem);
            Assert.That(_session.Item(Slots.Top), Is.Null);
            Assert.That(_session.Item(Slots.Bottom), Is.Null);

            Assert.That(_session.SetItem(Slots.Top, "cloth.top.longsleeve_01", out problem), Is.True, problem);
            Assert.That(_session.Item(Slots.OnePiece), Is.Null);
            Assert.That(_session.Item(Slots.Bottom), Is.Not.Null, "a bottom should be added when the dress comes off");
            Assert.That(DnaValidator.Validate(_catalog, _session.Dna), Is.Empty);
        }

        [Test]
        public void Edits_ThatBreakRules_AreRefused()
        {
            _session.SetContext("casual");
            Assert.That(_session.SetHairPiece(Slots.HairBack, "hair.back.bun_01", out string problem), Is.True, problem);
            string before = Json(_session.Dna);

            Assert.That(_session.SetItem(Slots.Hat, "acc.hat.beanie_01", out problem), Is.False);
            StringAssert.Contains("conflicts", problem);
            Assert.That(_session.SetItem(Slots.Top, null, out _), Is.False, "the top is required");
            Assert.That(_session.SetHairPiece(Slots.HairBack, null, out _), Is.False, "hair needs a back piece");
            Assert.That(Json(_session.Dna), Is.EqualTo(before));
        }

        [Test]
        public void ShareCodes_RoundTrip_AndBadCodesAreReported()
        {
            string code = _session.ShareCode;
            var other = new CreatorSession(_generator, _generator.Generate(1));

            Assert.That(other.TryLoad(code, out string problem), Is.True, problem);
            Assert.That(Json(other.Dna), Is.EqualTo(Json(_session.Dna)));
            Assert.That(other.TryLoad("dna1.garbage", out problem), Is.False);
            Assert.That(problem, Is.Not.Empty);
        }

        static string Json(CharacterDna dna) => DnaSerializer.ToJson(dna, indented: false);
    }
}
