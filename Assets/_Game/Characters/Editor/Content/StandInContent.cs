using System.Collections.Generic;
using Game.Characters.Building;
using Game.Characters.Modular;
using Game.Characters.Springs;
using Game.Characters.StandIns;
using UnityEditor;
using UnityEngine;

namespace Game.Characters.ContentAuthoring
{
    /// <summary>
    /// Writes Content/CharacterContent.asset: the bindings from catalog IDs to the stand-in body, parts, patterns,
    /// blendshapes and face bones. Real art replaces the prefabs and bone mappings without touching the catalog.
    /// </summary>
    public static class StandInContent
    {
        public const string Path = "Assets/_Game/Characters/Content/CharacterContent.asset";
        public const string CatalogPath = "Assets/_Game/Characters/Content/Catalog.json";

        public static CharacterContent Load() => AssetDatabase.LoadAssetAtPath<CharacterContent>(Path);

        /// <summary>Regenerates stand-in meshes, patterns and the content asset in one go.</summary>
        [MenuItem("Game/Characters/Content/Regenerate All Stand-in Content")]
        public static void RegenerateAll()
        {
            StandInLibrary.Generate();
            PatternTextures.Generate();
            DefaultContent.SkinPalette();
            Write();
        }

        public static CharacterContent Write()
        {
            CharacterContent content = Load();
            bool created = content == null;
            if (created) content = ScriptableObject.CreateInstance<CharacterContent>();

            content.Catalog = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogPath);
            content.BodyPrefab = AssetDatabase.LoadAssetAtPath<ModularCharacter>(StandInLibrary.BodyPrefabPath);
            content.SkinPalette = DefaultContent.SkinPalette();
            content.Reload();

            content.Parts = new List<PartBinding>();
            foreach (StandInLibrary.Recipe recipe in StandInLibrary.Parts())
                content.Parts.Add(new PartBinding { Id = recipe.Id, Prefab = AssetDatabase.LoadAssetAtPath<CharacterPart>(StandInLibrary.PartPath(recipe.Id)) });

            content.Patterns = new List<PatternBinding>();
            foreach ((string id, _) in PatternTextures.All)
                content.Patterns.Add(new PatternBinding { Id = id, Texture = PatternTextures.Load(id) });

            content.Sliders = Sliders();
            content.Styles = Styles();
            content.SpringColliders = new List<SpringColliderBinding>
            {
                // The skull, not the hair: tail roots sit just outside it.
                new SpringColliderBinding { Bone = "Head", Offset = new Vector3(0f, 0.1f, 0f), Radius = 0.108f },
                new SpringColliderBinding { Bone = "Neck", Offset = new Vector3(0f, 0.04f, 0f), Radius = 0.06f },
                new SpringColliderBinding { Bone = "Chest", Offset = new Vector3(0f, 0.12f, -0.02f), Radius = 0.13f },
                new SpringColliderBinding { Bone = "Chest", Offset = new Vector3(-0.12f, 0.14f, 0f), Radius = 0.07f },
                new SpringColliderBinding { Bone = "Chest", Offset = new Vector3(0.12f, 0.14f, 0f), Radius = 0.07f },
            };

            if (created) AssetDatabase.CreateAsset(content, Path);
            EditorUtility.SetDirty(content);
            AssetDatabase.SaveAssets();

            List<string> errors = content.Validate();
            if (errors.Count > 0) Debug.LogError("[Content] CharacterContent is incomplete:\n" + string.Join("\n", errors));
            else Debug.Log("[Content] Wrote " + Path);
            return content;
        }

        static List<SliderBinding> Sliders() => new List<SliderBinding>
        {
            // Body: blendshapes where bones can't do it, bones everywhere else (clothing follows bones for free).
            Shape("body.weight"), Shape("body.chest"), Shape("body.hips"),
            Bone("body.height", SliderKind.BoneScale, "Armature", null, Vector3.one * 0.92f, Vector3.one * 1.07f),
            Bone("body.shoulders", SliderKind.BonePosition, "UpperArm.R", "UpperArm.L", new Vector3(-0.018f, 0f, 0f), new Vector3(0.026f, 0f, 0f)),
            // Longer legs lower the knees and raise the hips by the same amount, so feet stay on the ground.
            Bone("body.legs", SliderKind.BonePosition, "LowerLeg.R", "LowerLeg.L", new Vector3(0f, 0.035f, 0f), new Vector3(0f, -0.045f, 0f)),
            Bone("body.legs", SliderKind.BonePosition, "Hips", null, new Vector3(0f, -0.035f, 0f), new Vector3(0f, 0.045f, 0f)),
            Bone("body.head", SliderKind.BoneScale, "Head", null, Vector3.one * 0.88f, Vector3.one * 1.12f),

            // Face: stand-in face bones.
            Bone("eye.scale", SliderKind.BoneScale, "Eye.R", "Eye.L", Vector3.one * 0.78f, Vector3.one * 1.22f),
            Bone("eye.height", SliderKind.BonePosition, "Eye.R", "Eye.L", new Vector3(0f, -0.008f, 0f), new Vector3(0f, 0.008f, 0f)),
            Bone("eye.spacing", SliderKind.BonePosition, "Eye.R", "Eye.L", new Vector3(-0.006f, 0f, 0f), new Vector3(0.008f, 0f, 0f)),
            Bone("eye.tilt", SliderKind.BoneRotation, "Eye.R", "Eye.L", new Vector3(0f, 0f, -14f), new Vector3(0f, 0f, 14f)),
            Bone("brow.height", SliderKind.BonePosition, "Brow.R", "Brow.L", new Vector3(0f, -0.006f, 0f), new Vector3(0f, 0.009f, 0f)),
            Bone("brow.tilt", SliderKind.BoneRotation, "Brow.R", "Brow.L", new Vector3(0f, 0f, -16f), new Vector3(0f, 0f, 16f)),
            Bone("mouth.width", SliderKind.BoneScale, "Mouth", null, new Vector3(0.7f, 1f, 1f), new Vector3(1.35f, 1f, 1f)),
            Bone("mouth.height", SliderKind.BonePosition, "Mouth", null, new Vector3(0f, -0.006f, 0f), new Vector3(0f, 0.006f, 0f)),
            Bone("nose.size", SliderKind.BoneScale, "Nose", null, Vector3.one * 0.72f, Vector3.one * 1.3f),
        };

        static List<StyleBinding> Styles() => new List<StyleBinding>
        {
            Style("eye.style.round", "Eye.R", "Eye.L", scale: new Vector3(1.06f, 0.98f, 1f)),
            Style("eye.style.tall", "Eye.R", "Eye.L", scale: new Vector3(0.94f, 1.14f, 1f)),
            Style("eye.style.narrow", "Eye.R", "Eye.L", scale: new Vector3(1.06f, 0.74f, 1f)),
            Style("eye.style.droopy", "Eye.R", "Eye.L", scale: new Vector3(1f, 0.9f, 1f), euler: new Vector3(0f, 0f, -9f)),
            Style("brow.style.thin", "Brow.R", "Brow.L", scale: new Vector3(1f, 0.65f, 1f)),
            Style("brow.style.thick", "Brow.R", "Brow.L", scale: new Vector3(1.06f, 1.6f, 1.2f)),
            Style("brow.style.soft", "Brow.R", "Brow.L", scale: new Vector3(0.94f, 1f, 1f), euler: new Vector3(0f, 0f, -6f)),
            Style("eye.style.cat", "Eye.R", "Eye.L", scale: new Vector3(1.04f, 0.84f, 1f), euler: new Vector3(0f, 0f, 11f)),
            Style("eye.style.big", "Eye.R", "Eye.L", scale: new Vector3(1.14f, 1.12f, 1f)),
            Style("eye.style.almond", "Eye.R", "Eye.L", scale: new Vector3(1.1f, 0.86f, 1f), euler: new Vector3(0f, 0f, 4f)),
            Style("brow.style.arched", "Brow.R", "Brow.L", scale: new Vector3(1.05f, 0.85f, 1f), euler: new Vector3(0f, 0f, 9f)),
            Style("brow.style.straight", "Brow.R", "Brow.L", scale: new Vector3(1.14f, 1.25f, 1.1f), euler: new Vector3(0f, 0f, -2f)),
        };

        static SliderBinding Shape(string id) => new SliderBinding
        {
            Id = id, Kind = SliderKind.BlendShape, Target = id, InputMin = 0f, InputMax = 1f, AtMin = Vector3.zero, AtMax = Vector3.right,
        };

        static SliderBinding Bone(string id, SliderKind kind, string target, string mirror, Vector3 atMin, Vector3 atMax) => new SliderBinding
        {
            Id = id, Kind = kind, Target = target, MirrorBone = mirror, InputMin = -1f, InputMax = 1f, AtMin = atMin, AtMax = atMax,
        };

        static StyleBinding Style(string id, string bone, string mirror, Vector3 scale, Vector3 euler = default) => new StyleBinding
        {
            Id = id,
            Tweaks = new List<BoneTweak> { new BoneTweak { Bone = bone, MirrorBone = mirror, Scale = scale, Euler = euler } },
        };
    }
}
