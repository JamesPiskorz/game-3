using System.Collections.Generic;
using Game.Characters.Building;
using Game.Characters.ContentAuthoring;
using Game.Characters.Import;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// Renders the Blender-exported pipeline test assets (Tools/Blender/make_pipeline_test_assets.py) as imported:
    /// body alone, with the vest and scarf, with the body shape at full, and posed (Docs/decisions/img/0005-pipeline.png).
    /// </summary>
    public static class PipelineSheet
    {
        const string Folder = CharacterModelImporter.Folder + "PipelineTest/";

        [MenuItem("Game/Characters/Review/Render Pipeline Test Sheet")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var palette = DefaultContent.SkinPalette();
            var bodyModel = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "PipelineBody.fbx");
            var vest = AssetDatabase.LoadAssetAtPath<CharacterPart>(Folder + "cloth.top.vest_test.fbx");
            var scarf = AssetDatabase.LoadAssetAtPath<CharacterPart>(Folder + "acc.neck.scarf_test.fbx");

            var positions = new[] { new Vector3(-1.5f, 0f, 0f), new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(1.5f, 0f, 0f) };
            LightingRig.Day.Apply(positions);
            for (int i = 0; i < positions.Length; i++)
            {
                var character = Object.Instantiate(bodyModel, positions[i], Quaternion.identity).GetComponent<ModularCharacter>();
                ToonMaterial.SetSkin(character.BodyMaterial, palette.Evaluate(0.15f + i * 0.22f, 0.5f));
                if (i > 0)
                {
                    Colour(character, character.Equip(vest), new Color(0.24f, 0.45f, 0.72f));
                    Colour(character, character.Equip(scarf), new Color(0.85f, 0.32f, 0.3f));
                }
                if (i >= 2) character.SetShape("body.weight", 1f);
                if (i == 3) Poses.Relaxed(character.transform);
                foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                    renderer.forceMatrixRecalculationPerRender = true;
            }

            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background);
            var shots = new List<Texture2D>
            {
                Snapshot.Shoot(camera, new Vector3(0f, 1.1f, 6f), new Vector3(0f, 0.95f, 0f), 1600, 800),
                Snapshot.Shoot(camera, new Vector3(-3.2f, 1.6f, 3.6f), new Vector3(0f, 1.0f, 0f), 1600, 800),
            };
            Snapshot.Save(Snapshot.StackVertically(shots, Color.black), "Docs/decisions/img/0005-pipeline.png");
            Debug.Log("[Review] Wrote Docs/decisions/img/0005-pipeline.png");
        }

        static void Colour(ModularCharacter character, CharacterPart part, Color colour)
        {
            foreach (Material material in character.PartMaterials(part.Slot))
                for (int region = 0; region < 3; region++)
                    ToonMaterial.SetRegionColour(material, region, region == 0 ? colour : colour * 0.7f);
        }
    }
}
