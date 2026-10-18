using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Characters.Tests.Modular
{
    /// <summary>
    /// The stand-ins' procedural lightmaps (graphics plan G1.1), as the generator wrote them: per vertex in UV2, as
    /// (shadow bias - 0.5, specular mask, metal mask, material type), zero being neutral, packed into SNorm8.
    /// </summary>
    public class StandInLightMapTests
    {
        const string Meshes = "Assets/_Game/Characters/Content/StandIn/Meshes";
        const float Hair = 32f / 255f, Metal = 128f / 255f, Step = 1f / 127f; // SNorm8's resolution

        static Mesh Load(string name) => AssetDatabase.LoadAssetAtPath<Mesh>($"{Meshes}/{name}.asset");

        [Test]
        public void EveryStandInMesh_CarriesALightMap()
        {
            string[] paths = Directory.GetFiles(Meshes, "*.asset");
            Assert.That(paths, Is.Not.Empty);
            foreach (string path in paths)
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path.Replace('\\', '/'));
                Assert.That(mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord2), Is.EqualTo(4), path);
                Assert.That(mesh.GetVertexAttributeFormat(VertexAttribute.TexCoord2), Is.EqualTo(VertexAttributeFormat.SNorm8), path);
            }
        }

        [Test]
        public void Body_IsDarkerUnderTheHeadAndAtTheElbows_AndNeutralOnTheFace()
        {
            Mesh body = Load("StandInBody");
            Vector3[] positions = body.vertices;
            List<Vector4> lightMap = LightMap(body);

            float Bias(System.Func<Vector3, bool> where) => Enumerable.Range(0, positions.Length).Where(i => where(positions[i])).Average(i => lightMap[i].x);
            Assert.That(Bias(p => p.y > 1.5f && p.y < 1.53f && new Vector2(p.x, p.z).magnitude < 0.06f), Is.LessThan(-0.1f), "the neck under the head");
            Assert.That(Bias(p => Mathf.Abs(Mathf.Abs(p.x) - 0.44f) < 0.01f && p.z > 0.03f), Is.LessThan(-0.08f), "the elbows' crooks");
            Assert.That(Bias(p => Mathf.Abs(Mathf.Abs(p.x) - 0.3f) < 0.02f && p.y > 1.43f), Is.EqualTo(0f), "the tops of the upper arms");
            Assert.That(Bias(p => p.y > 1.6f && p.z > 0.08f), Is.EqualTo(0f), "the face");
            Assert.That(lightMap.Max(t => t.x), Is.LessThanOrEqualTo(0f), "nothing on the body stays lit longer");
        }

        [Test]
        public void Hair_ShinesOnItsBand_AndMetalAccessoriesAreMetal()
        {
            Mesh crop = Load("hair.back.crop_01");
            Color32[] colours = crop.colors32;
            List<Vector4> lightMap = LightMap(crop);
            Assert.That(Enumerable.Range(0, colours.Length).All(i => Mathf.Abs(lightMap[i].y - colours[i].a / 255f) <= Step), "shine mask = the band (region 3)");
            Assert.That(lightMap.Any(t => t.y == 1f) && lightMap.Any(t => t.y == 0f));
            Assert.That(lightMap.All(t => Mathf.Abs(t.w - Hair) <= Step));

            List<Vector4> glasses = LightMap(Load("acc.glasses.round_01"));
            Assert.That(glasses.All(t => t.z == 1f && Mathf.Abs(t.w - Metal) <= Step));
        }

        static List<Vector4> LightMap(Mesh mesh)
        {
            var uv2 = new List<Vector4>();
            mesh.GetUVs(2, uv2);
            return uv2;
        }
    }
}
