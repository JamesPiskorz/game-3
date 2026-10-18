using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Characters.Rendering
{
    /// <summary>
    /// The pipeline settings the game renders with (graphics plan G0.2-G0.3): MSAA 4× on PC, and one post-processing
    /// profile set on the pipeline assets, so every camera that runs post (all of them, via <see cref="GameCamera"/>)
    /// shares it without a Volume in each scene. Re-runnable: it rewrites the profile and settings from these values.
    /// Headless: -executeMethod Game.Characters.Rendering.RenderingSetup.Apply
    /// </summary>
    public static class RenderingSetup
    {
        public const string ProfilePath = "Assets/Settings/GamePost.asset";
        const string PcPipeline = "Assets/Settings/PC_RPAsset.asset";
        static readonly string[] Pipelines = { PcPipeline, "Assets/Settings/Mobile_RPAsset.asset" };

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
                // Outlines are geometry, so MSAA is what cleans them up. Mobile keeps its own (off) setting.
                if (path == PcPipeline) pipeline.msaaSampleCount = 4;
                EditorUtility.SetDirty(pipeline);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[RenderingSetup] MSAA 4× on PC; {ProfilePath} on every pipeline asset.");
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
