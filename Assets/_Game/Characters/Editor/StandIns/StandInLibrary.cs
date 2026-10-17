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
            public string[] SpringChains = Array.Empty<string>();
            public Action<StandInMesh> Build;
        }

        static readonly Vector3 HeadCentre = StandInSkeleton.HeadCentre;
        const float HeadR = StandInSkeleton.HeadRadius;

        static readonly StandInBone[] Ponytail = StandInSkeleton.Chain("Hair.Ponytail", "Head", new Vector3(0f, 1.69f, -0.112f), new Vector3(0f, 1.36f, -0.2f), 3);
        static readonly StandInBone[] TwinL = StandInSkeleton.Chain("Hair.TwinTail.L", "Head", new Vector3(-0.112f, 1.69f, -0.03f), new Vector3(-0.17f, 1.36f, -0.07f), 3);
        static readonly StandInBone[] SideTail = StandInSkeleton.Chain("Hair.SideTail", "Head", new Vector3(0.105f, 1.67f, -0.06f), new Vector3(0.17f, 1.34f, -0.05f), 3);
        static readonly StandInBone[] TwinR = StandInSkeleton.Chain("Hair.TwinTail.R", "Head", new Vector3(0.112f, 1.69f, -0.03f), new Vector3(0.17f, 1.36f, -0.07f), 3);
        static readonly StandInBone[] Braid = StandInSkeleton.Chain("Hair.Braid", "Head", new Vector3(0f, 1.6f, -0.118f), new Vector3(0f, 1.14f, -0.17f), 4);

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

            // ---------------- more tops
            yield return new Recipe
            {
                Id = "cloth.top.hoodie_01", Slot = "top", Material = "Cloth",
                Covers = UpperTorso | LowerTorso | UpperArmL | UpperArmR | ForearmL | ForearmR,
                Build = m =>
                {
                    const float thick = Cloth * 1.8f;
                    m.AddTube(StandInBody.TubeFor(UpperTorso), thick, Cap.Open, Cap.Collar, Region1, false, collarRadius: 0.075f);
                    m.AddTube(StandInBody.TubeFor(LowerTorso), thick, Cap.Open, Cap.Open, Trim(0.12f, true, false), false);
                    foreach (BodyRegions arm in new[] { UpperArmL, UpperArmR })
                        m.AddTube(StandInBody.TubeFor(arm), thick, Cap.Open, Cap.Open, Region1, false);
                    foreach (BodyRegions forearm in new[] { ForearmL, ForearmR })
                        m.AddTube(StandInBody.TubeFor(forearm), thick, Cap.Open, Cap.Open, Trim(0.18f, false, true), false);
                    // The hood, lying across the shoulders, and a front pocket in the trim colour.
                    m.AddEllipsoid(new Vector3(0f, 1.47f, -0.09f), new Vector3(0.115f, 0.075f, 0.06f), None, false, "Chest", Region1, d => d.z < 0.35f);
                    m.AddTube(new Tube { Region = LowerTorso, Start = new Vector3(-0.08f, 1.125f, 0.123f), End = new Vector3(0.08f, 1.125f, 0.123f),
                            R0 = 0.04f, R1 = 0.04f, Width = 1f, Depth = 0.22f, Bone = "Spine", ShapeRadius = 0.1f },
                        0f, Cap.Closed, Cap.Closed, RegionTwo, false);
                },
            };
            yield return new Recipe
            {
                Id = "cloth.top.blouse_01", Slot = "top", Material = "Cloth",
                Covers = UpperTorso | LowerTorso | UpperArmL | UpperArmR,
                Build = m =>
                {
                    m.AddTube(StandInBody.TubeFor(UpperTorso), Cloth, Cap.Open, Cap.Collar, Trim(0.2f, false, true), false, collarRadius: 0.06f);
                    m.AddTube(StandInBody.TubeFor(LowerTorso), Cloth, Cap.Open, Cap.Open, Region1, false);
                    // Short puffed sleeves.
                    foreach (BodyRegions arm in new[] { UpperArmL, UpperArmR })
                    {
                        Tube sleeve = StandInBody.TubeFor(arm);
                        sleeve.End = Vector3.Lerp(sleeve.Start, sleeve.End, 0.55f);
                        sleeve.R0 = 0.064f;
                        sleeve.R1 = 0.06f;
                        m.AddTube(sleeve, Cloth, Cap.Open, Cap.Open, Trim(0.2f, false, true), false);
                    }
                },
            };
            yield return new Recipe
            {
                Id = "cloth.top.blazer_01", Slot = "top", Material = "Cloth",
                Covers = UpperTorso | LowerTorso | UpperArmL | UpperArmR | ForearmL | ForearmR,
                Build = m =>
                {
                    const float thick = Cloth * 1.5f;
                    m.AddTube(StandInBody.TubeFor(UpperTorso), thick, Cap.Open, Cap.Collar, Region1, false, collarRadius: 0.07f);
                    m.AddTube(StandInBody.TubeFor(LowerTorso), thick, Cap.Open, Cap.Open, Trim(0.1f, true, false), false);
                    // Lapels as their own flat strips in the trim colour, opening into a V towards the collar (a colour
                    // band painted on the tube would be finer than its vertices and smear). They take the torso's shape
                    // response, so a fuller chest or build doesn't swallow them.
                    foreach (float side in new[] { -1f, 1f })
                        m.AddTube(new Tube { Region = UpperTorso, Start = new Vector3(0.012f * side, 1.25f, 0.129f), End = new Vector3(0.075f * side, 1.43f, 0.109f),
                                R0 = 0.016f, R1 = 0.024f, Width = 1f, Depth = 0.3f, Bone = "Chest", ShapeRadius = 0.1f },
                            0f, Cap.Closed, Cap.Closed, RegionTwo, false);
                    foreach (BodyRegions arm in new[] { UpperArmL, UpperArmR })
                        m.AddTube(StandInBody.TubeFor(arm), thick, Cap.Open, Cap.Open, Region1, false);
                    foreach (BodyRegions forearm in new[] { ForearmL, ForearmR })
                        m.AddTube(StandInBody.TubeFor(forearm), thick, Cap.Open, Cap.Open, Region1, false);
                },
            };

            // ---------------- more bottoms
            yield return new Recipe
            {
                Id = "cloth.bottom.longskirt_01", Slot = "bottom", Material = "Cloth",
                Covers = Pelvis | ThighL | ThighR,
                Build = m =>
                {
                    m.AddTube(StandInBody.TubeFor(Pelvis), Cloth * 0.5f, Cap.Closed, Cap.Open, new MaskFunction((a, b) => new Vector3(0f, 0f, 1f)), false);
                    m.AddSkirt(1.07f, 0.28f, 0.135f, 0.27f, 1.22f, 0.85f, Cloth, Trim(0.06f, true, true));
                },
            };
            yield return new Recipe
            {
                Id = "cloth.bottom.joggers_01", Slot = "bottom", Material = "Cloth",
                Covers = Pelvis | ThighL | ThighR | CalfL | CalfR,
                Build = m =>
                {
                    const float thick = Cloth * 1.4f;
                    m.AddTube(StandInBody.TubeFor(Pelvis), thick, Cap.Closed, Cap.Open, Trim(0.14f, false, true), false);
                    foreach ((BodyRegions thigh, BodyRegions calf, float outer) in new[] { (ThighL, CalfL, 0f), (ThighR, CalfR, 0.5f) })
                    {
                        // A stripe down the outside of each leg.
                        MaskFunction stripe = (along, around) =>
                        {
                            float d = Mathf.Abs(Mathf.Repeat(around - outer + 0.5f, 1f) - 0.5f);
                            return d < 0.06f ? new Vector4(0f, 1f, 0f, 0f) : new Vector4(1f, 0f, 0f, 0f);
                        };
                        m.AddTube(StandInBody.TubeFor(thigh), thick, Cap.Open, Cap.Open, stripe, false);
                        m.AddTube(StandInBody.TubeFor(calf), thick, Cap.Open, Cap.Open,
                            (along, around) => along > 0.88f ? new Vector4(0f, 1f, 0f, 0f) : stripe(along, around), false);
                    }
                },
            };

            // ---------------- more one-pieces and shoes
            yield return new Recipe
            {
                Id = "cloth.onepiece.sundress_01", Slot = "onepiece", Blocks = new[] { "top", "bottom" }, Material = "Cloth",
                Covers = UpperTorso | LowerTorso | Pelvis,
                Build = m =>
                {
                    m.AddTube(StandInBody.TubeFor(UpperTorso), Cloth, Cap.Open, Cap.Collar, Trim(0.07f, false, true), false, collarRadius: 0.1f);
                    m.AddTube(StandInBody.TubeFor(LowerTorso), Cloth, Cap.Open, Cap.Open, Region1, false);
                    m.AddTube(StandInBody.TubeFor(Pelvis), Cloth * 0.5f, Cap.Closed, Cap.Open, new MaskFunction((a, b) => new Vector3(1f, 0f, 0f)), false);
                    m.AddSkirt(1.07f, 0.68f, 0.135f, 0.25f, 1.22f, 0.85f, Cloth, Trim(0.07f, false, true));
                },
            };
            yield return new Recipe
            {
                Id = "cloth.shoes.sandals_01", Slot = "shoes", Material = "Cloth",
                Covers = None, // bare feet show between the straps
                Build = m =>
                {
                    foreach ((string bone, float x) in new[] { ("Foot.L", -0.09f), ("Foot.R", 0.09f) })
                    {
                        m.AddEllipsoid(new Vector3(x, 0.008f, 0.06f), new Vector3(0.047f, 0.008f, 0.115f), None, false, bone, RegionTwo);
                        foreach (float z in new[] { 0.1f, 0.0f })
                            m.AddTube(new Tube { Region = None, Start = new Vector3(x - 0.042f, 0.035f, z), End = new Vector3(x + 0.042f, 0.035f, z), R0 = 0.008f, R1 = 0.008f, Bone = bone },
                                0f, Cap.Closed, Cap.Closed, Region1, false, withShapes: false);
                    }
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
            yield return Hair("hair.front.parted_01", "hair.front", m =>
                m.AddEllipsoid(HeadCentre, new Vector3(HeadR + 0.019f, HeadR + 0.019f, HeadR + 0.019f), None, false, "Head", Region1,
                    d => d.z > 0.22f && d.y > 0.46f && Mathf.Abs(d.x) > 0.09f));
            yield return Hair("hair.back.wavy_01", "hair.back", m =>
            {
                Crown(m);
                m.AddCurtain(1.67f, 1.53f, 0.124f, 0.138f, -125f, 125f, null, Region1);
                m.AddCurtain(1.6f, 1.02f, 0.132f, 0.175f, -70f, 70f, "Chest", Gradient(0.3f));
            });
            yield return Hair("hair.back.sidetail_01", "hair.back", m =>
            {
                Crown(m);
                m.AddChain(SideTail, new Vector3(0.17f, 1.3f, -0.04f), 0.036f, 0.011f, Gradient(0.4f));
            }, SideTail);
            yield return Hair("hair.back.curly_01", "hair.back", m =>
            {
                Crown(m);
                // Curls piled on top and at the back of the crown.
                foreach (Vector3 direction in new[]
                {
                    new Vector3(0f, 1f, 0.25f), new Vector3(0.45f, 0.85f, 0.1f), new Vector3(-0.45f, 0.85f, 0.1f),
                    new Vector3(0.25f, 0.8f, -0.5f), new Vector3(-0.25f, 0.8f, -0.5f), new Vector3(0f, 0.7f, -0.7f),
                })
                    m.AddEllipsoid(HeadCentre + direction.normalized * (HeadR + 0.02f), new Vector3(0.032f, 0.042f, 0.032f), None, false, "Head", Region1);
            });

            yield return Hair("hair.back.braid_01", "hair.back", m =>
            {
                Crown(m);
                // A plait down the back: overlapping lobes that zigzag along the chain, each riding its own bone so
                // the braid swings, and a tie in the tip colour.
                Vector3 start = Braid[0].Position, end = new Vector3(0f, 1.1f, -0.172f);
                const int Lobes = 11;
                for (int i = 0; i < Lobes; i++)
                {
                    float t = (i + 0.5f) / Lobes;
                    Vector3 at = Vector3.Lerp(start, end, t) + new Vector3((i % 2 == 0 ? 1f : -1f) * 0.009f, 0f, 0f);
                    float r = Mathf.Lerp(0.03f, 0.017f, t);
                    string bone = Braid[Mathf.Min(Braid.Length - 1, (int)(t * Braid.Length))].Name;
                    m.AddEllipsoid(at, new Vector3(r, r * 1.35f, r * 0.85f), None, false, bone, Region1);
                }
                m.AddEllipsoid(end + new Vector3(0f, 0.005f, 0f), new Vector3(0.016f, 0.012f, 0.016f), None, false, Braid[Braid.Length - 1].Name, RegionTwo);
            }, Braid);
            yield return Hair("hair.back.spiky_01", "hair.back", m =>
            {
                Crown(m);
                // Short spikes standing up and back from the crown. Ellipsoids are axis-aligned, so each spike is
                // long along the axis it mostly points down.
                foreach (Vector3 direction in new[]
                {
                    new Vector3(0f, 1f, 0.4f), new Vector3(0.3f, 1f, 0.15f), new Vector3(-0.3f, 1f, 0.15f), new Vector3(0.15f, 1f, -0.25f),
                    new Vector3(-0.15f, 1f, -0.25f), new Vector3(0.45f, 0.85f, -0.35f), new Vector3(-0.45f, 0.85f, -0.35f),
                    new Vector3(0.25f, 0.55f, -0.8f), new Vector3(-0.25f, 0.55f, -0.8f), new Vector3(0f, 0.3f, -1f),
                })
                {
                    Vector3 d = direction.normalized;
                    Vector3 radii = Mathf.Abs(d.y) >= Mathf.Abs(d.z) ? new Vector3(0.021f, 0.034f, 0.021f) : new Vector3(0.021f, 0.021f, 0.036f);
                    m.AddEllipsoid(HeadCentre + d * (HeadR + 0.01f), radii, None, false, "Head", Gradient(0.5f));
                }
            });
            yield return Hair("hair.front.curtain_01", "hair.front", m =>
                // Parted in the middle and longer at the sides, framing the face.
                m.AddEllipsoid(HeadCentre, new Vector3(HeadR + 0.019f, HeadR + 0.019f, HeadR + 0.019f), None, false, "Head", Region1,
                    d => d.z > 0.12f && Mathf.Abs(d.x) > 0.07f && d.y > 0.64f - 0.6f * Mathf.Abs(d.x)));

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
            // ---------------- outer layers: over any top, so thicker than all of them (the hoodie is Cloth * 1.8)
            yield return new Recipe
            {
                Id = "cloth.outer.jacket_01", Slot = "outer", Material = "Cloth",
                Covers = UpperTorso | LowerTorso | UpperArmL | UpperArmR | ForearmL | ForearmR,
                Build = m =>
                {
                    const float thick = Cloth * 2.8f;
                    // Open down the front, so the top shows through: the columns facing forward are left out.
                    Func<float, bool> open = around => Mathf.Abs(around - 0.25f) > 0.06f;
                    Tube chest = StandInBody.TubeFor(UpperTorso);
                    chest.KeepAround = open;
                    Tube waist = StandInBody.TubeFor(LowerTorso);
                    waist.KeepAround = open;
                    m.AddTube(chest, thick, Cap.Open, Cap.Collar, Region1, false, collarRadius: 0.085f);
                    m.AddTube(waist, thick, Cap.Open, Cap.Open, Trim(0.14f, true, false), false);
                    // A collar standing round the back of the neck, in the trim colour.
                    Tube collar = StandInBody.TubeFor(Neck);
                    collar.End = Vector3.Lerp(collar.Start, collar.End, 0.45f);
                    collar.KeepAround = around => Mathf.Abs(around - 0.25f) > 0.12f;
                    m.AddTube(collar, 0.03f, Cap.Open, Cap.Open, RegionTwo, false);
                    foreach (BodyRegions arm in new[] { UpperArmL, UpperArmR })
                        m.AddTube(StandInBody.TubeFor(arm), thick, Cap.Open, Cap.Open, Region1, false);
                    foreach (BodyRegions forearm in new[] { ForearmL, ForearmR })
                        m.AddTube(StandInBody.TubeFor(forearm), thick, Cap.Open, Cap.Open, Trim(0.16f, false, true), false);
                },
            };
            yield return new Recipe
            {
                Id = "cloth.outer.coat_01", Slot = "outer", Material = "Cloth",
                Covers = UpperTorso | LowerTorso | UpperArmL | UpperArmR | ForearmL | ForearmR,
                Build = m =>
                {
                    const float thick = Cloth * 3f;
                    m.AddTube(StandInBody.TubeFor(UpperTorso), thick, Cap.Open, Cap.Collar, Region1, false, collarRadius: 0.08f);
                    m.AddTube(StandInBody.TubeFor(LowerTorso), thick, Cap.Open, Cap.Open, Region1, false);
                    // The coat's skirt flares over the hips and whatever's under it (wider than every skirt and dress
                    // down to its hem), to mid-thigh, with a hem band.
                    m.AddSkirt(1.1f, 0.76f, 0.13f, 0.21f, 1.22f, 0.85f, thick, Trim(0.09f, false, true));
                    // Buttons down the front, laid on the coat like the blazer's lapels so body shapes carry them out too.
                    Tube torso = StandInBody.TubeFor(UpperTorso), belly = StandInBody.TubeFor(LowerTorso);
                    foreach (float y in new[] { 1.38f, 1.28f, 1.18f, 1.09f })
                    {
                        Tube under = y >= torso.Start.y ? torso : belly;
                        float t = Mathf.InverseLerp(under.Start.y, under.End.y, y);
                        float z = Mathf.Lerp(under.R0, under.R1, t) * under.Depth + thick + 0.002f;
                        m.AddTube(new Tube { Region = under.Region, Start = new Vector3(0f, y + 0.008f, z), End = new Vector3(0f, y - 0.008f, z),
                                R0 = 0.011f, R1 = 0.011f, Width = 1f, Depth = 0.45f, Bone = under.Bone, ShapeRadius = 0.11f },
                            0f, Cap.Closed, Cap.Closed, RegionTwo, false);
                    }
                    foreach (BodyRegions arm in new[] { UpperArmL, UpperArmR })
                        m.AddTube(StandInBody.TubeFor(arm), thick, Cap.Open, Cap.Open, Region1, false);
                    foreach (BodyRegions forearm in new[] { ForearmL, ForearmR })
                        m.AddTube(StandInBody.TubeFor(forearm), thick, Cap.Open, Cap.Open, Trim(0.14f, false, true), false);
                },
            };

            // ---------------- legwear: right on the skin, under every bottom (skirt waistbands sit at Cloth * 0.5) and boot
            yield return new Recipe
            {
                Id = "cloth.legwear.tights_01", Slot = "legwear", Material = "Cloth",
                Covers = Pelvis | ThighL | ThighR | CalfL | CalfR,
                Build = m =>
                {
                    const float thin = Cloth * 0.25f;
                    m.AddTube(StandInBody.TubeFor(Pelvis), thin, Cap.Closed, Cap.Open, Region1, false);
                    foreach (BodyRegions leg in new[] { ThighL, ThighR, CalfL, CalfR })
                        m.AddTube(StandInBody.TubeFor(leg), thin, Cap.Open, Cap.Open, Region1, false);
                },
            };
            yield return new Recipe
            {
                Id = "cloth.legwear.socks_01", Slot = "legwear", Material = "Cloth",
                Covers = CalfL | CalfR,
                Build = m =>
                {
                    const float thin = Cloth * 0.33f;
                    foreach ((BodyRegions thigh, BodyRegions calf) in new[] { (ThighL, CalfL), (ThighR, CalfR) })
                    {
                        // Up over the knee, with a band at the top in the second colour.
                        Tube above = StandInBody.TubeFor(thigh);
                        above.Start = Vector3.Lerp(above.Start, above.End, 0.4f);
                        m.AddTube(above, thin, Cap.Open, Cap.Open, Trim(0.22f, true, false), false);
                        m.AddTube(StandInBody.TubeFor(calf), thin, Cap.Open, Cap.Open, Region1, false);
                    }
                },
            };

            // ---------------- neckwear: details in front of the chest take its shape response (like the blazer's lapels)
            yield return new Recipe
            {
                Id = "acc.neck.scarf_01", Slot = "neck", Material = "Cloth",
                Build = m =>
                {
                    Tube wrap = StandInBody.TubeFor(Neck);
                    wrap.End = Vector3.Lerp(wrap.Start, wrap.End, 0.75f);
                    m.AddTube(wrap, 0.042f, Cap.Collar, Cap.Collar, (along, around) => Mathf.Repeat(along * 3f, 1f) > 0.6f ? new Vector4(0f, 1f, 0f, 0f) : new Vector4(1f, 0f, 0f, 0f), false);
                    // The hanging end lies over whatever's worn, a coat included: deep enough that its front clears
                    // an outer layer while its back meets a plain top.
                    m.AddTube(new Tube { Region = UpperTorso, Start = new Vector3(0.055f, 1.455f, 0.125f), End = new Vector3(0.085f, 1.2f, 0.142f),
                            R0 = 0.04f, R1 = 0.036f, Width = 1f, Depth = 0.55f, Bone = "Chest", ShapeRadius = 0.1f },
                        0f, Cap.Closed, Cap.Closed, (along, around) => along > 0.85f ? new Vector4(0f, 1f, 0f, 0f) : new Vector4(1f, 0f, 0f, 0f), false);
                },
            };
            yield return new Recipe
            {
                Id = "acc.neck.necklace_01", Slot = "neck", Material = "Accessory",
                Build = m =>
                {
                    // A string of beads around the base of the neck, and a pendant resting on the chest.
                    for (int i = 0; i < 16; i++)
                    {
                        float a = i / 16f * 2f * Mathf.PI;
                        var at = new Vector3(Mathf.Cos(a) * 0.056f, 1.48f - 0.03f * Mathf.Max(0f, Mathf.Sin(a)), Mathf.Sin(a) * 0.056f);
                        m.AddEllipsoid(at, Vector3.one * 0.0095f, None, false, "Neck", Region1);
                    }
                    m.AddTube(new Tube { Region = UpperTorso, Start = new Vector3(0f, 1.425f, 0.1f), End = new Vector3(0f, 1.39f, 0.112f),
                            R0 = 0.013f, R1 = 0.017f, Width = 1f, Depth = 0.5f, Bone = "Chest", ShapeRadius = 0.1f },
                        0f, Cap.Closed, Cap.Closed, RegionTwo, false);
                },
            };
            yield return new Recipe
            {
                Id = "acc.hat.cap_01", Slot = "hat", Material = "Cloth", FollowsBodyShapes = false,
                Build = m =>
                {
                    m.AddEllipsoid(HeadCentre + new Vector3(0f, 0.01f, -0.004f), new Vector3(0.133f, 0.13f, 0.133f), None, false, "Head", Region1, d => d.y > 0.28f);
                    m.AddEllipsoid(HeadCentre + new Vector3(0f, 0.05f, 0.105f), new Vector3(0.088f, 0.007f, 0.075f), None, false, "Head", RegionTwo, d => d.z > -0.3f);
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
            SpringChains = extraBones.Select(chain => chain[0].Name).ToArray(), // every extra chain swings
            Build = build,
        };

        // Shared crown: over the head, down the back to the nape, over the ears, with a high front hairline for bangs.
        static void Crown(StandInMesh mesh) =>
            mesh.AddEllipsoid(HeadCentre, new Vector3(HeadR + 0.014f, HeadR + 0.016f, HeadR + 0.014f), None, false, "Head", Region1,
                d => (d.z <= 0.3f && d.y > 0.2f) || (d.z > 0.3f && d.y > 0.55f) || (d.z < 0.1f && d.y > -0.45f) || (Mathf.Abs(d.x) > 0.8f && d.y > -0.15f));

        static readonly MaskFunction RegionTwo = (along, around) => new Vector4(0f, 1f, 0f, 0f);

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
            Mesh bodyMesh = SaveMesh(body, "StandInBody", withShapes: true);
            SaveBody(bodyMesh, materials["Skin"]);

            foreach (Recipe recipe in Parts())
            {
                var mesh = new StandInMesh(recipe.Bones);
                recipe.Build(mesh);
                Mesh asset = SaveMesh(mesh, recipe.Id, recipe.FollowsBodyShapes);
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
            part.SpringChains = recipe.SpringChains;
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

        // Meshes are rebuilt in place through the Mesh API: CopySerialized over a mesh whose vertex layout changed crashes the editor.
        static Mesh SaveMesh(StandInMesh builder, string name, bool withShapes)
        {
            string path = $"{MeshFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            Mesh mesh = builder.Build(name, withShapes, existing);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(existing);
            return mesh;
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
