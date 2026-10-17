using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Expressions;
using Game.Characters.Generation;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.Tests.Modular
{
    /// <summary>Builds generated characters from the project's real content (stand-ins) and checks the result.</summary>
    public class CharacterBuilderTests
    {
        const string ContentPath = "Assets/_Game/Characters/Content/CharacterContent.asset";

        CharacterContent _content;
        ContentCatalog _catalog;
        CharacterGenerator _generator;
        CharacterBuilder _builder;
        readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void LoadContent()
        {
            _content = AssetDatabase.LoadAssetAtPath<CharacterContent>(ContentPath);
            Assert.That(_content != null, "run Game/Characters/Content/Regenerate All Stand-in Content first");
            _content.Reload();
            _catalog = _content.LoadCatalog();
            _generator = new CharacterGenerator(_catalog);
            _builder = new CharacterBuilder(_content);
        }

        [TearDown]
        public void DestroyCharacters()
        {
            foreach (Object obj in _created)
                if (obj != null) Object.DestroyImmediate(obj);
            _created.Clear();
        }

        [Test]
        public void Content_BindsEveryCatalogEntry()
        {
            Assert.That(_content.Validate(), Is.Empty);
        }

        [Test]
        public void Build_WearsTheHairAndTheOutfitForTheContext()
        {
            for (ulong seed = 0; seed < 20; seed++)
            {
                CharacterDna dna = _generator.Generate(seed);
                ModularCharacter character = Build(dna, "casual");

                Assert.That(Worn(character), Is.EquivalentTo(Expected(dna, "casual")), $"seed {seed}");
            }
        }

        [Test]
        public void Apply_ChangesOutfitWithTheContext()
        {
            CharacterDna dna = _generator.Generate(7);
            ModularCharacter character = Build(dna, "casual");

            _builder.Apply(character, dna, "date");

            Assert.That(Worn(character), Is.EquivalentTo(Expected(dna, "date")));
        }

        [Test]
        public void Apply_IsIdempotent()
        {
            CharacterDna dna = _generator.Generate(11);
            ModularCharacter character = Build(dna, "casual");
            List<(Vector3, Quaternion, Vector3)> first = Pose(character);

            _builder.Apply(character, dna, "casual");
            _builder.Apply(character, dna, "casual");

            List<(Vector3, Quaternion, Vector3)> again = Pose(character);
            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(Vector3.Distance(first[i].Item1, again[i].Item1), Is.LessThan(1e-6f));
                Assert.That(Quaternion.Angle(first[i].Item2, again[i].Item2), Is.LessThan(1e-3f));
                Assert.That(Vector3.Distance(first[i].Item3, again[i].Item3), Is.LessThan(1e-6f));
            }
        }

        [Test]
        public void Sliders_DriveBonesAndBlendshapes()
        {
            CharacterDna dna = _generator.Generate(3);
            dna.Body.Sliders["body.height"] = 1f;
            dna.Body.Sliders["body.weight"] = 0.5f;
            ModularCharacter character = Build(dna, "casual");

            Transform armature = character.transform.Find("Armature");
            Assert.That(armature.localScale.y, Is.EqualTo(1.07f).Within(1e-4f));
            int weight = character.Body.sharedMesh.GetBlendShapeIndex("body.weight");
            Assert.That(character.Body.GetBlendShapeWeight(weight), Is.EqualTo(50f).Within(1e-3f));

            dna.Body.Sliders["body.height"] = -1f;
            _builder.Apply(character, dna, "casual");
            Assert.That(armature.localScale.y, Is.EqualTo(0.92f).Within(1e-4f));
        }

        [Test]
        public void MirroredFaceBones_MoveSymmetrically()
        {
            CharacterDna dna = _generator.Generate(5);
            ModularCharacter character = Build(dna, "casual");

            Transform left = Find(character, "Eye.L"), right = Find(character, "Eye.R");
            Assert.That(left.localPosition.x, Is.EqualTo(-right.localPosition.x).Within(1e-5f));
            Assert.That(left.localPosition.y, Is.EqualTo(right.localPosition.y).Within(1e-5f));
            Assert.That(left.localScale, Is.EqualTo(right.localScale));
            Assert.That(left.localEulerAngles.z, Is.EqualTo(Mathf.Repeat(-right.localEulerAngles.z, 360f)).Within(1e-3f));
        }

        [Test]
        public void Colours_ComeFromTheDna()
        {
            CharacterDna dna = _generator.Generate(9);
            ModularCharacter character = Build(dna, "casual");

            Color skin = _content.SkinPalette.Evaluate(dna.Skin.Tone, dna.Skin.Undertone).Base;
            AssertColour(character.BodyMaterial.GetColor(ToonMaterial.BaseColor), skin);

            OutfitDna outfit = dna.Outfits.First(o => o.Context == "casual");
            foreach (ItemDna item in outfit.Items)
            {
                string slot = _catalog.Part(item.ItemId).Slot;
                Color expected = CharacterBuilder.ToColour(item.RegionColors[0]);
                AssertColour(character.PartMaterials(slot)[0].GetColor("_Color1"), expected);
            }
        }

        // ---------------------------------------------------------------- helpers

        [Test]
        public void Expressions_MoveTheFaceWithoutCompoundingWithSliders()
        {
            CharacterDna dna = _generator.Generate(5);
            ModularCharacter character = Build(dna, "casual");
            var face = character.GetComponent<FaceExpression>();
            Transform mouth = character.GetComponentsInChildren<Transform>().First(t => t.name == "Mouth");
            Transform brow = character.GetComponentsInChildren<Transform>().First(t => t.name == "Brow.L");

            face.Snap(); // neutral: takes the base the builder left
            Vector3 restingMouth = mouth.localScale;
            Vector3 restingBrow = brow.localPosition;

            face.Show("surprised", 1f, float.PositiveInfinity);
            face.Snap();
            Vector3 surprisedMouth = mouth.localScale;
            Assert.That(surprisedMouth.y, Is.GreaterThan(restingMouth.y * 2f), "A surprised mouth opens.");
            Assert.That(brow.localPosition.y, Is.GreaterThan(restingBrow.y), "Surprise raises the brows.");

            // Re-applying DNA mid-expression must give the same face, not stack the expression on itself.
            _builder.Apply(character, dna, "date");
            face.Snap();
            Assert.That(Vector3.Distance(mouth.localScale, surprisedMouth), Is.LessThan(1e-5f));

            // The corners curve: up for happy, down for sad.
            Transform corner = character.GetComponentsInChildren<Transform>().First(t => t.name == "Mouth.L");
            Vector3 tip = new Vector3(-0.012f, 0f, 0f);
            face.Show("neutral");
            face.Snap();
            float resting = corner.TransformPoint(tip).y;
            face.Show("happy", 1f, float.PositiveInfinity);
            face.Snap();
            Assert.That(corner.TransformPoint(tip).y, Is.GreaterThan(resting + 0.002f), "A smile lifts the corners.");
            face.Show("sad", 1f, float.PositiveInfinity);
            face.Snap();
            Assert.That(corner.TransformPoint(tip).y, Is.LessThan(resting - 0.002f), "Sadness turns them down.");

            // Shyness brings out a blush; neutral hides it again.
            Transform blush = character.GetComponentsInChildren<Transform>().First(t => t.name == "Blush.L");
            face.Show("neutral");
            face.Snap();
            float hidden = blush.localPosition.z;
            face.Show("shy", 1f, float.PositiveInfinity);
            face.Snap();
            Assert.That(blush.localPosition.z, Is.GreaterThan(hidden + 0.005f), "A shy face blushes.");

            // Delight closes the eyes into arcs.
            Transform arc = character.GetComponentsInChildren<Transform>().First(t => t.name == "EyeSmile.L");
            Transform eye = character.GetComponentsInChildren<Transform>().First(t => t.name == "Eye.L");
            float arcAtRest = arc.localPosition.z;
            face.Show("delighted", 1f, float.PositiveInfinity);
            face.Snap();
            Assert.That(arc.localPosition.z, Is.GreaterThan(arcAtRest + 0.005f), "Delight brings out the closed-eye arcs.");
            Assert.That(eye.localScale.y, Is.LessThan(0.2f), "and shuts the eyes beneath them.");

            face.Show("neutral");
            face.Snap();
            Assert.That(blush.localPosition.z, Is.EqualTo(hidden).Within(1e-6f));
            Assert.That(arc.localPosition.z, Is.EqualTo(arcAtRest).Within(1e-6f));
            Assert.That(Vector3.Distance(mouth.localScale, restingMouth), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(brow.localPosition, restingBrow), Is.LessThan(1e-6f));
        }

        [Test]
        public void IdleMotion_IsRepeatableKeepsFeetPlantedAndSurvivesReapply()
        {
            CharacterDna dna = _generator.Generate(9);
            ModularCharacter character = Build(dna, "casual");
            Poses.Relaxed(character.transform);
            var idle = character.GetComponent<IdleMotion>();
            Transform head = character.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
            Transform foot = character.GetComponentsInChildren<Transform>().First(t => t.name == "Foot.L");

            idle.Pose(1.3f);
            Quaternion first = head.localRotation;
            Vector3 footFirst = foot.position;
            idle.Pose(2.9f);
            Assert.That(Quaternion.Angle(head.localRotation, first), Is.GreaterThan(0.1f), "The head moves over time.");
            Assert.That(Vector3.Distance(foot.position, footFirst), Is.LessThan(1e-5f), "Feet stay planted.");

            idle.Pose(1.3f);
            Assert.That(Quaternion.Angle(head.localRotation, first), Is.LessThan(1e-3f), "Same time, same pose: nothing accumulates.");

            _builder.Apply(character, dna, "date");
            Poses.Relaxed(character.transform);
            idle.Pose(1.3f);
            Assert.That(Quaternion.Angle(head.localRotation, first), Is.LessThan(1e-3f), "Re-applying DNA doesn't shift the motion.");

            // Walking swings the legs; stopping puts them back exactly.
            Transform thigh = character.GetComponentsInChildren<Transform>().First(t => t.name == "UpperLeg.L");
            Quaternion standing = thigh.localRotation;
            idle.Walking = 1f;
            idle.SnapWalking();
            idle.Pose(0.4f);
            Assert.That(Quaternion.Angle(thigh.localRotation, standing), Is.GreaterThan(3f), "Legs swing when walking.");
            idle.Walking = 0f;
            idle.SnapWalking();
            idle.Pose(0.4f);
            Assert.That(Quaternion.Angle(thigh.localRotation, standing), Is.LessThan(1e-3f), "Standing still restores the legs.");

            // Sitting lowers the hips onto a seat and folds the legs; standing up restores them.
            Transform hips = character.GetComponentsInChildren<Transform>().First(t => t.name == "Hips");
            float hipHeight = hips.position.y;
            idle.Sitting = 1f;
            idle.SnapSitting();
            idle.Pose(0.4f);
            Assert.That(hips.position.y, Is.LessThan(hipHeight - 0.3f), "Sitting drops the hips.");
            Assert.That(Quaternion.Angle(thigh.localRotation, standing), Is.GreaterThan(60f), "and brings the thighs forward.");
            Assert.That(foot.position.y, Is.LessThan(0.2f), "Feet stay near the floor.");
            idle.Sitting = 0f;
            idle.SnapSitting();
            idle.Pose(0.4f);
            Assert.That(hips.position.y, Is.EqualTo(hipHeight).Within(1e-4f));
            Assert.That(Quaternion.Angle(thigh.localRotation, standing), Is.LessThan(1e-3f));
        }

        ModularCharacter Build(CharacterDna dna, string context)
        {
            ModularCharacter character = _builder.Build(dna, context);
            _created.Add(character.gameObject);
            return character;
        }

        static IEnumerable<string> Worn(ModularCharacter character) =>
            character.EquippedSlots.Select(slot => character.GetPart(slot).name);

        static IEnumerable<string> Expected(CharacterDna dna, string context) =>
            dna.Hair.PieceIds.Concat(dna.Outfits.First(o => o.Context == context).Items.Select(i => i.ItemId));

        static List<(Vector3, Quaternion, Vector3)> Pose(ModularCharacter character) =>
            character.GetComponentsInChildren<Transform>().Select(t => (t.localPosition, t.localRotation, t.localScale)).ToList();

        static Transform Find(ModularCharacter character, string name) =>
            character.GetComponentsInChildren<Transform>().First(t => t.name == name);

        static void AssertColour(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(2e-3f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(2e-3f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(2e-3f));
        }
    }
}
