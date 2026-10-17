using System.Collections.Generic;
using System.Linq;
using Game.Characters.Modular;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Characters.Tests.Modular
{
    /// <summary>
    /// End-to-end checks on models exported by Tools/Blender/make_pipeline_test_assets.py and imported by
    /// CharacterModelImporter: the Blender → Unity pipeline (decision 0005).
    /// </summary>
    public class ImportedPartTests
    {
        const string Folder = "Assets/_Game/Characters/Content/Imported/PipelineTest/";
        readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (Object obj in _created)
                if (obj != null) Object.DestroyImmediate(obj);
            _created.Clear();
        }

        static GameObject Load(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name + ".fbx");

        [Test]
        public void ImportedBody_IsReadyToWear()
        {
            GameObject model = Load("PipelineBody");
            var character = model.GetComponent<ModularCharacter>();
            Assert.That(character != null, "the importer should add ModularCharacter to bodies");

            Mesh mesh = character.Body.sharedMesh;
            Assert.That(mesh.GetBlendShapeIndex("body.weight"), Is.GreaterThanOrEqualTo(0), "body shape keys should survive export");
            HashSet<int> regions = new HashSet<int>(mesh.colors.Select(c => Mathf.RoundToInt(c.r * 255f)));
            Assert.That(regions, Is.SupersetOf(new[] { 0, 1, 2, 3, 4, 5, 6, 11, 16 }), "region IDs should arrive unaltered (linear vertex colours)");
            Assert.That(mesh.tangents.Length, Is.EqualTo(mesh.vertexCount), "outline normals should be baked into tangents");
            Assert.That(character.Body.sharedMaterial != null && character.Body.sharedMaterial.shader.name == "Game/Characters/Toon");
        }

        [Test]
        public void ImportedParts_ShareTheBodysRig()
        {
            Transform body = Load("PipelineBody").transform.Find("Armature");
            foreach (string part in new[] { "cloth.top.vest_test", "acc.neck.scarf_test" })
                Assert.That(RigCheck.Compare(body, Load(part).transform.Find("Armature")), Is.Empty, part);
        }

        [Test]
        public void ImportedParts_EquipCleanly_AndFollowBodyShapes()
        {
            var character = Track(Object.Instantiate(Load("PipelineBody"))).GetComponent<ModularCharacter>();
            CharacterPart vest = character.Equip(Load("cloth.top.vest_test").GetComponent<CharacterPart>());
            character.Equip(Load("acc.neck.scarf_test").GetComponent<CharacterPart>());
            LogAssert.NoUnexpectedReceived(); // no "lacks body shapes" warnings

            Transform armature = character.transform.Find("Armature");
            foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                foreach (Transform bone in renderer.bones)
                    Assert.That(bone.IsChildOf(armature), $"{renderer.name}: {bone.name}");
            Assert.That(character.HiddenRegions, Is.EqualTo(BodyRegions.UpperTorso | BodyRegions.LowerTorso));

            // The vest carries the body's shape, pushing its surface outward like the torso beneath it. (Checked on
            // the shape data: SkinnedMeshRenderer.BakeMesh ignores blendshape weights in edit-mode tests.)
            var vestRenderer = vest.GetComponentInChildren<SkinnedMeshRenderer>();
            Mesh mesh = vestRenderer.sharedMesh;
            int index = mesh.GetBlendShapeIndex("body.weight");
            var deltas = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(index, 0, deltas, null, null);
            Vector3[] vertices = mesh.vertices;
            Vector3 centre = vertices.Aggregate(Vector3.zero, (sum, v) => sum + v) / vertices.Length;
            float outward = vertices.Zip(deltas, (v, d) => Vector3.Dot(d, Horizontal(v - centre).normalized)).Average();
            Assert.That(outward, Is.GreaterThan(0.01f), "the vest's body.weight shape should push it outward");

            character.SetShape("body.weight", 1f);
            Assert.That(vestRenderer.GetBlendShapeWeight(index), Is.EqualTo(100f));
        }

        static Vector3 Horizontal(Vector3 v) => new Vector3(v.x, 0f, v.z);

        T Track<T>(T obj) where T : Object
        {
            _created.Add(obj);
            return obj;
        }
    }
}
