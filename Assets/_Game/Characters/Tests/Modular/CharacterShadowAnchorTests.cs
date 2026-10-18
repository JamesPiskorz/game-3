using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.Tests.Modular
{
    /// <summary>Graphics plan R2-R3: every toon material of a character samples the environment's shadow at one point by its head.</summary>
    public class CharacterShadowAnchorTests
    {
        CharacterBuilder _builder;
        Game.Characters.Content.ContentCatalog _catalog;
        ModularCharacter _character;

        [SetUp]
        public void Build()
        {
            var content = AssetDatabase.LoadAssetAtPath<CharacterContent>("Assets/_Game/Characters/Content/CharacterContent.asset");
            content.Reload();
            _catalog = content.LoadCatalog();
            _builder = new CharacterBuilder(content);
            _character = _builder.Build(new CharacterGenerator(_catalog).Generate(3), "casual");
        }

        [TearDown]
        public void Destroy() => Object.DestroyImmediate(_character.gameObject);

        [Test]
        public void UntilItRuns_CharactersAreUnshadowed()
        {
            // The portrait studio and review sheets render before any LateUpdate: no stale shadow from elsewhere.
            Assert.That(ToonMaterials().All(m => m.GetVector(ToonMaterial.ShadowAnchor).w < 0f), Is.True);
        }

        [Test]
        public void EveryToonMaterial_SamplesOnePointByTheHead()
        {
            _character.transform.position = new Vector3(4f, 0f, -2f);
            var anchor = _character.GetComponent<CharacterShadowAnchor>();
            anchor.Apply();

            Vector3 head = _character.GetComponentsInChildren<Transform>().First(t => t.name == "Head").position;
            List<Vector4> anchors = ToonMaterials().Select(m => m.GetVector(ToonMaterial.ShadowAnchor)).ToList();
            Assert.That(anchors, Is.Not.Empty);
            Assert.That(anchors.All(a => Vector3.Distance(a, head) < 1e-3f && Mathf.Approximately(a.w, CharacterShadowAnchor.TowardLight)), Is.True,
                "Same point on every material: the character is shadowed as a whole.");

            _character.transform.position += Vector3.right;
            anchor.Apply();
            Assert.That(ToonMaterials().All(m => Mathf.Approximately(m.GetVector(ToonMaterial.ShadowAnchor).x, head.x + 1f)), Is.True, "It follows the character.");
        }

        [Test]
        public void NewParts_PickUpTheAnchorAfterARebuild()
        {
            var anchor = _character.GetComponent<CharacterShadowAnchor>();
            anchor.Apply();
            CharacterDna other = new CharacterGenerator(_catalog).Generate(4);
            _builder.Apply(_character, other, "casual");
            anchor.Apply();
            Assert.That(ToonMaterials().All(m => m.GetVector(ToonMaterial.ShadowAnchor).w > 0f), Is.True);
        }

        [Test]
        public void AMaterialDestroyedWithoutARefresh_IsSkippedThenDropped()
        {
            // In play, a swapped-out part's materials die at the end of the frame; the anchor must not touch them after.
            var anchor = _character.GetComponent<CharacterShadowAnchor>();
            anchor.Apply();
            Material doomed = ToonMaterials().First(m => m != _character.BodyMaterial);
            Renderer owner = _character.GetComponentsInChildren<Renderer>().First(r => r.sharedMaterials.Contains(doomed));
            owner.gameObject.SetActive(false);
            Object.DestroyImmediate(doomed);

            _character.transform.position += Vector3.forward;
            Assert.DoesNotThrow(() => anchor.Apply());
            _character.transform.position += Vector3.forward;
            Assert.DoesNotThrow(() => anchor.Apply());
            IEnumerable<Material> survivors = _character.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && m.shader.name == ToonMaterial.ShaderName);
            Assert.That(survivors.All(m => Mathf.Approximately(m.GetVector(ToonMaterial.ShadowAnchor).z, anchor.Point.z)), Is.True,
                "The survivors still follow the character.");
        }

        IEnumerable<Material> ToonMaterials() =>
            _character.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && m.shader.name == ToonMaterial.ShaderName);
    }
}
