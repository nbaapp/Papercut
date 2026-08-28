using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Accumulates fan-triangulated convex polygons and line quads into one mesh. Used by
    /// <see cref="RenderTextureFoldRenderer"/> for the sheet's layers and by <see cref="PushableBlock"/> for its
    /// clipped drawing.
    /// </summary>
    public sealed class PolygonMeshBuilder
    {
        readonly List<Vector3> vertices = new();
        readonly List<Vector2> uvs = new();
        readonly List<int> triangles = new();

        public void Clear()
        {
            vertices.Clear();
            uvs.Clear();
            triangles.Clear();
        }

        public void AddPolygon(ConvexPolygon polygon, System.Func<Vector2, Vector2> uvOf) => AddPolygon(polygon, uvOf, 0f);

        /// <summary>Adds <paramref name="polygon"/> at local depth <paramref name="z"/>, texture coordinates from <paramref name="uvOf"/>.</summary>
        public void AddPolygon(ConvexPolygon polygon, System.Func<Vector2, Vector2> uvOf, float z) => AddPolygon(polygon, uvOf, z, Vector2.zero);

        /// <summary>
        /// Adds <paramref name="polygon"/> (given in some space) translated by −<paramref name="origin"/> into the mesh's
        /// local space, at depth <paramref name="z"/>; <paramref name="uvOf"/> receives the untranslated vertices.
        /// </summary>
        public void AddPolygon(ConvexPolygon polygon, System.Func<Vector2, Vector2> uvOf, float z, Vector2 origin)
        {
            if (polygon.IsEmpty)
                return;
            var start = vertices.Count;
            var verts = polygon.Vertices;
            // Unity front faces wind clockwise as seen by the camera, which looks along +z at this xy plane.
            var ccw = SignedArea(verts) > 0f;
            for (int i = 0; i < verts.Count; i++)
            {
                vertices.Add(new Vector3(verts[i].x - origin.x, verts[i].y - origin.y, z));
                uvs.Add(uvOf(verts[i]));
            }
            for (int i = 1; i + 1 < verts.Count; i++)
            {
                triangles.Add(start);
                triangles.Add(ccw ? start + i + 1 : start + i);
                triangles.Add(ccw ? start + i : start + i + 1);
            }
        }

        public void AddLine(Vector2 a, Vector2 b, float width)
        {
            var dir = (b - a).normalized;
            var n = new Vector2(-dir.y, dir.x) * (width * 0.5f);
            AddPolygon(new ConvexPolygon(new List<Vector2> { a - n, b - n, b + n, a + n }), _ => Vector2.zero);
        }

        public void Apply(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        static float SignedArea(IReadOnlyList<Vector2> v)
        {
            float twice = 0f;
            for (int i = 0, n = v.Count; i < n; i++)
                twice += v[i].x * v[(i + 1) % n].y - v[(i + 1) % n].x * v[i].y;
            return twice;
        }
    }
}
