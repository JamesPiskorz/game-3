using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Game.Characters.Modular;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Game.Characters.Tests.Modular
{
    public class ModularCharacterTests
    {
        static readonly string[] SkeletonBones = { "Hips", "Spine", "Head" };
        static readonly string[] BodyShapes = { "body.weight", "body.chest" };

        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void DestroyCreatedObjects()
        {
            foreach (Object obj in _created)
                if (obj != null) Object.DestroyImmediate(obj);
            _created.Clear();
        }

        [Test]
        public void Equip_RebindsPartOntoCharacterSkeleton_AndDiscardsItsOwnCopy()
        {
            ModularCharacter character = CreateCharacter();

            CharacterPart part = character.Equip(CreatePart("Shirt", "top", BodyRegions.UpperTorso, BodyShapes));

            Transform armature = character.transform.Find("Armature");
            var renderer = part.GetComponentInChildren<SkinnedMeshRenderer>();
            foreach (Transform bone in renderer.bones)
                Assert.That(bone.IsChildOf(armature), $"{bone.name} is not on the character's skeleton");
            Assert.That(renderer.rootBone, Is.SameAs(armature.Find("Hips")));
            Assert.That(part.transform.Find("Armature") == null, "the part's own skeleton copy should be discarded");
        }

        [Test]
        public void Equip_GraftsBonesOnlyThePartHas_AndUnequipRemovesThem()
        {
            ModularCharacter character = CreateCharacter();

            character.Equip(CreatePart("Hair", "hair", BodyRegions.None, new string[0], false, "Hair.Tail.1", "Hair.Tail.2"));

            Transform head = character.transform.Find("Armature/Hips/Spine/Head");
            Transform tail = head.Find("Hair.Tail.1");
            Assert.That(tail != null, "Hair.Tail.1 should be grafted under the character's Head");
            Assert.That(tail.Find("Hair.Tail.2") != null, "grafting should bring the bone's children along");
            Assert.That(tail.localPosition, Is.EqualTo(new Vector3(0f, 0f, -0.1f)));

            character.Unequip("hair");

            Assert.That(head.Find("Hair.Tail.1") == null, "unequipping should remove grafted bones");
        }

        [Test]
        public void Equip_ReplacesThePartInTheSameSlot()
        {
            ModularCharacter character = CreateCharacter();

            CharacterPart first = character.Equip(CreatePart("Shirt", "top", BodyRegions.UpperTorso, BodyShapes));
            CharacterPart second = character.Equip(CreatePart("Hoodie", "top", BodyRegions.UpperTorso | BodyRegions.UpperArmL, BodyShapes));

            Assert.That(first == null, "the previous part should be destroyed");
            Assert.That(character.GetPart("top"), Is.SameAs(second));
        }

        [Test]
        public void Equip_OnePieceDisplacesTopAndBottom_AndATopDisplacesTheOnePiece()
        {
            ModularCharacter character = CreateCharacter();
            character.Equip(CreatePart("Shirt", "top", BodyRegions.UpperTorso, BodyShapes));
            character.Equip(CreatePart("Shorts", "bottom", BodyRegions.Pelvis, BodyShapes));
            CharacterPart dressTemplate = CreatePart("Dress", "onepiece", BodyRegions.UpperTorso | BodyRegions.Pelvis, BodyShapes);
            dressTemplate.BlocksSlots = new[] { "top", "bottom" };

            CharacterPart dress = character.Equip(dressTemplate);

            Assert.That(character.GetPart("top") == null && character.GetPart("bottom") == null, "a dress should displace top and bottom");
            Assert.That(character.GetPart("onepiece"), Is.SameAs(dress));

            character.Equip(CreatePart("Tee", "top", BodyRegions.UpperTorso, BodyShapes));

            Assert.That(character.GetPart("onepiece") == null, "a top should displace the dress");
        }

        [Test]
        public void PartMaterials_AreInstances_ReleasedOnUnequip()
        {
            ModularCharacter character = CreateCharacter();
            CharacterPart template = CreatePart("Shirt", "top", BodyRegions.UpperTorso, BodyShapes);
            Material shared = Track(new Material(Shader.Find("Game/Characters/Toon")));
            template.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial = shared;

            character.Equip(template);
            Material instance = character.PartMaterials("top")[0];

            Assert.That(instance, Is.Not.SameAs(shared));
            character.Unequip("top");
            Assert.That(instance == null, "unequipping should destroy the part's material instances");
        }

        [Test]
        public void SetShape_DrivesSameNamedShapes_OnBodyAndParts_IncludingPartsEquippedLater()
        {
            ModularCharacter character = CreateCharacter();

            character.SetShape("body.weight", 0.5f);
            var part = character.Equip(CreatePart("Shirt", "top", BodyRegions.UpperTorso, BodyShapes)).GetComponentInChildren<SkinnedMeshRenderer>();

            Assert.That(Weight(character.Body, "body.weight"), Is.EqualTo(50f));
            Assert.That(Weight(part, "body.weight"), Is.EqualTo(50f));

            character.SetShape("body.weight", 0.25f);

            Assert.That(Weight(part, "body.weight"), Is.EqualTo(25f));
        }

        [Test]
        public void Equip_WarnsAboutBodyShapesThePartLacks()
        {
            ModularCharacter character = CreateCharacter();
            CharacterPart shirt = CreatePart("Shirt", "top", BodyRegions.UpperTorso, new[] { "body.weight" });

            LogAssert.Expect(LogType.Warning, new Regex(@"lacks body shapes body\.chest"));
            character.Equip(shirt);

            Mesh partMesh = shirt.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
            Assert.That(ModularCharacter.MissingBodyShapes(character.Body.sharedMesh, partMesh), Is.EqualTo(new[] { "body.chest" }));
        }

        [Test]
        public void HiddenRegions_AreTheUnionOfEquippedParts()
        {
            ModularCharacter character = CreateCharacter();

            character.Equip(CreatePart("Shirt", "top", BodyRegions.UpperTorso | BodyRegions.LowerTorso, BodyShapes));
            character.Equip(CreatePart("Shorts", "bottom", BodyRegions.Pelvis, BodyShapes));

            const BodyRegions expected = BodyRegions.UpperTorso | BodyRegions.LowerTorso | BodyRegions.Pelvis;
            Assert.That(character.HiddenRegions, Is.EqualTo(expected));
            Assert.That(character.Body.sharedMaterial.GetInteger("_HiddenRegions"), Is.EqualTo((int)expected));

            character.Unequip("top");

            Assert.That(character.Body.sharedMaterial.GetInteger("_HiddenRegions"), Is.EqualTo((int)BodyRegions.Pelvis));
        }

        [Test]
        public void HidingRegions_LeavesTheSharedBodyMaterialUntouched()
        {
            ModularCharacter character = CreateCharacter();
            Material shared = character.Body.sharedMaterial;

            character.Equip(CreatePart("Shirt", "top", BodyRegions.UpperTorso, BodyShapes));

            Assert.That(character.Body.sharedMaterial, Is.Not.SameAs(shared));
            Assert.That(shared.GetInteger("_HiddenRegions"), Is.EqualTo(0));
        }

        [TestCase(BodyRegions.Head, 0)]
        [TestCase(BodyRegions.UpperTorso, 2)]
        [TestCase(BodyRegions.FootR, 16)]
        public void RegionIds_AreFlagBitIndices(BodyRegions region, int id)
        {
            Assert.That(BodyRegionIds.IdOf(region), Is.EqualTo(id));
        }

        [Test]
        public void RegionIds_RejectCombinedRegions()
        {
            Assert.Throws<ArgumentException>(() => BodyRegionIds.IdOf(BodyRegions.ThighL | BodyRegions.ThighR));
            Assert.Throws<ArgumentException>(() => BodyRegionIds.IdOf(BodyRegions.None));
        }

        // ---------------------------------------------------------------- fixtures

        ModularCharacter CreateCharacter()
        {
            var root = Track(new GameObject("Character"));
            Transform armature = CreateSkeleton(root.transform);
            SkinnedMeshRenderer body = CreateRenderer(root.transform, "Body", armature, SkeletonBones, BodyShapes);
            body.sharedMaterial = Track(new Material(Shader.Find("Game/Characters/StandIn")));
            var character = root.AddComponent<ModularCharacter>();
            character.Initialize(armature, body);
            return character;
        }

        /// <summary>A part template with its own skeleton copy (plus <paramref name="extraBones"/> chained under Head).</summary>
        CharacterPart CreatePart(string name, string slot, BodyRegions covers, string[] shapes, bool followsBodyShapes = true, params string[] extraBones)
        {
            var root = Track(new GameObject(name));
            Transform armature = CreateSkeleton(root.transform);
            var boneNames = new List<string>(SkeletonBones);
            Transform parent = armature.Find("Hips/Spine/Head");
            foreach (string extra in extraBones)
            {
                var bone = new GameObject(extra).transform;
                bone.SetParent(parent, false);
                bone.localPosition = new Vector3(0f, 0f, -0.1f);
                boneNames.Add(extra);
                parent = bone;
            }
            CreateRenderer(root.transform, "Mesh", armature, boneNames, shapes);

            var part = root.AddComponent<CharacterPart>();
            part.Slot = slot;
            part.CoveredRegions = covers;
            part.FollowsBodyShapes = followsBodyShapes;
            return part;
        }

        static Transform CreateSkeleton(Transform parent)
        {
            var armature = new GameObject("Armature").transform;
            armature.SetParent(parent, false);
            Transform current = armature;
            foreach (string name in SkeletonBones)
            {
                var bone = new GameObject(name).transform;
                bone.SetParent(current, false);
                bone.localPosition = Vector3.up * 0.5f;
                current = bone;
            }
            return armature;
        }

        // One small triangle per bone, fully weighted to it, plus the given blendshapes.
        SkinnedMeshRenderer CreateRenderer(Transform parent, string name, Transform armature, IList<string> boneNames, string[] shapes)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var bones = new Transform[boneNames.Count];
            var vertices = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var bindposes = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = FindDeep(armature, boneNames[i]);
                bindposes[i] = bones[i].worldToLocalMatrix * go.transform.localToWorldMatrix;
                Vector3 origin = bones[i].position;
                vertices.Add(origin);
                vertices.Add(origin + Vector3.right * 0.1f);
                vertices.Add(origin + Vector3.up * 0.1f);
                for (int v = 0; v < 3; v++) weights.Add(new BoneWeight { boneIndex0 = i, weight0 = 1f });
            }

            var mesh = Track(new Mesh { name = name });
            mesh.SetVertices(vertices);
            var triangles = new int[vertices.Count];
            for (int i = 0; i < triangles.Length; i++) triangles[i] = i;
            mesh.SetTriangles(triangles, 0);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bindposes;
            var deltas = new Vector3[vertices.Count];
            for (int i = 0; i < deltas.Length; i++) deltas[i] = Vector3.forward * 0.01f;
            foreach (string shape in shapes)
                mesh.AddBlendShapeFrame(shape, 100f, deltas, null, null);

            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = bones[0];
            return renderer;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            throw new ArgumentException($"No bone named '{name}' under '{root.name}'.");
        }

        static float Weight(SkinnedMeshRenderer renderer, string shape) =>
            renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex(shape));

        T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }
    }
}
