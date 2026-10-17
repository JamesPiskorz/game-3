using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using Game.Characters.Springs;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Characters.Tests.Modular
{
    public class CharacterLodTests
    {
        ModularCharacter _character;
        CharacterLod _lod;

        [SetUp]
        public void Build()
        {
            var content = AssetDatabase.LoadAssetAtPath<CharacterContent>("Assets/_Game/Characters/Content/CharacterContent.asset");
            content.Reload();
            CharacterDna dna = new CharacterGenerator(content.LoadCatalog()).Generate(3);
            dna.Hair.PieceIds = new List<string> { "hair.back.ponytail_01" };
            // Earrings, so there's a small part to test.
            OutfitDna casual = dna.Outfits.First(o => o.Context == "casual");
            casual.Items.RemoveAll(i => i.ItemId.StartsWith("acc.earrings"));
            casual.Items.Add(new ItemDna { ItemId = "acc.earrings.hoops_01", RegionColors = { new HsvColor(0.12f, 0.5f, 0.9f) } });
            _character = new CharacterBuilder(content).Build(dna, "casual");
            _lod = _character.GetComponent<CharacterLod>();
        }

        [TearDown]
        public void Destroy() => Object.DestroyImmediate(_character.gameObject);

        [Test]
        public void NearCharacters_KeepFullDetail()
        {
            _lod.Apply(3f);

            Assert.That(Materials().All(m => m.GetShaderPassEnabled("SRPDefaultUnlit")), Is.True);
            Assert.That(Renderers().Where(r => !Small(r)).All(r => r.shadowCastingMode == ShadowCastingMode.On), Is.True);
            Assert.That(Chains().All(c => c.enabled), Is.True);
        }

        [Test]
        public void SmallParts_CastNoShadowAndAreOutlinedOnlyCloseUp()
        {
            List<Renderer> small = Renderers().Where(Small).ToList();
            Assert.That(small, Is.Not.Empty);
            _lod.Apply(3f);
            Assert.That(small.All(r => r.shadowCastingMode == ShadowCastingMode.Off), "Too small to need a shadow.");
            Assert.That(small.SelectMany(r => r.sharedMaterials).All(m => m.GetShaderPassEnabled("SRPDefaultUnlit")), "Outlined in a close-up.");

            _lod.Apply(_lod.SmallOutlineDistance + 3f);
            Assert.That(small.SelectMany(r => r.sharedMaterials).Any(m => m.GetShaderPassEnabled("SRPDefaultUnlit")), Is.False, "Not at conversation distance.");
            Assert.That(Renderers().Where(r => !Small(r)).SelectMany(r => r.sharedMaterials).All(m => m.GetShaderPassEnabled("SRPDefaultUnlit")),
                "Everything else keeps its outline.");
        }

        [Test]
        public void FarCharacters_DropOutlinesSpringsAndShadows()
        {
            _lod.Apply(40f);

            Assert.That(Materials().Any(m => m.GetShaderPassEnabled("SRPDefaultUnlit")), Is.False);
            Assert.That(Renderers().All(r => r.shadowCastingMode == ShadowCastingMode.Off), Is.True);
            Assert.That(Chains().Any(c => c.enabled), Is.False);
        }

        [Test]
        public void Thresholds_HaveHysteresis()
        {
            _lod.Apply(3f);
            _lod.Apply(_lod.OutlineDistance + 0.5f);
            Assert.That(_lod.Outlines, Is.True, "just past the threshold keeps the current state");
            _lod.Apply(_lod.OutlineDistance + 1.5f);
            Assert.That(_lod.Outlines, Is.False);
            _lod.Apply(_lod.OutlineDistance - 0.5f);
            Assert.That(_lod.Outlines, Is.False, "just inside the threshold keeps the current state");
            _lod.Apply(_lod.OutlineDistance - 1.5f);
            Assert.That(_lod.Outlines, Is.True);
        }

        IEnumerable<Renderer> Renderers() => _character.GetComponentsInChildren<Renderer>();
        static bool Small(Renderer renderer) => renderer.GetComponentInParent<CharacterPart>()?.Small == true;
        IEnumerable<Material> Materials() => Renderers().SelectMany(r => r.sharedMaterials).Where(m => m != null);
        IEnumerable<SpringBoneChain> Chains() => _character.GetComponents<SpringBoneChain>();
    }
}
