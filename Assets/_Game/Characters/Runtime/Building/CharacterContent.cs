using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using Game.Characters.Springs;
using UnityEngine;

namespace Game.Characters.Building
{
    /// <summary>
    /// The engine side of the content: binds catalog IDs (pure data, Catalog.json) to prefabs, textures, blendshapes
    /// and bones. The generator reads only the catalog; the builder reads both.
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Characters/Character Content")]
    public sealed class CharacterContent : ScriptableObject
    {
        public TextAsset Catalog;
        public ModularCharacter BodyPrefab;
        public SkinTonePalette SkinPalette;
        public List<PartBinding> Parts = new List<PartBinding>();
        public List<PatternBinding> Patterns = new List<PatternBinding>();

        [Tooltip("Each catalog fabric's material: its surface texture, tiling and gain are copied onto parts made in it.")]
        public List<FabricBinding> Fabrics = new List<FabricBinding>();
        public List<SliderBinding> Sliders = new List<SliderBinding>();
        public List<StyleBinding> Styles = new List<StyleBinding>();

        [Tooltip("Lighting presets for the places characters appear: town, creator, menu, portraits (graphics plan G0.4).")]
        public LightingLibrary Lighting;

        [Tooltip("Spheres on the body that spring-bone hair and cloth can't pass through.")]
        public List<SpringColliderBinding> SpringColliders = new List<SpringColliderBinding>();

        ContentCatalog _catalog;

        /// <summary>The parsed catalog (cached; call <see cref="Reload"/> after editing the JSON).</summary>
        public ContentCatalog LoadCatalog() => _catalog ?? (_catalog = CatalogSerializer.FromJson(Catalog.text));

        public void Reload() => _catalog = null;

        public CharacterPart Part(string id) => Parts.FirstOrDefault(p => p.Id == id)?.Prefab;
        public Texture Pattern(string id) => Patterns.FirstOrDefault(p => p.Id == id)?.Texture;
        public Material Fabric(string id) => id == null ? null : Fabrics.FirstOrDefault(f => f.Id == id)?.Material;
        public StyleBinding Style(string id) => Styles.FirstOrDefault(s => s.Id == id);

        /// <summary>Catalog IDs that have no binding here, and bindings whose asset is missing.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            ContentCatalog catalog = LoadCatalog();
            foreach (PartEntry part in catalog.Parts)
                if (Part(part.Id) == null) errors.Add($"Part '{part.Id}' has no prefab.");
            foreach (PatternEntry pattern in catalog.Patterns)
                if (Pattern(pattern.Id) == null) errors.Add($"Pattern '{pattern.Id}' has no texture.");
            foreach (FabricEntry fabric in catalog.Fabrics)
                if (Fabric(fabric.Id) == null) errors.Add($"Fabric '{fabric.Id}' has no material.");
            foreach (SliderEntry slider in catalog.BodySliders.Concat(catalog.FaceSliders))
                if (Sliders.All(s => s.Id != slider.Id)) errors.Add($"Slider '{slider.Id}' has no binding.");
            foreach (StyleEntry style in catalog.EyeStyles.Concat(catalog.BrowStyles))
                if (Style(style.Id) == null) errors.Add($"Style '{style.Id}' has no binding.");
            if (BodyPrefab == null) errors.Add("No body prefab.");
            if (SkinPalette == null) errors.Add("No skin palette.");
            return errors;
        }
    }

    [Serializable]
    public sealed class FabricBinding
    {
        public string Id;
        public Material Material;
    }

    [Serializable]
    public sealed class PartBinding
    {
        public string Id;
        public CharacterPart Prefab;
    }

    [Serializable]
    public sealed class PatternBinding
    {
        public string Id;
        public Texture2D Texture;
    }

    public enum SliderKind
    {
        BlendShape,
        BoneScale,
        BonePosition,
        BoneRotation,
    }

    /// <summary>
    /// What a slider moves. The slider value is remapped from Input range onto AtMin..AtMax:
    /// blendshape weight (x, 0..1), bone scale multiplier, local position offset, or local euler offset.
    /// MirrorBone gets the same effect mirrored across the body's centre (X negated for positions; Y and Z
    /// negated for rotations). One slider may have several bindings.
    /// </summary>
    [Serializable]
    public sealed class SliderBinding
    {
        public string Id;
        public SliderKind Kind;
        public string Target;
        public string MirrorBone;
        public float InputMin = -1f;
        public float InputMax = 1f;
        public Vector3 AtMin;
        public Vector3 AtMax;

        public Vector3 Evaluate(float value) => Vector3.Lerp(AtMin, AtMax, Mathf.InverseLerp(InputMin, InputMax, value));
    }

    /// <summary>A style ID (eye or brow style) realised as bone tweaks on top of the sliders.</summary>
    [Serializable]
    public sealed class StyleBinding
    {
        public string Id;
        public List<BoneTweak> Tweaks = new List<BoneTweak>();
    }

    [Serializable]
    public sealed class BoneTweak
    {
        public string Bone;
        public string MirrorBone;
        public Vector3 Position;
        public Vector3 Scale = Vector3.one;
        public Vector3 Euler;
    }
}
