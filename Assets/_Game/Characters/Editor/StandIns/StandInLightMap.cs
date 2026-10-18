using UnityEngine;
using static Game.Characters.StandIns.StandInMesh;

namespace Game.Characters.StandIns
{
    /// <summary>
    /// Procedural lightmaps for the stand-ins (graphics plan G1.1), painted per vertex from where each vertex sits on
    /// the rest-pose body, so the body and every part laid over the same spot agree. The shadow bias drops in the
    /// creases at the joints (armpits, elbows, crotch, knees), round the neck under the head and at the collar, and on
    /// the hair's lower layers, so those fall into shade sooner (R1). The hair band is the shine mask (R8), metal
    /// accessories carry the metal mask (R6), and A is the material type (R4). The face stays neutral: its shading is
    /// G1.4's face map.
    /// </summary>
    public static class StandInLightMap
    {
        // Material types in A, out of 255 (the toon shader's lightmap layout).
        public const float SkinType = 0f, HairType = 32f / 255f, ClothType = 64f / 255f, MetalType = 128f / 255f;

        // The deepest any spot gets: a threshold moved this far still leaves a crease's lit side lit under a high sun.
        const float MostShade = 0.35f;

        /// <summary>The body: creased skin, with the head and face left neutral.</summary>
        public static readonly LightMapFunction Body = (position, normal, regions) =>
            OnHead(position) ? Neutral(SkinType) : new Vector4(0.5f - Creases(position, normal), 0f, 0f, SkinType);

        public static LightMapFunction For(StandInLibrary.Recipe recipe)
        {
            switch (recipe.Material)
            {
                // Marks lie on the face.
                case "Mark": return (position, normal, regions) => Neutral(SkinType);
                // The band of highlight colour (region 3) is the shine mask.
                case "Hair": return (position, normal, regions) =>
                    new Vector4(0.5f - Mathf.Min(MostShade, Creases(position, normal) + LowerLayers(position)), regions.z, 0f, HairType);
            }
            if (recipe.Metal) return (position, normal, regions) => new Vector4(0.5f - Creases(position, normal), 0f, 1f, MetalType);
            return (position, normal, regions) => new Vector4(0.5f - Creases(position, normal), 0f, 0f, ClothType);
        }

        static Vector4 Neutral(float type) => new Vector4(0.5f, 0f, 0f, type);

        // On or inside the head sphere: the face, and the top of the neck it hides.
        static bool OnHead(Vector3 position) => Vector3.Distance(position, StandInSkeleton.HeadCentre) < StandInSkeleton.HeadRadius + 0.005f;

        /// <summary>How far the shadow bias drops below neutral at a rest-pose point (T-pose, facing +Z).</summary>
        static float Creases(Vector3 p, Vector3 n)
        {
            float shade = 0f;
            foreach (float side in new[] { -1f, 1f })
            {
                // Armpits: the hollow where each arm's underside meets the side of the torso.
                shade += 0.24f * Near(p, new Vector3(0.165f * side, 1.35f, 0f), 0.09f);
                // Elbows: a band round the joint, deepest in the crook (in front at rest, where the forearm folds).
                float aroundArm = new Vector2(p.y - 1.4f, p.z).magnitude;
                shade += 0.16f * Ease(0.05f, 0f, Mathf.Abs(p.x * side - 0.44f)) * Ease(0.11f, 0.08f, aroundArm) * Facing(n, Vector3.forward);
                // Knees: the same, deepest behind (the calf folds back).
                float aroundLeg = new Vector2(p.x - 0.09f * side, p.z).magnitude;
                shade += 0.16f * Ease(0.06f, 0f, Mathf.Abs(p.y - 0.5f)) * Ease(0.11f, 0.085f, aroundLeg) * Facing(n, Vector3.back);
            }
            // The crotch, between the tops of the thighs.
            shade += 0.2f * Near(p, new Vector3(0f, 0.84f, 0f), 0.1f);
            // The neck, all round under the head: under the jaw, and the nape under the hair...
            float aroundNeck = new Vector2(p.x, p.z).magnitude;
            shade += 0.22f * Ease(0.09f, 0.07f, aroundNeck) * Ease(1.44f, 1.53f, p.y) * Ease(1.6f, 1.55f, p.y);
            // ...and at the collar, where a top's neckline (and the collar standing round it) meets the neck.
            shade += 0.18f * Ease(0.12f, 0.08f, aroundNeck) * Ease(0.06f, 0f, Mathf.Abs(p.y - 1.44f));
            return Mathf.Min(shade, MostShade);
        }

        // Hair below the middle of the head and close round it, under the crown: the nape and the inner curtains.
        static float LowerLayers(Vector3 p) => 0.2f * Ease(1.64f, 1.48f, p.y) * Ease(0.2f, 0.13f, new Vector2(p.x, p.z).magnitude);

        // 1 at the centre, easing to 0 at the radius.
        static float Near(Vector3 position, Vector3 centre, float radius) => Ease(radius, 0f, Vector3.Distance(position, centre));

        // 1 facing the crook, 0.3 facing away from it.
        static float Facing(Vector3 normal, Vector3 crook) => Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(Vector3.Dot(normal, crook) * 0.5f + 0.5f));

        // 0 at from, easing to 1 at to (either way round).
        static float Ease(float from, float to, float value) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));
    }
}
