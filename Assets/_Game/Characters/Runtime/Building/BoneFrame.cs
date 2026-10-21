using System.Collections.Generic;
using UnityEngine;

namespace Game.Characters.Building
{
    /// <summary>
    /// How a bone sits in its character, so it can be moved in the character's axes (x across, y up, z out of the face)
    /// whatever way the rig's bones rest. The stand-in's bones rest square with the character, so there every move
    /// passes through unchanged. The generated rig's keep Blender's bone roll: its face bones point out of the face
    /// (their own y is forwards and z up), its armature is z-up and its legs point down. And the generated body's
    /// masculine shape fits the face features to a broader head set further back without moving their bones (bones
    /// don't follow blendshapes), leaving each feature centimetres off its pivot; turned or stretched about the bone,
    /// it would sink into the face or float off it. So turns and stretches go about the feature.
    /// </summary>
    public readonly struct BoneFrame
    {
        /// <summary>The bone's axes and its parent's in the character's, to the nearest quarter turn.</summary>
        public readonly Quaternion Axes, ParentAxes;

        /// <summary>Where the bone's feature is, in the bone's space: off the bone where body shapes have moved it.</summary>
        public readonly Vector3 Shift;

        public BoneFrame(Quaternion axes, Quaternion parentAxes, Vector3 shift)
        {
            Axes = axes;
            ParentAxes = parentAxes;
            Shift = shift;
        }

        /// <summary>
        /// <paramref name="bone"/>'s frame as it and its parent are posed now in <paramref name="character"/>, and with
        /// <paramref name="body"/>'s shapes at their current weights (no body: no shift). Snapping to quarter turns
        /// keeps a few degrees of rest pose, a style's tilt or a nod from skewing the axes moves go along.
        /// </summary>
        public static BoneFrame Of(Transform character, Transform bone, SkinnedMeshRenderer body)
        {
            Quaternion toCharacter = Quaternion.Inverse(character.rotation);
            return new BoneFrame(Square(toCharacter * bone.rotation),
                bone.parent == null ? Quaternion.identity : Square(toCharacter * bone.parent.rotation),
                body != null ? FeatureShift(body, bone) : Vector3.zero);
        }

        /// <summary>
        /// Poses <paramref name="bone"/> as <paramref name="from"/> (its local pose) moved by <paramref name="offset"/>,
        /// turned by <paramref name="euler"/> and stretched by <paramref name="scale"/>, all in the character's axes.
        /// The turn and stretch are about the feature: the bone moves so that the point the feature has shifted to
        /// stays put (it's the bone's own origin when nothing shifted it).
        /// </summary>
        public void Move(Transform bone, RestPose.Pose from, Vector3 offset, Vector3 euler, Vector3 scale)
        {
            Quaternion rotation = from.Rotation * (Quaternion.Inverse(Axes) * Quaternion.Euler(euler) * Axes);
            Vector3 stretch = Vector3.Scale(from.Scale,
                new Vector3(Along(Axes * Vector3.right, scale), Along(Axes * Vector3.up, scale), Along(Axes * Vector3.forward, scale)));
            Vector3 about = from.Rotation * Vector3.Scale(from.Scale, Shift) - rotation * Vector3.Scale(stretch, Shift);
            bone.localPosition = from.Position + Quaternion.Inverse(ParentAxes) * offset + about;
            bone.localRotation = rotation;
            bone.localScale = stretch;
        }

        /// <summary>The stretch along whichever of the character's axes <paramref name="axis"/> lies on.</summary>
        static float Along(Vector3 axis, Vector3 scale) =>
            Mathf.Abs(axis.x) > 0.5f ? scale.x : Mathf.Abs(axis.y) > 0.5f ? scale.y : scale.z;

        /// <summary><paramref name="rotation"/> to the nearest quarter turn about each axis.</summary>
        static Quaternion Square(Quaternion rotation)
        {
            Vector3 forward = Nearest(rotation * Vector3.forward), up = Nearest(rotation * Vector3.up);
            return Vector3.Dot(forward, up) == 0f ? Quaternion.LookRotation(forward, up) : Quaternion.identity;
        }

        static Vector3 Nearest(Vector3 v)
        {
            float x = Mathf.Abs(v.x), y = Mathf.Abs(v.y), z = Mathf.Abs(v.z);
            if (x >= y && x >= z) return new Vector3(Mathf.Sign(v.x), 0f, 0f);
            return y >= z ? new Vector3(0f, Mathf.Sign(v.y), 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        // ---------------------------------------------------------------- feature shift

        /// <summary>
        /// Per body mesh: for each feature bone, its feature's mean offset under each blendshape at full weight, in the
        /// bone's space. Measured once per mesh until scripts reload, so a body re-exported while the editor is open
        /// keeps its old offsets till then.
        /// </summary>
        static readonly Dictionary<Mesh, Dictionary<int, Vector3[]>> FeatureShapes = new Dictionary<Mesh, Dictionary<int, Vector3[]>>();

        /// <summary>
        /// How far <paramref name="body"/>'s blendshapes, at their current weights, move the feature riding rigidly on
        /// <paramref name="bone"/>, in the bone's space. Only features count: bones every vertex of which is wholly on
        /// them (eyes, brows, mouth pieces). A bone blended into its neighbours, like the head, turns about its joint.
        /// </summary>
        static Vector3 FeatureShift(SkinnedMeshRenderer body, Transform bone)
        {
            Mesh mesh = body.sharedMesh;
            if (mesh == null || mesh.blendShapeCount == 0 || !mesh.isReadable) return Vector3.zero;
            int index = System.Array.IndexOf(body.bones, bone);
            if (index < 0) return Vector3.zero;
            if (!FeatureShapes.TryGetValue(mesh, out Dictionary<int, Vector3[]> features))
                FeatureShapes[mesh] = features = MeasureFeatureShapes(mesh);
            if (!features.TryGetValue(index, out Vector3[] offsets)) return Vector3.zero;

            Vector3 shift = Vector3.zero;
            for (int shape = 0; shape < offsets.Length; shape++)
            {
                float weight = body.GetBlendShapeWeight(shape);
                if (weight != 0f)
                    shift += offsets[shape] * (weight / mesh.GetBlendShapeFrameWeight(shape, mesh.GetBlendShapeFrameCount(shape) - 1));
            }
            return shift;
        }

        static Dictionary<int, Vector3[]> MeasureFeatureShapes(Mesh mesh)
        {
            var rigid = new Dictionary<int, List<int>>(); // bone -> the vertices wholly on it
            var blended = new HashSet<int>(); // bones with any vertex shared with another
            BoneWeight[] weights = mesh.boneWeights;
            for (int v = 0; v < weights.Length; v++)
            {
                BoneWeight w = weights[v];
                if (w.weight0 >= 0.99f)
                {
                    if (!rigid.TryGetValue(w.boneIndex0, out List<int> on)) rigid[w.boneIndex0] = on = new List<int>();
                    on.Add(v);
                    continue;
                }
                if (w.weight0 > 0f) blended.Add(w.boneIndex0);
                if (w.weight1 > 0f) blended.Add(w.boneIndex1);
                if (w.weight2 > 0f) blended.Add(w.boneIndex2);
                if (w.weight3 > 0f) blended.Add(w.boneIndex3);
            }
            foreach (int bone in blended) rigid.Remove(bone);

            Matrix4x4[] bindposes = mesh.bindposes;
            var features = new Dictionary<int, Vector3[]>();
            foreach (int bone in rigid.Keys) features[bone] = new Vector3[mesh.blendShapeCount];
            var deltas = new Vector3[mesh.vertexCount];
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                mesh.GetBlendShapeFrameVertices(shape, mesh.GetBlendShapeFrameCount(shape) - 1, deltas, null, null);
                foreach (KeyValuePair<int, List<int>> on in rigid)
                {
                    Vector3 sum = Vector3.zero;
                    foreach (int v in on.Value) sum += deltas[v];
                    features[on.Key][shape] = bindposes[on.Key].MultiplyVector(sum / on.Value.Count);
                }
            }
            return features;
        }
    }
}
