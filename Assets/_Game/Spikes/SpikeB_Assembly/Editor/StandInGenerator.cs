using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Modular;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using static Game.Characters.Modular.BodyRegions;
using Object = UnityEngine.Object;

namespace Game.Spikes.SpikeB
{
    /// <summary>
    /// Builds Spike B's stand-in assets procedurally:
    /// - a capsule mannequin body with region IDs and three body shapes;
    /// - a T-shirt and shorts that carry the same shapes, computed from the body surface under each cloth vertex
    ///   (what the scripted Blender shape-key transfer will do in production);
    /// - hair with a ponytail on bones that only the hair has.
    /// Every part prefab contains its own copy of the skeleton, as an FBX import would.
    /// </summary>
    static class StandInGenerator
    {
        public const string Folder = "Assets/_Game/Spikes/SpikeB_Assembly";
        public const string GeneratedFolder = Folder + "/Generated";
        public const string CharacterPrefabPath = GeneratedFolder + "/StandInCharacter.prefab";
        public const string ScenePath = Folder + "/SpikeB_Assembly.unity";
        public static readonly string[] PartPrefabPaths =
        {
            GeneratedFolder + "/cloth.top.tshirt_01.prefab",
            GeneratedFolder + "/cloth.bottom.shorts_01.prefab",
            GeneratedFolder + "/hair.ponytail_01.prefab",
        };

        public static readonly string[] BodyShapes = { "body.weight", "body.chest", "body.hips" };

        const int Sides = 24;
        const int Rings = 12;
        const float ClothOffset = 0.012f;
        static readonly Vector3 HeadCentre = new Vector3(0f, 1.64f, 0f);
        const float HeadRadius = 0.11f;

        [MenuItem("Game/Spikes/Spike B/Generate Stand-in Assets")]
        public static void Generate()
        {
            EnsureFolder(GeneratedFolder);
            Material skin = SaveMaterial("StandInSkin", new Color(0.93f, 0.74f, 0.62f), CullMode.Back);
            Material shirtMaterial = SaveMaterial("StandInShirt", new Color(0.22f, 0.42f, 0.78f), CullMode.Off);
            Material shortsMaterial = SaveMaterial("StandInShorts", new Color(0.16f, 0.17f, 0.22f), CullMode.Off);
            Material hairMaterial = SaveMaterial("StandInHair", new Color(0.33f, 0.19f, 0.11f), CullMode.Off);

            var body = new MeshBuilder(SkeletonBones);
            foreach (Tube tube in BodyTubes())
                AddTube(body, tube, 0f, Cap.Closed, Cap.Closed);
            AddSphere(body, HeadCentre, HeadRadius, Head, "Head");
            AddSphere(body, new Vector3(0f, 1.62f, 0.105f), 0.022f, Head, "Head"); // nose, to show which way the head faces
            Mesh bodyMesh = SaveAsset(body.Build("StandInBody", withShapes: true), GeneratedFolder + "/StandInBody.asset");
            SaveCharacter(bodyMesh, skin);

            var shirt = new MeshBuilder(SkeletonBones);
            AddTube(shirt, BodyTube(UpperTorso), ClothOffset, Cap.Open, Cap.Collar, collarRadius: 0.07f);
            AddTube(shirt, BodyTube(LowerTorso), ClothOffset, Cap.Open, Cap.Open);
            AddTube(shirt, BodyTube(UpperArmL), ClothOffset, Cap.Open, Cap.Open);
            AddTube(shirt, BodyTube(UpperArmR), ClothOffset, Cap.Open, Cap.Open);
            SavePart(PartPrefabPaths[0], "top", UpperTorso | LowerTorso | UpperArmL | UpperArmR, true, shirt, shirtMaterial, SkeletonBones);

            var shorts = new MeshBuilder(SkeletonBones);
            AddTube(shorts, BodyTube(Pelvis), ClothOffset, Cap.Closed, Cap.Open);
            AddTube(shorts, BodyTube(ThighL), ClothOffset, Cap.Open, Cap.Open);
            AddTube(shorts, BodyTube(ThighR), ClothOffset, Cap.Open, Cap.Open);
            SavePart(PartPrefabPaths[1], "bottom", Pelvis | ThighL | ThighR, true, shorts, shortsMaterial, SkeletonBones);

            BoneDef[] hairBones = SkeletonBones.Concat(PonytailBones).ToArray();
            var hair = new MeshBuilder(hairBones);
            AddSphere(hair, HeadCentre, HeadRadius + 0.016f, Head, "Head", direction =>
                direction.y > 0.25f                                        // crown, down to the hairline
                || (direction.z < 0.05f && direction.y > -0.6f)            // back of the head and nape
                || (Mathf.Abs(direction.x) > 0.8f && direction.y > -0.35f)); // sides, over the ears
            AddTube(hair, new Tube
            {
                Region = Head, Start = Bone(hairBones, "Hair.Ponytail.1"), End = new Vector3(0f, 1.36f, -0.2f), R0 = 0.04f, R1 = 0.012f,
                Bone = "Hair.Ponytail.1", ChildBone = "Hair.Ponytail.2", ChildBlendStart = 0.15f, ChildBlendMax = 1f,
            }, 0f, Cap.Closed, Cap.Closed);
            SavePart(PartPrefabPaths[2], "hair", None, false, hair, hairMaterial, hairBones);

            AssetDatabase.SaveAssets();
            Debug.Log("[SpikeB] Generated stand-in assets in " + GeneratedFolder);
        }

        [MenuItem("Game/Spikes/Spike B/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Camera camera = Camera.main;
            camera.transform.position = new Vector3(0f, 1.15f, 3.2f);
            camera.transform.LookAt(new Vector3(0f, 1f, 0f));

            var character = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefabPath));
            var demo = new GameObject("Spike B Demo").AddComponent<SpikeBDemo>();
            demo.Character = character.GetComponent<ModularCharacter>();
            demo.Outfit = PartPrefabPaths.Select(path => AssetDatabase.LoadAssetAtPath<CharacterPart>(path)).ToList();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[SpikeB] Saved demo scene " + ScenePath);
        }

        // ---------------------------------------------------------------- skeleton

        readonly struct BoneDef
        {
            public readonly string Name;
            public readonly string Parent;
            public readonly Vector3 Position;

            public BoneDef(string name, string parent, Vector3 position)
            {
                Name = name;
                Parent = parent;
                Position = position;
            }
        }

        // Rest pose: T-pose facing +Z, character's left at -X, metres. Parents are listed before children.
        static readonly BoneDef[] SkeletonBones =
        {
            new BoneDef("Hips", null, new Vector3(0f, 0.95f, 0f)),
            new BoneDef("Spine", "Hips", new Vector3(0f, 1.07f, 0f)),
            new BoneDef("Chest", "Spine", new Vector3(0f, 1.24f, 0f)),
            new BoneDef("Neck", "Chest", new Vector3(0f, 1.44f, 0f)),
            new BoneDef("Head", "Neck", new Vector3(0f, 1.54f, 0f)),
            new BoneDef("UpperArm.L", "Chest", new Vector3(-0.16f, 1.4f, 0f)),
            new BoneDef("LowerArm.L", "UpperArm.L", new Vector3(-0.44f, 1.4f, 0f)),
            new BoneDef("Hand.L", "LowerArm.L", new Vector3(-0.69f, 1.4f, 0f)),
            new BoneDef("UpperArm.R", "Chest", new Vector3(0.16f, 1.4f, 0f)),
            new BoneDef("LowerArm.R", "UpperArm.R", new Vector3(0.44f, 1.4f, 0f)),
            new BoneDef("Hand.R", "LowerArm.R", new Vector3(0.69f, 1.4f, 0f)),
            new BoneDef("UpperLeg.L", "Hips", new Vector3(-0.09f, 0.9f, 0f)),
            new BoneDef("LowerLeg.L", "UpperLeg.L", new Vector3(-0.09f, 0.5f, 0f)),
            new BoneDef("Foot.L", "LowerLeg.L", new Vector3(-0.09f, 0.09f, 0f)),
            new BoneDef("UpperLeg.R", "Hips", new Vector3(0.09f, 0.9f, 0f)),
            new BoneDef("LowerLeg.R", "UpperLeg.R", new Vector3(0.09f, 0.5f, 0f)),
            new BoneDef("Foot.R", "LowerLeg.R", new Vector3(0.09f, 0.09f, 0f)),
        };

        // Bones only the hair has; equipping grafts them under the character's Head.
        static readonly BoneDef[] PonytailBones =
        {
            new BoneDef("Hair.Ponytail.1", "Head", new Vector3(0f, 1.67f, -0.115f)),
            new BoneDef("Hair.Ponytail.2", "Hair.Ponytail.1", new Vector3(0f, 1.52f, -0.16f)),
        };

        static Vector3 Bone(IEnumerable<BoneDef> bones, string name) => bones.First(bone => bone.Name == name).Position;

        // ---------------------------------------------------------------- body shapes

        const BodyRegions Fleshy = Pelvis | LowerTorso | UpperTorso | UpperArmL | UpperArmR | ForearmL | ForearmR | ThighL | ThighR | CalfL | CalfR;

        /// <summary>
        /// How each body shape moves the body surface at a point. Cloth vertices use the body point beneath them,
        /// so clothing moves exactly as much as the skin it covers.
        /// </summary>
        static Vector3[] ShapeDeltas(BodyRegions region, float t, Vector3 normal, float surfaceRadius)
        {
            var deltas = new Vector3[BodyShapes.Length];
            if ((Fleshy & region) != 0)
                deltas[0] = normal * (surfaceRadius * 0.22f);
            if (region == UpperTorso)
            {
                float front = Mathf.Max(0f, normal.z);
                deltas[1] = normal * (0.055f * front * front * Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - 0.05f) / 0.85f)));
            }
            if (region == Pelvis)
                deltas[2] = new Vector3(normal.x, 0f, 0f) * (0.05f * Mathf.Sin(Mathf.PI * t));
            if (region == ThighL || region == ThighR)
                deltas[2] = new Vector3(normal.x, 0f, 0f) * (0.04f * Mathf.Clamp01(1f - t / 0.45f));
            return deltas;
        }

        // ---------------------------------------------------------------- geometry

        sealed class Tube
        {
            public BodyRegions Region;
            public Vector3 Start, End;
            public float R0, R1;
            public float Width = 1f, Depth = 1f;     // ellipse scale across / front-to-back (vertical tubes: X / Z)
            public string Bone, ParentBone, ChildBone;
            public float ParentBlend = 0.2f;         // below this t, weight blends towards ParentBone (50% at t = 0)
            public float ChildBlendStart = 0.8f;     // above this t, weight blends towards ChildBone...
            public float ChildBlendMax = 0.5f;       // ...reaching this weight at t = 1
        }

        enum Cap { Open, Closed, Collar }

        static IEnumerable<Tube> BodyTubes()
        {
            yield return new Tube { Region = Pelvis, Start = new Vector3(0f, 0.84f, 0f), End = new Vector3(0f, 1.07f, 0f), R0 = 0.15f, R1 = 0.13f, Width = 1.22f, Depth = 0.78f, Bone = "Hips", ChildBone = "Spine" };
            yield return new Tube { Region = LowerTorso, Start = new Vector3(0f, 1.07f, 0f), End = new Vector3(0f, 1.24f, 0f), R0 = 0.13f, R1 = 0.145f, Width = 1.22f, Depth = 0.75f, Bone = "Spine", ParentBone = "Hips", ChildBone = "Chest" };
            yield return new Tube { Region = UpperTorso, Start = new Vector3(0f, 1.24f, 0f), End = new Vector3(0f, 1.45f, 0f), R0 = 0.145f, R1 = 0.13f, Width = 1.22f, Depth = 0.75f, Bone = "Chest", ParentBone = "Spine", ChildBone = "Neck" };
            yield return new Tube { Region = Neck, Start = new Vector3(0f, 1.42f, 0f), End = new Vector3(0f, 1.58f, 0f), R0 = 0.05f, R1 = 0.045f, Bone = "Neck", ParentBone = "Chest", ChildBone = "Head" };

            foreach (bool left in new[] { true, false })
            {
                float x = left ? -1f : 1f;
                string s = left ? ".L" : ".R";
                yield return new Tube { Region = left ? UpperArmL : UpperArmR, Start = new Vector3(0.15f * x, 1.4f, 0f), End = new Vector3(0.44f * x, 1.4f, 0f), R0 = 0.052f, R1 = 0.042f, Bone = "UpperArm" + s, ParentBone = "Chest", ChildBone = "LowerArm" + s };
                yield return new Tube { Region = left ? ForearmL : ForearmR, Start = new Vector3(0.44f * x, 1.4f, 0f), End = new Vector3(0.69f * x, 1.4f, 0f), R0 = 0.041f, R1 = 0.032f, Bone = "LowerArm" + s, ParentBone = "UpperArm" + s, ChildBone = "Hand" + s };
                yield return new Tube { Region = left ? HandL : HandR, Start = new Vector3(0.69f * x, 1.4f, 0f), End = new Vector3(0.84f * x, 1.4f, 0f), R0 = 0.03f, R1 = 0.022f, Bone = "Hand" + s, ParentBone = "LowerArm" + s };
                yield return new Tube { Region = left ? ThighL : ThighR, Start = new Vector3(0.09f * x, 0.9f, 0f), End = new Vector3(0.09f * x, 0.5f, 0f), R0 = 0.078f, R1 = 0.055f, Bone = "UpperLeg" + s, ParentBone = "Hips", ChildBone = "LowerLeg" + s };
                yield return new Tube { Region = left ? CalfL : CalfR, Start = new Vector3(0.09f * x, 0.5f, 0f), End = new Vector3(0.09f * x, 0.08f, 0f), R0 = 0.054f, R1 = 0.035f, Bone = "LowerLeg" + s, ParentBone = "UpperLeg" + s, ChildBone = "Foot" + s };
                yield return new Tube { Region = left ? FootL : FootR, Start = new Vector3(0.09f * x, 0.045f, -0.04f), End = new Vector3(0.09f * x, 0.04f, 0.16f), R0 = 0.042f, R1 = 0.034f, Bone = "Foot" + s, ParentBone = "LowerLeg" + s };
            }
        }

        static Tube BodyTube(BodyRegions region) => BodyTubes().First(tube => tube.Region == region);

        static (string, float)[] SkinWeights(Tube tube, float t)
        {
            float parent = tube.ParentBone != null && t < tube.ParentBlend ? 0.5f * (1f - t / tube.ParentBlend) : 0f;
            float child = tube.ChildBone != null && t > tube.ChildBlendStart ? tube.ChildBlendMax * (t - tube.ChildBlendStart) / (1f - tube.ChildBlendStart) : 0f;
            return new[] { (tube.Bone, 1f - parent - child), (tube.ParentBone, parent), (tube.ChildBone, child) };
        }

        /// <param name="offset">Distance outside the body surface (cloth thickness); 0 for the body itself.</param>
        static void AddTube(MeshBuilder mesh, Tube tube, float offset, Cap startCap, Cap endCap, float collarRadius = 0f)
        {
            Vector3 axis = (tube.End - tube.Start).normalized;
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.forward)) > 0.9f ? Vector3.up : Vector3.forward;
            Vector3 u = Vector3.Cross(axis, reference).normalized;
            Vector3 v = Vector3.Cross(u, axis);

            int first = mesh.VertexCount;
            for (int ring = 0; ring <= Rings; ring++)
            {
                float t = ring / (float)Rings;
                Vector3 centre = Vector3.Lerp(tube.Start, tube.End, t);
                float radius = Mathf.Lerp(tube.R0, tube.R1, t);
                BoneWeight weights = mesh.Weights(SkinWeights(tube, t));
                for (int side = 0; side < Sides; side++)
                {
                    float angle = side * 2f * Mathf.PI / Sides;
                    float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                    Vector3 spoke = u * (c * tube.Width) + v * (s * tube.Depth);
                    Vector3 normal = (u * (c / tube.Width) + v * (s / tube.Depth)).normalized;
                    Vector3 position = centre + spoke * radius + normal * offset;
                    mesh.AddVertex(position, normal, tube.Region, weights, ShapeDeltas(tube.Region, t, normal, radius * spoke.magnitude));
                }
            }

            for (int ring = 0; ring < Rings; ring++)
                for (int side = 0; side < Sides; side++)
                {
                    int a = first + ring * Sides + side;
                    int b = first + ring * Sides + (side + 1) % Sides;
                    mesh.AddQuad(a, b, b + Sides, a + Sides);
                }

            AddCap(mesh, tube, startCap, first, 0f, -axis, u, v, offset, collarRadius);
            AddCap(mesh, tube, endCap, first + Rings * Sides, 1f, axis, u, v, offset, collarRadius);
        }

        static void AddCap(MeshBuilder mesh, Tube tube, Cap cap, int ringStart, float t, Vector3 normal, Vector3 u, Vector3 v, float offset, float collarRadius)
        {
            if (cap == Cap.Open) return;

            Vector3 centre = Vector3.Lerp(tube.Start, tube.End, t) + normal * offset;
            BoneWeight weights = mesh.Weights(SkinWeights(tube, t));
            int rim = mesh.VertexCount;
            for (int side = 0; side < Sides; side++)
                mesh.AddVertex(mesh.Position(ringStart + side), normal, tube.Region, weights, mesh.Deltas(ringStart + side));

            if (cap == Cap.Closed)
            {
                int hub = mesh.AddVertex(centre, normal, tube.Region, weights, null);
                for (int side = 0; side < Sides; side++)
                    mesh.AddTriangle(hub, rim + side, rim + (side + 1) % Sides);
                return;
            }

            // Collar: an annulus leaving a hole for the neck.
            int inner = mesh.VertexCount;
            for (int side = 0; side < Sides; side++)
            {
                float angle = side * 2f * Mathf.PI / Sides;
                Vector3 position = centre + (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * collarRadius;
                mesh.AddVertex(position, normal, tube.Region, weights, null);
            }
            for (int side = 0; side < Sides; side++)
            {
                int next = (side + 1) % Sides;
                mesh.AddQuad(rim + side, rim + next, inner + next, inner + side);
            }
        }

        static void AddSphere(MeshBuilder mesh, Vector3 centre, float radius, BodyRegions region, string bone, Func<Vector3, bool> keep = null)
        {
            const int Latitudes = 16, Longitudes = 24;
            BoneWeight weights = mesh.Weights(new[] { (bone, 1f) });
            int first = mesh.VertexCount;
            for (int lat = 0; lat <= Latitudes; lat++)
            {
                float theta = Mathf.PI * lat / Latitudes;
                for (int lon = 0; lon < Longitudes; lon++)
                {
                    float phi = 2f * Mathf.PI * lon / Longitudes;
                    var direction = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    mesh.AddVertex(centre + direction * radius, direction, region, weights, null);
                }
            }

            for (int lat = 0; lat < Latitudes; lat++)
                for (int lon = 0; lon < Longitudes; lon++)
                {
                    int a = first + lat * Longitudes + lon;
                    int b = first + lat * Longitudes + (lon + 1) % Longitudes;
                    Vector3 middle = ((mesh.Position(a) + mesh.Position(b + Longitudes)) * 0.5f - centre).normalized;
                    if (keep == null || keep(middle))
                        mesh.AddQuad(a, b, b + Longitudes, a + Longitudes);
                }
        }

        sealed class MeshBuilder
        {
            readonly BoneDef[] _bones;
            readonly Dictionary<string, int> _boneIndex;
            readonly List<Vector3> _positions = new List<Vector3>();
            readonly List<Vector3> _normals = new List<Vector3>();
            readonly List<Color32> _colours = new List<Color32>();
            readonly List<BoneWeight> _weights = new List<BoneWeight>();
            readonly List<Vector3[]> _deltas = new List<Vector3[]>();
            readonly List<int> _triangles = new List<int>();

            public MeshBuilder(BoneDef[] bones)
            {
                _bones = bones;
                _boneIndex = bones.Select((bone, index) => (bone.Name, index)).ToDictionary(pair => pair.Name, pair => pair.index);
            }

            public int VertexCount => _positions.Count;
            public Vector3 Position(int index) => _positions[index];
            public Vector3[] Deltas(int index) => _deltas[index];

            public int AddVertex(Vector3 position, Vector3 normal, BodyRegions region, BoneWeight weights, Vector3[] shapeDeltas)
            {
                _positions.Add(position);
                _normals.Add(normal);
                _colours.Add(new Color32((byte)BodyRegionIds.IdOf(region), 0, 0, 255));
                _weights.Add(weights);
                _deltas.Add(shapeDeltas);
                return _positions.Count - 1;
            }

            /// <summary>Adds a triangle, flipping its winding if needed so it faces along its vertex normals.</summary>
            public void AddTriangle(int a, int b, int c)
            {
                Vector3 facing = Vector3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]);
                Vector3 outward = _normals[a] + _normals[b] + _normals[c];
                if (Vector3.Dot(facing, outward) < 0f) (b, c) = (c, b);
                _triangles.Add(a);
                _triangles.Add(b);
                _triangles.Add(c);
            }

            public void AddQuad(int a, int b, int c, int d)
            {
                AddTriangle(a, b, c);
                AddTriangle(a, c, d);
            }

            public BoneWeight Weights((string bone, float weight)[] influences)
            {
                var used = influences.Where(i => i.bone != null && i.weight > 0f).OrderByDescending(i => i.weight).Take(4).ToArray();
                float total = used.Sum(i => i.weight);
                var result = new BoneWeight();
                if (used.Length > 0) { result.boneIndex0 = _boneIndex[used[0].bone]; result.weight0 = used[0].weight / total; }
                if (used.Length > 1) { result.boneIndex1 = _boneIndex[used[1].bone]; result.weight1 = used[1].weight / total; }
                if (used.Length > 2) { result.boneIndex2 = _boneIndex[used[2].bone]; result.weight2 = used[2].weight / total; }
                if (used.Length > 3) { result.boneIndex3 = _boneIndex[used[3].bone]; result.weight3 = used[3].weight / total; }
                return result;
            }

            public Mesh Build(string name, bool withShapes)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_positions);
                mesh.SetNormals(_normals);
                mesh.SetColors(_colours);
                mesh.boneWeights = _weights.ToArray();
                // Rest bones have identity rotation and the mesh sits at the character's origin.
                mesh.bindposes = _bones.Select(bone => Matrix4x4.Translate(-bone.Position)).ToArray();
                mesh.SetTriangles(_triangles, 0);
                if (withShapes)
                    for (int shape = 0; shape < BodyShapes.Length; shape++)
                        mesh.AddBlendShapeFrame(BodyShapes[shape], 100f, _deltas.Select(d => d == null ? Vector3.zero : d[shape]).ToArray(), null, null);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // ---------------------------------------------------------------- assets

        static void SaveCharacter(Mesh bodyMesh, Material skin)
        {
            var root = new GameObject("StandInCharacter");
            var bones = new Dictionary<string, Transform>();
            Transform armature = BuildSkeleton(root.transform, SkeletonBones, bones);
            SkinnedMeshRenderer body = AddRenderer(root.transform, "Body", bodyMesh, SkeletonBones, bones, skin);
            root.AddComponent<ModularCharacter>().Initialize(armature, body);
            PrefabUtility.SaveAsPrefabAsset(root, CharacterPrefabPath);
            Object.DestroyImmediate(root);
        }

        static void SavePart(string path, string slot, BodyRegions covers, bool followsBodyShapes, MeshBuilder builder, Material material, BoneDef[] bones)
        {
            string id = System.IO.Path.GetFileNameWithoutExtension(path);
            Mesh mesh = SaveAsset(builder.Build(id, withShapes: followsBodyShapes), $"{GeneratedFolder}/{id}.asset");

            var root = new GameObject(id);
            var part = root.AddComponent<CharacterPart>();
            part.Slot = slot;
            part.CoveredRegions = covers;
            part.FollowsBodyShapes = followsBodyShapes;
            var boneMap = new Dictionary<string, Transform>();
            BuildSkeleton(root.transform, bones, boneMap);
            AddRenderer(root.transform, "Mesh", mesh, bones, boneMap, material);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        static Transform BuildSkeleton(Transform parent, IEnumerable<BoneDef> bones, Dictionary<string, Transform> map)
        {
            var armature = new GameObject("Armature").transform;
            armature.SetParent(parent, false);
            foreach (BoneDef bone in bones)
            {
                var transform = new GameObject(bone.Name).transform;
                transform.SetParent(bone.Parent == null ? armature : map[bone.Parent], false);
                transform.position = bone.Position; // the root sits at the origin, so world = rest position
                map[bone.Name] = transform;
            }
            return armature;
        }

        static SkinnedMeshRenderer AddRenderer(Transform parent, string name, Mesh mesh, IEnumerable<BoneDef> bones, Dictionary<string, Transform> map, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones.Select(bone => map[bone.Name]).ToArray();
            renderer.rootBone = map["Hips"];
            renderer.sharedMaterial = material;
            renderer.updateWhenOffscreen = true; // stand-in only; real parts get authored bounds
            return renderer;
        }

        static Material SaveMaterial(string name, Color colour, CullMode cull)
        {
            var material = new Material(Shader.Find("Game/Characters/StandIn")) { name = name };
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Cull", (float)cull);
            return SaveAsset(material, $"{GeneratedFolder}/{name}.mat");
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
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
