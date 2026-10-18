using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Import;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// The scripted-art spike (decision 0009): the character built entirely by script in Blender
    /// (Tools/Blender/make_scripted_character.py) on the right, a stand-in wearing the matching pieces on the left, in
    /// the same colours, pose and lighting, rendered with the game's toon shader (Docs/review/scripted-character.png),
    /// and both in three test poses (Docs/review/scripted-poses.png); then the scripted character under a light
    /// turntable with and without its lightmaps (Docs/review/scripted-lightmap.png). The scripted character borrows the
    /// stand-in sneakers (it has no shoes of its own); nothing here touches the catalogue.
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.ScriptedCharacterSheet.Render
    /// </summary>
    public static class ScriptedCharacterSheet
    {
        const string Folder = CharacterModelImporter.Folder + "ScriptedSpike/";
        const string BodyId = "ScriptedBody";
        static readonly string[] PartIds = { "hair.back.layered_s1", "hair.front.sidelocks_s1", "cloth.top.jacket_s1", "cloth.bottom.shorts_s1" };

        // One look, applied to both characters (sRGB HSV, as DNA stores colours).
        const float SkinTone = 0.2f, SkinUndertone = 0.55f;
        static readonly HsvColor HairBase = new HsvColor(0.93f, 0.36f, 0.30f);
        static readonly HsvColor HairTip = new HsvColor(0.97f, 0.38f, 0.56f);
        static readonly HsvColor HairHighlight = new HsvColor(0.95f, 0.18f, 0.74f);
        const float HairGradient = 0.7f;
        static readonly HsvColor Iris = new HsvColor(0.52f, 0.62f, 0.72f);
        static readonly HsvColor JacketMain = new HsvColor(0.62f, 0.52f, 0.34f);
        static readonly HsvColor JacketTrim = new HsvColor(0.11f, 0.12f, 0.93f);
        static readonly HsvColor JacketLining = new HsvColor(0.99f, 0.52f, 0.58f);
        static readonly HsvColor JacketButtons = new HsvColor(0.12f, 0.62f, 0.86f);
        static readonly HsvColor ShortsMain = new HsvColor(0.09f, 0.16f, 0.72f);
        static readonly HsvColor ShortsTrim = new HsvColor(0.08f, 0.22f, 0.5f);
        static readonly HsvColor ShoeMain = new HsvColor(0f, 0f, 0.95f);
        static readonly HsvColor ShoeTrim = new HsvColor(0.62f, 0.45f, 0.35f);
        static readonly Dictionary<string, float> Shapes = new Dictionary<string, float> { ["body.weight"] = 0.15f, ["body.chest"] = 0.45f, ["body.hips"] = 0.4f };

        // Another look on the same parts (regions 1-4 each), for the turntable's recolour row.
        static readonly HsvColor[] OtherHair = { new HsvColor(0.12f, 0.32f, 0.86f), new HsvColor(0.11f, 0.22f, 0.95f), new HsvColor(0.12f, 0.08f, 0.98f), new HsvColor(0f, 0f, 1f) };
        static readonly HsvColor[] OtherJacket = { new HsvColor(0.38f, 0.55f, 0.32f), new HsvColor(0.12f, 0.45f, 0.8f), new HsvColor(0.58f, 0.4f, 0.7f), new HsvColor(0f, 0f, 0.85f) };
        static readonly HsvColor[] OtherShorts = { new HsvColor(0.65f, 0.12f, 0.3f), new HsvColor(0.65f, 0.1f, 0.5f), new HsvColor(0.65f, 0.1f, 0.5f), new HsvColor(0.65f, 0.1f, 0.5f) };
        const int TurntableSteps = 8;

        // Seen from the front, +X is on the left of the frame: the stand-in stands left, the scripted character right.
        static readonly Vector3 StandInAt = new Vector3(0.55f, 0f, 0f);
        static readonly Vector3 ScriptedAt = new Vector3(-0.55f, 0f, 0f);

        [MenuItem("Game/Characters/Review/Render Scripted Character Sheet")]
        public static void Render()
        {
            PrepareTextures();
            var shots = new List<Texture2D>();
            foreach (LightingRig rig in new[] { LightingRig.Day, LightingRig.Sunset })
            {
                (ModularCharacter standIn, ModularCharacter scripted, Camera camera) = Stage(rig);
                if (rig == LightingRig.Day)
                {
                    shots.Add(Snapshot.Shoot(camera, new Vector3(0f, 0.95f, 5.6f), new Vector3(0f, 0.92f, 0f), 1600, 1000));
                    shots.Add(Snapshot.Shoot(camera, new Vector3(0f, 1.0f, -5.6f), new Vector3(0f, 0.95f, 0f), 1600, 1000));
                    shots.Add(Row(camera, (eye, target) => Snapshot.Shoot(camera, eye, target, 400, 400),
                        new Vector3(0f, 1.63f, 0.95f), new Vector3(0f, 1.62f, 0f),           // face, front
                        new Vector3(-0.55f, 1.66f, 0.75f), new Vector3(0f, 1.62f, 0f)));     // face, three-quarter
                    shots.Add(Row(camera, (eye, target) => Snapshot.Shoot(camera, eye, target, 400, 500),
                        new Vector3(0f, 1.3f, 1.9f), new Vector3(0f, 1.25f, 0f),             // upper body, front
                        new Vector3(-0.2f, 1.45f, -1.9f), new Vector3(0f, 1.3f, 0f)));       // hair from behind
                }
                shots.Add(Snapshot.Shoot(camera, new Vector3(-2.7f, 1.35f, 4.3f), new Vector3(0f, 0.95f, 0f), 1600, 1000));
                Object.DestroyImmediate(standIn.gameObject);
                Object.DestroyImmediate(scripted.gameObject);
            }
            Snapshot.Save(Snapshot.StackVertically(shots, Color.black), GeneratedLineup.Folder + "/scripted-character.png");
            Debug.Log("[Review] Wrote Docs/review/scripted-character.png (stand-in left, scripted right; mirrored in the back view)");

            RenderPoses();
            RenderLightTurntable();
        }

        /// <summary>
        /// The scripted character's upper body under a light turntable (Docs/review/scripted-lightmap.png), the sun
        /// stepped round in 45° increments, one column per step. Row 1 without its lightmaps (as before G1.1), row 2
        /// with them: the baked shadow bias moves the shadow edge, so painted creases (between hair layers, at the
        /// armpits and cloth folds, on the collar under the jaw) fall into shade sooner. Row 3 marks what changed
        /// between them. Row 4 is row 2 in other colours (recolouring still works).
        /// Headless: -executeMethod Game.Characters.Review.ScriptedCharacterSheet.RenderLightTurntable
        /// </summary>
        public static void RenderLightTurntable()
        {
            PrepareTextures();
            (ModularCharacter standIn, ModularCharacter scripted, Camera camera) = Stage(LightingRig.Day);
            standIn.gameObject.SetActive(false);
            Light sun = RenderSettings.sun;
            // Every toon material on the character, with the lightmap UseTextures gave it (none on the borrowed shoes).
            var lightMaps = scripted.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Where(m => m != null && m.shader.name == ToonMaterial.ShaderName).Distinct()
                .Select(m => (Material: m, LightMap: m.GetTexture(ToonMaterial.LightMap))).ToList();

            List<Texture2D> Turntable(bool withLightMaps, bool otherLook)
            {
                foreach ((Material material, Texture lightMap) in lightMaps) ToonMaterial.SetLightMap(material, withLightMaps ? lightMap : null);
                ColourParts(scripted, otherLook);
                var row = new List<Texture2D>();
                for (int i = 0; i < TurntableSteps; i++)
                {
                    sun.transform.rotation = Quaternion.Euler(LightingRig.Day.SunAngles.x, LightingRig.Day.SunAngles.y + 360f / TurntableSteps * i, 0f);
                    row.Add(Snapshot.Shoot(camera, ScriptedAt + new Vector3(0f, 1.32f, 1.95f), ScriptedAt + new Vector3(0f, 1.3f, 0f), 300, 400));
                }
                return row;
            }

            List<Texture2D> without = Turntable(false, false), with = Turntable(true, false);
            List<Texture2D> shots = without.Concat(with).Concat(without.Zip(with, Changes)).Concat(Turntable(true, true)).ToList();
            Object.DestroyImmediate(standIn.gameObject);
            Object.DestroyImmediate(scripted.gameObject);
            Snapshot.Save(Snapshot.Grid(shots, TurntableSteps, Color.black), GeneratedLineup.Folder + "/scripted-lightmap.png");
            Debug.Log("[Review] Wrote Docs/review/scripted-lightmap.png (sun stepped round; rows: no lightmaps, lightmaps, what they changed, lightmaps in other colours)");
        }

        /// <summary>
        /// <paramref name="after"/> dimmed, with the pixels that came out darker than in <paramref name="before"/> in blue
        /// and lighter in orange: where the lightmaps moved the shadow edge.
        /// </summary>
        static Texture2D Changes(Texture2D before, Texture2D after)
        {
            const int Threshold = 8; // levels in any channel: above MSAA and post noise
            Color32[] a = before.GetPixels32(), b = after.GetPixels32(), marked = new Color32[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                int change = Mathf.Max(Mathf.Abs(b[i].r - a[i].r), Mathf.Abs(b[i].g - a[i].g), Mathf.Abs(b[i].b - a[i].b));
                bool darker = b[i].r + b[i].g + b[i].b < a[i].r + a[i].g + a[i].b;
                marked[i] = change <= Threshold ? Color32.Lerp(new Color32(0, 0, 0, 255), b[i], 0.35f)
                    : darker ? new Color32(60, 120, 255, 255) : new Color32(255, 140, 40, 255);
            }
            var image = new Texture2D(after.width, after.height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            image.SetPixels32(marked);
            image.Apply();
            return image;
        }

        /// <summary>Both characters in three test poses (rows: relaxed, reach, sit); tiles: stand-in and scripted from the front, then from the side.</summary>
        static void RenderPoses()
        {
            var shots = new List<Texture2D>();
            foreach (string pose in new[] { "relaxed", "reach", "sit" })
            {
                (ModularCharacter standIn, ModularCharacter scripted, Camera camera) = Stage(LightingRig.Day, pose);
                // Each character on its own: hide the other.
                foreach ((Vector3 eye, Vector3 target) in new[] { (new Vector3(0.6f, 1.0f, 4.2f), new Vector3(0f, 0.85f, 0f)), (new Vector3(-4.2f, 1.0f, 0.5f), new Vector3(0f, 0.85f, 0f)) })
                    foreach ((ModularCharacter subject, ModularCharacter other) in new[] { (standIn, scripted), (scripted, standIn) })
                    {
                        other.gameObject.SetActive(false);
                        subject.gameObject.SetActive(true);
                        Vector3 at = subject.transform.position;
                        shots.Add(Snapshot.Shoot(camera, at + eye, at + target, 500, 700));
                    }
                Object.DestroyImmediate(standIn.gameObject);
                Object.DestroyImmediate(scripted.gameObject);
            }
            Snapshot.Save(Snapshot.Grid(shots, 4, Color.black), GeneratedLineup.Folder + "/scripted-poses.png");
            Debug.Log("[Review] Wrote Docs/review/scripted-poses.png (rows relaxed, reach, sit; stand-in, scripted, stand-in side, scripted side)");
        }

        /// <summary>Close-ups of each character from two angles, side by side: stand-in, scripted, stand-in, scripted.</summary>
        static Texture2D Row(Camera camera, System.Func<Vector3, Vector3, Texture2D> shoot,
            Vector3 eyeA, Vector3 targetA, Vector3 eyeB, Vector3 targetB)
        {
            var images = new List<Texture2D>();
            foreach ((Vector3 eye, Vector3 target) in new[] { (eyeA, targetA), (eyeB, targetB) })
                foreach (Vector3 at in new[] { StandInAt, ScriptedAt })
                    images.Add(shoot(at + eye, at + target));
            return Snapshot.Grid(images, images.Count, Color.black);
        }

        static (ModularCharacter, ModularCharacter, Camera) Stage(LightingRig rig, string pose = "relaxed")
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = StandInContent.Load();
            rig.Apply(new[] { StandInAt, ScriptedAt });

            ModularCharacter standIn = new CharacterBuilder(content).Build(StandInDna(), "casual");
            standIn.transform.position = StandInAt;
            // The stand-in's body shapes come from its sliders; the scripted body gets the same weights.
            ModularCharacter scripted = BuildScripted(content, standIn);
            scripted.transform.position = ScriptedAt;

            foreach (ModularCharacter character in new[] { standIn, scripted })
            {
                Pose(character.transform, pose);
                foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                    renderer.forceMatrixRecalculationPerRender = true;
            }
            return (standIn, scripted, Snapshot.CreateCamera(rig.Background));
        }

        static CharacterDna StandInDna()
        {
            var dna = new CharacterDna { Style = "casual" };
            dna.Body.Femininity = 0.85f;
            foreach (KeyValuePair<string, float> shape in Shapes) dna.Body.Sliders[shape.Key] = shape.Value;
            dna.Skin.Tone = SkinTone;
            dna.Skin.Undertone = SkinUndertone;
            dna.Face.IrisLeft = dna.Face.IrisRight = Iris;
            dna.Hair.PieceIds = new List<string> { "hair.back.long_01", "hair.front.swept_01" };
            dna.Hair.Base = HairBase;
            dna.Hair.Tip = HairTip;
            dna.Hair.Highlight = HairHighlight;
            dna.Hair.Gradient = HairGradient;
            dna.Outfits.Add(new OutfitDna
            {
                Context = "casual",
                Items =
                {
                    new ItemDna { ItemId = "cloth.top.blazer_01", RegionColors = { JacketMain, JacketTrim } },
                    new ItemDna { ItemId = "cloth.bottom.shorts_01", RegionColors = { ShortsMain, ShortsTrim } },
                    new ItemDna { ItemId = "cloth.shoes.sneakers_01", RegionColors = { ShoeMain, ShoeTrim } },
                },
            });
            return dna;
        }

        static ModularCharacter BuildScripted(CharacterContent content, ModularCharacter standIn)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + BodyId + ".fbx");
            ModularCharacter character = Object.Instantiate(model).GetComponent<ModularCharacter>();
            character.name = "Scripted character";

            Material body = character.BodyMaterial;
            UseTextures(body, BodyId);
            SkinTonePalette.Stop skin = content.SkinPalette.Evaluate(SkinTone, SkinUndertone);
            ToonMaterial.SetSkin(body, skin);
            ToonMaterial.SetRegionColour(body, 0, CharacterBuilder.ToColour(Iris));
            ToonMaterial.SetRegionColour(body, 1, CharacterBuilder.ToColour(new HsvColor(HairBase.H, Mathf.Min(1f, HairBase.S + 0.05f), HairBase.V * 0.72f)));
            ToonMaterial.SetRegionColour(body, 2, new Color(0.97f, 0.96f, 0.95f));
            ToonMaterial.SetRegionColour(body, 3, LipsFrom(skin.Shade));

            foreach (string id in PartIds)
            {
                CharacterPart part = character.Equip(LoadPart(id));
                foreach (Material material in character.PartMaterials(part.Slot)) UseTextures(material, id);
            }
            ColourParts(character, otherLook: false);

            // Borrowed stand-in sneakers: rebuilt for this skeleton's rest pose (Blender bones aren't identity at rest).
            CharacterPart shoes = character.Equip(content.Part("cloth.shoes.sneakers_01"));
            FitToSkeleton(shoes);
            foreach (Material material in character.PartMaterials(shoes.Slot))
            {
                ToonMaterial.SetRegionColour(material, 0, CharacterBuilder.ToColour(ShoeMain));
                ToonMaterial.SetRegionColour(material, 1, CharacterBuilder.ToColour(ShoeTrim));
                ToonMaterial.SetRegionColour(material, 2, CharacterBuilder.ToColour(ShoeMain));
            }

            SkinnedMeshRenderer standInBody = standIn.Body;
            for (int i = 0; i < standInBody.sharedMesh.blendShapeCount; i++)
                character.SetShape(standInBody.sharedMesh.GetBlendShapeName(i), standInBody.GetBlendShapeWeight(i) / 100f);
            return character;
        }

        static CharacterPart LoadPart(string id) => AssetDatabase.LoadAssetAtPath<CharacterPart>(Folder + id + ".fbx");

        /// <summary>Region colours on the scripted parts: the sheet's look, or another one to show recolouring.</summary>
        static void ColourParts(ModularCharacter character, bool otherLook)
        {
            foreach (string id in PartIds)
            {
                Color[] colours = PartColours(id, otherLook);
                foreach (Material material in character.PartMaterials(LoadPart(id).Slot))
                    for (int region = 0; region < 4; region++) ToonMaterial.SetRegionColour(material, region, colours[region]);
            }
        }

        static Color[] PartColours(string id, bool otherLook)
        {
            if (otherLook)
                return (id.StartsWith("hair") ? OtherHair : id.Contains("jacket") ? OtherJacket : OtherShorts).Select(CharacterBuilder.ToColour).ToArray();
            if (id.StartsWith("hair"))
            {
                Color tip = Color.Lerp(CharacterBuilder.ToColour(HairBase), CharacterBuilder.ToColour(HairTip), HairGradient);
                return new[] { CharacterBuilder.ToColour(HairBase), tip, CharacterBuilder.ToColour(HairHighlight), Color.white };
            }
            return (id.Contains("jacket") ? new[] { JacketMain, JacketTrim, JacketLining, JacketButtons } : new[] { ShortsMain, ShortsTrim, ShortsTrim, ShortsTrim })
                .Select(CharacterBuilder.ToColour).ToArray();
        }

        /// <summary>
        /// Rebakes bind poses so a part skinned to the stand-in skeleton (identity bone rotations) renders unchanged on
        /// this skeleton at rest. Real parts don't need this: they share the body's rig.
        /// </summary>
        static void FitToSkeleton(CharacterPart part)
        {
            foreach (SkinnedMeshRenderer renderer in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = Object.Instantiate(renderer.sharedMesh);
                mesh.hideFlags = HideFlags.DontSave;
                Matrix4x4 meshToWorld = renderer.transform.localToWorldMatrix;
                mesh.bindposes = renderer.bones.Select(bone => bone.worldToLocalMatrix * meshToWorld).ToArray();
                renderer.sharedMesh = mesh;
            }
        }

        static Color LipsFrom(Color shade)
        {
            Color.RGBToHSV(shade, out float h, out float s, out float v);
            return Color.HSVToRGB(Mathf.Repeat(h - 0.015f, 1f), Mathf.Clamp01(s + 0.15f), v * 0.62f);
        }

        static void UseTextures(Material material, string id)
        {
            material.DisableKeyword(ToonMaterial.VertexColourMaskKeyword);
            material.SetFloat("_MaskFromVertexColour", 0f);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + id + "_base.png"));
            material.SetTexture("_MaskMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + id + "_mask.png"));
            ToonMaterial.SetLightMap(material, AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + id + "_light.png"));
        }

        /// <summary>Masks and lightmaps are data (linear); detail maps are colour. None compressed, so lash lines stay crisp.</summary>
        static void PrepareTextures()
        {
            foreach (string id in PartIds.Append(BodyId))
                foreach ((string suffix, bool colour) in new[] { ("_base", true), ("_mask", false), ("_light", false) })
                {
                    var importer = AssetImporter.GetAtPath(Folder + id + suffix + ".png") as TextureImporter;
                    if (importer == null) continue;
                    if (importer.sRGBTexture == colour && importer.textureCompression == TextureImporterCompression.Uncompressed && !importer.alphaIsTransparency)
                        continue;
                    importer.sRGBTexture = colour;
                    importer.alphaIsTransparency = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }
        }

        // ---------------------------------------------------------------- poses

        /// <summary>Test poses, given as bends in the character's frame (rig-agnostic, like Poses.Relaxed).</summary>
        static void Pose(Transform character, string pose)
        {
            Poses.Relaxed(character);
            if (pose == "relaxed") return;
            var bends = pose == "reach"
                ? new (string, Vector3)[]
                {
                    ("Chest", new Vector3(0f, 14f, 0f)), ("Head", new Vector3(-8f, -26f, 0f)),
                    ("UpperArm.L", new Vector3(-85f, 0f, 0f)), ("LowerArm.R", new Vector3(-110f, 0f, 0f)),
                    ("UpperLeg.L", new Vector3(-75f, 0f, 0f)), ("LowerLeg.L", new Vector3(95f, 0f, 0f)),
                }
                : new (string, Vector3)[]
                {
                    ("UpperLeg.L", new Vector3(-88f, 0f, 0f)), ("UpperLeg.R", new Vector3(-88f, 0f, 0f)),
                    ("LowerLeg.L", new Vector3(88f, 0f, 0f)), ("LowerLeg.R", new Vector3(88f, 0f, 0f)),
                    ("LowerArm.L", new Vector3(-40f, 0f, 0f)), ("LowerArm.R", new Vector3(-40f, 0f, 0f)),
                };
            Quaternion root = character.rotation;
            var bones = character.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            foreach ((string name, Vector3 euler) in bends)
            {
                if (!bones.TryGetValue(name, out Transform bone)) continue;
                // Turn about axes in the character's frame, through the bone's own pivot.
                Quaternion turn = root * Quaternion.Euler(euler) * Quaternion.Inverse(root);
                bone.rotation = turn * bone.rotation;
            }
        }
    }
}
