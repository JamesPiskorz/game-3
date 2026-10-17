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

        // The resolution in use: full by default, lower inside a Coarse() block.
        int _sides = Sides, _rings = Rings, _latitudes = 16, _longitudes = 24;

        /// <summary>
        /// Lower resolution for small details (beads, studs, buttons, straps, face strokes) until disposed: they
        /// don't need a hero mesh's vertex count, and there are many of them in a town.
        /// </summary>
        public IDisposable Coarse(int sides = 8, int rings = 2, int latitudes = 6, int longitudes = 10)
        {
            var previous = (_sides, _rings, _latitudes, _longitudes);
            (_sides, _rings, _latitudes, _longitudes) = (sides, rings, latitudes, longitudes);
            return new Restore(() => (_sides, _rings, _latitudes, _longitudes) = previous);
        }

        sealed class Restore : IDisposable
        {
            readonly Action _undo;
            public Restore(Action undo) => _undo = undo;
            public void Dispose() => _undo();
        }

        readonly StandInBone[] _bones;
        readonly Dictionary<string, int> _boneIndex;
        readonly List<Vector3> _positions = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<Vector2> _uv1 = new List<Vector2>();
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

        /// <summary>
        /// Recolour weights for regions 1-4: x, y, z go to vertex colour green, blue, alpha; w goes to UV1.x.
        /// Whatever is left over is the base region.
        /// </summary>
        public delegate Vector4 MaskFunction(float along, float around);

        public static readonly MaskFunction BaseOnly = (along, around) => Vector4.zero;
        public static readonly MaskFunction Region1 = (along, around) => new Vector4(1f, 0f, 0f, 0f);

        /// <summary>Region 1 everywhere, region 2 within <paramref name="width"/> of the chosen ends (cuffs, hems, collars).</summary>
        public static MaskFunction Trim(float width, bool atStart, bool atEnd) => (along, around) =>
            (atStart && along < width) || (atEnd && along > 1f - width) ? new Vector4(0f, 1f, 0f, 0f) : new Vector4(1f, 0f, 0f, 0f);

        /// <summary>Region 1 fading to region 2 along the piece (hair tips).</summary>
        public static MaskFunction Gradient(float start) => (along, around) =>
        {
            float tip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, 1f, along));
            return new Vector4(1f - tip, tip, 0f, 0f);
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

            /// <summary>
            /// Surface radius the body-shape deltas are scaled by; 0 uses the tube's own. Small details laid on a
            /// garment (lapels) set the garment's, so they move out with it under weight and chest shapes.
            /// </summary>
            public float ShapeRadius;

            /// <summary>
            /// Which parts of the way round to keep (0..1 around the tube; for upright tubes 0.25 faces forward). Null
            /// keeps all of it; leaving out the front makes an open jacket.
            /// </summary>
            public Func<float, bool> KeepAround;

            public Tube Clone() => (Tube)MemberwiseClone();
        }

        public enum Cap { Open, Closed, Collar }

        public int VertexCount => _positions.Count;

        int AddVertex(Vector3 position, Vector3 normal, Vector2 uv, BodyRegions region, Vector4 mask, BoneWeight weights, Vector3[] deltas) =>
            AddVertex(position, normal, uv, region == None ? (byte)0 : (byte)BodyRegionIds.IdOf(region), mask, weights, deltas);

        int AddVertex(Vector3 position, Vector3 normal, Vector2 uv, byte regionId, Vector4 mask, BoneWeight weights, Vector3[] deltas)
        {
            _positions.Add(position);
            _normals.Add(normal);
            _uvs.Add(uv);
            _uv1.Add(new Vector2(Mathf.Clamp01(mask.w), 0f));
            _colours.Add(new Color32(regionId, ToByte(mask.x), ToByte(mask.y), ToByte(mask.z)));
            _weights.Add(weights);
            _deltas.Add(deltas);
            return _positions.Count - 1;
        }

        /// <summary>
        /// Joins rows of vertices (rings) into quads. Where the mask steps between two rings (a trim or hem), that
        /// band gets its own copies of both rings in the band's colour, so the edge stays crisp instead of smearing.
        /// </summary>
        void Stitch(int first, int columns, int bands, Func<int, float> along, MaskFunction mask, Func<float, bool> keepAround = null)
        {
            for (int band = 0; band < bands; band++)
            {
                int lower = first + band * columns, upper = lower + columns;
                Vector4 a = mask(along(band), 0f), b = mask(along(band + 1), 0f);
                if (Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y), Mathf.Abs(a.z - b.z), Mathf.Abs(a.w - b.w)) > 0.5f)
                {
                    float middle = (along(band) + along(band + 1)) * 0.5f;
                    lower = Duplicate(lower, columns, middle, mask);
                    upper = Duplicate(upper, columns, middle, mask);
                }
                for (int column = 0; column < columns - 1; column++)
                    if (keepAround == null || keepAround((column + 0.5f) / (columns - 1)))
                        AddQuad(lower + column, lower + column + 1, upper + column + 1, upper + column);
            }
        }

        int Duplicate(int start, int columns, float along, MaskFunction mask)
        {
            int first = VertexCount;
            for (int column = 0; column < columns; column++)
            {
                int i = start + column;
                AddVertex(_positions[i], _normals[i], _uvs[i], _colours[i].r, mask(along, column / (columns - 1f)), _weights[i], _deltas[i]);
            }
            return first;
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
            for (int ring = 0; ring <= _rings; ring++)
            {
                float t = ring / (float)_rings;
                Vector3 centre = Vector3.Lerp(tube.Start, tube.End, t);
                float radius = Mathf.Lerp(tube.R0, tube.R1, t);
                BoneWeight weights = TubeWeights(tube, t);
                float circumference = 2f * Mathf.PI * (radius + offset) * (tube.Width + tube.Depth) * 0.5f;
                for (int side = 0; side <= _sides; side++) // the last column duplicates the first so UVs can wrap
                {
                    float around = side / (float)_sides;
                    float angle = around * 2f * Mathf.PI;
                    float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                    Vector3 spoke = u * (c * tube.Width) + v * (s * tube.Depth);
                    Vector3 normal = (u * (c / tube.Width) + v * (s / tube.Depth)).normalized;
                    Vector3 position = centre + spoke * radius + normal * offset;
                    Vector3[] deltas = withShapes ? ShapeDeltas(tube.Region, t, normal, tube.ShapeRadius > 0f ? tube.ShapeRadius : radius * spoke.magnitude) : null;
                    AddVertex(position, normal, new Vector2(around * circumference, t * length), painted, mask(t, around), weights, deltas);
                }
            }

            Stitch(first, _sides + 1, _rings, ring => ring / (float)_rings, mask, tube.KeepAround);

            AddCap(tube, startCap, first, 0f, -axis, u, v, offset, collarRadius, painted, mask);
            AddCap(tube, endCap, first + _rings * (_sides + 1), 1f, axis, u, v, offset, collarRadius, painted, mask);
        }

        void AddCap(Tube tube, Cap cap, int ringStart, float t, Vector3 normal, Vector3 u, Vector3 v, float offset, float collarRadius, BodyRegions painted, MaskFunction mask)
        {
            if (cap == Cap.Open) return;

            Vector3 centre = Vector3.Lerp(tube.Start, tube.End, t) + normal * offset;
            BoneWeight weights = TubeWeights(tube, t);
            Vector4 capMask = mask(t, 0f);
            int rim = VertexCount;
            for (int side = 0; side < _sides; side++)
                AddVertex(_positions[ringStart + side], normal, _uvs[ringStart + side], painted, capMask, weights, _deltas[ringStart + side]);

            if (cap == Cap.Closed)
            {
                int hub = AddVertex(centre, normal, Vector2.zero, painted, capMask, weights, null);
                for (int side = 0; side < _sides; side++)
                    AddTriangle(hub, rim + side, rim + (side + 1) % _sides);
                return;
            }

            int inner = VertexCount;
            for (int side = 0; side < _sides; side++)
            {
                float angle = side * 2f * Mathf.PI / _sides;
                AddVertex(centre + (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)) * collarRadius, normal, Vector2.zero, painted, capMask, weights, null);
            }
            for (int side = 0; side < _sides; side++)
            {
                if (tube.KeepAround != null && !tube.KeepAround((side + 0.5f) / _sides)) continue;
                int next = (side + 1) % _sides;
                AddQuad(rim + side, rim + next, inner + next, inner + side);
            }
        }

        /// <summary>
        /// Ellipsoid (scale per axis) around <paramref name="centre"/>, skinned to one bone. <paramref name="keep"/>
        /// selects which faces to keep by their direction from the centre (for hair caps and bangs).
        /// </summary>
        public void AddEllipsoid(Vector3 centre, Vector3 radii, BodyRegions region, bool paintRegion, string bone, MaskFunction mask, Func<Vector3, bool> keep = null)
        {
            int Latitudes = _latitudes, Longitudes = _longitudes;
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

            int Columns = Longitudes + 1;
            for (int lat = 0; lat < Latitudes; lat++)
                for (int lon = 0; lon < Longitudes; lon++)
                {
                    int a = first + lat * Columns + lon;
                    // Keep-tests use directions on the unit sphere, so squashed ellipsoids (lenses) cut where expected.
                    Vector3 middle = (Vector3.Scale(_positions[a] - centre, Inverse(radii)) + Vector3.Scale(_positions[a + 1 + Columns] - centre, Inverse(radii))).normalized;
                    if (keep == null || keep(middle))
                        AddQuad(a, a + 1, a + 1 + Columns, a + Columns);
                }
        }

        static Vector3 Inverse(Vector3 v) => new Vector3(1f / v.x, 1f / v.y, 1f / v.z);

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
            Stitch(first, Columns + 1, Rings, ring => ring / (float)Rings, mask);
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
            Stitch(first, Sides + 1, Rings, ring => ring / (float)Rings, mask);
        }

        /// <summary>Builds a new mesh, or rebuilds <paramref name="into"/> in place (keeping an existing asset's GUID).</summary>
        public Mesh Build(string name, bool withShapes, Mesh into = null)
        {
            Mesh mesh = into != null ? into : new Mesh();
            mesh.Clear();
            mesh.ClearBlendShapes();
            mesh.name = name;
            mesh.SetVertices(_positions);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.SetUVs(1, _uv1);
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
