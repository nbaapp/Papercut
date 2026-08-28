using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    public enum OutlineKind
    {
        /// <summary>A sheet edge, or the mirror image of one lying on it: walkable ground ends at the sheet's border here, and if a neighbour with room lies beyond, it is an exit.</summary>
        SheetEdge,
        /// <summary>The sheet ends here inside its own rect: a crease (beyond it the sheet is lifted) or the edge of a landed piece lying over empty desk. Always a wall.</summary>
        Wall,
    }

    /// <summary>One boundary segment of the walkable area, sheet-local.</summary>
    public readonly struct OutlineSegment
    {
        public Vector2 A { get; }
        public Vector2 B { get; }
        public OutlineKind Kind { get; }

        /// <summary>Unit normal pointing out of the walkable area.</summary>
        public Vector2 Outward { get; }

        /// <summary>For <see cref="OutlineKind.SheetEdge"/>: the grid direction this edge faces.</summary>
        public GridDirection Direction { get; }

        public OutlineSegment(Vector2 a, Vector2 b, OutlineKind kind, Vector2 outward, GridDirection direction)
        {
            A = a;
            B = b;
            Kind = kind;
            Outward = outward;
            Direction = direction;
        }

        public Vector2 Midpoint => (A + B) * 0.5f;
        public float Length => Vector2.Distance(A, B);
    }

    /// <summary>
    /// The boundary of the ground the player can stand on: the outline of the union of every layer of the
    /// sheet where it lies (<see cref="SheetLayers.Footprint"/>). Sheet-local, pure.
    /// </summary>
    /// <remarks>
    /// Every edge of every layer polygon, minus the open interiors of all the other polygons and minus any
    /// stretch where another polygon has the same edge facing the other way (the sheet continues across it), is
    /// boundary. A boundary piece on the sheet rect's border is a <see cref="OutlineKind.SheetEdge"/> toward
    /// that side — an exit candidate, and a wall of last resort; everything else is a <see cref="OutlineKind.Wall"/>.
    /// Exits are not authored (Bible §7). The outline can have concave corners once folds stack; see
    /// <see cref="IsConvexAt"/>.
    /// </remarks>
    public static class WalkableOutline
    {
        const float Tolerance = 1e-4f;

        public static List<OutlineSegment> Segments(IReadOnlyList<ConvexPolygon> layers)
        {
            var result = new List<OutlineSegment>();
            for (int i = 0; i < layers.Count; i++)
            {
                var polygon = layers[i];
                for (int e = 0; e < polygon.Count; e++)
                {
                    var a = polygon.Vertices[e];
                    var b = polygon.Vertices[(e + 1) % polygon.Count];
                    if (Vector2.Distance(a, b) <= Tolerance)
                        continue;
                    var outward = polygon.OutwardNormal(e);
                    var pieces = new List<(Vector2 a, Vector2 b)> { (a, b) };
                    for (int j = 0; j < layers.Count && pieces.Count > 0; j++)
                    {
                        if (j == i)
                            continue;
                        var remaining = new List<(Vector2 a, Vector2 b)>();
                        foreach (var (p, q) in pieces)
                        {
                            foreach (var piece in ConvexPolygon.SubtractFromSegment(p, q, layers[j]))
                                remaining.AddRange(SubtractOppositeEdges(piece.a, piece.b, outward, layers[j]));
                        }
                        pieces = remaining;
                    }
                    foreach (var (p, q) in pieces)
                    {
                        if (Vector2.Distance(p, q) <= Tolerance)
                            continue;
                        var kind = TryEdgeDirection(p, q, outward, out var direction) ? OutlineKind.SheetEdge : OutlineKind.Wall;
                        result.Add(new OutlineSegment(p, q, kind, outward, direction));
                    }
                }
            }
            return Merge(result);
        }

        /// <summary>
        /// True if the outline turns a convex corner (walkable interior angle below 180°) at <paramref name="vertex"/>,
        /// an endpoint of <paramref name="segment"/>: the neighbouring segment leaves the vertex on the non-outward
        /// side. True when no neighbour shares the vertex (an open end is closed like a convex corner).
        /// </summary>
        public static bool IsConvexAt(IReadOnlyList<OutlineSegment> segments, in OutlineSegment segment, Vector2 vertex)
        {
            const float tolerance = 1e-3f;
            foreach (var other in segments)
            {
                if (other.A == segment.A && other.B == segment.B)
                    continue;
                Vector2 far;
                if (Vector2.Distance(other.A, vertex) < tolerance) far = other.B;
                else if (Vector2.Distance(other.B, vertex) < tolerance) far = other.A;
                else continue;
                return Vector2.Dot(far - vertex, segment.Outward) <= tolerance;
            }
            return true;
        }

        /// <summary>
        /// Segment a–b minus every stretch where <paramref name="other"/> has a collinear edge facing the opposite
        /// way: the sheet continues on both sides there, so it is not boundary.
        /// </summary>
        static List<(Vector2 a, Vector2 b)> SubtractOppositeEdges(Vector2 a, Vector2 b, Vector2 outward, ConvexPolygon other)
        {
            var pieces = new List<(Vector2 a, Vector2 b)> { (a, b) };
            var dir = (b - a).normalized;
            for (int e = 0; e < other.Count && pieces.Count > 0; e++)
            {
                if (Vector2.Distance(other.OutwardNormal(e), -outward) > Tolerance)
                    continue;
                var c = other.Vertices[e];
                var d = other.Vertices[(e + 1) % other.Count];
                if (Off(a, dir, c) > Tolerance || Off(a, dir, d) > Tolerance)
                    continue;
                var lo = Mathf.Min(Vector2.Dot(c - a, dir), Vector2.Dot(d - a, dir));
                var hi = Mathf.Max(Vector2.Dot(c - a, dir), Vector2.Dot(d - a, dir));
                var remaining = new List<(Vector2 a, Vector2 b)>();
                foreach (var (p, q) in pieces)
                {
                    var s0 = Vector2.Dot(p - a, dir);
                    var s1 = Vector2.Dot(q - a, dir);
                    if (hi <= s0 + Tolerance || lo >= s1 - Tolerance)
                    {
                        remaining.Add((p, q));
                        continue;
                    }
                    if (lo > s0 + Tolerance)
                        remaining.Add((p, a + dir * lo));
                    if (hi < s1 - Tolerance)
                        remaining.Add((a + dir * hi, q));
                }
                pieces = remaining;
            }
            return pieces;
        }

        static float Off(Vector2 origin, Vector2 dir, Vector2 p) => Mathf.Abs(Vector2.Dot(p - origin, new Vector2(-dir.y, dir.x)));

        /// <summary>True if the segment lies on the sheet rect's border facing out; gives the side it lies on.</summary>
        static bool TryEdgeDirection(Vector2 a, Vector2 b, Vector2 outward, out GridDirection direction)
        {
            var half = SheetGeometry.HalfSize;
            foreach (var candidate in new[] { GridDirection.North, GridDirection.East, GridDirection.South, GridDirection.West })
            {
                var n = candidate.ToVector();
                if (Vector2.Distance(n, outward) > Tolerance)
                    continue;
                var extent = Mathf.Abs(Vector2.Dot(half, n));
                if (Mathf.Abs(Vector2.Dot(a, n) - extent) <= Tolerance && Mathf.Abs(Vector2.Dot(b, n) - extent) <= Tolerance)
                {
                    direction = candidate;
                    return true;
                }
            }
            direction = default;
            return false;
        }

        /// <summary>Joins collinear overlapping or touching segments of the same kind and direction (coincident edges of stacked or adjacent pieces).</summary>
        static List<OutlineSegment> Merge(List<OutlineSegment> segments)
        {
            var result = new List<OutlineSegment>(segments.Count);
            var used = new bool[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i])
                    continue;
                var current = segments[i];
                used[i] = true;
                bool grew;
                do
                {
                    grew = false;
                    for (int j = 0; j < segments.Count; j++)
                    {
                        if (used[j] || !Collinear(current, segments[j]))
                            continue;
                        var dir = (current.B - current.A).normalized;
                        float t0 = 0f, t1 = Vector2.Dot(current.B - current.A, dir);
                        var s0 = Vector2.Dot(segments[j].A - current.A, dir);
                        var s1 = Vector2.Dot(segments[j].B - current.A, dir);
                        var (lo, hi) = (Mathf.Min(s0, s1), Mathf.Max(s0, s1));
                        if (hi < t0 - 1e-5f || lo > t1 + 1e-5f)
                            continue;
                        var nLo = Mathf.Min(t0, lo);
                        var nHi = Mathf.Max(t1, hi);
                        current = new OutlineSegment(current.A + dir * nLo, current.A + dir * nHi, current.Kind, current.Outward, current.Direction);
                        used[j] = true;
                        grew = true;
                    }
                } while (grew);
                result.Add(current);
            }
            return result;
        }

        static bool Collinear(OutlineSegment x, OutlineSegment y)
        {
            if (x.Kind != y.Kind || x.Direction != y.Direction || Vector2.Distance(x.Outward, y.Outward) > Tolerance)
                return false;
            var dir = (x.B - x.A).normalized;
            return Off(x.A, dir, y.A) < Tolerance && Off(x.A, dir, y.B) < Tolerance;
        }
    }
}
