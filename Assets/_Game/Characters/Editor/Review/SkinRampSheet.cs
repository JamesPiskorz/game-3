using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.ContentAuthoring;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using Game.Characters.StandIns;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// Renders the skin-tone ramp (eight stops, same outfit) under every lighting rig, plus face close-ups,
    /// to Docs/decisions/img. This is the review sheet for retuning palette stops by eye. Headless:
    /// unity run . -- -executeMethod Game.Characters.Review.SkinRampSheet.Render
    /// </summary>
    public static class SkinRampSheet
    {
        const string LineupPath = "Docs/decisions/img/0002-skin-ramp.png";
        const string FacesPath = "Docs/decisions/img/0002-skin-faces.png";
        const int Count = 8;
        const float Spacing = 0.75f;

        public static void GenerateAndRender()
        {
            StandInLibrary.Generate();
            Render();
        }

        [MenuItem("Game/Characters/Review/Render Skin Ramp Sheet")]
        public static void Render()
        {
            SkinTonePalette palette = DefaultContent.SkinPalette();
            var rows = new List<Texture2D>();
            var faces = new List<Texture2D>();

            foreach (LightingRig rig in LightingRig.All)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Vector3[] positions = Enumerable.Range(0, Count).Select(i => new Vector3((i - (Count - 1) * 0.5f) * Spacing, 0f, 0f)).ToArray();
                rig.Apply(positions);
                AddGround();

                ModularCharacter[] characters = positions.Select((position, i) => Dress(position, palette.Evaluate(i / (Count - 1f), 0.5f))).ToArray();

                Camera camera = Snapshot.CreateCamera(rig.Background);
                rows.Add(Snapshot.Shoot(camera, new Vector3(0f, 1.05f, 5.4f), new Vector3(0f, 0.95f, 0f), 1800, 620));

                if (rig == LightingRig.Day)
                    foreach (ModularCharacter character in characters)
                    {
                        Vector3 head = character.transform.position + new Vector3(0f, 1.63f, 0f);
                        faces.Add(Snapshot.Shoot(camera, head + new Vector3(0.12f, 0.03f, 0.55f), head, 300, 300));
                    }
            }

            Snapshot.Save(Snapshot.StackVertically(rows, Color.black), LineupPath);
            Snapshot.Save(Snapshot.Grid(faces, Count, Color.black), FacesPath);
            Debug.Log($"[Review] Wrote {LineupPath} and {FacesPath}");
        }

        static ModularCharacter Dress(Vector3 position, SkinTonePalette.Stop skin)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StandInLibrary.BodyPrefabPath);
            GameObject instance = Object.Instantiate(prefab, position, Quaternion.identity);
            var character = instance.GetComponent<ModularCharacter>();

            ToonMaterial.SetSkin(character.BodyMaterial, skin);
            ToonMaterial.SetRegionColour(character.BodyMaterial, 0, new Color(0.3f, 0.2f, 0.13f));   // iris
            ToonMaterial.SetRegionColour(character.BodyMaterial, 1, new Color(0.16f, 0.1f, 0.08f));  // brows, mouth
            ToonMaterial.SetRegionColour(character.BodyMaterial, 2, new Color(0.97f, 0.96f, 0.95f)); // eye whites

            Equip(character, "cloth.top.tank_01", new Color(0.86f, 0.86f, 0.9f), new Color(0.55f, 0.58f, 0.66f));
            Equip(character, "cloth.bottom.shorts_01", new Color(0.22f, 0.32f, 0.56f), new Color(0.15f, 0.2f, 0.36f));
            Equip(character, "hair.back.crop_01", new Color(0.17f, 0.11f, 0.08f), new Color(0.17f, 0.11f, 0.08f));
            Equip(character, "hair.front.blunt_01", new Color(0.17f, 0.11f, 0.08f), new Color(0.17f, 0.11f, 0.08f));
            Poses.Relaxed(instance.transform);
            return character;
        }

        static void Equip(ModularCharacter character, string id, Color main, Color trim)
        {
            CharacterPart part = character.Equip(AssetDatabase.LoadAssetAtPath<CharacterPart>(StandInLibrary.PartPath(id)));
            foreach (Material material in character.PartMaterials(part.Slot))
            {
                ToonMaterial.SetRegionColour(material, 0, main);
                ToonMaterial.SetRegionColour(material, 1, trim);
            }
        }

        static void AddGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.localScale = new Vector3(3f, 1f, 3f);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", new Color(0.46f, 0.46f, 0.46f));
            ground.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
