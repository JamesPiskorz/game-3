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
        /// <summary>The body shape from the feminine base to the masculine one, weighted 1 - femininity.</summary>
        public const string PresentationShape = "body.masculine";

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

            // A generated body carries the masculine base as a shape (decision 0008), so femininity shapes the mesh,
            // not only the sliders' means. Bodies without the shape (the stand-in) ignore it.
            character.SetShape(PresentationShape, 1f - Mathf.Clamp01(dna.Body.Femininity));

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

            // Frames before any bone moves (so each is its bone's rest) and after the shapes (which move the features).
            var frames = offsets.Keys.ToDictionary(bone => bone, bone => BoneFrame.Of(character.transform, bone, character.Body));
            foreach (KeyValuePair<Transform, BoneOffset> pair in offsets)
                frames[pair.Key].Move(pair.Key, rest.Of(pair.Key), pair.Value.Position, pair.Value.Euler, pair.Value.Scale);
        }

        /// <summary>
        /// Adds a slider's or style's effect on a bone. Effects are in the character's axes (x across, y up, z out of the
        /// face), so they do the same on any rig whatever way its bones rest (<see cref="BoneFrame"/>), and the mirror
        /// bone's is mirrored across the character's middle.
        /// </summary>
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
            SkinTonePalette.Stop skin = _content.SkinPalette.Evaluate(dna.Skin.Tone, dna.Skin.Undertone);
            foreach (ItemDna item in outfit.Items)
            {
                PartEntry entry = _catalog.Part(item.ItemId);
                if (entry == null) continue;
                // Lace, mesh, crochet and fishnet show the skin through their holes: lingerie, swimwear and legwear
                // wholly, other clothes as if lined, part of it.
                float sheer = entry.Tags.Contains("sleep") || entry.Tags.Contains("swim") || entry.Slot == Slots.Legwear ? 1f : LinedSheer;
                // The fabric it's made in: another fabric's surface, or (back to) the part's own.
                Material fabric = _content.Fabric(item.FabricId) ?? OwnMaterial(item.ItemId);
                foreach (Material material in character.PartMaterials(entry.Slot))
                {
                    ToonMaterial.SetFabric(material, fabric);
                    ToonMaterial.SetSheer(material, sheer, skin);
                    for (int region = 0; region < item.RegionColors.Count && region < 4; region++)
                        ToonMaterial.SetRegionColour(material, region, ToColour(item.RegionColors[region]));
                    // Unused regions take region 1's colour, so stand-in masks never show template colours.
                    for (int region = item.RegionColors.Count; region < 3 && item.RegionColors.Count > 0; region++)
                        ToonMaterial.SetRegionColour(material, region, ToColour(item.RegionColors[0]));

                    Texture pattern = item.PatternId == null ? null : _content.Pattern(item.PatternId);
                    if (pattern != null && item.PatternColor.HasValue)
                    {
                        // Its second colour (leaves, a daisy's heart) follows the print colour by the print's rule.
                        PatternEntry print = _catalog.Patterns.FirstOrDefault(p => p.Id == item.PatternId);
                        HsvColor accent = print?.AccentFor(item.PatternColor.Value) ?? item.PatternColor.Value;
                        // A print sized for a dress is drawn finer on smaller things: leopard spots on a shoe, dots on tights.
                        float scale = item.PatternScale * (entry.Slot == Slots.Shoes ? 1.8f : entry.Slot == Slots.Legwear ? 1.4f : 1f);
                        ToonMaterial.SetPattern(material, pattern, ToColour(item.PatternColor.Value), 1f, scale, new Vector4(1f, 0f, 0f, 0f),
                                                ToColour(accent), Placed(print?.Placement, entry, item.PatternScale), RepeatOffset(dna.Seed, item.ItemId));
                    }
                    else
                        ToonMaterial.ClearPattern(material);
                }
            }
        }

        // Where placed prints go on the generated garments, whose fabric UVs are metres (u round from the centre front,
        // towards the wearer's left; v the height in the bind pose; the back panel's u from 10 m at its centre): a chest
        // graphic 20 cm across on the bust line, a badge 11 cm across over the left breast, a back graphic 24 cm across
        // between the shoulder blades.
        static readonly Vector4 Chest = new Vector4(0f, 1.24f, 0.1f, 0.1f);
        static readonly Vector4 Badge = new Vector4(0.085f, 1.3f, 0.055f, 0.055f);
        static readonly Vector4 Back = new Vector4(10f, 1.24f, 0.12f, 0.12f);

        /// <summary>
        /// Where a wearer's repeat of a print starts (in tiles), from their seed and the item: the same for that person
        /// every time, different from the next person in the same print.
        /// </summary>
        static Vector2 RepeatOffset(ulong seed, string itemId)
        {
            ulong h = seed ^ 0x9E3779B97F4A7C15UL;
            foreach (char c in itemId) h = (h ^ c) * 0x100000001B3UL;
            return new Vector2((h & 0xFFFF) / 65536f, ((h >> 16) & 0xFFFF) / 65536f);
        }

        // How much skin shows through an open fabric's holes on clothes that are usually lined.
        const float LinedSheer = 0.5f;

        static Vector4 Sized(Vector4 place, float scale)
        {
            float size = Mathf.Clamp(scale, 0.6f, 1.5f);
            return new Vector4(place.x, place.y, place.z * size, place.w * size);
        }

        /// <summary>
        /// Where a placed print goes on a garment (ToonMaterial.SetPattern's placement); zero for a repeat. A print over
        /// the whole garment spans its cloth's height, a centimetre beyond each end, and every panel and sleeve round it;
        /// a band (a border above the hem, an embroidered yoke under the neckline) is as tall as a tile is wide.
        /// </summary>
        static Vector4 Placed(string placement, PartEntry part, float tilesPerMetre)
        {
            float band = 0.5f / Mathf.Max(tilesPerMetre, 1f);
            bool measured = part.ClothTop - part.ClothBottom > 0.1f;
            switch (placement)
            {
                case "hem": return measured ? new Vector4(0f, part.ClothBottom + band + 0.015f, -1f, band) : Vector4.zero;
                case "yoke": return measured ? new Vector4(0f, part.ClothTop - 0.13f, -1f, band) : Vector4.zero;
                // A graphic's scale sizes it (0.8 to 1.25 of its place), so they differ from wearer to wearer.
                case "chest": return Sized(Chest, tilesPerMetre);
                case "badge": return Sized(Badge, tilesPerMetre);
                case "back": return Sized(Back, tilesPerMetre);
                case "body":
                    float half = (part.ClothTop - part.ClothBottom) / 2f + 0.01f;
                    return half > 0.06f ? new Vector4(40f, (part.ClothTop + part.ClothBottom) / 2f, 45f, half) : Vector4.zero;
                default: return Vector4.zero;
            }
        }

        /// <summary>The material a part was imported with (its own fabric), from its prefab.</summary>
        Material OwnMaterial(string partId)
        {
            CharacterPart prefab = _content.Part(partId);
            if (prefab == null) return null;
            Renderer renderer = prefab.GetComponentInChildren<Renderer>(true);
            return renderer != null ? renderer.sharedMaterial : null;
        }

        // Brows, lash lines and pupils: the hair's colour, deep enough to read as the dark lines anime faces are drawn with.
        static HsvColor BrowFromHair(HsvColor hair) => new HsvColor(hair.H, Math.Min(1f, hair.S + 0.1f), hair.V * 0.42f);

        // A drawn mouth line: the skin's shade colour, deeper and redder: a soft rose, as the references draw it.
        static Color LipsFromSkin(Color shade)
        {
            Color.RGBToHSV(shade, out float h, out float s, out float v);
            return Color.HSVToRGB(Mathf.Repeat(h - 0.025f, 1f), Mathf.Clamp01(s + 0.22f), v * 0.78f);
        }

        /// <summary>DNA colours are sRGB HSV; Unity <see cref="Color"/> values are sRGB too, and materials convert to linear.</summary>
        public static Color ToColour(HsvColor colour) => Color.HSVToRGB(Mathf.Repeat(colour.H, 1f), Mathf.Clamp01(colour.S), Mathf.Clamp01(colour.V));
    }
}
