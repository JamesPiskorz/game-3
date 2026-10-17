using System;
using System.Collections.Generic;
using System.Linq;
using Game.Characters.Modular;
using Game.Characters.Rendering;
using UnityEngine;
using static Game.Characters.Modular.BodyRegions;

namespace Game.Characters.StandIns
{
    /// <summary>
    /// Procedural mesh builder for stand-in characters and parts. Vertex colour carries the body region ID in red
    /// and recolour weights for regions 1-3 in green, blue and alpha (the Toon shader's vertex-colour mask). UV0 is in
    /// metres (around, along) so patterns tile at a consistent size on every piece. Body shapes are evaluated from
    /// the body surface beneath each vertex, which is what the Blender shape-key transfer does for real clothing.
    /// </summary>
    public sealed class StandInMesh
    {
        public static readonly string[] BodyShapes = { "body.weight", "body.chest", "body.hips" };

        const BodyRegions Fleshy = Pelvis | LowerTorso | UpperTorso | UpperArmL | UpperArmR | ForearmL | ForearmR | ThighL | ThighR | CalfL | CalfR;

        public const int Sides = 24;
        const int Rings = 12;

        readonly StandInBone[] _bones;
        readonly Dictionary<string, int> _boneIndex;
        readonly List<Vector3> _positions = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<Color32> _colours = new List<Color32>();
        readonly List<BoneWeight> _weights = new List<BoneWeight>();
        readonly List<Vector3[]> _deltas = new List<Vector3[]>();
        readonly List<int> _triangles = new List<int>();

        public StandInMesh(StandInBone[] bones)
        {
            _bones = bones;
            _boneIndex = bones.Select((bone, index) => (bone.Name, index)).ToDictionary(p => p.Name, p => p.index);
        }

        public StandInBone[] Bones => _bones;

        /// <summary>Recolour weights for regions 1-3 (green, blue, alpha); whatever is left is the base region.</summary>
        public delegate Vector3 MaskFunction(float along, float around);

        public static readonly MaskFunction BaseOnly = (along, around) => Vector3.zero;
        public static readonly MaskFunction Region1 = (along, around) => new Vector3(1f, 0f, 0f);

        /// <summary>Region 1 everywhere, region 2 within <paramref name="width"/> of the chosen ends (cuffs, hems, collars).</summary>
        public static MaskFunction Trim(float width, bool atStart, bool atEnd) => (along, around) =>
            (atStart && along < width) || (atEnd && along > 1f - width) ? new Vector3(0f, 1f, 0f) : new Vector3(1f, 0f, 0f);

        /// <summary>Region 1 fading to region 2 along the piece (hair tips).</summary>
        public static MaskFunction Gradient(float start) => (along, around) =>
        {
            float tip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, 1f, along));
            return new Vector3(1f - tip, tip, 0f);
        };

        public sealed class Tube
        {
            public BodyRegions Region;                // region ID painted on the body; shape-delta source for clothing
            public Vector3 Start, End;
            public float R0, R1;
            public float Width = 1f, Depth = 1f;      // ellipse scale across / front-to-back (vertical tubes: X / Z)
            public string Bone, ParentBone, ChildBone;
            public float ParentBlend = 0.2f;          // below this t, weight blends towards ParentBone (50% at t = 0)
            public float ChildBlendStart = 0.8f;      // above this t, weight blends towards ChildBone...
            public float ChildBlendMax = 0.5f;        // ...reaching this weight at t = 1

            public Tube Clone() => (Tube)MemberwiseClone();
        }

        public enum Cap { Open, Closed, Collar }

        public int VertexCount => _positions.Count;

        int AddVertex(Vector3 position, Vector3 normal, Vector2 uv, BodyRegions region, Vector3 mask, BoneWeight weights, Vector3[] deltas)
        {
            _positions.Add(position);
            _normals.Add(normal);
            _uvs.Add(uv);
            byte regionId = region == None ? (byte)0 : (byte)BodyRegionIds.IdOf(region);
            _colours.Add(new Color32(regionId, ToByte(mask.x), ToByte(mask.y), ToByte(mask.z)));
            _weights.Add(weights);
            _deltas.Add(deltas);
            return _positions.Count - 1;
        }

        static byte ToByte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);

        /// <summary>Adds a triangle, flipping its winding if needed so it faces along its vertex normals.</summary>
        void AddTriangle(int a, int b, int c)
        {
            Vector3 facing = Vector3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]);
            if (Vector3.Dot(facing, _normals[a] + _normals[b] + _normals[c]) < 0f) (b, c) = (c, b);
            _triangles.Add(a);
            _triangles.Add(b);
            _triangles.Add(c);
        }

        void AddQuad(int a, int b, int c, int d)
        {
            AddTriangle(a, b, c);
            AddTriangle(a, c, d);
        }

        BoneWeight Weights(params (string bone, float weight)[] influences)
        {
            var used = influences.Where(i => i.bone != null && i.weight > 1e-4f).OrderByDescending(i => i.weight).Take(4).ToArray();
            float total = used.Sum(i => i.weight);
            var result = new BoneWeight();
            if (used.Length > 0) { result.boneIndex0 = _boneIndex[used[0].bone]; result.weight0 = used[0].weight / total; }
            if (used.Length > 1) { result.boneIndex1 = _boneIndex[used[1].bone]; result.weight1 = used[1].weight / total; }
            if (used.Length > 2) { result.boneIndex2 = _boneIndex[used[2].bone]; result.weight2 = used[2].weight / total; }
            if (used.Length > 3) { result.boneIndex3 = _boneIndex[used[3].bone]; result.weight3 = used[3].weight / total; }
            return result;
        }

        BoneWeight TubeWeights(Tube tube, float t)
        {
            float parent = tube.ParentBone != null && t < tube.ParentBlend ? 0.5f * (1f - t / tube.ParentBlend) : 0f;
            float child = tube.ChildBone != null && t > tube.ChildBlendStart ? tube.ChildBlendMax * (t - tube.ChildBlendStart) / (1f - tube.ChildBlendStart) : 0f;
            return Weights((tube.Bone, 1f - parent - child), (tube.ParentBone, parent), (tube.ChildBone, child));
        }

        /// <summary>How each body shape moves the body surface at a point on a region.</summary>
        public static Vector3[] ShapeDeltas(BodyRegions region, float t, Vector3 normal, float surfaceRadius)
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

        static void Frame(Vector3 axis, out Vector3 u, out Vector3 v)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.forward)) > 0.9f ? Vector3.up : Vector3.forward;
            u = Vector3.Cross(axis, reference).normalized;
            v = Vector3.Cross(u, axis);
        }

        /// <param name="offset">Distance outside the body surface (cloth thickness); 0 for the body itself.</param>
        /// <param name="paintRegion">Whether to paint the tube's region ID (body) or leave red at 0 (parts).</param>
        public void AddTube(Tube tube, float offset, Cap startCap, Cap endCap, MaskFunction mask, bool paintRegion, float collarRadius = 0f, bool withShapes = true)
        {
            Vector3 axis = (tube.End - tube.Start).normalized;
            Frame(axis, out Vector3 u, out Vector3 v);
            float length = Vector3.Distance(tube.Start, tube.End);
            BodyRegions painted = paintRegion ? tube.Region : None;

            int first = VertexCount;
            for (int ring = 0; ring <= Rings; ring++)
            {
                float t = ring / (float)Rings;
                Vector3 centre = Vector3.Lerp(tube.Start, tube.End, t);
                float radius = Mathf.Lerp(tube.R0, tube.R1, t);
                BoneWeight weights = TubeWeights(tube, t);
                float circumference = 2f * Mathf.PI * (radius + offset) * (tube.Width + tube.Depth) * 0.5f;
                for (int side = 0; side <= Sides; side++) // the last column duplicates the first so UVs can wrap
                {
                    float around = side / (float)Sides;
                    float angle = around * 2f * Mathf.PI;
                    float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                    Vector3 spoke = u * (c * tube.Width) + v * (s * tube.Depth);
                    Vector3 normal = (u * (c / tube.Width) + v * (s / tube.Depth)).normalized;
                    Vector3 position = centre + spoke * radius + normal * offset;
                    Vector3[] deltas = withShapes ? ShapeDeltas(tube.Region, t, normal, radius * spoke.magnitude) : null;
                    AddVertex(position, normal, new Vector2(around * circumference, t * length), painted, mask(t, around), weights, deltas);
                }
            }

            const int Columns = Sides + 1;
            for (int ring = 0; ring < Rings; ring++)
                for (int side = 0; side < Sides; side++)
                {
                    int a = first + ring * Columns + side;
                    AddQuad(a, a + 1, a + 1 + Columns, a + Columns);
                }

            AddCap(tube, startCap, first, 0f, -axis, u, v, offset, collarRadius, painted, mask);
            AddCap(tube, endCap, first + Rings * Columns, 1f, axis, u, v, offset, collarRadius, painted, mask);
        }

        void AddCap(Tube tube, Cap cap, int ringStart, float t, Vector3 normal, Vector3 u, Vector3 v, float offset, float collarRadius, BodyRegions painted, MaskFunction mask)
        {
            if (cap == Cap.Open) return;

            Vector3 centre = Vector3.Lerp(tube.Start, tube.End, t) + normal * offset;
            BoneWeight weights = TubeWeights(tube, t);
            Vector3 capMask = mask(t, 0f);
            int rim = VertexCount;
            for (int side = 0; side < Sides; side++)
                AddVertex(_positions[ringStart + side], normal, _uvs[ringStart + side], painted, capMask, weights, _deltas[ringStart + side]);

            if (cap == Cap.Closed)
            {
                int hub = AddVertex(centre, normal, Vector2.zero, painted, capMask, weights, null);
                for (int side = 0; side < Sides; side++)
                    AddTriangle(hub, rim + side, rim + (side + 1) % Sides);
                return;
            }

            int inner = VertexCount;
            for (int side = 0; side < Sides; side++)
            {
                float angle = side * 2f * Mathf.PI / Sides;
                AddVertex(centre + (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * collarRadius, normal, Vector2.zero, painted, capMask, weights, null);
            }
            for (int side = 0; side < Sides; side++)
            {
                int next = (side + 1) % Sides;
                AddQuad(rim + side, rim + next, inner + next, inner + side);
            }
        }

        /// <summary>
        /// Ellipsoid (scale per axis) around <paramref name="centre"/>, skinned to one bone. <paramref name="keep"/>
        /// selects which faces to keep by their direction from the centre (for hair caps and bangs).
        /// </summary>
        public void AddEllipsoid(Vector3 centre, Vector3 radii, BodyRegions region, bool paintRegion, string bone, MaskFunction mask, Func<Vector3, bool> keep = null)
        {
            const int Latitudes = 16, Longitudes = 24;
            BoneWeight weights = Weights((bone, 1f));
            BodyRegions painted = paintRegion ? region : None;
            int first = VertexCount;
            for (int lat = 0; lat <= Latitudes; lat++)
            {
                float theta = Mathf.PI * lat / Latitudes;
                for (int lon = 0; lon <= Longitudes; lon++)
                {
                    float phi = 2f * Mathf.PI * lon / Longitudes;
                    var direction = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    Vector3 normal = new Vector3(direction.x / radii.x, direction.y / radii.y, direction.z / radii.z).normalized;
                    float along = lat / (float)Latitudes;
                    var uv = new Vector2(lon / (float)Longitudes * 2f * Mathf.PI * radii.x, along * Mathf.PI * radii.y);
                    AddVertex(centre + Vector3.Scale(direction, radii), normal, uv, painted, mask(along, lon / (float)Longitudes), weights, null);
                }
            }

            const int Columns = Longitudes + 1;
            for (int lat = 0; lat < Latitudes; lat++)
                for (int lon = 0; lon < Longitudes; lon++)
                {
                    int a = first + lat * Columns + lon;
                    Vector3 middle = ((_positions[a] + _positions[a + 1 + Columns]) * 0.5f - centre).normalized;
                    if (keep == null || keep(middle))
                        AddQuad(a, a + 1, a + 1 + Columns, a + Columns);
                }
        }

        /// <summary>
        /// A vertical partial cylinder around the head axis (hair curtains), from <paramref name="top"/> down to
        /// <paramref name="bottom"/>, covering yaw angles <paramref name="fromDegrees"/>..<paramref name="toDegrees"/>
        /// (0 = behind the head, positive = character's right). Weights blend from Head to <paramref name="lowerBone"/>.
        /// </summary>
        public void AddCurtain(float top, float bottom, float radiusTop, float radiusBottom, float fromDegrees, float toDegrees, string lowerBone, MaskFunction mask)
        {
            const int Columns = 16;
            int first = VertexCount;
            float length = top - bottom;
            for (int ring = 0; ring <= Rings; ring++)
            {
                float t = ring / (float)Rings;
                float y = Mathf.Lerp(top, bottom, t);
                float radius = Mathf.Lerp(radiusTop, radiusBottom, t);
                BoneWeight weights = lowerBone == null ? Weights(("Head", 1f)) : Weights(("Head", 1f - t), (lowerBone, t));
                for (int column = 0; column <= Columns; column++)
                {
                    float around = column / (float)Columns;
                    float yaw = Mathf.Lerp(fromDegrees, toDegrees, around) * Mathf.Deg2Rad;
                    var normal = new Vector3(Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
                    Vector3 position = new Vector3(StandInSkeleton.HeadCentre.x, y, StandInSkeleton.HeadCentre.z - 0.01f) + normal * radius;
                    AddVertex(position, normal, new Vector2(around * radius * Mathf.Abs(toDegrees - fromDegrees) * Mathf.Deg2Rad, t * length), None, mask(t, around), weights, null);
                }
            }
            for (int ring = 0; ring < Rings; ring++)
                for (int column = 0; column < Columns; column++)
                {
                    int a = first + ring * (Columns + 1) + column;
                    AddQuad(a, a + 1, a + 2 + Columns, a + 1 + Columns);
                }
        }

        /// <summary>
        /// A closed tapering tube from the first bone of <paramref name="chain"/> to <paramref name="end"/>, skinned
        /// smoothly along the chain (hair tails that spring bones can swing).
        /// </summary>
        public void AddChain(StandInBone[] chain, Vector3 end, float radiusStart, float radiusEnd, MaskFunction mask)
        {
            var tube = new Tube { Region = None, Start = chain[0].Position, End = end, R0 = radiusStart, R1 = radiusEnd, Bone = chain[0].Name };
            int first = VertexCount;
            AddTube(tube, 0f, Cap.Closed, Cap.Closed, mask, paintRegion: false, withShapes: false);

            // Re-weight along the chain: each bone owns its stretch, blending into the next.
            for (int i = first; i < VertexCount; i++)
            {
                float t = Mathf.Clamp01(Vector3.Dot(_positions[i] - tube.Start, end - tube.Start) / (end - tube.Start).sqrMagnitude);
                float segment = t * chain.Length - 0.5f;
                int lower = Mathf.Clamp(Mathf.FloorToInt(segment), 0, chain.Length - 1);
                int upper = Mathf.Min(lower + 1, chain.Length - 1);
                float blend = Mathf.Clamp01(segment - lower);
                _weights[i] = Weights((chain[lower].Name, 1f - blend), (chain[upper].Name, blend));
            }
        }

        /// <summary>A cone hanging from the waist (skirts), skinned to Hips at the top and the thighs lower down.</summary>
        public void AddSkirt(float top, float bottom, float radiusTop, float radiusBottom, float width, float depth, float offset, MaskFunction mask)
        {
            int first = VertexCount;
            float length = top - bottom;
            for (int ring = 0; ring <= Rings; ring++)
            {
                float t = ring / (float)Rings;
                float y = Mathf.Lerp(top, bottom, t);
                float radius = Mathf.Lerp(radiusTop, radiusBottom, t) + offset;
                for (int side = 0; side <= Sides; side++)
                {
                    float around = side / (float)Sides;
                    float angle = around * 2f * Mathf.PI;
                    float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                    var normal = new Vector3(c / width, 0f, s / depth).normalized;
                    var position = new Vector3(c * width * radius, y, s * depth * radius);
                    // Each side follows its thigh further down, so walking poses drag the hem along.
                    float legShare = Mathf.SmoothStep(0f, 0.7f, t) * Mathf.Abs(c);
                    string leg = c < 0f ? "UpperLeg.L" : "UpperLeg.R";
                    BoneWeight weights = Weights(("Hips", 1f - legShare), (leg, legShare));
                    // Hips and weight push the skirt out like the pelvis beneath it.
                    Vector3[] deltas = ShapeDeltas(Pelvis, 0.5f, normal, radiusTop * width);
                    AddVertex(position, normal, new Vector2(around * 2f * Mathf.PI * radius, t * length), None, mask(t, around), weights, deltas);
                }
            }
            const int Columns = Sides + 1;
            for (int ring = 0; ring < Rings; ring++)
                for (int side = 0; side < Sides; side++)
                {
                    int a = first + ring * Columns + side;
                    AddQuad(a, a + 1, a + 1 + Columns, a + Columns);
                }
        }

        public Mesh Build(string name, bool withShapes)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(_positions);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetColors(_colours);
            mesh.boneWeights = _weights.ToArray();
            // Rest bones have identity rotation and every mesh sits at the character's origin.
            mesh.bindposes = _bones.Select(bone => Matrix4x4.Translate(-bone.Position)).ToArray();
            mesh.SetTriangles(_triangles, 0);
            if (withShapes)
                for (int shape = 0; shape < BodyShapes.Length; shape++)
                    mesh.AddBlendShapeFrame(BodyShapes[shape], 100f, _deltas.Select(d => d == null ? Vector3.zero : d[shape]).ToArray(), null, null);
            mesh.RecalculateBounds();
            OutlineNormals.Bake(mesh);
            return mesh;
        }
    }
}
