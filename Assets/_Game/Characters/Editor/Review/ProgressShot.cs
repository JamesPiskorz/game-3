using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Expressions;
using Game.Characters.Generation;
using Game.Characters.Modular;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// How close the generated female character comes to her references (the look loop, decision 0008): the generated
    /// image she was made from (outfit-01 with the bob, References/character-base-images/hair/hair-bob.png) beside the
    /// game's render of her dressed to match it, in its T-pose and in the game's relaxed pose; then her face close up,
    /// front and three-quarter, between the image's face and a face from the style board (References/stylistic). Each
    /// run writes Docs/progress/&lt;date-time&gt;.png and latest.png, so progress can be compared commit by commit.
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.ProgressShot.Render
    /// </summary>
    public static class ProgressShot
    {
        const string Folder = "Docs/progress";
        const string Reference = "References/character-base-images/hair/hair-bob.png";
        const string StyleBoard = "References/stylistic/blog-final.jpg";

        // Where the faces are in the references (pixels from the top left, square crops).
        static readonly RectInt ReferenceFace = new RectInt(232, 20, 100, 100);
        static readonly RectInt StyleBoardFace = new RectInt(430, 230, 400, 400);

        [MenuItem("Game/Characters/Review/Render Progress Shot")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = GeneratedOutfitSheet.Content(out List<GeneratedOutfitSheet.GeneratedOutfit> outfits, out List<GeneratedOutfitSheet.HairStyle> styles);
            if (content == null) return;
            GeneratedOutfitSheet.HairStyle bob = styles.FirstOrDefault(s => s.Name == "hair-bob");
            GeneratedOutfitSheet.GeneratedOutfit casual = outfits.FirstOrDefault(o => o.Name == "outfit-01");
            if (bob == null || casual == null)
            {
                Debug.LogError("[Review] The progress shot needs the generated bob and outfit-01.");
                return;
            }

            var builder = new CharacterBuilder(content);
            CharacterDna dna = Matching(new CharacterGenerator(StandInContent.Load().LoadCatalog()), bob, casual);
            var positions = new[] { Vector3.zero, new Vector3(3f, 0f, 0f) };
            LightingRig.Day.Apply(positions);

            ModularCharacter tpose = Build(builder, dna, positions[0], relaxed: false);
            ModularCharacter relaxed = Build(builder, dna, positions[1], relaxed: true);
            bool blinking = FaceExpression.Blinking;
            FaceExpression.Blinking = false;
            foreach (ModularCharacter character in new[] { tpose, relaxed })
                character.GetComponent<FaceExpression>()?.Snap();

            // Near-orthographic, like the reference's front view.
            Camera far = Snapshot.CreateCamera(LightingRig.Day.Background, 8f);
            Camera near = Snapshot.CreateCamera(LightingRig.Day.Background, 26f);
            const int Big = 800, Small = 400;
            var top = new List<Texture2D>
            {
                Resize(Load(Reference), Big, Big),
                Shoot(far, tpose, positions[0], new Vector3(0f, 0.88f, 13f), new Vector3(0f, 0.88f, 0f), Big),
                Shoot(far, relaxed, positions[1], new Vector3(0f, 0.88f, 13f), new Vector3(0f, 0.88f, 0f), Big),
            };
            Vector3 face = FaceCentre(relaxed) - positions[1];
            var bottom = new List<Texture2D>
            {
                Resize(Crop(Load(Reference), ReferenceFace), Small, Small),
                Shoot(near, relaxed, positions[1], face + new Vector3(0f, 0.01f, 0.9f), face, Small),
                Shoot(near, relaxed, positions[1], face + new Vector3(0.55f, 0.04f, 0.72f), face, Small),
                Resize(Crop(Load(StyleBoard), StyleBoardFace), Small, Small),
                Shoot(near, relaxed, positions[1], face + new Vector3(-0.45f, 0.08f, -0.8f), face, Small),
                Shoot(near, relaxed, positions[1], new Vector3(0f, 1.25f, 1.55f), new Vector3(0f, 1.2f, 0f), Small),
            };
            FaceExpression.Blinking = blinking;

            Texture2D sheet = Snapshot.StackVertically(new[] { Snapshot.Grid(top, 3, Color.black), Snapshot.Grid(bottom, 6, Color.black) }, Color.black);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
            Directory.CreateDirectory(Folder);
            Snapshot.Save(sheet, $"{Folder}/{stamp}.png");
            Snapshot.Save(sheet, $"{Folder}/latest.png");
            Debug.Log($"[Review] Wrote {Folder}/{stamp}.png (and latest.png): the generated bob image beside the game's render of it");
        }

        // A feminine person dressed and coloured like the reference image: outfit-01 in its image's colours, the bob in
        // emerald green, light skin, dark brown eyes, no body or face sliders.
        static CharacterDna Matching(CharacterGenerator generator, GeneratedOutfitSheet.HairStyle bob, GeneratedOutfitSheet.GeneratedOutfit outfit)
        {
            CharacterDna dna = generator.Generate(41, new GenerationRequest { Archetype = "casual", FemininityMin = 1f, FemininityMax = 1f, FantasyHair = false });
            dna.Hair.PieceIds = bob.Pieces.ToList();
            dna.Hair.Base = HsvColor.FromHex("#1b8a50");
            dna.Hair.Tip = HsvColor.FromHex("#167244");
            dna.Hair.Highlight = HsvColor.FromHex("#5fd197");
            dna.Hair.Gradient = 0.4f;
            dna.Skin.Tone = 0.12f;
            dna.Face.IrisLeft = dna.Face.IrisRight = HsvColor.FromHex("#3b2a22");
            // The canonical shapes: the image is the base, not this person's sliders (a curvy roll read as a mismatch).
            dna.Body.Sliders.Clear();
            dna.Face.Sliders.Clear();
            dna.Outfits = new List<OutfitDna> { new OutfitDna { Context = "casual", Items = outfit.Items } };
            return dna;
        }

        static ModularCharacter Build(CharacterBuilder builder, CharacterDna dna, Vector3 position, bool relaxed)
        {
            ModularCharacter character = builder.Build(dna, "casual");
            character.transform.position = position;
            if (relaxed) Poses.Relaxed(character.transform);
            foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                renderer.forceMatrixRecalculationPerRender = true;
            return character;
        }

        static Vector3 FaceCentre(ModularCharacter character)
        {
            Transform head = character.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
            return head.TransformPoint(new Vector3(0f, 0.11f, 0f));
        }

        // Shoots ``subject`` alone (the other characters hidden), the camera placed relative to ``origin``.
        static Texture2D Shoot(Camera camera, ModularCharacter subject, Vector3 origin, Vector3 from, Vector3 at, int size)
        {
            ModularCharacter[] all = UnityEngine.Object.FindObjectsByType<ModularCharacter>(FindObjectsSortMode.None);
            foreach (ModularCharacter other in all) other.gameObject.SetActive(other == subject);
            Texture2D shot = Snapshot.Shoot(camera, origin + from, origin + at, size, size);
            foreach (ModularCharacter other in all) other.gameObject.SetActive(true);
            return shot;
        }

        static Texture2D Load(string path)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(File.ReadAllBytes(path));
            return texture;
        }

        // ``area`` in pixels from the top left, as image editors count.
        static Texture2D Crop(Texture2D source, RectInt area)
        {
            var crop = new Texture2D(area.width, area.height, TextureFormat.RGBA32, false);
            crop.SetPixels(source.GetPixels(area.x, source.height - area.y - area.height, area.width, area.height));
            crop.Apply();
            return crop;
        }

        static Texture2D Resize(Texture2D source, int width, int height)
        {
            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            source.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, target);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
            resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resized.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            return resized;
        }
    }
}
