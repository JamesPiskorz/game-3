using System.Collections.Generic;
using System.Linq;
using Game.Characters.Building;
using Game.Characters.ContentAuthoring;
using Game.Characters.Dna;
using Game.Characters.Modular;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Characters.Review
{
    /// <summary>
    /// The stand-ins' procedural lightmaps (graphics plan G1.1) under a light turntable (Docs/review/standin-lightmap.png):
    /// one stand-in, posed to open its creases (arms off its sides, elbows and a knee bent), the sun stepped round in 45°
    /// increments, one column per step. Row 1 is the lightmap itself from each view (StandInLightMap: blue falls into
    /// shade sooner, green is the shine mask). Then four views of three rows each: without lightmaps (zeroed, as
    /// before G1.1), with them, and what moved (ScriptedCharacterSheet.Changes: blue darker, orange lighter). The views
    /// are the arms (armpits, elbows), the legs (crotch, knees), head and shoulders from the front (the neck down to the
    /// collar) and from behind (the nape, the neck at the collar and the hair's lower layers). The last row is the whole
    /// body in other colours, with lightmaps: recolouring still works.
    /// Headless: unity run . -- -executeMethod Game.Characters.Review.StandInLightMapSheet.Render
    /// </summary>
    public static class StandInLightMapSheet
    {
        const int Steps = 8, TileWidth = 240, TileHeight = 340;

        // Camera eye and target per view; the character stands at the origin facing +Z.
        static readonly (string Name, Vector3 Eye, Vector3 Target)[] Views =
        {
            ("arms", new Vector3(0.55f, 1.15f, 2.2f), new Vector3(0f, 1.22f, 0f)),
            ("legs", new Vector3(-0.7f, 0.62f, 2.1f), new Vector3(0f, 0.5f, 0f)),
            ("head and shoulders", new Vector3(0.5f, 1.56f, 1.2f), new Vector3(0f, 1.46f, 0f)),
            ("from behind", new Vector3(-0.5f, 1.47f, -1.2f), new Vector3(0f, 1.5f, 0f)), // level with the collar, under the bob
        };
        static readonly (string Name, Vector3 Eye, Vector3 Target) WholeBody = ("whole body", new Vector3(1.75f, 1.05f, 3.2f), new Vector3(0f, 0.9f, 0f));

        [MenuItem("Game/Characters/Review/Render Stand-in Lightmap Sheet")]
        public static void Render()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var builder = new CharacterBuilder(StandInContent.Load());
            LightingRig.Day.Apply(new[] { Vector3.zero });
            Light sun = RenderSettings.sun;
            Camera camera = Snapshot.CreateCamera(LightingRig.Day.Background);
            Shader diagnostics = Shader.Find("Hidden/Game/Diagnostics");

            ModularCharacter character = Build(builder, otherLook: false);
            List<(SkinnedMeshRenderer Renderer, Mesh With, Mesh Without)> meshes = Neutralised(character);

            List<Texture2D> Turntable((string Name, Vector3 Eye, Vector3 Target) view)
            {
                var row = new List<Texture2D>();
                for (int i = 0; i < Steps; i++)
                {
                    sun.transform.rotation = Quaternion.Euler(LightingRig.Day.SunAngles.x, LightingRig.Day.SunAngles.y + 360f / Steps * i, 0f);
                    row.Add(Snapshot.Shoot(camera, view.Eye, view.Target, TileWidth, TileHeight));
                }
                return row;
            }

            var turntables = new List<Texture2D>();
            var report = new List<string>();
            foreach (var view in Views)
            {
                Use(meshes, withLightMaps: false);
                List<Texture2D> without = Turntable(view);
                Use(meshes, withLightMaps: true);
                List<Texture2D> with = Turntable(view);
                turntables.AddRange(without.Concat(with).Concat(without.Zip(with, ScriptedCharacterSheet.Changes)));
                report.Add($"{view.Name} {Moved(without, with):P1}");
            }
            // The debug views come after the turntables: in a fresh editor, the first render with post after one without
            // it comes out black.
            List<Texture2D> tiles = Views.Append(WholeBody).Select(view => DiagnosticSheet.DebugView(camera, character, diagnostics, 3f, Color.white,
                DiagnosticSheet.LightMapBackdrop, () => Snapshot.Shoot(camera, view.Eye, view.Target, TileWidth, TileHeight))).ToList();
            while (tiles.Count < Steps) tiles.Add(Blank());
            tiles.AddRange(turntables);
            foreach (var (_, _, without) in meshes) Object.DestroyImmediate(without);
            Object.DestroyImmediate(character.gameObject);

            ModularCharacter other = Build(builder, otherLook: true);
            tiles.AddRange(Turntable(WholeBody));
            Object.DestroyImmediate(other.gameObject);

            Snapshot.Save(Snapshot.Grid(tiles, Steps, Color.black), GeneratedLineup.Folder + "/standin-lightmap.png");
            Debug.Log("[Review] Wrote Docs/review/standin-lightmap.png (sun stepped round; rows: the lightmap per view, then per view no lightmaps, " +
                "lightmaps, what they changed; last, lightmaps in other colours). Pixels the lightmaps moved, over the turntable: " + string.Join(", ", report));
        }

        static ModularCharacter Build(CharacterBuilder builder, bool otherLook)
        {
            ModularCharacter character = builder.Build(Dna(otherLook), "casual");
            Pose(character.transform);
            foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>())
                renderer.forceMatrixRecalculationPerRender = true;
            return character;
        }

        // A bob, which leaves the nape bare under its lower layer, blunt bangs carrying the shine band, a jacket over a
        // T-shirt (cloth at the elbows), and shorts that leave the knees bare.
        static CharacterDna Dna(bool otherLook)
        {
            var dna = new CharacterDna { Style = "casual" };
            dna.Body.Femininity = 0.6f;
            dna.Skin.Tone = otherLook ? 0.72f : 0.3f;
            dna.Skin.Undertone = otherLook ? 0.4f : 0.55f;
            dna.Face.IrisLeft = dna.Face.IrisRight = otherLook ? new HsvColor(0.33f, 0.5f, 0.6f) : new HsvColor(0.58f, 0.55f, 0.7f);
            dna.Hair.PieceIds = new List<string> { "hair.back.bob_01", "hair.front.blunt_01" };
            dna.Hair.Base = otherLook ? new HsvColor(0.12f, 0.45f, 0.9f) : new HsvColor(0.06f, 0.45f, 0.3f);
            dna.Hair.Tip = otherLook ? new HsvColor(0.1f, 0.55f, 0.72f) : new HsvColor(0.07f, 0.4f, 0.48f);
            dna.Hair.Highlight = otherLook ? new HsvColor(0.13f, 0.2f, 1f) : new HsvColor(0.07f, 0.25f, 0.72f);
            dna.Hair.Gradient = 0.6f;
            dna.Outfits.Add(new OutfitDna
            {
                Context = "casual",
                Items =
                {
                    new ItemDna { ItemId = "cloth.top.tshirt_01", RegionColors = otherLook
                        ? new List<HsvColor> { new HsvColor(0.98f, 0.6f, 0.72f), new HsvColor(0f, 0f, 0.95f) }
                        : new List<HsvColor> { new HsvColor(0.57f, 0.3f, 0.88f), new HsvColor(0.1f, 0.12f, 0.96f) } },
                    new ItemDna { ItemId = "cloth.outer.jacket_01", RegionColors = otherLook
                        ? new List<HsvColor> { new HsvColor(0.5f, 0.45f, 0.45f), new HsvColor(0.12f, 0.55f, 0.9f) }
                        : new List<HsvColor> { new HsvColor(0.11f, 0.28f, 0.82f), new HsvColor(0.02f, 0.45f, 0.78f) } },
                    new ItemDna { ItemId = "cloth.bottom.shorts_01", RegionColors = otherLook
                        ? new List<HsvColor> { new HsvColor(0.25f, 0.4f, 0.48f), new HsvColor(0.25f, 0.2f, 0.8f) }
                        : new List<HsvColor> { new HsvColor(0.62f, 0.45f, 0.35f), new HsvColor(0.6f, 0.25f, 0.65f) } },
                    new ItemDna { ItemId = "cloth.shoes.sneakers_01", RegionColors = otherLook
                        ? new List<HsvColor> { new HsvColor(0.6f, 0.6f, 0.4f), new HsvColor(0f, 0f, 0.95f) }
                        : new List<HsvColor> { new HsvColor(0f, 0f, 0.95f), new HsvColor(0f, 0.6f, 0.7f) } },
                },
            });
            return dna;
        }

        // Arms off the sides and folded at the elbows, and one knee forward, so the creases face the camera. Children
        // turn first: turning a parent afterwards carries the child's bend with it.
        static void Pose(Transform character)
        {
            Dictionary<string, Transform> bones = character.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            foreach ((string name, Vector3 euler) in new[]
            {
                ("LowerArm.L", new Vector3(0f, 35f, 0f)), ("LowerArm.R", new Vector3(0f, -35f, 0f)), ("LowerLeg.L", new Vector3(40f, 0f, 0f)),
                ("UpperArm.L", new Vector3(0f, 10f, 48f)), ("UpperArm.R", new Vector3(0f, -10f, -48f)), ("UpperLeg.L", new Vector3(-28f, 0f, 0f)),
            })
                if (bones.TryGetValue(name, out Transform bone)) bone.rotation = Quaternion.Euler(euler) * bone.rotation;
        }

        // Each renderer's mesh, and a copy with its lightmap zeroed (neutral) for the rows without.
        static List<(SkinnedMeshRenderer, Mesh, Mesh)> Neutralised(ModularCharacter character)
        {
            var meshes = new List<(SkinnedMeshRenderer, Mesh, Mesh)>();
            foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh without = Object.Instantiate(renderer.sharedMesh);
                without.hideFlags = HideFlags.HideAndDontSave;
                without.SetUVs(2, new List<Vector4>(new Vector4[without.vertexCount]));
                meshes.Add((renderer, renderer.sharedMesh, without));
            }
            return meshes;
        }

        static void Use(List<(SkinnedMeshRenderer Renderer, Mesh With, Mesh Without)> meshes, bool withLightMaps)
        {
            foreach ((SkinnedMeshRenderer renderer, Mesh with, Mesh without) in meshes)
            {
                float[] weights = Enumerable.Range(0, with.blendShapeCount).Select(renderer.GetBlendShapeWeight).ToArray();
                renderer.sharedMesh = withLightMaps ? with : without;
                for (int i = 0; i < weights.Length; i++) renderer.SetBlendShapeWeight(i, weights[i]);
            }
        }

        // The share of pixels that differ by more than noise (as Changes marks them) over a turntable.
        static float Moved(List<Texture2D> before, List<Texture2D> after)
        {
            long moved = 0, total = 0;
            foreach ((Texture2D a, Texture2D b) in before.Zip(after, (a, b) => (a, b)))
            {
                Color32[] x = a.GetPixels32(), y = b.GetPixels32();
                for (int i = 0; i < x.Length; i++)
                    if (Mathf.Max(Mathf.Abs(x[i].r - y[i].r), Mathf.Abs(x[i].g - y[i].g), Mathf.Abs(x[i].b - y[i].b)) > 8) moved++;
                total += x.Length;
            }
            return moved / (float)total;
        }

        static Texture2D Blank()
        {
            var image = new Texture2D(TileWidth, TileHeight, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            image.SetPixels32(Enumerable.Repeat(new Color32(0, 0, 0, 255), TileWidth * TileHeight).ToArray());
            image.Apply();
            return image;
        }
    }
}
