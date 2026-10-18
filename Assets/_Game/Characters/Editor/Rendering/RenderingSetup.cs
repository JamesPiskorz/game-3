using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Characters.Rendering
{
    /// <summary>
    /// The pipeline settings the game renders with (graphics plan G0.2-G0.3, G0.6-G0.9): MSAA 4× on PC; one
    /// post-processing profile on the pipeline assets, so every camera that runs post (all of them, via
    /// <see cref="GameCamera"/>) shares it without a Volume in each scene; shadows sized for small stages; toon
    /// materials on the sets; soft rain. Re-runnable: it rewrites everything from these values.
    /// Headless: -executeMethod Game.Characters.Rendering.RenderingSetup.Apply
    /// </summary>
    public static class RenderingSetup
    {
        public const string ProfilePath = "Assets/Settings/GamePost.asset";
        const string PcPipeline = "Assets/Settings/PC_RPAsset.asset";
        const string PcRenderer = "Assets/Settings/PC_Renderer.asset";
        static readonly string[] Pipelines = { PcPipeline, "Assets/Settings/Mobile_RPAsset.asset" };
        public const string EnvironmentShader = "Game/Environment/Toon";

        // Set materials and their outline width (the creator and menu floors are cylinders without outline normals).
        static readonly (string Path, float Outline)[] SetMaterials =
        {
            ("Assets/_Game/Town/Materials/TownProp.mat", 1f),
            ("Assets/_Game/Town/Materials/MenuFloor.mat", 0f),
            ("Assets/_Game/Creator/UI/CreatorFloor.mat", 0f),
        };
        const string RainMaterial = "Assets/_Game/Town/Materials/Rain.mat";

        [MenuItem("Game/Rendering/Apply Pipeline Settings")]
        public static void Apply()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            // No tonemapping: the toon colours are picked for the screen as they are, and a neutral or filmic curve
            // would dull and shift them. [Art to decide] in the graphics plan: None plus a LUT, or Neutral.
            Set<Tonemapping>(profile, t => t.mode.Override(TonemappingMode.None));
            // Bloom only on what's brighter than sunlit white: lamps, emission, highlights (R12).
            Set<Bloom>(profile, b =>
            {
                b.threshold.Override(1.3f);
                b.intensity.Override(0.25f);
                b.scatter.Override(0.6f);
                b.highQualityFiltering.Override(true);
            });
            // No vignette here: this profile also reaches the portrait studio and the review sheets, where every portrait
            // and tile would get dark corners. A screen vignette belongs to a scene's main camera (G0.4's preset volumes).
            Set<Vignette>(profile, v => v.intensity.Override(0f));
            EditorUtility.SetDirty(profile);

            foreach (string path in Pipelines)
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (pipeline == null) continue;
                pipeline.volumeProfile = profile;
                if (path == PcPipeline)
                {
                    // Outlines are geometry, so MSAA is what cleans them up. Mobile keeps its own (off) setting.
                    pipeline.msaaSampleCount = 4;
                    // G0.7: stages are small and shots stay within ~15 m. Two cascades over 25 m at 4096 put each at
                    // 2048 px over half the old range: about four times the texel density, with half the shadow passes.
                    pipeline.shadowDistance = 25f;
                    pipeline.shadowCascadeCount = 2;
                    pipeline.cascade2Split = 0.33f;
                    pipeline.mainLightShadowmapResolution = 4096;
                    // G0.9: nothing samples the opaque texture.
                    pipeline.supportsCameraOpaqueTexture = false;
                }
                EditorUtility.SetDirty(pipeline);
            }

            // G0.9: SSAO removed. Characters and sets are toon-shaded and neither samples it, so it was cost for nothing.
            // Removed, not just switched off: a build strips the shaders of an inactive SSAO feature, and the player
            // then throws creating it. This is what URP's renderer editor does: the feature, its map entry, its sub-asset.
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(PcRenderer);
            if (renderer != null)
            {
                var serialized = new SerializedObject(renderer);
                SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
                SerializedProperty map = serialized.FindProperty("m_RendererFeatureMap");
                for (int i = features.arraySize - 1; i >= 0; i--)
                {
                    var feature = features.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererFeature;
                    if (feature == null || feature.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;
                    int count = features.arraySize;
                    features.DeleteArrayElementAtIndex(i);
                    if (features.arraySize == count) features.DeleteArrayElementAtIndex(i); // object references null first
                    map.DeleteArrayElementAtIndex(i);
                    serialized.ApplyModifiedProperties();
                    UnityEngine.Object.DestroyImmediate(feature, true);
                    EditorUtility.SetDirty(renderer);
                }
            }

            // G0.6: the sets use the toon environment shader, keeping their colour.
            Shader environment = Shader.Find(EnvironmentShader);
            foreach ((string path, float outline) in SetMaterials)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) continue;
                Color colour = material.HasColor("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
                if (material.shader != environment) material.shader = environment;
                material.SetColor("_BaseColor", colour);
                material.SetFloat("_OutlineWidth", outline);
                EditorUtility.SetDirty(material);
            }

            // G0.8: soft, see-through rain instead of opaque streaks.
            var rain = AssetDatabase.LoadAssetAtPath<Material>(RainMaterial);
            if (rain != null)
            {
                rain.SetFloat("_Surface", 1f);
                rain.SetFloat("_Blend", 0f);
                rain.SetOverrideTag("RenderType", "Transparent");
                rain.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                rain.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                rain.SetFloat("_ZWrite", 0f);
                rain.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                rain.renderQueue = (int)RenderQueue.Transparent;
                rain.SetColor("_BaseColor", new Color(0.8f, 0.86f, 0.96f, 0.35f));
                EditorUtility.SetDirty(rain);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[RenderingSetup] MSAA 4× on PC; {ProfilePath} on every pipeline asset; shadows 25 m in 2 cascades at 4096; SSAO and the opaque texture off; toon set materials; soft rain.");
        }

        static void Set<T>(VolumeProfile profile, Action<T> configure) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>();
                component.name = typeof(T).Name;
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile); // or it won't be saved with the profile
            }
            configure(component);
            EditorUtility.SetDirty(component);
        }
    }
}
