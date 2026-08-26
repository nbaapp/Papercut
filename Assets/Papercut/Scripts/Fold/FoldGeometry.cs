using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Every geometric rule of a fold, in sheet-local space (origin at the sheet centre, +y north, +x east).
    /// Pure and Unity-object-free so it can be unit-tested; the only place crease families, reflection,
    /// coverage and the Back pose are defined.
    /// </summary>
    /// <remarks>
    /// Useful facts that the rest of the fold system relies on:
    /// <list type="bullet">
    /// <item>Every legal crease is at 0°, 90° or ±45°, so the mirror image of an axis-aligned rect is an axis-aligned rect.</item>
    /// <item>Flap ∪ landed Flap, within the sheet, is an axis-aligned rect (<see cref="HiddenRect"/>): a strip of
    /// depth 2d for an edge fold, a d×d square at the corner for a corner fold.</item>
    /// <item>Back-to-Front mirroring composed with reflection across the crease is a pure rotation (<see cref="BackPose"/>).</item>
    /// </list>
    /// </remarks>
    public static class FoldGeometry
    {
        static readonly Rect SheetRect = SheetGeometry.BoundsAt(Vector2.zero);

        /// <summary>Tolerance for depth and containment comparisons, in sheet units.</summary>
        public const float DepthEpsilon = 1e-5f;

        /// <summary>The sheet in sheet-local space.</summary>
        public static Rect Sheet => SheetRect;

        public static Crease CreaseOf(Fold fold)
        {
            var half = SheetGeometry.HalfSize;
            if (fold.IsEdge)
            {
                var outward = fold.Anchor.EdgeDirection().ToVector();
                var halfExtent = Mathf.Abs(Vector2.Dot(half, outward));
                var point = outward * (halfExtent - fold.Depth);
                var direction = new Vector2(-outward.y, outward.x);
                return new Crease(point, direction, outward);
            }

            var signs = fold.Anchor.CornerSigns();
            var corner = Vector2.Scale(signs, half);
            var mid = corner - signs * (fold.Depth * 0.5f);
            var normal = signs / Mathf.Sqrt(2f);
            var along = new Vector2(-signs.y, signs.x) / Mathf.Sqrt(2f);
            return new Crease(mid, along, normal);
        }

        /// <summary>
        /// The largest depth whose landed Flap stays on the sheet (Aaron, 2026-08-26: folds may not extend past
        /// the edge). Edge fold: half the extent, so the Flap's edge lands on the far edge at most. Corner fold:
        /// the sheet height, so the reflected corner stays inside (the sheet is wider than it is tall).
        /// The geometry below is still general; this is the rule input and the model enforce.
        /// </summary>
        public static float MaxDepth(FoldAnchor anchor)
        {
            if (anchor.IsCorner())
                return Mathf.Min(SheetGeometry.Width, SheetGeometry.Height);
            var outward = anchor.EdgeDirection().ToVector();
            return (outward.x != 0f ? SheetGeometry.Width : SheetGeometry.Height) * 0.5f;
        }

        /// <summary>
        /// The Seam: where the landed Flap's edge meets the Front — the mirror image of the anchoring sheet
        /// edge (edge fold) or of the two corner-adjacent edges (corner fold), each clipped to the Flap side of the
        /// crease. One segment for an edge fold, two legs meeting at the reflected corner for a corner fold.
        /// Sheet-local. The crease is the Flap's other boundary and is not part of the seam. Exact for depths up
        /// to <see cref="MaxDepth"/> (the only ones the game makes); a deeper corner fold would also have a
        /// reflected far-edge piece, which is not computed.
        /// </summary>
        public static List<(Vector2 a, Vector2 b)> SeamSegments(Fold fold)
        {
            var crease = CreaseOf(fold);
            var half = SheetGeometry.HalfSize;
            var seams = new List<(Vector2, Vector2)>(2);

            void AddEdge(Vector2 a, Vector2 b)
            {
                var da = crease.SignedDistance(a);
                var db = crease.SignedDistance(b);
                if (da <= 1e-6f && db <= 1e-6f)
                    return;
                if (da < 0f) a = a + (b - a) * (da / (da - db));
                else if (db < 0f) b = a + (b - a) * (da / (da - db));
                if (Vector2.Distance(a, b) > 1e-5f)
                    seams.Add((crease.Reflect(a), crease.Reflect(b)));
            }

            if (fold.IsEdge)
            {
                var outward = fold.Anchor.EdgeDirection().ToVector();
                var along = new Vector2(-outward.y, outward.x);
                var mid = Vector2.Scale(outward, half);
                var halfAlong = Mathf.Abs(Vector2.Dot(half, along));
                AddEdge(mid - along * halfAlong, mid + along * halfAlong);
            }
            else
            {
                var signs = fold.Anchor.CornerSigns();
                var corner = Vector2.Scale(signs, half);
                AddEdge(corner, new Vector2(-corner.x, corner.y)); // edge along x from the corner
                AddEdge(corner, new Vector2(corner.x, -corner.y)); // edge along y from the corner
            }
            return seams;
        }

        /// <summary>Distance from <paramref name="local"/> to the nearest seam segment (∞ if there is none).</summary>
        public static float DistanceToSeam(Fold fold, Vector2 local)
        {
            var best = float.PositiveInfinity;
            foreach (var (a, b) in SeamSegments(fold))
                best = Mathf.Min(best, DistanceToSegment(local, a, b));
            return best;
        }

        /// <summary>The part of the sheet that lifts: the sheet on the Flap side of the crease.</summary>
        public static ConvexPolygon FlapRegion(Fold fold)
        {
            var crease = CreaseOf(fold);
            return ConvexPolygon.FromRect(SheetRect).ClipToHalfPlane(crease.Point, crease.FlapNormal);
        }

        /// <summary>Where the Flap lies after folding: its mirror image across the crease. May overhang the sheet.</summary>
        public static ConvexPolygon LandedRegion(Fold fold) => FlapRegion(fold).Reflect(CreaseOf(fold));

        /// <summary>The mirror image of the whole sheet rect across the crease — still axis-aligned.</summary>
        public static Rect ReflectedSheet(Fold fold) => ConvexPolygon.FromRect(SheetRect).Reflect(CreaseOf(fold)).Bounds;

        /// <summary>
        /// Flap ∪ landed Flap, within the sheet: the region of the Front face that is no longer visible or
        /// interactive. Always an axis-aligned rectangle (see class remarks).
        /// </summary>
        public static Rect HiddenRect(Fold fold)
        {
            var half = SheetGeometry.HalfSize;
            Rect region;
            if (fold.IsEdge)
            {
                var outward = fold.Anchor.EdgeDirection().ToVector();
                var depth = 2f * fold.Depth;
                region = outward.x != 0f
                    ? Rect.MinMaxRect(outward.x > 0f ? half.x - depth : -half.x, -half.y, outward.x > 0f ? half.x : -half.x + depth, half.y)
                    : Rect.MinMaxRect(-half.x, outward.y > 0f ? half.y - depth : -half.y, half.x, outward.y > 0f ? half.y : -half.y + depth);
            }
            else
            {
                var signs = fold.Anchor.CornerSigns();
                var d = fold.Depth;
                var xMin = signs.x > 0f ? half.x - d : -half.x;
                var xMax = signs.x > 0f ? half.x : -half.x + d;
                var yMin = signs.y > 0f ? half.y - d : -half.y;
                var yMax = signs.y > 0f ? half.y : -half.y + d;
                region = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
            }
            return Intersect(region, SheetRect);
        }

        /// <summary>
        /// Local pose of the Back root that puts every Back object at its landed position: position and
        /// rotation (degrees). Back point b appears at Reflect(BackToFront(b)); two mirrors compose to a rotation
        /// by 2θ + 180° (θ = crease angle) about wherever the Back origin lands.
        /// </summary>
        public static (Vector2 position, float rotationDegrees) BackPose(Fold fold)
        {
            var crease = CreaseOf(fold);
            return (crease.Reflect(Vector2.zero), crease.AngleDegrees * 2f + 180f);
        }

        /// <summary>Where a Back-space point appears once <paramref name="fold"/> is made.</summary>
        public static Vector2 BackToLanded(Fold fold, Vector2 backLocal)
            => CreaseOf(fold).Reflect(SheetGeometry.BackToFront(backLocal));

        /// <summary>The Back-space point that lies beneath a landed-Flap point (inverse of <see cref="BackToLanded"/>).</summary>
        public static Vector2 LandedToBack(Fold fold, Vector2 landedLocal)
            => SheetGeometry.BackToFront(CreaseOf(fold).Reflect(landedLocal));

        /// <summary>
        /// Depth such that the grabbed edge or corner sits under <paramref name="local"/>: the edge lands 2d
        /// in, so d is half the inward distance; the corner lands d along both edges, so d is the mean of the
        /// two inward distances. Clamped to [0, <see cref="MaxDepth"/>].
        /// </summary>
        public static float DepthForDragPoint(FoldAnchor anchor, Vector2 local)
        {
            var half = SheetGeometry.HalfSize;
            float depth;
            if (anchor.IsEdge())
            {
                var outward = anchor.EdgeDirection().ToVector();
                var halfExtent = Mathf.Abs(Vector2.Dot(half, outward));
                var inward = halfExtent - Vector2.Dot(local, outward);
                depth = inward * 0.5f;
            }
            else
            {
                var signs = anchor.CornerSigns();
                var u = half.x - signs.x * local.x;
                var v = half.y - signs.y * local.y;
                depth = (u + v) * 0.5f;
            }
            return Mathf.Clamp(depth, 0f, MaxDepth(anchor));
        }

        /// <summary>The crease's endpoints where it meets the sheet rect. False if it misses the sheet.</summary>
        public static bool TryCreaseSegment(Fold fold, out Vector2 a, out Vector2 b)
            => TryClipLineToRect(CreaseOf(fold), SheetRect, out a, out b);

        /// <summary>Distance from <paramref name="local"/> to the crease segment inside the sheet.</summary>
        public static float DistanceToCrease(Fold fold, Vector2 local)
        {
            if (!TryCreaseSegment(fold, out var a, out var b))
                return float.PositiveInfinity;
            return DistanceToSegment(local, a, b);
        }

        /// <summary>
        /// Coverage of an axis-aligned footprint (face-local) by <paramref name="fold"/>. Front: hidden where
        /// it meets <see cref="HiddenRect"/>; the visible remainder is up to four rects. Back: exposed where its
        /// Front-space image is on the Flap side of the crease; a straddling footprint is clipped once.
        /// </summary>
        public static CoverageResult Coverage(Rect footprint, Fold fold, SheetFace face)
        {
            if (footprint.width <= 0f || footprint.height <= 0f)
                return CoverageResult.None(FoldCoverage.Uncovered);

            if (face == SheetFace.Front)
            {
                var hidden = HiddenRect(fold);
                var inter = Intersect(footprint, hidden);
                if (inter.width <= 0f || inter.height <= 0f)
                    return CoverageResult.Whole(FoldCoverage.Uncovered, footprint);
                if (Contains(hidden, footprint))
                    return CoverageResult.None(FoldCoverage.Covered);
                return CoverageResult.Clipped(Subtract(footprint, hidden));
            }

            // Back-space: p_front = (-x, y). dot(p_front - point, n) >= 0  <=>  dot(p_back - point', n') >= 0
            // with point' = (-point.x, point.y), n' = (-n.x, n.y).
            var crease = CreaseOf(fold);
            var backPoint = new Vector2(-crease.Point.x, crease.Point.y);
            var backNormal = new Vector2(-crease.FlapNormal.x, crease.FlapNormal.y);
            var exposed = ConvexPolygon.FromRect(footprint).ClipToHalfPlane(backPoint, backNormal).ClipToRect(SheetRect);
            var area = footprint.width * footprint.height;
            if (exposed.IsEmpty)
                return CoverageResult.None(FoldCoverage.Uncovered);
            if (exposed.Area >= area - ConvexPolygon.AreaEpsilon)
                return CoverageResult.Whole(FoldCoverage.Covered, footprint);
            return CoverageResult.Clipped(new[] { exposed });
        }

        /// <summary>The rects of <paramref name="a"/> outside <paramref name="b"/> (up to four), each with positive area.</summary>
        public static List<ConvexPolygon> Subtract(Rect a, Rect b)
        {
            var parts = new List<ConvexPolygon>(4);
            void Add(float xMin, float yMin, float xMax, float yMax)
            {
                if (xMax - xMin > 1e-6f && yMax - yMin > 1e-6f)
                    parts.Add(ConvexPolygon.FromRect(Rect.MinMaxRect(xMin, yMin, xMax, yMax)));
            }

            Add(a.xMin, a.yMin, Mathf.Min(a.xMax, b.xMin), a.yMax);            // left strip
            Add(Mathf.Max(a.xMin, b.xMax), a.yMin, a.xMax, a.yMax);            // right strip
            var midMin = Mathf.Max(a.xMin, b.xMin);
            var midMax = Mathf.Min(a.xMax, b.xMax);
            Add(midMin, a.yMin, midMax, Mathf.Min(a.yMax, b.yMin));            // below
            Add(midMin, Mathf.Max(a.yMin, b.yMax), midMax, a.yMax);            // above
            return parts;
        }

        public static Rect Intersect(Rect a, Rect b)
        {
            var xMin = Mathf.Max(a.xMin, b.xMin);
            var yMin = Mathf.Max(a.yMin, b.yMin);
            var xMax = Mathf.Min(a.xMax, b.xMax);
            var yMax = Mathf.Min(a.yMax, b.yMax);
            if (xMax <= xMin || yMax <= yMin)
                return Rect.zero;
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        public static bool Contains(Rect outer, Rect inner)
            => inner.xMin >= outer.xMin - DepthEpsilon && inner.xMax <= outer.xMax + DepthEpsilon
            && inner.yMin >= outer.yMin - DepthEpsilon && inner.yMax <= outer.yMax + DepthEpsilon;

        /// <summary>The segment of a line inside <paramref name="rect"/> (Liang–Barsky). False if the line misses it.</summary>
        public static bool TryClipLineToRect(Crease line, Rect rect, out Vector2 a, out Vector2 b)
        {
            var p = line.Point;
            var d = line.Direction;
            var tMin = float.NegativeInfinity;
            var tMax = float.PositiveInfinity;

            bool Clip(float denom, float numer)
            {
                if (Mathf.Abs(denom) < 1e-9f)
                    return numer <= 0f; // parallel: inside iff already on the inner side
                var t = numer / denom;
                if (denom > 0f) tMin = Mathf.Max(tMin, t);
                else tMax = Mathf.Min(tMax, t);
                return true;
            }

            var ok = Clip(d.x, rect.xMin - p.x) && Clip(-d.x, p.x - rect.xMax)
                  && Clip(d.y, rect.yMin - p.y) && Clip(-d.y, p.y - rect.yMax);
            if (!ok || tMax - tMin <= 1e-6f)
            {
                a = b = p;
                return false;
            }
            a = p + d * tMin;
            b = p + d * tMax;
            return true;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var len2 = ab.sqrMagnitude;
            if (len2 < 1e-12f)
                return Vector2.Distance(p, a);
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
