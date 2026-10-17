using Game.Characters.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace Game.Characters.Tests.Modular
{
    public class SkinTonePaletteTests
    {
        SkinTonePalette _palette;

        [SetUp]
        public void CreatePalette()
        {
            _palette = ScriptableObject.CreateInstance<SkinTonePalette>();
            _palette.Stops = new[] { Stop(Color.red), Stop(Color.green), Stop(Color.blue) };
            _palette.UndertoneHueShift = 0.02f;
        }

        [TearDown]
        public void DestroyPalette() => Object.DestroyImmediate(_palette);

        [Test]
        public void Evaluate_HitsEachStopExactly()
        {
            AssertColour(_palette.Evaluate(0f, 0.5f).Base, Color.red);
            AssertColour(_palette.Evaluate(0.5f, 0.5f).Base, Color.green);
            AssertColour(_palette.Evaluate(1f, 0.5f).Base, Color.blue);
        }

        [Test]
        public void Evaluate_BlendsBetweenNeighbouringStops()
        {
            AssertColour(_palette.Evaluate(0.25f, 0.5f).Shade, Color.Lerp(Color.red, Color.green, 0.5f) * 0.5f);
        }

        [Test]
        public void Evaluate_ClampsToneToTheRamp()
        {
            AssertColour(_palette.Evaluate(-3f, 0.5f).Base, Color.red);
            AssertColour(_palette.Evaluate(7f, 0.5f).Base, Color.blue);
        }

        [Test]
        public void Undertone_WarmRaisesHue_CoolLowersIt()
        {
            _palette.Stops = new[] { Stop(new Color(1f, 0.5f, 0f)) };
            Color.RGBToHSV(new Color(1f, 0.5f, 0f), out float neutral, out _, out _);

            Color.RGBToHSV(_palette.Evaluate(0f, 1f).Base, out float warm, out _, out _);
            Color.RGBToHSV(_palette.Evaluate(0f, 0f).Base, out float cool, out _, out _);

            Assert.That(warm, Is.EqualTo(neutral + 0.02f).Within(1e-3f));
            Assert.That(cool, Is.EqualTo(neutral - 0.02f).Within(1e-3f));
        }

        static SkinTonePalette.Stop Stop(Color colour) => new SkinTonePalette.Stop { Base = colour, Shade = colour * 0.5f, Rim = Color.white };

        static void AssertColour(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(1e-3f), $"r of {actual}");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(1e-3f), $"g of {actual}");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(1e-3f), $"b of {actual}");
        }
    }
}
