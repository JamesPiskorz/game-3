using System.Reflection;
using Game.Characters.Rendering;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.Tests.Modular
{
    /// <summary>The toon shader stays SRP Batcher compatible as G1 adds inputs (PLAN.md §3.7, graphics plan G1).</summary>
    public class ToonShaderTests
    {
        // What the shader inspector's "SRP Batcher: compatible" line reads; Unity keeps it internal. 0 is compatible;
        // 1 means not known yet: Unity works it out when a variant first renders.
        static readonly MethodInfo CompatibilityCode = typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityCode", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly MethodInfo IssueReason = typeof(ShaderUtil).GetMethod("GetSRPBatcherCompatibilityIssueReason", BindingFlags.Static | BindingFlags.NonPublic);
        const int Compatible = 0, NotKnownYet = 1;

        [Test]
        public void ToonShader_IsSrpBatcherCompatible()
        {
            Assert.That(CompatibilityCode, Is.Not.Null, "Unity's internal SRP Batcher check has moved; find its new name.");
            Shader shader = Shader.Find(ToonMaterial.ShaderName);
            RenderOnce(shader);
            int code = Code(shader);
            Assert.That(code, Is.EqualTo(Compatible), () => Reason(shader, code));
        }

        [Test]
        public void TheCheck_CatchesAnIncompatibleShader()
        {
            // A built-in shader keeps its material properties outside a UnityPerMaterial buffer.
            Assert.That(CompatibilityCode, Is.Not.Null);
            Shader shader = Shader.Find("Unlit/Color");
            RenderOnce(shader);
            int code = Code(shader);
            Assert.That(code, Is.Not.EqualTo(Compatible).And.Not.EqualTo(NotKnownYet), () => Reason(shader, code));
        }

        [Test]
        public void ToonShader_HasALightMap()
        {
            var material = new Material(Shader.Find(ToonMaterial.ShaderName));
            var lightMap = new Texture2D(4, 4);
            ToonMaterial.SetLightMap(material, lightMap);
            Assert.That(material.GetTexture(ToonMaterial.LightMap), Is.SameAs(lightMap));
            ToonMaterial.SetLightMap(material, null);
            Assert.That(material.GetTexture(ToonMaterial.LightMap), Is.Null, "Unset, the shader's neutral default applies.");
            Object.DestroyImmediate(lightMap);
            Object.DestroyImmediate(material);
        }

        static void RenderOnce(Shader shader)
        {
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var material = new Material(shader);
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.GetComponent<Renderer>().sharedMaterial = material;
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -3f);
            var target = new RenderTexture(64, 64, 24);
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = null;
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(camera.gameObject);
            Object.DestroyImmediate(sphere);
            Object.DestroyImmediate(material);
            ShaderUtil.allowAsyncCompilation = async;
        }

        static int Code(Shader shader) => (int)CompatibilityCode.Invoke(null, new object[] { shader, 0 });

        static string Reason(Shader shader, int code) => (string)IssueReason?.Invoke(null, new object[] { shader, 0, code });
    }
}
