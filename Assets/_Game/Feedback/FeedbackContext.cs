using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.Characters.Dna;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Feedback
{
    /// <summary>
    /// Gathers what the game knew when feedback was asked for, into a ticket: the build and machine, the screen and
    /// quality settings, every camera and light, every character with its DNA and each material's shader values (what
    /// is being rendered), each screen's own state (<see cref="IFeedbackSource"/>: the town's day, place and people), the
    /// text on screen and the recent log. Main thread only.
    /// </summary>
    public static class FeedbackContext
    {
        public static FeedbackTicket Capture(IReadOnlyList<float> frameTimes, float timeScale)
        {
            var ticket = new FeedbackTicket();
            JObject context = ticket.Context;
            List<IFeedbackSource> sources = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<IFeedbackSource>().ToList();
            var names = new Dictionary<ulong, string>();
            foreach (IFeedbackSource source in sources)
                Try(() =>
                {
                    if (source.FeedbackNames != null)
                        foreach (KeyValuePair<ulong, string> name in source.FeedbackNames) names[name.Key] = name.Value;
                });

            Camera camera = ScreenCamera();
            JObject build = Build();
            context["build"] = build;
            context["system"] = SystemJson();
            context["display"] = Display();
            context["performance"] = Performance(frameTimes, timeScale);
            context["scenes"] = new JArray(Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i).name));
            context["cameras"] = new JArray(Camera.allCameras.Select(c => DescribeCamera(c, c == camera)));
            context["lighting"] = Lighting();
            context["characters"] = Characters(camera, names, ticket.OnScreen);

            var game = new JObject();
            ticket.Summary.Add(("Scene", SceneManager.GetActiveScene().name));
            foreach (IFeedbackSource source in sources)
            {
                string section = source.FeedbackSection ?? source.GetType().Name;
                try
                {
                    object state = source.DescribeForFeedback();
                    game[section] = state == null ? JValue.CreateNull() : JToken.FromObject(state);
                    ticket.Summary.AddRange(source.FeedbackSummary ?? Array.Empty<(string, string)>());
                    if (source.FeedbackAttachments != null)
                        foreach (KeyValuePair<string, string> attachment in source.FeedbackAttachments)
                            ticket.Attachments[attachment.Key] = attachment.Value;
                }
                catch (Exception e)
                {
                    game[section] = "Couldn't describe this: " + e.Message;
                }
            }
            context["game"] = game;
            context["ui"] = Ui();
            context["logs"] = new JArray(FeedbackLog.Recent().Select(e => new JObject
            {
                ["time"] = e.Time, ["type"] = e.Type, ["message"] = e.Message, ["stack"] = e.Stack,
            }));

            string commit = (string)build["commit"];
            ticket.Summary.Add(("Build", $"{Application.version}{(commit != null ? " · " + commit.Substring(0, Math.Min(10, commit.Length)) : "")}" +
                                         $"{((bool?)build["dirty"] == true ? " (uncommitted changes)" : "")} · " +
                                         (Application.isEditor ? "editor" : Debug.isDebugBuild ? "development build" : "release build")));
            ticket.Summary.Add(("Screen", $"{Screen.width}×{Screen.height}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}"));
            ticket.Summary.Add(("Machine", $"{SystemInfo.operatingSystem} · {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType})"));
            return ticket;
        }

        // ---------------------------------------------------------------- the game

        /// <summary>The camera drawing to the screen last: what the player sees.</summary>
        public static Camera ScreenCamera() =>
            Camera.allCameras.Where(c => c.targetTexture == null).OrderByDescending(c => c.depth).FirstOrDefault();

        static JArray Characters(Camera camera, IReadOnlyDictionary<ulong, string> names, List<string> onScreen)
        {
            var list = new JArray();
            Plane[] frustum = camera != null ? GeometryUtility.CalculateFrustumPlanes(camera) : null;
            Vector3 eye = camera != null ? camera.transform.position : Vector3.zero;
            IEnumerable<ModularCharacter> characters = Object.FindObjectsByType<ModularCharacter>(FindObjectsSortMode.None)
                .OrderBy(c => (c.transform.position - eye).sqrMagnitude);
            foreach (ModularCharacter character in characters)
            {
                CharacterDna dna = character.Dna;
                string name = dna != null && names.TryGetValue(dna.Seed, out string known) ? known : null;
                Renderer body = character.Body;
                bool seen = character.isActiveAndEnabled && frustum != null && body != null && body.enabled
                            && GeometryUtility.TestPlanesAABB(frustum, body.bounds);
                Vector3 screen = camera != null ? camera.WorldToScreenPoint(character.transform.position + Vector3.up * 1.5f) : Vector3.zero;

                var parts = new JObject();
                var worn = new List<string>();
                foreach (IGrouping<string, Renderer> group in character.GetComponentsInChildren<Renderer>(true)
                             .GroupBy(r => r.GetComponentInParent<CharacterPart>(true) is CharacterPart part ? part.Slot ?? part.name : "body"))
                {
                    CharacterPart part = group.First().GetComponentInParent<CharacterPart>(true);
                    parts[group.Key] = new JObject
                    {
                        ["part"] = part != null ? Clean(part.name) : "body",
                        ["renderers"] = new JArray(group.Select(DescribeRenderer)),
                    };
                    if (part != null && Wearing(part, group) is string wearing) worn.Add(wearing);
                }

                list.Add(new JObject
                {
                    ["name"] = name,
                    ["object"] = character.name,
                    ["seed"] = dna != null ? dna.Seed.ToString("x") : null,
                    ["style"] = dna?.Style,
                    ["outfit"] = character.OutfitContext,
                    ["onScreen"] = seen,
                    ["screenPosition"] = seen ? Vec(new Vector2(screen.x, screen.y)) : null,
                    ["distance"] = camera != null ? Round((character.transform.position - eye).magnitude) : (JToken)null,
                    ["position"] = Vec(character.transform.position),
                    ["rotation"] = Vec(character.transform.eulerAngles),
                    ["lod"] = Lod(character),
                    ["parts"] = parts,
                    ["dna"] = dna != null ? JToken.Parse(DnaSerializer.ToJson(dna, indented: false)) : null,
                });
                if (seen)
                    onScreen.Add($"**{name ?? character.name}**" + (dna != null ? $" · seed `{dna.Seed:x}` · {dna.Style}" : "") +
                                 (character.OutfitContext != null ? $" · {character.OutfitContext} outfit" : "") +
                                 (worn.Count > 0 ? ": " + string.Join(", ", worn) : ""));
            }
            return list;
        }

        /// <summary>"top.tshirt in jersey, print tee_cat" for a garment, read off the material it's drawn with; null for hair and accessories.</summary>
        static string Wearing(CharacterPart part, IEnumerable<Renderer> renderers)
        {
            Material material = renderers.SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m != null && m.HasTexture(ToonMaterial.BaseMap));
            Texture fabric = material != null ? material.GetTexture(ToonMaterial.BaseMap) : null;
            if (fabric == null || !fabric.name.StartsWith("fabric.")) return null;
            string text = $"{Clean(part.name)} in {fabric.name.Substring("fabric.".Length)}";
            Texture print = material.HasTexture(ToonMaterial.PatternMap) ? material.GetTexture(ToonMaterial.PatternMap) : null;
            if (print != null && material.GetColor(ToonMaterial.PatternColor).a > 0f)
                text += ", print " + print.name.Replace("pattern.", "").Replace("_01", "");
            return text;
        }

        /// <summary>"cloth.top.tshirt_gen01(Clone)" → "top.tshirt".</summary>
        static string Clean(string name) => name.Replace("(Clone)", "").Replace("cloth.", "").Replace("_gen01", "").Trim();

        /// <summary>Which of the distance-dependent extras the character has right now (outlines, spring bones, shadows).</summary>
        static JToken Lod(ModularCharacter character)
        {
            CharacterLod lod = character.GetComponent<CharacterLod>();
            return lod == null ? null : new JObject { ["outlines"] = lod.Outlines, ["springs"] = lod.Springs, ["shadows"] = lod.Shadows };
        }

        static JObject DescribeRenderer(Renderer renderer)
        {
            Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
            return new JObject
            {
                ["name"] = renderer.name,
                ["type"] = renderer.GetType().Name,
                ["enabled"] = renderer.enabled && renderer.gameObject.activeInHierarchy,
                ["visible"] = renderer.isVisible,
                ["mesh"] = mesh != null ? $"{mesh.name} ({mesh.vertexCount} vertices)" : null,
                ["shadows"] = renderer.shadowCastingMode.ToString(),
                ["materials"] = new JArray(renderer.sharedMaterials.Where(m => m != null).Select(DescribeMaterial)),
            };
        }

        /// <summary>A material's shader and every property value it's drawn with: colours and vectors, floats, textures by name.</summary>
        public static JObject DescribeMaterial(Material material)
        {
            var properties = new JObject();
            Shader shader = material.shader;
            for (int i = 0; shader != null && i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i);
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                        properties[name] = Vec(material.GetColor(name));
                        break;
                    case ShaderPropertyType.Vector:
                        properties[name] = Vec(material.GetVector(name));
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        properties[name] = Round(material.GetFloat(name));
                        break;
                    case ShaderPropertyType.Int:
                        properties[name] = material.GetInteger(name);
                        break;
                    case ShaderPropertyType.Texture:
                        Texture texture = material.GetTexture(name);
                        properties[name] = texture != null ? $"{texture.name} ({texture.width}×{texture.height})" : null;
                        if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.NoScaleOffset) == 0)
                        {
                            Vector2 scale = material.GetTextureScale(name), offset = material.GetTextureOffset(name);
                            if (scale != Vector2.one || offset != Vector2.zero) properties[name + "_ST"] = Vec(new Vector4(scale.x, scale.y, offset.x, offset.y));
                        }
                        break;
                }
            }
            return new JObject
            {
                ["name"] = material.name,
                ["shader"] = shader != null ? shader.name : null,
                ["renderQueue"] = material.renderQueue,
                ["keywords"] = new JArray(material.shaderKeywords),
                ["properties"] = properties,
            };
        }

        static JObject DescribeCamera(Camera camera, bool screen) => new JObject
        {
            ["name"] = camera.name,
            ["drawsTheScreen"] = screen,
            ["target"] = camera.targetTexture != null ? camera.targetTexture.name : "screen",
            ["depth"] = camera.depth,
            ["position"] = Vec(camera.transform.position),
            ["rotation"] = Vec(camera.transform.eulerAngles),
            ["fieldOfView"] = Round(camera.fieldOfView),
            ["orthographic"] = camera.orthographic,
            ["near"] = Round(camera.nearClipPlane),
            ["far"] = Round(camera.farClipPlane),
            ["clear"] = camera.clearFlags.ToString(),
            ["background"] = Vec(camera.backgroundColor),
            ["cullingMask"] = camera.cullingMask,
            ["pixelRect"] = Vec(new Vector4(camera.pixelRect.x, camera.pixelRect.y, camera.pixelRect.width, camera.pixelRect.height)),
        };

        static JObject Lighting() => new JObject
        {
            ["ambientMode"] = RenderSettings.ambientMode.ToString(),
            ["ambientSky"] = Vec(RenderSettings.ambientSkyColor),
            ["ambientEquator"] = Vec(RenderSettings.ambientEquatorColor),
            ["ambientGround"] = Vec(RenderSettings.ambientGroundColor),
            ["ambientIntensity"] = Round(RenderSettings.ambientIntensity),
            ["skybox"] = RenderSettings.skybox != null ? RenderSettings.skybox.name : null,
            ["sun"] = RenderSettings.sun != null ? RenderSettings.sun.name : null,
            ["fog"] = RenderSettings.fog ? new JObject
            {
                ["mode"] = RenderSettings.fogMode.ToString(), ["colour"] = Vec(RenderSettings.fogColor),
                ["density"] = Round(RenderSettings.fogDensity), ["start"] = Round(RenderSettings.fogStartDistance), ["end"] = Round(RenderSettings.fogEndDistance),
            } : null,
            ["lights"] = new JArray(Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l => l.isActiveAndEnabled).Take(40).Select(light => new JObject
            {
                ["name"] = light.name, ["type"] = light.type.ToString(), ["colour"] = Vec(light.color),
                ["intensity"] = Round(light.intensity), ["range"] = Round(light.range), ["shadows"] = light.shadows.ToString(),
                ["position"] = Vec(light.transform.position), ["rotation"] = Vec(light.transform.eulerAngles),
            })),
        };

        /// <summary>Every piece of text showing on the screen's UI documents, top to bottom.</summary>
        static JArray Ui()
        {
            var documents = new JArray();
            foreach (UIDocument document in Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                if (!document.isActiveAndEnabled || document.rootVisualElement == null) continue;
                var texts = new List<string>();
                Collect(document.rootVisualElement, texts);
                documents.Add(new JObject { ["document"] = document.name, ["texts"] = new JArray(texts.Distinct().Take(400)) });
            }
            return documents;
        }

        static void Collect(VisualElement element, List<string> texts)
        {
            if (element.resolvedStyle.display == DisplayStyle.None || element.resolvedStyle.visibility == Visibility.Hidden) return;
            if (element is TextElement text && !string.IsNullOrWhiteSpace(text.text)) texts.Add(text.text.Trim());
            foreach (VisualElement child in element.Children()) Collect(child, texts);
        }

        // ---------------------------------------------------------------- the build and the machine

        static JObject Build()
        {
            var build = new JObject
            {
                ["product"] = Application.productName,
                ["version"] = Application.version,
                ["unity"] = Application.unityVersion,
                ["platform"] = Application.platform.ToString(),
                ["editor"] = Application.isEditor,
                ["development"] = Debug.isDebugBuild,
                ["buildGuid"] = Application.buildGUID,
            };
            // A build carries the commit it was built from (FeedbackBuildInfo writes it); the editor asks git.
            TextAsset baked = Application.isEditor ? null : Resources.Load<TextAsset>(BuildInfoResource);
            if (baked != null) Try(() => build.Merge(JObject.Parse(baked.text)));
            else if (FeedbackPaths.RepoRoot != null)
            {
                Shell.Result commit = Shell.Run("git", FeedbackPaths.RepoRoot, new[] { "rev-parse", "HEAD" }, 10000);
                Shell.Result branch = Shell.Run("git", FeedbackPaths.RepoRoot, new[] { "rev-parse", "--abbrev-ref", "HEAD" }, 10000);
                Shell.Result status = Shell.Run("git", FeedbackPaths.RepoRoot, new[] { "status", "--porcelain" }, 10000);
                if (commit.Ok) build["commit"] = commit.Output;
                if (branch.Ok) build["branch"] = branch.Output;
                if (status.Ok) build["dirty"] = status.Output.Length > 0;
            }
            return build;
        }

        /// <summary>The Resources text asset a player build carries its commit in.</summary>
        public const string BuildInfoResource = "feedback-build";

        static JObject SystemJson() => new JObject
        {
            ["os"] = SystemInfo.operatingSystem,
            ["device"] = SystemInfo.deviceModel,
            ["cpu"] = $"{SystemInfo.processorType} ({SystemInfo.processorCount} threads)",
            ["memory"] = $"{SystemInfo.systemMemorySize} MB",
            ["gpu"] = $"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsMemorySize} MB)",
            ["graphicsApi"] = SystemInfo.graphicsDeviceVersion,
            ["shaderLevel"] = SystemInfo.graphicsShaderLevel,
            ["language"] = Application.systemLanguage.ToString(),
        };

        static JObject Display() => new JObject
        {
            ["size"] = $"{Screen.width}×{Screen.height}",
            ["mode"] = Screen.fullScreenMode.ToString(),
            ["refreshRate"] = Round((float)Screen.currentResolution.refreshRateRatio.value),
            ["dpi"] = Round(Screen.dpi),
            ["quality"] = QualitySettings.names[QualitySettings.GetQualityLevel()],
            ["renderPipeline"] = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "built-in",
            ["colorSpace"] = QualitySettings.activeColorSpace.ToString(),
            ["vSync"] = QualitySettings.vSyncCount,
            ["targetFrameRate"] = Application.targetFrameRate,
            ["antiAliasing"] = QualitySettings.antiAliasing,
            ["shadowDistance"] = Round(QualitySettings.shadowDistance),
            ["lodBias"] = Round(QualitySettings.lodBias),
        };

        static JObject Performance(IReadOnlyList<float> frameTimes, float timeScale)
        {
            float average = frameTimes.Count > 0 ? frameTimes.Average() : 0f;
            return new JObject
            {
                ["fps"] = average > 0f ? Round(1f / average) : (JToken)null,
                ["slowestFrameMs"] = frameTimes.Count > 0 ? Round(frameTimes.Max() * 1000f) : (JToken)null,
                ["frames"] = Time.frameCount,
                ["secondsRunning"] = Round(Time.realtimeSinceStartup),
                ["timeScale"] = Round(timeScale),
                ["allocatedMb"] = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024),
                ["reservedMb"] = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / (1024 * 1024),
                ["managedMb"] = GC.GetTotalMemory(false) / (1024 * 1024),
            };
        }

        // ---------------------------------------------------------------- JSON helpers

        static JToken Round(float value) => float.IsNaN(value) || float.IsInfinity(value) ? (JToken)value.ToString(CultureInfo.InvariantCulture) : Math.Round(value, 4);

        static JArray Vec(Vector2 v) => new JArray(Round(v.x), Round(v.y));
        static JArray Vec(Vector3 v) => new JArray(Round(v.x), Round(v.y), Round(v.z));
        static JArray Vec(Vector4 v) => new JArray(Round(v.x), Round(v.y), Round(v.z), Round(v.w));
        static JArray Vec(Color c) => new JArray(Round(c.r), Round(c.g), Round(c.b), Round(c.a));

        static void Try(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Feedback] " + e.Message);
            }
        }
    }
}
