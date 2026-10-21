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
        public void FaceMarks_StayOnWhateverTheOutfit_AndLipstickColoursTheLips()
        {
            CharacterDna dna = _generator.Generate(4);
            dna.Face.Makeup = new List<OverlayDna>
            {
                new OverlayDna { OverlayId = "face.mark.freckles_01" },
                new OverlayDna { OverlayId = Makeup.Lipstick, Tint = new HsvColor(0.98f, 0.8f, 0.7f) },
            };
            ModularCharacter character = Build(dna, "casual");
            Assert.That(Worn(character), Does.Contain("face.mark.freckles_01"));
            _builder.Apply(character, dna, "date");
            Assert.That(Worn(character), Does.Contain("face.mark.freckles_01"), "Freckles aren't part of an outfit.");
            Color lips = character.BodyMaterial.GetColor("_Color4");
            Color expected = CharacterBuilder.ToColour(new HsvColor(0.98f, 0.8f, 0.7f));
            Assert.That(Vector4.Distance(lips, expected), Is.LessThan(0.02f), "Lipstick is the lips' colour.");

            dna.Face.Makeup.Clear();
            _builder.Apply(character, dna, "date");
            Assert.That(Worn(character), Has.None.StartsWith("face."));
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
        public void Expressions_MoveTheGeneratedFaceAlongTheCharactersAxes()
        {
            // The generated rig's face bones rest pointing out of the face (Blender keeps bone roll), and its masculine
            // shape moves the features off them. Neither may change what an expression does.
            CharacterContent content = GeneratedContent();
            CharacterDna dna = new CharacterGenerator(content.LoadCatalog())
                .Generate(5, new GenerationRequest { FemininityMin = 0f, FemininityMax = 0f });
            ModularCharacter character = new CharacterBuilder(content).Build(dna, "casual");
            _created.Add(character.gameObject);
            var face = character.GetComponent<FaceExpression>();
            Transform mouth = Find(character, "Mouth"), corner = Find(character, "Mouth.L"), eye = Find(character, "Eye.L");
            Vector3 up = character.transform.up, forward = character.transform.forward;

            float Middle(bool shaped = true)
            {
                List<float> heights = Features(character, mouth, shaped).Select(p => Vector3.Dot(p, up)).ToList();
                return (heights.Min() + heights.Max()) / 2f;
            }
            Vector3 outward = (corner.position - mouth.position).normalized;
            int tip = 0; // the corner's outermost point
            Vector3[] cornerAtRest = Features(character, corner, true);
            for (int i = 1; i < cornerAtRest.Length; i++)
                if (Vector3.Dot(cornerAtRest[i], outward) > Vector3.Dot(cornerAtRest[tip], outward)) tip = i;
            float Curve() => Vector3.Dot(Features(character, corner, true)[tip], up) - Middle();

            face.Snap();
            Matrix4x4 mouthRest = mouth.localToWorldMatrix, eyeRest = eye.localToWorldMatrix;
            float restingMiddle = Middle(), restingCurve = Curve();
            Assert.That(Mathf.Abs(restingMiddle - Middle(shaped: false)), Is.GreaterThan(0.0005f),
                "The masculine shape should move the mouth off its bone, or this test checks nothing.");

            face.Show("surprised", 1f, float.PositiveInfinity);
            face.Snap();
            Assert.That(Moved(mouth, mouthRest).MultiplyVector(up).magnitude, Is.GreaterThan(3f), "A surprised mouth opens up and down,");
            Assert.That(Moved(mouth, mouthRest).MultiplyVector(forward).magnitude, Is.EqualTo(1f).Within(1e-3f), "not out of the face,");
            Assert.That(Middle(), Is.EqualTo(restingMiddle).Within(0.002f), "and stays where the shape put it.");

            face.Show("happy", 1f, float.PositiveInfinity);
            face.Snap();
            Assert.That(Curve(), Is.GreaterThan(restingCurve + 0.002f), "A smile lifts the corners.");
            face.Show("sad", 1f, float.PositiveInfinity);
            face.Snap();
            Assert.That(Curve(), Is.LessThan(restingCurve - 0.002f), "Sadness turns them down.");

            face.Show("delighted", 1f, float.PositiveInfinity);
            face.Snap();
            Assert.That(Moved(eye, eyeRest).MultiplyVector(up).magnitude, Is.LessThan(0.2f), "Delight shuts the eyes.");
        }

        [Test]
        public void SlidersAndStyles_ActAlongTheCharactersAxesOnTheGeneratedRig()
        {
            // The generated rig's bones keep Blender's roll: its face bones point out of the face, its armature is z-up
            // and its legs point down. Sliders and styles must still stretch, turn and move along the character's axes.
            CharacterContent content = GeneratedContent();
            var builder = new CharacterBuilder(content);
            CharacterDna dna = new CharacterGenerator(content.LoadCatalog())
                .Generate(5, new GenerationRequest { FemininityMin = 1f, FemininityMax = 1f }); // no shape moves the face
            Plain(dna);
            ModularCharacter character = builder.Build(dna, "casual");
            _created.Add(character.gameObject);
            Transform eye = Find(character, "Eye.L"), brow = Find(character, "Brow.L");
            Transform hips = Find(character, "Hips"), foot = Find(character, "Foot.L");
            Matrix4x4 eyeRest = eye.localToWorldMatrix, browRest = brow.localToWorldMatrix;
            Vector3 hipsRest = hips.position, footRest = foot.position;
            Vector3 up = character.transform.up, forward = character.transform.forward;

            dna.Face.EyeStyleId = "eye.style.tall"; // 0.92 wide, 1.2 tall
            dna.Face.Sliders["brow.tilt"] = 1f; // 16 degrees
            dna.Body.Sliders["body.legs"] = 1f;
            builder.Apply(character, dna, "casual");

            Matrix4x4 eyeMoved = Moved(eye, eyeRest), browMoved = Moved(brow, browRest);
            Assert.That(eyeMoved.MultiplyVector(up).magnitude, Is.EqualTo(1.2f).Within(1e-3f), "Tall eyes are taller,");
            Assert.That(eyeMoved.MultiplyVector(forward).magnitude, Is.EqualTo(1f).Within(1e-3f), "not deeper.");
            Assert.That(Vector3.Angle(browMoved.MultiplyVector(forward), forward), Is.LessThan(0.1f), "Brows tilt in the face's plane,");
            Assert.That(Vector3.Angle(browMoved.MultiplyVector(up), up), Is.EqualTo(16f).Within(0.1f), "by the slider's angle.");
            Vector3 lift = hips.position - hipsRest;
            Assert.That(Vector3.Dot(lift, up), Is.GreaterThan(0.035f), "Longer legs raise the hips");
            Assert.That(Vector3.ProjectOnPlane(lift, up).magnitude, Is.LessThan(1e-3f), "straight up,");
            Assert.That(Mathf.Abs(Vector3.Dot(foot.position - footRest, up)), Is.LessThan(5e-4f), "and lower the knees as far, so the feet stay down.");
        }

        [Test]
        public void FaceSlidersAndStyles_StretchAndTurnTheGeneratedFeaturesAboutThemselves()
        {
            // The masculine shape sets the generated face's features centimetres behind their bones. A bigger, tilted eye
            // or a thicker brow must move the feature only as it would where the bone sits in it (the feminine face):
            // stretched about the bone, the masculine one would sink into the head or come out of it.
            CharacterContent content = GeneratedContent();
            var builder = new CharacterBuilder(content);
            CharacterDna dna = new CharacterGenerator(content.LoadCatalog())
                .Generate(5, new GenerationRequest { FemininityMin = 0f, FemininityMax = 0f });
            Plain(dna);
            ModularCharacter character = builder.Build(dna, "casual");
            _created.Add(character.gameObject);
            Transform eye = Find(character, "Eye.L"), brow = Find(character, "Brow.L");

            var rest = new Dictionary<Transform, (Matrix4x4 Bone, Vector3 At, Vector3 Unshaped)>();
            foreach (Transform bone in new[] { eye, brow })
                rest[bone] = (bone.localToWorldMatrix, Centre(Features(character, bone, true)), Centre(Features(character, bone, false)) - bone.position);
            float eyeSize = Spread(Features(character, eye, true));
            Assert.That(Vector3.Distance(rest[eye].At - eye.position, rest[eye].Unshaped), Is.GreaterThan(0.01f),
                "The masculine shape should move the eye well off its bone, or this test checks nothing.");

            dna.Face.Sliders["eye.scale"] = 1f; // 1.22 times
            dna.Face.Sliders["eye.tilt"] = 1f; // 14 degrees
            dna.Face.BrowStyleId = "brow.style.thick"; // 1.08 wide, 1.85 tall, 1.25 deep
            builder.Apply(character, dna, "casual");

            Assert.That(Spread(Features(character, eye, true)) / eyeSize, Is.EqualTo(1.22f).Within(0.01f), "The eye grows,");
            foreach (Transform bone in new[] { eye, brow })
            {
                // How the bone's turn and stretch carry the feature where the bone sits in it, with no shape.
                Vector3 unshaped = rest[bone].Unshaped, expected = Moved(bone, rest[bone].Bone).MultiplyVector(unshaped) - unshaped;
                Assert.That(Vector3.Distance(Centre(Features(character, bone, true)) - rest[bone].At, expected), Is.LessThan(1e-4f),
                    $"and {bone.name} moves no further for the shape having moved it off its bone.");
            }
        }

        const string GeneratedBody = "Assets/_Game/Characters/Content/Imported/Generated/GeneratedBody.fbx";

        /// <summary>A copy of the content with the generated body, and no parts: the stand-in parts don't fit its skeleton.</summary>
        CharacterContent GeneratedContent()
        {
            var body = AssetDatabase.LoadAssetAtPath<ModularCharacter>(GeneratedBody);
            Assert.That(body != null, $"no generated body at {GeneratedBody}");
            CharacterContent content = Object.Instantiate(_content);
            _created.Add(content);
            content.BodyPrefab = body;
            content.Parts = new List<PartBinding>();
            return content;
        }

        /// <summary>No face preset or styles, and every face slider mid-range: nothing turns or stretches the features.</summary>
        static void Plain(CharacterDna dna)
        {
            dna.Face.PresetId = null; // face sliders are offsets from the preset's values
            foreach (string id in dna.Face.Sliders.Keys.ToList()) dna.Face.Sliders[id] = 0f;
            dna.Face.EyeStyleId = dna.Face.BrowStyleId = dna.Face.LashStyleId = null;
        }

        static Vector3 Centre(Vector3[] points) => points.Aggregate(Vector3.zero, (sum, p) => sum + p) / points.Length;

        /// <summary>How far, on average, the points are from their centre: grows with a stretch, not with a turn.</summary>
        static float Spread(Vector3[] points)
        {
            Vector3 centre = Centre(points);
            return points.Average(p => Vector3.Distance(p, centre));
        }

        /// <summary>How a bone's pose moves the features on it: from where its rest left them to where it puts them.</summary>
        static Matrix4x4 Moved(Transform bone, Matrix4x4 rest) => bone.localToWorldMatrix * rest.inverse;

        /// <summary>Where the features wholly on <paramref name="bone"/> are now, with or without the body's shapes.</summary>
        static Vector3[] Features(ModularCharacter character, Transform bone, bool shaped)
        {
            Mesh mesh = character.Body.sharedMesh;
            int index = System.Array.IndexOf(character.Body.bones, bone);
            Vector3[] vertices = mesh.vertices;
            var offsets = new Vector3[mesh.vertexCount];
            for (int shape = 0; shaped && shape < mesh.blendShapeCount; shape++)
            {
                var deltas = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(shape, 0, deltas, null, null);
                float weight = character.Body.GetBlendShapeWeight(shape) / 100f;
                for (int v = 0; v < deltas.Length; v++) offsets[v] += deltas[v] * weight;
            }
            Matrix4x4 skin = bone.localToWorldMatrix * mesh.bindposes[index];
            BoneWeight[] weights = mesh.boneWeights;
            return Enumerable.Range(0, vertices.Length)
                .Where(v => weights[v].boneIndex0 == index && weights[v].weight0 > 0.99f)
                .Select(v => skin.MultiplyPoint3x4(vertices[v] + offsets[v])).ToArray();
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
            dna.Hair.PieceIds.Concat(dna.Outfits.First(o => o.Context == context).Items.Select(i => i.ItemId))
                .Concat(dna.Face.Makeup.Select(m => m.OverlayId).Where(id => id != Makeup.Lipstick));

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
