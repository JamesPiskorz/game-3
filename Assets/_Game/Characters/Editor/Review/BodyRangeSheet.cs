using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.Content;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Generation;
using Game.Characters.Modular;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// Each body slider at its minimum, its presentation's average and its maximum, on one feminine and one masculine
    /// character in the same clothes (Docs/review/body-range.png): one row per slider, full body. Shows the range the
    /// content supports, against which the generator's spreads can be judged. RenderGenerated does the same on the
    /// generated body, undressed (Docs/review/generated-body-range.png).
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.BodyRangeSheet.Render
    /// </summary>
    public static class BodyRangeSheet
    {
        [MenuItem("Game/Characters/Review/Render Body Range Sheet")]
        public static void Render() => Render(StandInContent.Load, "body-range.png");

        [MenuItem("Game/Characters/Review/Render Generated Body Range Sheet")]
        public static void RenderGenerated() => Render(GeneratedBodySheet.Content, "generated-body-range.png");

        /// <summary>Loads the content after opening the scene: a new scene unloads assets nothing in it refers to.</summary>
        static void Render(System.Func<CharacterContent> load, string file)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CharacterContent content = load();
            if (content == null) return;
            ContentCatalog catalog = content.LoadCatalog();
            var generator = new CharacterGenerator(catalog);
            var builder = new CharacterBuilder(content);
            CharacterDna[] people =
            {
                generator.Generate(11, new GenerationRequest { FemininityMin = 0.85f, FemininityMax = 0.9f, Archetype = "casual" }),
                generator.Generate(12, new GenerationRequest { FemininityMin = 0.1f, FemininityMax = 0.15f, Archetype = "casual" }),
            };
            LightingRig.Day.Apply(new[] { Vector3.zero });
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background, 26f);

            var shots = new List<Texture2D>();
            ModularCharacter character = null;
            foreach (SliderEntry slider in catalog.BodySliders)
                foreach (CharacterDna person in people)
                {
                    float mean = Mathf.Lerp(slider.MeanMasculine, slider.MeanFeminine, person.Body.Femininity);
                    foreach (float value in new[] { slider.Min, mean, slider.Max })
                    {
                        CharacterDna shaped = DnaSerializer.FromJson(DnaSerializer.ToJson(person, indented: false));
                        foreach (SliderEntry other in catalog.BodySliders)
                            shaped.Body.Sliders[other.Id] = Mathf.Lerp(other.MeanMasculine, other.MeanFeminine, person.Body.Femininity);
                        shaped.Body.Sliders[slider.Id] = value;
                        if (character == null) character = builder.Build(shaped, "casual");
                        else builder.Apply(character, shaped, "casual");
                        foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                            renderer.forceMatrixRecalculationPerRender = true;
                        Poses.Relaxed(character.transform);
                        shots.Add(Snapshot.Shoot(camera, new Vector3(0f, 0.95f, 5f), new Vector3(0f, 0.92f, 0f), 160, 280));
                    }
                }

            Snapshot.Save(Snapshot.Grid(shots, people.Length * 3, Color.black), GeneratedLineup.Folder + "/" + file);
            Debug.Log("[Review] Body range: " + string.Join(", ", catalog.BodySliders.Select(s => s.Id)));
        }
    }
}
