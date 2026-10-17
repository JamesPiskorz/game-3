using System.Collections.Generic;
using UnityEngine;

namespace Game.Characters.Rendering
{
    /// <summary>
    /// Bakes smoothed normals (averaged over vertices that share a position) into a mesh's tangent channel for
    /// the toon outline pass. Tangents are skinned along with positions and normals, unlike UV channels, so the
    /// outline follows posed bones. Character meshes don't use normal maps, so the tangents are free.
    /// </summary>
    public static class OutlineNormals
    {
        const float WeldDistance = 1e-4f;

        public static void Bake(Mesh mesh)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            mesh.GetVertices(positions);
            mesh.GetNormals(normals);
            if (normals.Count != positions.Count)
            {
                mesh.RecalculateNormals();
                mesh.GetNormals(normals);
            }

            var sums = new Dictionary<Vector3Int, Vector3>();
            var keys = new Vector3Int[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                keys[i] = Vector3Int.RoundToInt(positions[i] / WeldDistance);
                sums.TryGetValue(keys[i], out Vector3 sum);
                sums[keys[i]] = sum + normals[i];
            }

            var tangents = new Vector4[positions.Count];
            for (int i = 0; i < positions.Count; i++)
            {
                Vector3 smooth = sums[keys[i]];
                smooth = smooth.sqrMagnitude > 1e-8f ? smooth.normalized : normals[i];
                tangents[i] = new Vector4(smooth.x, smooth.y, smooth.z, 1f);
            }
            mesh.tangents = tangents;
        }
    }
}
