using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    public enum OutlineKind
    {
        /// <summary>A sheet edge, or the mirror image of one on the landed Flap: walkable ground ends here, and if a neighbour with room lies beyond, it is an exit.</summary>
        SheetEdge,
        /// <summary>The crease: beyond it the sheet is lifted. Always a wall.</summary>
        Crease,
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
    /// The boundary of the ground the player can stand on. Sheet-local, pure.
    /// </summary>
    /// <remarks>
    /// With one fold the walkable area is the Base plus the landed Flap:
    /// <c>(sheet ∪ reflect(sheet)) ∩ H−</c>, where <c>reflect(sheet)</c> is an axis-aligned rect (every legal
    /// crease is at 0°, 90° or ±45°) and <c>H−</c> is the non-Flap side of the crease. Its boundary is the
    /// boundary of the two-rect union clipped to H−, plus the crease where it runs through the union.
    /// Exits are not authored: every <see cref="OutlineKind.SheetEdge"/> segment is an exit candidate toward
    /// the direction it faces, and a wall of last resort. With more than one fold the union would be of
    /// n + 1 rects clipped by n half-planes; only <see cref="AddRectEdges"/> would need generalising.
    /// </remarks>
    public static class WalkableOutline
    {
        public static List<OutlineSegment> Segments(IReadOnlyList<Fold> folds)
        {
            var sheet = FoldGeometry.Sheet;
            var result = new List<OutlineSegment>();

            if (folds.Count == 0)
            {
                AddRectEdges(result, sheet, null, null);
                return Merge(result);
            }

            if (folds.Count > 1)
                Debug.LogError("WalkableOutline supports one fold; using the first.");

            var fold = folds[0];
            var crease = FoldGeometry.CreaseOf(fold);
            var mirrored = FoldGeometry.ReflectedSheet(fold);

            AddRectEdges(result, sheet, mirrored, crease);
            AddRectEdges(result, mirrored, sheet, crease);

            // The crease is a wall wherever it crosses the union of the two rects.
            var intervals = new List<(float, float)>(2);
            if (FoldGeometry.TryClipLineToRect(crease, sheet, out var a1, out var b1))
                intervals.Add(Param(crease, a1, b1));
            if (FoldGeometry.TryClipLineToRect(crease, mirrored, out var a2, out var b2))
                intervals.Add(Param(crease, a2, b2));
            foreach (var (t0, t1) in MergeIntervals(intervals))
                result.Add(new OutlineSegment(crease.Point + crease.Direction * t0, crease.Point + crease.Direction * t1,
                    OutlineKind.Crease, crease.FlapNormal, default));

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
        /// The four edges of <paramref name="rect"/> minus the interior of <paramref name="other"/>, clipped to
        /// the non-Flap side of <paramref name="crease"/>. Both rects are axis-aligned, so each edge faces its own
        /// side whichever sheet edge it is the mirror image of.
        /// </summary>
        static void AddRectEdges(List<OutlineSegment> result, Rect rect, Rect? other, Crease? crease)
        {
            foreach (var direction in new[] { GridDirection.North, GridDirection.East, GridDirection.South, GridDirection.West })
            {
                var facing = direction;
                var outward = facing.ToVector();
                Vector2 a, b;
                switch (direction)
                {
                    case GridDirection.North: a = new Vector2(rect.xMin, rect.yMax); b = new Vector2(rect.xMax, rect.yMax); break;
                    case GridDirection.South: a = new Vector2(rect.xMin, rect.yMin); b = new Vector2(rect.xMax, rect.yMin); break;
                    case GridDirection.East: a = new Vector2(rect.xMax, rect.yMin); b = new Vector2(rect.xMax, rect.yMax); break;
                    default: a = new Vector2(rect.xMin, rect.yMin); b = new Vector2(rect.xMin, rect.yMax); break;
                }

                foreach (var (p, q) in other.HasValue ? SubtractOpenRect(a, b, other.Value) : new List<(Vector2, Vector2)> { (a, b) })
                {
                    var s = p;
                    var e = q;
                    if (crease.HasValue && !TryClipToNonFlapSide(p, q, crease.Value, out s, out e))
                        continue;
                    if (Vector2.Distance(s, e) > 1e-5f)
                        result.Add(new OutlineSegment(s, e, OutlineKind.SheetEdge, outward, facing));
                }
            }
        }

        /// <summary>An axis-aligned segment minus the open interior of <paramref name="rect"/> (≤ 2 pieces).</summary>
        static List<(Vector2, Vector2)> SubtractOpenRect(Vector2 a, Vector2 b, Rect rect)
        {
            var pieces = new List<(Vector2, Vector2)>(2);
            var horizontal = Mathf.Approximately(a.y, b.y);
            var fixedCoord = horizontal ? a.y : a.x;
            var (fixedMin, fixedMax) = horizontal ? (rect.yMin, rect.yMax) : (rect.xMin, rect.xMax);
            if (fixedCoord <= fixedMin + 1e-6f || fixedCoord >= fixedMax - 1e-6f)
            {
                pieces.Add((a, b));
                return pieces;
            }

            var (lo, hi) = horizontal ? (rect.xMin, rect.xMax) : (rect.yMin, rect.yMax);
            var sa = horizontal ? a.x : a.y;
            var sb = horizontal ? b.x : b.y;
            var sMin = Mathf.Min(sa, sb);
            var sMax = Mathf.Max(sa, sb);
            Vector2 At(float s) => horizontal ? new Vector2(s, fixedCoord) : new Vector2(fixedCoord, s);

            if (lo > sMin + 1e-6f)
                pieces.Add((At(sMin), At(Mathf.Min(lo, sMax))));
            if (hi < sMax - 1e-6f)
                pieces.Add((At(Mathf.Max(hi, sMin)), At(sMax)));
            return pieces;
        }

        static bool TryClipToNonFlapSide(Vector2 a, Vector2 b, Crease crease, out Vector2 p, out Vector2 q)
        {
            const float onLine = 1e-6f;
            var da = crease.SignedDistance(a);
            var db = crease.SignedDistance(b);
            p = a;
            q = b;
            var aOut = da > onLine;
            var bOut = db > onLine;
            if (aOut && bOut)
                return false;
            if (aOut)
                p = a + (b - a) * (da / (da - db));
            else if (bOut)
                q = a + (b - a) * (da / (da - db));
            return Vector2.Distance(p, q) > 1e-5f;
        }

        static (float, float) Param(Crease c, Vector2 a, Vector2 b)
        {
            var ta = Vector2.Dot(a - c.Point, c.Direction);
            var tb = Vector2.Dot(b - c.Point, c.Direction);
            return (Mathf.Min(ta, tb), Mathf.Max(ta, tb));
        }

        static List<(float, float)> MergeIntervals(List<(float, float)> intervals)
        {
            intervals.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            var merged = new List<(float, float)>();
            foreach (var (lo, hi) in intervals)
            {
                if (merged.Count > 0 && lo <= merged[^1].Item2 + 1e-5f)
                    merged[^1] = (merged[^1].Item1, Mathf.Max(merged[^1].Item2, hi));
                else
                    merged.Add((lo, hi));
            }
            return merged;
        }

        /// <summary>Joins collinear overlapping or touching segments of the same kind and direction (coincident edges of the two rects).</summary>
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
            if (x.Kind != y.Kind || x.Direction != y.Direction || Vector2.Distance(x.Outward, y.Outward) > 1e-4f)
                return false;
            var dir = (x.B - x.A).normalized;
            float Off(Vector2 p) => Mathf.Abs(Vector2.Dot(p - x.A, new Vector2(-dir.y, dir.x)));
            return Off(y.A) < 1e-4f && Off(y.B) < 1e-4f;
        }
    }
}
