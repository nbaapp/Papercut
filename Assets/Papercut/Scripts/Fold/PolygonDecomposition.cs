using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Simple polygons (any winding, concave allowed, no holes) as convex pieces: validation for authoring and
    /// load, and the decomposition the fold system needs (everything folding touches is convex, see
    /// <see cref="ConvexPolygon"/>). Pure C#, unit-testable.
    /// </summary>
    /// <remarks>
    /// Decomposition is ear clipping followed by merging across every diagonal whose removal keeps both sides
    /// convex (Hertel–Mehlhorn). The result is not minimal, but a convex input comes back as one piece and a
    /// typical concave outline as a handful, which is what physics and per-fold clipping want.
    /// </remarks>
    public static class PolygonDecomposition
    {
        /// <summary>Vertices closer than this are one vertex (sheet units).</summary>
        public const float DuplicateEpsilon = 1e-5f;

        /// <summary>Twice-the-area values at or below this are collinear (square sheet units).</summary>
        const float CrossEpsilon = 1e-6f;

        /// <summary>
        /// The outline with consecutive duplicates (including a repeated first vertex at the end) and vertices
        /// lying strictly between their neighbours removed. A spike — a vertex whose neighbours fold back along
        /// the same line — is kept, so <see cref="Validate"/> can reject it rather than silently reshaping the
        /// outline.
        /// </summary>
        public static List<Vector2> Clean(IReadOnlyList<Vector2> outline)
        {
            var result = new List<Vector2>(outline.Count);
            foreach (var p in outline)
            {
                if (result.Count == 0 || Vector2.Distance(result[result.Count - 1], p) > DuplicateEpsilon)
                    result.Add(p);
            }
            while (result.Count > 1 && Vector2.Distance(result[0], result[result.Count - 1]) <= DuplicateEpsilon)
                result.RemoveAt(result.Count - 1);

            bool removed;
            do
            {
                removed = false;
                for (int i = 0; i < result.Count && result.Count >= 3; i++)
                {
                    var p = result[(i + result.Count - 1) % result.Count];
                    var q = result[i];
                    var r = result[(i + 1) % result.Count];
                    var a = q - p;
                    var b = r - q;
                    if (Mathf.Abs(Cross(a, b)) <= CrossEpsilon && Vector2.Dot(a, b) > 0f)
                    {
                        result.RemoveAt(i);
                        removed = true;
                        break;
                    }
                }
            } while (removed);
            return result;
        }

        /// <summary>
        /// True if <paramref name="outline"/> is a simple polygon: at least three distinct vertices, non-zero
        /// area, and no two non-adjacent edges that cross or touch. <paramref name="reason"/> explains a refusal
        /// in a sentence (shown by the Sheet Studio and logged by a region that refuses itself).
        /// </summary>
        public static bool Validate(IReadOnlyList<Vector2> outline, out string reason)
        {
            var points = Clean(outline);
            if (points.Count < 3)
            {
                reason = "the outline needs at least three distinct vertices";
                return false;
            }
            // Crossing before area: a bow-tie's signed area cancels to zero, and "crosses itself" is the useful message.
            var n = points.Count;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1)
                        continue; // Adjacent around the wrap.
                    if (SegmentsTouch(points[i], points[(i + 1) % n], points[j], points[(j + 1) % n]))
                    {
                        reason = "the outline crosses itself";
                        return false;
                    }
                }
            }
            if (Mathf.Abs(SignedAreaTwice(points)) * 0.5f <= ConvexPolygon.AreaEpsilon)
            {
                reason = "the outline encloses no area";
                return false;
            }
            reason = null;
            return true;
        }

        /// <summary>
        /// Disjoint convex pieces whose union is the polygon and whose areas sum to its area. Precondition:
        /// <see cref="Validate"/>. Pieces wind counter-clockwise whatever the input's winding.
        /// </summary>
        public static List<ConvexPolygon> Decompose(IReadOnlyList<Vector2> outline)
        {
            var points = Clean(outline);
            var pieces = new List<ConvexPolygon>();
            if (points.Count < 3)
                return pieces;
            if (SignedAreaTwice(points) < 0f)
                points.Reverse();

            var cycles = Triangulate(points);
            Merge(points, cycles);
            foreach (var cycle in cycles)
            {
                var verts = new List<Vector2>(cycle.Count);
                foreach (var index in cycle)
                    verts.Add(points[index]);
                var piece = new ConvexPolygon(Clean(verts));
                if (!piece.IsEmpty)
                    pieces.Add(piece);
            }
            return pieces;
        }

        // ----- Ear clipping -----

        /// <summary>Triangles as index cycles into <paramref name="points"/> (counter-clockwise input).</summary>
        static List<List<int>> Triangulate(List<Vector2> points)
        {
            var triangles = new List<List<int>>();
            var remaining = new List<int>(points.Count);
            for (int i = 0; i < points.Count; i++)
                remaining.Add(i);

            while (remaining.Count > 3)
            {
                var ear = -1;
                var bestCross = float.NegativeInfinity;
                var bestConvex = -1;
                for (int i = 0; i < remaining.Count; i++)
                {
                    var prev = points[remaining[(i + remaining.Count - 1) % remaining.Count]];
                    var curr = points[remaining[i]];
                    var next = points[remaining[(i + 1) % remaining.Count]];
                    var cross = Cross(curr - prev, next - curr);
                    if (cross <= CrossEpsilon)
                        continue; // Reflex or degenerate: never an ear.
                    if (cross > bestCross)
                    {
                        bestCross = cross;
                        bestConvex = i;
                    }
                    if (!AnyVertexInside(points, remaining, i, prev, curr, next))
                    {
                        ear = i;
                        break;
                    }
                }
                // A simple polygon always has an ear (two-ears theorem); the fallback only guards against
                // float noise on an outline Validate accepted, so the loop always terminates.
                if (ear < 0)
                    ear = bestConvex >= 0 ? bestConvex : 0;
                triangles.Add(new List<int>
                {
                    remaining[(ear + remaining.Count - 1) % remaining.Count],
                    remaining[ear],
                    remaining[(ear + 1) % remaining.Count],
                });
                remaining.RemoveAt(ear);
            }
            triangles.Add(new List<int>(remaining));
            return triangles;
        }

        static bool AnyVertexInside(List<Vector2> points, List<int> remaining, int earIndex, Vector2 a, Vector2 b, Vector2 c)
        {
            var prevIndex = remaining[(earIndex + remaining.Count - 1) % remaining.Count];
            var currIndex = remaining[earIndex];
            var nextIndex = remaining[(earIndex + 1) % remaining.Count];
            foreach (var index in remaining)
            {
                if (index == prevIndex || index == currIndex || index == nextIndex)
                    continue;
                var p = points[index];
                // Inside or on the boundary of the counter-clockwise triangle (a vertex on the diagonal is not an ear).
                if (Cross(b - a, p - a) >= -CrossEpsilon && Cross(c - b, p - b) >= -CrossEpsilon && Cross(a - c, p - c) >= -CrossEpsilon)
                    return true;
            }
            return false;
        }

        // ----- Hertel–Mehlhorn merge -----

        /// <summary>Merges cycles across shared diagonals while the union stays convex.</summary>
        static void Merge(List<Vector2> points, List<List<int>> cycles)
        {
            bool merged;
            do
            {
                merged = false;
                for (int a = 0; a < cycles.Count && !merged; a++)
                {
                    for (int b = a + 1; b < cycles.Count && !merged; b++)
                    {
                        if (!TryMerge(points, cycles[a], cycles[b], out var union))
                            continue;
                        cycles[a] = union;
                        cycles.RemoveAt(b);
                        merged = true;
                    }
                }
            } while (merged);
        }

        /// <summary>
        /// If <paramref name="first"/> has a directed edge u→v that <paramref name="second"/> has as v→u, their
        /// union along it; true only when that union is convex.
        /// </summary>
        static bool TryMerge(List<Vector2> points, List<int> first, List<int> second, out List<int> union)
        {
            union = null;
            for (int i = 0; i < first.Count; i++)
            {
                var u = first[i];
                var v = first[(i + 1) % first.Count];
                var j = second.IndexOf(v);
                if (j < 0 || second[(j + 1) % second.Count] != u)
                    continue;

                // Walk first from v around to u, then second from u around to v (dropping the shared edge).
                var result = new List<int>(first.Count + second.Count - 2);
                for (int k = 0; k < first.Count - 1; k++)
                    result.Add(first[(i + 1 + k) % first.Count]);
                for (int k = 0; k < second.Count - 1; k++)
                    result.Add(second[(j + 1 + k) % second.Count]);
                if (!IsConvex(points, result))
                    return false;
                union = result;
                return true;
            }
            return false;
        }

        static bool IsConvex(List<Vector2> points, List<int> cycle)
        {
            for (int i = 0; i < cycle.Count; i++)
            {
                var p = points[cycle[(i + cycle.Count - 1) % cycle.Count]];
                var q = points[cycle[i]];
                var r = points[cycle[(i + 1) % cycle.Count]];
                if (Cross(q - p, r - q) < -CrossEpsilon)
                    return false;
            }
            return true;
        }

        // ----- Geometry -----

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static float SignedAreaTwice(List<Vector2> points)
        {
            var twice = 0f;
            for (int i = 0, n = points.Count; i < n; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % n];
                twice += a.x * b.y - b.x * a.y;
            }
            return twice;
        }

        /// <summary>True if closed segments ab and cd share any point (a crossing, a touch, or a collinear overlap).</summary>
        static bool SegmentsTouch(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var d1 = Cross(b - a, c - a);
            var d2 = Cross(b - a, d - a);
            var d3 = Cross(d - c, a - c);
            var d4 = Cross(d - c, b - c);
            if (((d1 > CrossEpsilon && d2 < -CrossEpsilon) || (d1 < -CrossEpsilon && d2 > CrossEpsilon))
                && ((d3 > CrossEpsilon && d4 < -CrossEpsilon) || (d3 < -CrossEpsilon && d4 > CrossEpsilon)))
                return true;
            return (Mathf.Abs(d1) <= CrossEpsilon && OnSegment(a, b, c))
                || (Mathf.Abs(d2) <= CrossEpsilon && OnSegment(a, b, d))
                || (Mathf.Abs(d3) <= CrossEpsilon && OnSegment(c, d, a))
                || (Mathf.Abs(d4) <= CrossEpsilon && OnSegment(c, d, b));
        }

        /// <summary>True if <paramref name="p"/>, known collinear with segment ab, lies within it.</summary>
        static bool OnSegment(Vector2 a, Vector2 b, Vector2 p)
            => p.x >= Mathf.Min(a.x, b.x) - DuplicateEpsilon && p.x <= Mathf.Max(a.x, b.x) + DuplicateEpsilon
            && p.y >= Mathf.Min(a.y, b.y) - DuplicateEpsilon && p.y <= Mathf.Max(a.y, b.y) + DuplicateEpsilon;
    }
}
