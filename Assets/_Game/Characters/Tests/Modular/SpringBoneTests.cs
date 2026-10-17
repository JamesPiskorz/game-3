using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Modular;
using Game.Characters.Springs;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.Tests.Modular
{
    public class SpringBoneTests
    {
        readonly List<Object> _created = new List<Object>();
        Transform _anchor;

        [TearDown]
        public void DestroyCreated()
        {
            foreach (Object obj in _created)
                if (obj != null) Object.DestroyImmediate(obj);
            _created.Clear();
        }

        [Test]
        public void WithoutForces_AChainStaysAtRest()
        {
            SpringBoneChain chain = CreateChain(Vector3.down);
            chain.Gravity = Vector3.zero;
            List<Vector3> rest = chain.Tails().ToList();

            Simulate(chain, 2f);

            List<Vector3> after = chain.Tails().ToList();
            for (int i = 0; i < rest.Count; i++)
                Assert.That(Vector3.Distance(rest[i], after[i]), Is.LessThan(1e-4f), $"joint {i}");
        }

        [Test]
        public void WhenTheAnchorTurns_TheChainLagsThenSettles()
        {
            SpringBoneChain chain = CreateChain(Vector3.down);
            chain.Gravity = Vector3.zero;

            _anchor.rotation = Quaternion.Euler(0f, 0f, 90f); // the chain's rest direction swings from down to sideways
            Vector3 target = _anchor.position + _anchor.rotation * Vector3.down * 0.3f;
            chain.Advance(1f / 30f);
            float lagging = Vector3.Distance(chain.Tails().Last(), target);
            Simulate(chain, 4f);
            float settled = Vector3.Distance(chain.Tails().Last(), target);

            Assert.That(lagging, Is.GreaterThan(0.1f), "the tip should trail behind a sudden turn");
            Assert.That(settled, Is.LessThan(0.01f), "the tip should come back to rest");
        }

        [Test]
        public void Gravity_BendsAHorizontalChainDown()
        {
            SpringBoneChain chain = CreateChain(Vector3.right);
            chain.Gravity = new Vector3(0f, -9.8f, 0f);
            float restTipY = chain.Tails().Last().y;

            Simulate(chain, 3f);

            Assert.That(chain.Tails().Last().y, Is.LessThan(restTipY - 0.02f));
        }

        [Test]
        public void Colliders_KeepTailsOutside()
        {
            SpringBoneChain chain = CreateChain(Vector3.down);
            var sphere = Track(new GameObject("Collider")).AddComponent<SpringCollider>();
            sphere.transform.position = _anchor.position + new Vector3(0f, -0.2f, 0.03f);
            sphere.Radius = 0.06f;
            chain.Colliders.Add(sphere);

            Simulate(chain, 3f);

            foreach (Vector3 tail in chain.Tails().Skip(1))
                Assert.That(Vector3.Distance(tail, sphere.Centre), Is.GreaterThanOrEqualTo(sphere.Radius + chain.HitRadius - 1e-3f));
        }

        [Test]
        public void Teleporting_SnapsToRestInsteadOfFlailing()
        {
            SpringBoneChain chain = CreateChain(Vector3.down);
            chain.Gravity = Vector3.zero;

            _anchor.position += new Vector3(5f, 0f, 0f);
            chain.Advance(1f / 30f);

            Vector3 target = _anchor.position + Vector3.down * 0.3f;
            Assert.That(Vector3.Distance(chain.Tails().Last(), target), Is.LessThan(1e-3f));
        }

        [Test]
        public void HugeTimeSteps_StayFinite()
        {
            SpringBoneChain chain = CreateChain(Vector3.down);
            _anchor.rotation = Quaternion.Euler(70f, 30f, 10f);

            chain.Advance(100f);

            foreach (Vector3 tail in chain.Tails())
                Assert.That(float.IsNaN(tail.x) || float.IsInfinity(tail.x), Is.False);
        }

        [Test]
        public void Builder_AddsChainsForTailedHair_AndRemovesThemWithIt()
        {
            var content = AssetDatabase.LoadAssetAtPath<CharacterContent>("Assets/_Game/Characters/Content/CharacterContent.asset");
            content.Reload();
            var generator = new CharacterGenerator(content.LoadCatalog());
            var builder = new CharacterBuilder(content);
            CharacterDna dna = generator.Generate(1);
            dna.Hair.PieceIds = new List<string> { "hair.back.twintails_01" };

            ModularCharacter character = builder.Build(dna, "casual");
            Track(character.gameObject);
            Assert.That(character.GetComponents<SpringBoneChain>().Length, Is.EqualTo(2));
            Assert.That(character.GetComponentsInChildren<SpringCollider>().Length, Is.EqualTo(content.SpringColliders.Count));

            dna.Hair.PieceIds = new List<string> { "hair.back.crop_01" };
            builder.Apply(character, dna, "casual");
            Assert.That(character.GetComponents<SpringBoneChain>().Length, Is.EqualTo(0));
        }

        // Frame by frame, as LateUpdate would: one Advance call runs at most a few fixed steps.
        static void Simulate(SpringBoneChain chain, float seconds)
        {
            for (float t = 0f; t < seconds; t += 1f / 60f) chain.Advance(1f / 60f);
        }

        /// <summary>An anchor with a three-bone chain (0.1 apart) pointing along <paramref name="direction"/>.</summary>
        SpringBoneChain CreateChain(Vector3 direction)
        {
            _anchor = Track(new GameObject("Anchor")).transform;
            _anchor.position = new Vector3(0f, 1f, 0f);
            Transform parent = _anchor;
            Transform root = null;
            for (int i = 0; i < 3; i++)
            {
                var bone = new GameObject("Bone" + i).transform;
                bone.SetParent(parent, false);
                bone.localPosition = i == 0 ? Vector3.zero : direction * 0.1f;
                root = root ?? bone;
                parent = bone;
            }
            var chain = _anchor.gameObject.AddComponent<SpringBoneChain>();
            chain.Root = root;
            chain.Initialize();
            return chain;
        }

        T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }
    }
}
