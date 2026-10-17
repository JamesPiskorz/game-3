using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Game.Characters.Modular.BodyRegions;
using static Game.Characters.StandIns.StandInMesh;

namespace Game.Characters.StandIns
{
    /// <summary>
    /// The stand-in body: a capsule mannequin with a simple face. Skin is the base region; recolour regions carry
    /// the face colours: 1 = iris, 2 = brows and mouth, 3 = eye whites.
    /// </summary>
    public static class StandInBody
    {
        public static IEnumerable<Tube> Tubes()
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

        /// <summary>A fresh copy of the body tube for a region, for clothing to follow.</summary>
        public static Tube TubeFor(Game.Characters.Modular.BodyRegions region) => Tubes().First(tube => tube.Region == region).Clone();

        static readonly MaskFunction Iris = (along, around) => new Vector3(1f, 0f, 0f);
        static readonly MaskFunction Dark = (along, around) => new Vector3(0f, 1f, 0f);
        static readonly MaskFunction White = (along, around) => new Vector3(0f, 0f, 1f);

        public static void Build(StandInMesh mesh)
        {
            foreach (Tube tube in Tubes())
                mesh.AddTube(tube, 0f, Cap.Closed, Cap.Closed, BaseOnly, paintRegion: true);

            Vector3 head = StandInSkeleton.HeadCentre;
            mesh.AddEllipsoid(head, Vector3.one * StandInSkeleton.HeadRadius, Head, true, "Head", BaseOnly);
            mesh.AddEllipsoid(Bone("Nose"), new Vector3(0.015f, 0.02f, 0.017f), Head, true, "Nose", BaseOnly);
            foreach (string side in new[] { ".L", ".R" })
            {
                Vector3 eye = Bone("Eye" + side);
                mesh.AddEllipsoid(eye, new Vector3(0.019f, 0.025f, 0.006f), Head, true, "Eye" + side, White);
                mesh.AddEllipsoid(eye + new Vector3(0f, -0.002f, 0.004f), new Vector3(0.012f, 0.018f, 0.004f), Head, true, "Eye" + side, Iris);
                mesh.AddEllipsoid(Bone("Brow" + side), new Vector3(0.019f, 0.0045f, 0.004f), Head, true, "Brow" + side, Dark);
            }
            mesh.AddEllipsoid(Bone("Mouth"), new Vector3(0.016f, 0.003f, 0.004f), Head, true, "Mouth", Dark);
        }

        static Vector3 Bone(string name) => StandInSkeleton.Position(StandInSkeleton.Bones, name);
    }
}
