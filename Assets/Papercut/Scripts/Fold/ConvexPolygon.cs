using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// An immutable convex polygon in 2D. Everything folding touches is convex: the sheet rect, every piece of
    /// it a sequence of creases cuts out, their mirror images, and box footprints.
    /// </summary>
    /// <remarks>
    /// Vertices are stored in the order they were given; clipping preserves winding, a mirror reverses it, and
    /// every operation is winding-aware. Pure C#, no Unity objects, so it is unit-testable.
    /// </remarks>
    public sealed class ConvexPolygon
    {
        /// <summary>Areas at or below this are treated as empty (square units).</summary>
        public const float AreaEpsilon = 1e-5f;

        /// <summary>Distances within this of a clipping line count as on it.</summary>
        const float OnLineEpsilon = 1e-6f;

        public static readonly ConvexPolygon Empty = new(new List<Vector2>());

        readonly List<Vector2> vertices;

        public IReadOnlyList<Vector2> Vertices => vertices;
        public int Count => vertices.Count;
        public bool IsEmpty => vertices.Count < 3 || Area <= AreaEpsilon;

        public ConvexPolygon(List<Vector2> vertices)
        {
            this.vertices = vertices;
        }

        public static ConvexPolygon FromRect(Rect rect) => new(new List<Vector2>
        {
            new(rect.xMin, rect.yMin),
            new(rect.xMax, rect.yMin),
            new(rect.xMax, rect.yMax),
            new(rect.xMin, rect.yMax),
        });

        /// <summary>Unsigned area (shoelace).</summary>
        public float Area => Mathf.Abs(SignedArea) * 0.5f;

        /// <summary>Twice the signed area: positive for counter-clockwise vertices.</summary>
        float SignedArea
        {
            get
            {
                float twice = 0f;
                for (int i = 0, n = vertices.Count; i < n; i++)
                {
                    var a = vertices[i];
                    var b = vertices[(i + 1) % n];
                    twice += a.x * b.y - b.x * a.y;
                }
                return twice;
            }
        }

        /// <summary>Axis-aligned bounds; a zero rect when empty.</summary>
        public Rect Bounds
        {
            get
            {
                if (vertices.Count == 0)
                    return Rect.zero;
                var min = vertices[0];
                var max = vertices[0];
                foreach (var v in vertices)
                {
                    min = Vector2.Min(min, v);
                    max = Vector2.Max(max, v);
                }
                return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }

        /// <summary>Unit normal of edge i (from vertex i to i + 1) pointing out of the polygon, whatever the winding.</summary>
        public Vector2 OutwardNormal(int edge)
        {
            var a = vertices[edge];
            var b = vertices[(edge + 1) % vertices.Count];
            var dir = (b - a).normalized;
            var n = new Vector2(dir.y, -dir.x); // outward for counter-clockwise winding
            return SignedArea >= 0f ? n : -n;
        }

        /// <summary>
        /// The part of this polygon on the side of the line through <paramref name="point"/> where
        /// dot(p - point, normal) is ≥ 0 (or ≤ 0 when <paramref name="keepPositive"/> is false).
        /// Sutherland–Hodgman against one plane.
        /// </summary>
        public ConvexPolygon ClipToHalfPlane(Vector2 point, Vector2 normal, bool keepPositive = true)
        {
            var sign = keepPositive ? 1f : -1f;
            var result = new List<Vector2>(vertices.Count + 2);
            for (int i = 0, n = vertices.Count; i < n; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % n];
                var da = sign * Vector2.Dot(a - point, normal);
                var db = sign * Vector2.Dot(b - point, normal);
                var aIn = da >= -OnLineEpsilon;
                var bIn = db >= -OnLineEpsilon;

                if (aIn)
                    result.Add(a);
                // A vertex on the line is kept as itself; only a true crossing adds a new vertex.
                if (aIn != bIn && Mathf.Abs(da) > OnLineEpsilon && Mathf.Abs(db) > OnLineEpsilon)
                {
                    var t = da / (da - db);
                    result.Add(a + (b - a) * t);
                }
            }
            return new ConvexPolygon(result);
        }

        /// <summary>This polygon clipped to <paramref name="rect"/>.</summary>
        public ConvexPolygon ClipToRect(Rect rect) => this
            .ClipToHalfPlane(new Vector2(rect.xMin, 0f), Vector2.right)
            .ClipToHalfPlane(new Vector2(rect.xMax, 0f), Vector2.left)
            .ClipToHalfPlane(new Vector2(0f, rect.yMin), Vector2.up)
            .ClipToHalfPlane(new Vector2(0f, rect.yMax), Vector2.down);

        /// <summary>This polygon clipped to the inside of <paramref name="other"/> (the intersection).</summary>
        public ConvexPolygon Intersect(ConvexPolygon other)
        {
            var result = this;
            for (int i = 0; i < other.Count && !result.IsEmpty; i++)
                result = result.ClipToHalfPlane(other.vertices[i], other.OutwardNormal(i), keepPositive: false);
            return result;
        }

        /// <summary>
        /// This polygon minus <paramref name="other"/>, as disjoint convex pieces: for each edge of the other,
        /// the part of what is left that lies outside that edge. The whole polygon (one piece) if they do not overlap.
        /// </summary>
        public List<ConvexPolygon> Subtract(ConvexPolygon other)
        {
            var pieces = new List<ConvexPolygon>();
            if (!Overlaps(other))
            {
                pieces.Add(this);
                return pieces;
            }
            var remaining = this;
            for (int i = 0; i < other.Count && !remaining.IsEmpty; i++)
            {
                var point = other.vertices[i];
                var outward = other.OutwardNormal(i);
                var outside = remaining.ClipToHalfPlane(point, outward, keepPositive: true);
                if (!outside.IsEmpty)
                    pieces.Add(outside);
                remaining = remaining.ClipToHalfPlane(point, outward, keepPositive: false);
            }
            return pieces;
        }

        /// <summary>Mirror image across <paramref name="crease"/>. A mirror reverses winding; the result is still convex.</summary>
        public ConvexPolygon Reflect(Crease crease)
        {
            var result = new List<Vector2>(vertices.Count);
            foreach (var v in vertices)
                result.Add(crease.Reflect(v));
            return new ConvexPolygon(result);
        }

        /// <summary>This polygon under a rigid transform.</summary>
        public ConvexPolygon Transform(Isometry2D transform)
        {
            var result = new List<Vector2>(vertices.Count);
            foreach (var v in vertices)
                result.Add(transform.Apply(v));
            return new ConvexPolygon(result);
        }

        /// <summary>True if this polygon and <paramref name="rect"/> share any area.</summary>
        public bool Overlaps(Rect rect) => !ClipToRect(rect).IsEmpty;

        /// <summary>True if the two polygons share any area (touching edges do not count).</summary>
        public bool Overlaps(ConvexPolygon other) => !Intersect(other).IsEmpty;

        /// <summary>True if every vertex is inside <paramref name="rect"/>, within <paramref name="tolerance"/>.</summary>
        public bool ContainedIn(Rect rect, float tolerance)
        {
            foreach (var v in vertices)
            {
                if (v.x < rect.xMin - tolerance || v.x > rect.xMax + tolerance || v.y < rect.yMin - tolerance || v.y > rect.yMax + tolerance)
                    return false;
            }
            return true;
        }

        /// <summary>True if <paramref name="p"/> is inside or on the boundary.</summary>
        public bool Contains(Vector2 p)
        {
            if (vertices.Count < 3)
                return false;
            for (int i = 0; i < vertices.Count; i++)
            {
                if (Vector2.Dot(p - vertices[i], OutwardNormal(i)) > OnLineEpsilon)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The segment of the line through <paramref name="point"/> along <paramref name="direction"/> that lies
        /// inside this polygon (Liang–Barsky against every edge). False if the line misses it or only grazes it.
        /// </summary>
        public bool TryClipLine(Vector2 point, Vector2 direction, out Vector2 a, out Vector2 b)
        {
            var tMin = float.NegativeInfinity;
            var tMax = float.PositiveInfinity;
            for (int i = 0; i < vertices.Count; i++)
            {
                var n = OutwardNormal(i);
                var denom = Vector2.Dot(direction, n);
                var numer = Vector2.Dot(vertices[i] - point, n); // inside where dot(p - v, n) <= 0, i.e. t * denom <= numer
                if (Mathf.Abs(denom) < 1e-9f)
                {
                    if (numer <= OnLineEpsilon) // parallel and outside, or along this edge (grazing): no chord
                    {
                        a = b = point;
                        return false;
                    }
                    continue;
                }
                var t = numer / denom;
                if (denom > 0f) tMax = Mathf.Min(tMax, t);
                else tMin = Mathf.Max(tMin, t);
            }
            if (vertices.Count < 3 || tMax - tMin <= 1e-5f)
            {
                a = b = point;
                return false;
            }
            a = point + direction * tMin;
            b = point + direction * tMax;
            return true;
        }

        /// <summary>
        /// The parts of segment <paramref name="a"/>–<paramref name="b"/> that are not inside
        /// <paramref name="polygon"/> (≤ 2 pieces). Open by default: a segment lying along an edge of the polygon
        /// is kept whole. <paramref name="closed"/> removes the stretch along the boundary too.
        /// </summary>
        public static List<(Vector2 a, Vector2 b)> SubtractFromSegment(Vector2 a, Vector2 b, ConvexPolygon polygon, bool closed = false)
        {
            var pieces = new List<(Vector2, Vector2)>(2);
            var tIn = 0f;
            var tOut = 1f;
            var threshold = closed ? OnLineEpsilon : -OnLineEpsilon;
            for (int i = 0; i < polygon.Count; i++)
            {
                var n = polygon.OutwardNormal(i);
                var da = Vector2.Dot(a - polygon.vertices[i], n); // < 0 inside this edge's half-plane
                var db = Vector2.Dot(b - polygon.vertices[i], n);
                var aIn = da < threshold;
                var bIn = db < threshold;
                if (!aIn && !bIn)
                {
                    pieces.Add((a, b)); // never strictly inside
                    return pieces;
                }
                if (aIn && bIn)
                    continue;
                var t = da / (da - db);
                if (aIn) tOut = Mathf.Min(tOut, t);
                else tIn = Mathf.Max(tIn, t);
            }
            if (tOut - tIn <= 1e-5f)
            {
                pieces.Add((a, b));
                return pieces;
            }
            var len = Vector2.Distance(a, b);
            if (tIn * len > 1e-5f)
                pieces.Add((a, a + (b - a) * tIn));
            if ((1f - tOut) * len > 1e-5f)
                pieces.Add((a + (b - a) * tOut, b));
            return pieces;
        }
    }
}
