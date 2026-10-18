using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Town.Tests
{
    /// <summary>
    /// End-to-end checks on props cleaned by Tools/Blender/clean_generated_prop.py and imported by PropModelImporter:
    /// the generated mesh → Blender → Unity pipeline (graphics plan G5.1, Docs/hunyuan3d.md).
    /// </summary>
    public class ImportedPropTests
    {
        const string Folder = "Assets/_Game/Town/Content/Props/PipelineTest/";
        static readonly string[] Props = { "armchair_test", "penguin_test" };

        [Serializable]
        sealed class Sidecar
        {
            public string[] colours = Array.Empty<string>();
            public float height;
            public int triangles;
            public string licence;
        }

        static GameObject Load(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name + ".fbx");
        static Sidecar Read(string name) => JsonUtility.FromJson<Sidecar>(File.ReadAllText(Folder + name + ".prop.json"));

        [Test]
        public void ImportedProps_AreFlatPaletteColoursInTheEnvironmentToonShader()
        {
            foreach (string name in Props)
            {
                Material[] materials = Load(name).GetComponentInChildren<MeshRenderer>().sharedMaterials;
                Assert.That(materials.Select(m => m.shader.name), Is.All.EqualTo("Game/Environment/Toon"), name);
                string[] colours = materials.Select(m => ColorUtility.ToHtmlStringRGB(m.GetColor("_BaseColor"))).ToArray();
                Assert.That(colours, Is.EquivalentTo(Read(name).colours), $"{name}: one material per palette colour the Blender side used");
            }
        }

        [Test]
        public void ImportedProps_HaveOutlineNormals_AndNoColliders()
        {
            foreach (string name in Props)
            {
                GameObject model = Load(name);
                Mesh mesh = model.GetComponentInChildren<MeshFilter>().sharedMesh;
                Assert.That(mesh.tangents.Length, Is.EqualTo(mesh.vertexCount), $"{name}: outline normals should be baked into tangents");
                Assert.That(model.GetComponentsInChildren<Collider>(), Is.Empty, name);
            }
        }

        [Test]
        public void ImportedProps_StandOnTheirPivot_AtTheirHeight_WithinBudget()
        {
            foreach (string name in Props)
            {
                Sidecar sidecar = Read(name);
                MeshFilter filter = Load(name).GetComponentInChildren<MeshFilter>();
                Bounds bounds = filter.sharedMesh.bounds;
                Vector3 scale = filter.transform.lossyScale;
                Assert.That(bounds.min.y * scale.y, Is.EqualTo(0f).Within(0.005f), $"{name}: the pivot should be at the base");
                Assert.That(bounds.size.y * scale.y, Is.EqualTo(sidecar.height).Within(sidecar.height * 0.01f), $"{name}: real height in metres");
                Assert.That(filter.sharedMesh.triangles.Length / 3, Is.LessThanOrEqualTo(sidecar.triangles), name);
                Assert.That(sidecar.licence, Is.Not.Empty, $"{name}: the sidecar should log the licence");
            }
        }
    }
}
