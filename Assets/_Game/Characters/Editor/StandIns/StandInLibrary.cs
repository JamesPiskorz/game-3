using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static Game.Characters.Modular.BodyRegions;
using static Game.Characters.StandIns.StandInMesh;
using Object = UnityEngine.Object;

namespace Game.Characters.StandIns
{
    /// <summary>
    /// Writes the procedural stand-in content library (body and wearable parts) to Content/StandIn, the dev content
    /// that the builder, generator and review tools run on until real art exists. Each part prefab carries its own
    /// skeleton copy, like an FBX import. Re-running overwrites assets in place, keeping their GUIDs.
    /// </summary>
    public static class StandInLibrary
    {
        public const string Folder = "Assets/_Game/Characters/Content/StandIn";
        public const string MeshFolder = Folder + "/Meshes";
        public const string MaterialFolder = Folder + "/Materials";
        public const string PrefabFolder = Folder + "/Parts";
        public const string BodyPrefabPath = Folder + "/StandInBody.prefab";

        const float Cloth = 0.012f;

        /// <summary>One wearable stand-in part.</summary>
        public sealed class Recipe
        {
            public string Id;
            public string Slot;
            public string[] Blocks = Array.Empty<string>();
            public BodyRegions Covers;
            public bool FollowsBodyShapes = true;
            public string Material;
            public StandInBone[] Bones = StandInSkeleton.Bones;
            public Action<StandInMesh> Build;
        }

        static readonly Vector3 HeadCentre = StandInSkeleton.HeadCentre;
        const float HeadR = StandInSkeleton.HeadRadius;

        static readonly StandInBone[] Ponytail = StandInSkeleton.Chain("Hair.Ponytail", "Head", new Vector3(0f, 1.69f, -0.112f), new Vector3(0f, 1.36f, -0.2f), 3);
        static readonly StandInBone[] TwinL = StandInSkeleton.Chain("Hair.TwinTail.L", "Head", new Vector3(-0.112f, 1.69f, -0.03f), new Vector3(-0.17f, 1.36f, -0.07f), 3);
        static readonly StandInBone[] TwinR = StandInSkeleton.Chain("Hair.TwinTail.R", "Head", new Vector3(0.112f, 1.69f, -0.03f), new Vector3(0.17f, 1.36f, -0.07f), 3);

        public static IEnumerable<Recipe> Parts()
        {
            // ---------------- tops
            yield return new Recipe
            {
                Id = "cloth.top.tshirt_01", Slot = "top", Material = "Cloth",
                Covers = UpperTorso | LowerTorso | UpperArmL | UpperArmR,
                Build = m =>
                {
                    m.AddTube(StandInBody.TubeFor(UpperTorso), Cloth, Cap.Open, Cap.Collar, Trim(0.12f, false, true), false, collarRadius: 0.07f);
                    m.AddTube(StandInBody.TubeFor(LowerTorso), Cloth, Cap.Open, Cap.Open, Trim(0.14f, true, false), false);
                    m.AddTube(StandInBody.TubeFor(UpperArmL), Cloth, Cap.Open, Cap.Open, Trim(0.14f, false, true), false);
                    m.AddTube(StandInBody.TubeFor(UpperArmR), Cloth, Cap.Open, Cap.Open, Trim(0.14f, false, true), false);
                },
            };
            yield return new Recipe
            {
                Id = "cloth.top.longsleeve_01", Slot = "top", Material = "Cloth",
                Covers = UpperTorso | LowerTorso | UpperArmL | UpperArmR | ForearmL | ForearmR,
                Build = m =>
                {
                    m.AddTube(StandInBody.TubeFor(UpperTorso), Cloth, Cap.Open, Cap.Collar, Trim(0.1f, false, true), false, collarRadius: 0.065f);
                    m.AddTube(StandInBody.TubeFor(LowerTorso), Cloth, Cap.Open, Cap.Open, Region1, false);
                    foreach (BodyRegions arm in new[] { UpperArmL, UpperArmR })
                        m.AddTube(StandInBody.TubeFor(arm), Cloth, Cap.Open, Cap.Open, Region1, false);
                    foreach (BodyRegions forearm in new[] { ForearmL, ForearmR })
                        m.AddTube(StandInBody.TubeFor(forearm), Cloth, Cap.Open, Cap.Open, Trim(0.16f, false, true), false);
                },
            };
            yield return new Recipe
            {
                Id = "cloth.top.tank_01", Slot = "top", Material = "Cloth",
                Covers = UpperTorso | LowerTorso,
                Build = m =>
                {
                    m.AddTube(StandInBody.TubeFor(UpperTorso), Cloth, Cap.Open, Cap.Collar, Trim(0.08f, false, true), false, collarRadius: 0.1f);
                    m.AddTube(StandInBody.TubeFor(LowerTorso), Cloth, Cap.Open, Cap.Open, Region1, false);
                },
            };

            // ---------------- bottoms
            yield return new Recipe
            {
                Id = "cloth.bottom.shorts_01", Slot = "bottom", Material = "Cloth",
                Covers = Pelvis | ThighL | ThighR,
                Build = m =>
                {
                    m.AddTube(StandInBody.TubeFor(Pelvis), Cloth, Cap.Closed, Cap.Open, Trim(0.16f, false, true), false);
                    m.AddTube(StandInBody.TubeFor(ThighL), Cloth, Cap.Open, Cap.Open, Trim(0.1f, false, true), false);
                    m.AddTube(StandInBody.TubeFor(ThighR), Cloth, Cap.Open, Cap.Open, Trim(0.1f, false, true), false);
                },
            };
            yield return new Recipe
            {
                Id = "cloth.bottom.pants_01", Slot = "bottom", Material = "Cloth",
                Covers = Pelvis | ThighL | ThighR | CalfL | CalfR,
                Build = m =>
                {
                    m.AddTube(StandInBody.TubeFor(Pelvis), Cloth, Cap.Closed, Cap.Open, Trim(0.14f, false, true), false);
                    foreach (BodyRegions thigh in new[] { ThighL, ThighR })
                        m.AddTube(StandInBody.TubeFor(thigh), Cloth, Cap.Open, Cap.Open, Region1, false);
                    foreach (BodyRegions calf in new[] { CalfL, CalfR })
                        m.AddTube(StandInBody.TubeFor(calf), Cloth, Cap.Open, Cap.Open, Trim(0.08f, false, true), false);
                },
            };
            yield return new Recipe
            {
                Id = "cloth.bottom.skirt_01", Slot = "bottom", Material = "Cloth",
                Covers = Pelvis,
                Build = m =>
                {
                    m.AddTube(StandInBody.TubeFor(Pelvis), Cloth * 0.5f, Cap.Closed, Cap.Open, new MaskFunction((a, b) => new Vector3(0f, 0f, 1f)), false);
                    m.AddSkirt(1.07f, 0.6f, 0.135f, 0.215f, 1.22f, 0.85f, Cloth, Trim(0.09f, true, true));
                },
            };

            // ---------------- one-pieces
            yield return new Recipe
            {
                Id = "cloth.onepiece.dress_01", Slot = "onepiece", Blocks = new[] { "top", "bottom" }, Material = "Cloth",
                Covers = UpperTorso | LowerTorso | Pelvis,
                Build = m =>
                {
                    m.AddTube(StandInBody.TubeFor(UpperTorso), Cloth, Cap.Open, Cap.Collar, Trim(0.1f, false, true), false, collarRadius: 0.085f);
                    m.AddTube(StandInBody.TubeFor(LowerTorso), Cloth, Cap.Open, Cap.Open, Region1, false);
                    m.AddTube(StandInBody.TubeFor(Pelvis), Cloth * 0.5f, Cap.Closed, Cap.Open, new MaskFunction((a, b) => new Vector3(0f, 0f, 1f)), false);
                    m.AddSkirt(1.07f, 0.55f, 0.135f, 0.23f, 1.22f, 0.85f, Cloth, Trim(0.08f, false, true));
                },
            };

            // ---------------- shoes
            yield return new Recipe
            {
                Id = "cloth.shoes.sneakers_01", Slot = "shoes", Material = "Cloth",
                Covers = FootL | FootR,
                Build = m =>
                {
                    foreach (BodyRegions foot in new[] { FootL, FootR })
                        m.AddTube(StandInBody.TubeFor(foot), 0.012f, Cap.Closed, Cap.Closed, Sole, false);
                },
            };
            yield return new Recipe
            {
                Id = "cloth.shoes.boots_01", Slot = "shoes", Material = "Cloth",
                Covers = FootL | FootR | CalfL | CalfR,
                Build = m =>
                {
                    foreach (BodyRegions foot in new[] { FootL, FootR })
                        m.AddTube(StandInBody.TubeFor(foot), 0.014f, Cap.Closed, Cap.Closed, Sole, false);
                    foreach (BodyRegions calf in new[] { CalfL, CalfR })
                        m.AddTube(StandInBody.TubeFor(calf), 0.016f, Cap.Collar, Cap.Open, Trim(0.08f, true, false), false, collarRadius: 0.06f);
                },
            };

            // ---------------- hair: back pieces (crown + length) and front pieces (bangs)
            yield return Hair("hair.back.crop_01", "hair.back", m => Crown(m));
            yield return Hair("hair.back.bob_01", "hair.back", m =>
            {
                Crown(m);
                m.AddCurtain(1.67f, 1.53f, 0.124f, 0.136f, -125f, 125f, null, Gradient(0.55f));
            });
            yield return Hair("hair.back.long_01", "hair.back", m =>
            {
                Crown(m);
                m.AddCurtain(1.67f, 1.53f, 0.124f, 0.134f, -125f, 125f, null, Region1);
                m.AddCurtain(1.6f, 1.16f, 0.13f, 0.155f, -62f, 62f, "Chest", Gradient(0.45f));
            });
            yield return Hair("hair.back.ponytail_01", "hair.back", m =>
            {
                Crown(m);
                m.AddChain(Ponytail, new Vector3(0f, 1.3f, -0.22f), 0.04f, 0.012f, Gradient(0.4f));
            }, Ponytail);
            yield return Hair("hair.back.twintails_01", "hair.back", m =>
            {
                Crown(m);
                m.AddChain(TwinL, new Vector3(-0.18f, 1.3f, -0.08f), 0.034f, 0.01f, Gradient(0.4f));
                m.AddChain(TwinR, new Vector3(0.18f, 1.3f, -0.08f), 0.034f, 0.01f, Gradient(0.4f));
            }, TwinL, TwinR);
            yield return Hair("hair.back.bun_01", "hair.back", m =>
            {
                Crown(m);
                m.AddEllipsoid(new Vector3(0f, 1.715f, -0.11f), new Vector3(0.055f, 0.05f, 0.05f), None, false, "Head", Region1);
            });
            yield return Hair("hair.front.blunt_01", "hair.front", m =>
                m.AddEllipsoid(HeadCentre, new Vector3(HeadR + 0.019f, HeadR + 0.019f, HeadR + 0.019f), None, false, "Head", Region1,
                    d => d.z > 0.25f && d.y > 0.52f));
            yield return Hair("hair.front.swept_01", "hair.front", m =>
                m.AddEllipsoid(HeadCentre, new Vector3(HeadR + 0.019f, HeadR + 0.019f, HeadR + 0.019f), None, false, "Head", Region1,
                    d => d.z > 0.2f && d.y > 0.5f + 0.3f * d.x));

            // ---------------- accessories (no body shapes reach the head)
            yield return new Recipe
            {
                Id = "acc.glasses.round_01", Slot = "glasses", Material = "Accessory", FollowsBodyShapes = false,
                Build = m =>
                {
                    foreach (float x in new[] { -0.04f, 0.04f })
                        m.AddEllipsoid(new Vector3(x, 1.655f, 0.112f), new Vector3(0.024f, 0.019f, 0.004f), None, false, "Head", Region1, d => Mathf.Abs(d.z) < 0.55f);
                    m.AddTube(new Tube { Region = None, Start = new Vector3(-0.016f, 1.66f, 0.113f), End = new Vector3(0.016f, 1.66f, 0.113f), R0 = 0.0025f, R1 = 0.0025f, Bone = "Head" }, 0f, Cap.Closed, Cap.Closed, Region1, false, withShapes: false);
                    foreach (float x in new[] { -0.064f, 0.064f })
                        m.AddTube(new Tube { Region = None, Start = new Vector3(x, 1.66f, 0.108f), End = new Vector3(x * 1.72f, 1.655f, -0.005f), R0 = 0.0025f, R1 = 0.0025f, Bone = "Head" }, 0f, Cap.Closed, Cap.Closed, Region1, false, withShapes: false);
                },
            };
            yield return new Recipe
            {
                Id = "acc.hat.beanie_01", Slot = "hat", Material = "Cloth", FollowsBodyShapes = false,
                Build = m => m.AddEllipsoid(HeadCentre + new Vector3(0f, 0.012f, -0.004f), new Vector3(0.137f, 0.135f, 0.137f), None, false, "Head",
                    (along, around) => along > 0.36f ? new Vector3(0f, 1f, 0f) : new Vector3(1f, 0f, 0f), d => d.y > 0.12f),
            };
        }

        static Recipe Hair(string id, string slot, Action<StandInMesh> build, params StandInBone[][] extraBones) => new Recipe
        {
            Id = id, Slot = slot, Material = "Hair", FollowsBodyShapes = false,
            Bones = extraBones.Length == 0 ? StandInSkeleton.Bones : StandInSkeleton.With(extraBones),
            Build = build,
        };

        // Shared crown: over the head, down the back to the nape, over the ears, with a high front hairline for bangs.
        static void Crown(StandInMesh mesh) =>
            mesh.AddEllipsoid(HeadCentre, new Vector3(HeadR + 0.014f, HeadR + 0.016f, HeadR + 0.014f), None, false, "Head", Region1,
                d => (d.z <= 0.3f && d.y > 0.2f) || (d.z > 0.3f && d.y > 0.62f) || (d.z < 0.1f && d.y > -0.45f) || (Mathf.Abs(d.x) > 0.8f && d.y > -0.15f));

        // Region 1 upper, region 2 for the sole (faces pointing down).
        static readonly MaskFunction Sole = (along, around) =>
            Mathf.Sin(around * 2f * Mathf.PI) < -0.35f ? new Vector3(0f, 1f, 0f) : new Vector3(1f, 0f, 0f);

        // ---------------------------------------------------------------- writing assets

        [MenuItem("Game/Characters/Stand-ins/Generate Library")]
        public static void Generate()
        {
            EnsureFolder(MeshFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);

            var materials = new Dictionary<string, Material>
            {
                ["Skin"] = SaveMaterial("StandIn_Skin", CullMode.Back, new Color(0.93f, 0.76f, 0.65f), new Color(0.82f, 0.56f, 0.5f), outline: 1.4f),
                ["Cloth"] = SaveMaterial("StandIn_Cloth", CullMode.Off, Color.white, new Color(0.7f, 0.66f, 0.74f), outline: 1.4f),
                ["Hair"] = SaveMaterial("StandIn_Hair", CullMode.Off, Color.white, new Color(0.7f, 0.66f, 0.74f), outline: 1.2f),
                ["Accessory"] = SaveMaterial("StandIn_Accessory", CullMode.Back, Color.white, new Color(0.7f, 0.66f, 0.74f), outline: 0.8f),
            };

            var body = new StandInMesh(StandInSkeleton.Bones);
            StandInBody.Build(body);
            Mesh bodyMesh = SaveAsset(body.Build("StandInBody", withShapes: true), $"{MeshFolder}/StandInBody.asset");
            SaveBody(bodyMesh, materials["Skin"]);

            foreach (Recipe recipe in Parts())
            {
                var mesh = new StandInMesh(recipe.Bones);
                recipe.Build(mesh);
                Mesh asset = SaveAsset(mesh.Build(recipe.Id, recipe.FollowsBodyShapes), $"{MeshFolder}/{recipe.Id}.asset");
                SavePart(recipe, asset, materials[recipe.Material]);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[StandIns] Wrote the body and {Parts().Count()} parts to {Folder}");
        }

        public static string PartPath(string id) => $"{PrefabFolder}/{id}.prefab";

        static void SaveBody(Mesh mesh, Material skin)
        {
            var root = new GameObject("StandInBody");
            var bones = new Dictionary<string, Transform>();
            Transform armature = StandInSkeleton.Build(root.transform, StandInSkeleton.Bones, bones);
            SkinnedMeshRenderer renderer = AddRenderer(root.transform, "Body", mesh, StandInSkeleton.Bones, bones, skin);
            root.AddComponent<ModularCharacter>().Initialize(armature, renderer);
            PrefabUtility.SaveAsPrefabAsset(root, BodyPrefabPath);
            Object.DestroyImmediate(root);
        }

        static void SavePart(Recipe recipe, Mesh mesh, Material material)
        {
            var root = new GameObject(recipe.Id);
            var part = root.AddComponent<CharacterPart>();
            part.Slot = recipe.Slot;
            part.BlocksSlots = recipe.Blocks;
            part.CoveredRegions = recipe.Covers;
            part.FollowsBodyShapes = recipe.FollowsBodyShapes;
            var bones = new Dictionary<string, Transform>();
            StandInSkeleton.Build(root.transform, recipe.Bones, bones);
            AddRenderer(root.transform, "Mesh", mesh, recipe.Bones, bones, material);
            PrefabUtility.SaveAsPrefabAsset(root, PartPath(recipe.Id));
            Object.DestroyImmediate(root);
        }

        static SkinnedMeshRenderer AddRenderer(Transform parent, string name, Mesh mesh, IEnumerable<StandInBone> bones, Dictionary<string, Transform> map, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones.Select(bone => map[bone.Name]).ToArray();
            renderer.rootBone = map["Hips"];
            renderer.sharedMaterial = material;
            renderer.updateWhenOffscreen = true; // stand-ins only; real parts get authored bounds
            return renderer;
        }

        static Material SaveMaterial(string name, CullMode cull, Color baseColour, Color shade, float outline)
        {
            var material = new Material(Shader.Find(ToonMaterial.ShaderName)) { name = name };
            material.EnableKeyword(ToonMaterial.VertexColourMaskKeyword);
            material.SetFloat("_MaskFromVertexColour", 1f);
            material.SetColor(ToonMaterial.BaseColor, baseColour);
            material.SetColor(ToonMaterial.BaseShade, shade);
            material.SetFloat(ToonMaterial.Cull, (float)cull);
            material.SetFloat(ToonMaterial.OutlineWidth, outline);
            ToonMaterial.SetRegionColour(material, 0, new Color(0.55f, 0.6f, 0.75f));
            ToonMaterial.SetRegionColour(material, 1, new Color(0.25f, 0.22f, 0.2f));
            ToonMaterial.SetRegionColour(material, 2, Color.white);
            return SaveAsset(material, $"{MaterialFolder}/{name}.mat");
        }

        /// <summary>Creates the asset, or overwrites an existing one in place so its GUID (and references to it) survive.</summary>
        static T SaveAsset<T>(T asset, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                return asset;
            }
            EditorUtility.CopySerialized(asset, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(asset);
            return existing;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
