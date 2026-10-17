using System;
using System.IO;
using System.Linq;
using Game.Characters.Modular;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Spikes.SpikeB
{
    /// <summary>
    /// Renders the Spike B lineup to PNGs in Docs/decisions/img: the same stand-in in six configurations, from the
    /// front, from behind, and close up. Runs headless:
    /// unity run . -- -executeMethod Game.Spikes.SpikeB.SpikeBLineup.GenerateAndRender
    /// </summary>
    static class SpikeBLineup
    {
        const string ImageFolder = "Docs/decisions/img";
        const float Spacing = 0.95f;

        static readonly (string Label, Action<ModularCharacter> Setup)[] Configurations =
        {
            ("body only", character => { }),
            ("outfit", EquipOutfit),
            ("outfit, all shapes at 1", character => { EquipOutfit(character); SetAllShapes(character, 1f); }),
            ("outfit, posed, weight 0.5", character => { EquipOutfit(character); character.SetShape("body.weight", 0.5f); Pose(character); }),
            ("outfit, head bone scaled 1.25", character => { EquipOutfit(character); FindBone(character, "Head").localScale = Vector3.one * 1.25f; }),
            ("control: shapes on body only, no hiding", ShapesOnBodyOnly),
        };

        [MenuItem("Game/Spikes/Spike B/Generate, Build Scene and Render Lineup")]
        public static void GenerateAndRender()
        {
            StandInGenerator.Generate();
            StandInGenerator.CreateDemoScene();
            Render();
        }

        [MenuItem("Game/Spikes/Spike B/Render Lineup")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(35f, -25f, 0f);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StandInGenerator.CharacterPrefabPath);
            ModularCharacter[] characters = Configurations.Select((configuration, index) =>
            {
                GameObject instance = Object.Instantiate(prefab);
                instance.name = configuration.Label;
                instance.transform.position = new Vector3((index - (Configurations.Length - 1) * 0.5f) * Spacing, 0f, 0f);
                var character = instance.GetComponent<ModularCharacter>();
                configuration.Setup(character);
                return character;
            }).ToArray();

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.17f, 0.18f, 0.21f);
            camera.fieldOfView = 30f;

            Directory.CreateDirectory(ImageFolder);
            Shoot(camera, new Vector3(0f, 1.0f, 6.4f), new Vector3(0f, 0.92f, 0f), "0001-spike-b-front.png", 1920, 760);
            Shoot(camera, new Vector3(-3.6f, 1.6f, -5.4f), new Vector3(0f, 0.95f, 0f), "0001-spike-b-back.png", 1920, 760);
            foreach (int index in new[] { 2, 3, 5 })
            {
                Vector3 position = characters[index].transform.position;
                Shoot(camera, position + new Vector3(0.75f, 1.45f, 1.55f), position + new Vector3(0f, 1.15f, 0f), $"0001-spike-b-closeup-{index}.png", 900, 900);
            }

            Debug.Log("[SpikeB] Rendered lineup to " + ImageFolder);
        }

        static void EquipOutfit(ModularCharacter character)
        {
            foreach (string path in StandInGenerator.PartPrefabPaths)
                character.Equip(AssetDatabase.LoadAssetAtPath<CharacterPart>(path));
        }

        static void SetAllShapes(ModularCharacter character, float weight)
        {
            foreach (string shape in StandInGenerator.BodyShapes)
                character.SetShape(shape, weight);
        }

        // What happens without the system: shapes reach the body but not the clothes, and skin is not hidden.
        static void ShapesOnBodyOnly(ModularCharacter character)
        {
            EquipOutfit(character);
            for (int i = 0; i < character.Body.sharedMesh.blendShapeCount; i++)
                character.Body.SetBlendShapeWeight(i, 100f);
            character.Body.sharedMaterial.SetInteger("_HiddenRegions", 0);
        }

        static void Pose(ModularCharacter character)
        {
            FindBone(character, "UpperArm.L").localRotation = Quaternion.Euler(0f, 0f, 65f);
            FindBone(character, "UpperArm.R").localRotation = Quaternion.Euler(0f, -20f, -65f);
            FindBone(character, "LowerArm.R").localRotation = Quaternion.Euler(0f, -70f, 0f);
            FindBone(character, "UpperLeg.L").localRotation = Quaternion.Euler(-35f, 0f, 0f);
            FindBone(character, "LowerLeg.L").localRotation = Quaternion.Euler(55f, 0f, 0f);
            FindBone(character, "Spine").localRotation = Quaternion.Euler(0f, 0f, 6f);
            FindBone(character, "Head").localRotation = Quaternion.Euler(0f, 35f, 0f);
        }

        static Transform FindBone(ModularCharacter character, string name) =>
            character.GetComponentsInChildren<Transform>(true).First(t => t.name == name);

        static void Shoot(Camera camera, Vector3 position, Vector3 target, string fileName, int width, int height)
        {
            camera.transform.position = position;
            camera.transform.LookAt(target);

            var target4x = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            RenderTexture resolved = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            camera.targetTexture = target4x;
            camera.Render();
            camera.targetTexture = null;
            Graphics.Blit(target4x, resolved);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = resolved;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;

            File.WriteAllBytes(Path.Combine(ImageFolder, fileName), image.EncodeToPNG());
            Object.DestroyImmediate(image);
            RenderTexture.ReleaseTemporary(resolved);
            Object.DestroyImmediate(target4x);
        }
    }
}
