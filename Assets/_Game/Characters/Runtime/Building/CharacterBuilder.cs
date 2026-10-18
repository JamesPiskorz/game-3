using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Content;
using Game.Characters.Dna;
using Game.Characters.Expressions;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using Game.Characters.Springs;
using UnityEngine;

namespace Game.Characters.Building
{
    /// <summary>
    /// DNA → character (PLAN.md §3). Knows meshes, bones and materials; knows nothing about taste. Apply is
    /// idempotent: it reconciles the equipped parts with the DNA, resets bound bones to their rest pose before
    /// applying sliders and styles, and recolours every material. So the creator can call it after every edit.
    /// </summary>
    public sealed class CharacterBuilder
    {
        readonly CharacterContent _content;
        readonly ContentCatalog _catalog;

        public CharacterBuilder(CharacterContent content)
        {
            _content = content;
            _catalog = content.LoadCatalog();
        }

        public CharacterContent Content => _content;

        public ModularCharacter Build(CharacterDna dna, string context = null, Transform parent = null)
        {
            ModularCharacter character = UnityEngine.Object.Instantiate(_content.BodyPrefab, parent);
            character.name = dna.Style != null ? $"Character {dna.Seed:x} ({dna.Style})" : "Character";
            character.gameObject.AddComponent<RestPose>().Capture();
            character.gameObject.AddComponent<CharacterLod>();
            character.gameObject.AddComponent<CharacterShadowAnchor>();
            character.gameObject.AddComponent<FaceExpression>();
            character.gameObject.AddComponent<IdleMotion>();
            Apply(character, dna, context);
            return character;
        }

        /// <summary>Makes <paramref name="character"/> match <paramref name="dna"/>, dressed for <paramref name="context"/> (default: the first outfit).</summary>
        public void Apply(ModularCharacter character, CharacterDna dna, string context = null)
        {
            OutfitDna outfit = dna.Outfits.FirstOrDefault(o => o.Context == context) ?? dna.Outfits.FirstOrDefault();
            EquipParts(character, dna, outfit);
            ApplyShapesAndBones(character, dna);
            SpringBoneRig.Sync(character, _content.SpringColliders);
            character.GetComponent<CharacterLod>()?.Refresh(); // new parts bring new materials and chains
            character.GetComponent<CharacterShadowAnchor>()?.Refresh();
            ApplyBodyColours(character, dna);
            ApplyPartColours(character, dna, outfit);
            character.GetComponent<FaceExpression>()?.Rebase(); // bones were just reset and re-posed
            character.GetComponent<IdleMotion>()?.Rebase();
        }

        // ---------------------------------------------------------------- parts

        void EquipParts(ModularCharacter character, CharacterDna dna, OutfitDna outfit)
        {
            var wanted = new Dictionary<string, string>(); // slot → part id
            IEnumerable<string> ids = dna.Hair.PieceIds.Concat(outfit?.Items.Select(i => i.ItemId) ?? Enumerable.Empty<string>())
                .Concat(Marks(dna).Select(m => m.Mark.OverlayId)); // freckles and the like, whatever the outfit
            foreach (string id in ids)
            {
                PartEntry entry = _catalog.Part(id);
                if (entry == null || _content.Part(id) == null)
                {
                    Debug.LogWarning($"No content for part '{id}'; skipping it.");
                    continue;
                }
                wanted[entry.Slot] = id;
            }

            foreach (string slot in character.EquippedSlots.ToList())
                if (!wanted.ContainsKey(slot)) character.Unequip(slot);
            foreach (KeyValuePair<string, string> pair in wanted)
            {
                CharacterPart current = character.GetPart(pair.Key);
                if (current == null || current.name != pair.Value)
                    character.Equip(_content.Part(pair.Value));
            }
        }

        // ---------------------------------------------------------------- shapes and bones

        void ApplyShapesAndBones(ModularCharacter character, CharacterDna dna)
        {
            RestPose rest = character.GetComponent<RestPose>();
            if (rest == null)
            {
                rest = character.gameObject.AddComponent<RestPose>();
                rest.Capture();
            }
            rest.Restore();

            var offsets = new Dictionary<Transform, BoneOffset>();
            FacePreset preset = _catalog.FacePresets.FirstOrDefault(p => p.Id == dna.Face.PresetId);

            foreach (SliderBinding binding in _content.Sliders)
            {
                float value;
                if (dna.Body.Sliders.TryGetValue(binding.Id, out float body)) value = body;
                else if (dna.Face.Sliders.TryGetValue(binding.Id, out float offset))
                {
                    float presetValue = 0f;
                    preset?.Values.TryGetValue(binding.Id, out presetValue);
                    value = presetValue + offset;
                }
                else continue;

                Vector3 effect = binding.Evaluate(value);
                if (binding.Kind == SliderKind.BlendShape)
                {
                    character.SetShape(binding.Target, effect.x);
                    continue;
                }
                Accumulate(rest, offsets, binding.Target, binding.Kind, effect, mirrored: false);
                if (!string.IsNullOrEmpty(binding.MirrorBone))
                    Accumulate(rest, offsets, binding.MirrorBone, binding.Kind, effect, mirrored: true);
            }

            foreach (string styleId in new[] { dna.Face.EyeStyleId, dna.Face.BrowStyleId, dna.Face.LashStyleId })
            {
                StyleBinding style = styleId == null ? null : _content.Style(styleId);
                if (style == null) continue;
                foreach (BoneTweak tweak in style.Tweaks)
                {
                    Accumulate(rest, offsets, tweak.Bone, SliderKind.BonePosition, tweak.Position, false);
                    Accumulate(rest, offsets, tweak.Bone, SliderKind.BoneScale, tweak.Scale, false);
                    Accumulate(rest, offsets, tweak.Bone, SliderKind.BoneRotation, tweak.Euler, false);
                    if (string.IsNullOrEmpty(tweak.MirrorBone)) continue;
                    Accumulate(rest, offsets, tweak.MirrorBone, SliderKind.BonePosition, tweak.Position, true);
                    Accumulate(rest, offsets, tweak.MirrorBone, SliderKind.BoneScale, tweak.Scale, true);
                    Accumulate(rest, offsets, tweak.MirrorBone, SliderKind.BoneRotation, tweak.Euler, true);
                }
            }

            foreach (KeyValuePair<Transform, BoneOffset> pair in offsets)
            {
                RestPose.Pose pose = rest.Of(pair.Key);
                pair.Key.localPosition = pose.Position + pair.Value.Position;
                pair.Key.localRotation = pose.Rotation * Quaternion.Euler(pair.Value.Euler);
                pair.Key.localScale = Vector3.Scale(pose.Scale, pair.Value.Scale);
            }
        }

        static void Accumulate(RestPose rest, Dictionary<Transform, BoneOffset> offsets, string boneName, SliderKind kind, Vector3 effect, bool mirrored)
        {
            Transform bone = rest.Find(boneName);
            if (bone == null) return;
            if (!offsets.TryGetValue(bone, out BoneOffset offset)) offset = BoneOffset.Identity;
            switch (kind)
            {
                case SliderKind.BonePosition:
                    offset.Position += mirrored ? new Vector3(-effect.x, effect.y, effect.z) : effect;
                    break;
                case SliderKind.BoneRotation:
                    offset.Euler += mirrored ? new Vector3(effect.x, -effect.y, -effect.z) : effect;
                    break;
                case SliderKind.BoneScale:
                    offset.Scale = Vector3.Scale(offset.Scale, effect);
                    break;
            }
            offsets[bone] = offset;
        }

        struct BoneOffset
        {
            public Vector3 Position;
            public Vector3 Euler;
            public Vector3 Scale;
            public static BoneOffset Identity => new BoneOffset { Scale = Vector3.one };
        }

        // ---------------------------------------------------------------- colours

        void ApplyBodyColours(ModularCharacter character, CharacterDna dna)
        {
            Material body = character.BodyMaterial;
            SkinTonePalette.Stop skin = _content.SkinPalette.Evaluate(dna.Skin.Tone, dna.Skin.Undertone);
            ToonMaterial.SetSkin(body, skin);
            // Stand-in face regions: 1 iris, 2 brows, 3 eye whites, 4 lips.
            ToonMaterial.SetRegionColour(body, 0, ToColour(dna.Face.IrisLeft));
            ToonMaterial.SetRegionColour(body, 1, ToColour(dna.Hair.BrowOverride ?? BrowFromHair(dna.Hair.Base)));
            ToonMaterial.SetRegionColour(body, 2, new Color(0.97f, 0.96f, 0.95f));
            OverlayDna lipstick = dna.Face.Makeup?.FirstOrDefault(m => m.OverlayId == Makeup.Lipstick);
            ToonMaterial.SetRegionColour(body, 3, lipstick != null ? ToColour(lipstick.Tint) : LipsFromSkin(skin.Shade));

            // Freckles and beauty marks take a deeper shade of the skin; blush has a colour of its own.
            foreach ((OverlayDna mark, PartEntry part) in Marks(dna))
            {
                Color colour = part.Slot == Slots.FaceBlush
                    ? Color.Lerp(skin.Base, ToColour(mark.Tint), 0.7f) // blush sits in the skin, so it's deeper on deeper skin
                    : MarkFromSkin(skin.Shade, part.Slot == Slots.FaceMole ? 0.4f : 0.74f);
                foreach (Material material in character.PartMaterials(part.Slot))
                    ToonMaterial.SetRegionColour(material, 0, colour);
            }
        }

        /// <summary>The DNA's face marks that are parts (freckles, beauty marks, blush), with their catalog entries.</summary>
        IEnumerable<(OverlayDna Mark, PartEntry Part)> Marks(CharacterDna dna)
        {
            if (dna.Face.Makeup == null) yield break;
            foreach (OverlayDna mark in dna.Face.Makeup)
            {
                PartEntry part = mark?.OverlayId == null ? null : _catalog.Part(mark.OverlayId);
                if (part != null && Makeup.IsMark(part.Slot)) yield return (mark, part);
            }
        }

        static Color MarkFromSkin(Color shade, float depth)
        {
            Color.RGBToHSV(shade, out float h, out float s, out float v);
            return Color.HSVToRGB(Mathf.Repeat(h - 0.01f, 1f), Mathf.Clamp01(s + 0.18f), v * depth);
        }

        void ApplyPartColours(ModularCharacter character, CharacterDna dna, OutfitDna outfit)
        {
            Color hairBase = ToColour(dna.Hair.Base);
            Color hairTip = Color.Lerp(hairBase, ToColour(dna.Hair.Tip), dna.Hair.Gradient);
            foreach (string id in dna.Hair.PieceIds)
            {
                PartEntry entry = _catalog.Part(id);
                if (entry == null) continue;
                foreach (Material material in character.PartMaterials(entry.Slot))
                {
                    ToonMaterial.SetRegionColour(material, 0, hairBase);
                    ToonMaterial.SetRegionColour(material, 1, hairTip);
                    ToonMaterial.SetRegionColour(material, 2, ToColour(dna.Hair.Highlight));
                }
            }

            if (outfit == null) return;
            foreach (ItemDna item in outfit.Items)
            {
                PartEntry entry = _catalog.Part(item.ItemId);
                if (entry == null) continue;
                foreach (Material material in character.PartMaterials(entry.Slot))
                {
                    for (int region = 0; region < item.RegionColors.Count && region < 4; region++)
                        ToonMaterial.SetRegionColour(material, region, ToColour(item.RegionColors[region]));
                    // Unused regions take region 1's colour, so stand-in masks never show template colours.
                    for (int region = item.RegionColors.Count; region < 3 && item.RegionColors.Count > 0; region++)
                        ToonMaterial.SetRegionColour(material, region, ToColour(item.RegionColors[0]));

                    Texture pattern = item.PatternId == null ? null : _content.Pattern(item.PatternId);
                    if (pattern != null && item.PatternColor.HasValue)
                        ToonMaterial.SetPattern(material, pattern, ToColour(item.PatternColor.Value), 1f, item.PatternScale, new Vector4(1f, 0f, 0f, 0f));
                    else
                        ToonMaterial.ClearPattern(material);
                }
            }
        }

        static HsvColor BrowFromHair(HsvColor hair) => new HsvColor(hair.H, Math.Min(1f, hair.S + 0.05f), hair.V * 0.72f);

        // A drawn mouth line: the skin's shade colour, deeper and redder.
        static Color LipsFromSkin(Color shade)
        {
            Color.RGBToHSV(shade, out float h, out float s, out float v);
            return Color.HSVToRGB(Mathf.Repeat(h - 0.015f, 1f), Mathf.Clamp01(s + 0.15f), v * 0.62f);
        }

        /// <summary>DNA colours are sRGB HSV; Unity <see cref="Color"/> values are sRGB too, and materials convert to linear.</summary>
        public static Color ToColour(HsvColor colour) => Color.HSVToRGB(Mathf.Repeat(colour.H, 1f), Mathf.Clamp01(colour.S), Mathf.Clamp01(colour.V));
    }
}
