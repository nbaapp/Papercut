using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The geometry of a single crease, in sheet-local space (origin at the sheet centre, +y north, +x east):
    /// the two legal crease families, the flat-sheet depth limit, and the drag-to-depth rule. What a crease does
    /// to the sheet as it lies now is <see cref="SheetLayers"/>. Pure and Unity-object-free so it can be unit-tested.
    /// </summary>
    /// <remarks>
    /// Every legal crease is at 0°, 90° or ±45°, so the mirror image of an axis-aligned rect is an axis-aligned rect.
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
        /// The largest depth whose landed Flap stays on the flat sheet (Aaron, 2026-08-26: folds may not extend
        /// past the edge). Edge fold: half the extent, so the Flap's edge lands on the far edge at most. Corner
        /// fold: the sheet height, so the reflected corner stays inside (the sheet is wider than it is tall).
        /// The live limit, which depends on where the sheet lies once folded, is <see cref="SheetLayers.MaxDepth"/>;
        /// flat, the two agree.
        /// </summary>
        public static float MaxDepth(FoldAnchor anchor)
        {
            if (anchor.IsCorner())
                return Mathf.Min(SheetGeometry.Width, SheetGeometry.Height);
            var outward = anchor.EdgeDirection().ToVector();
            return (outward.x != 0f ? SheetGeometry.Width : SheetGeometry.Height) * 0.5f;
        }

        /// <summary>
        /// Depth such that the grabbed fold line sits under <paramref name="local"/>. The grabbed line is
        /// <paramref name="grabDepth"/> in from the anchoring edge (0 for the sheet's own edge or corner; an
        /// existing fold's depth when its crease is grabbed to fold it further); it reflects across the new
        /// crease at depth d to 2d − g in, so d = (c + g)/2 for the cursor c in. For a corner the same holds with
        /// the mean of the two inward distances. Never negative; the upper limit is the caller's
        /// (<see cref="SheetLayers.MaxDepth"/>).
        /// </summary>
        public static float DepthForDragPoint(FoldAnchor anchor, Vector2 local, float grabDepth = 0f)
        {
            var half = SheetGeometry.HalfSize;
            float depth;
            if (anchor.IsEdge())
            {
                var outward = anchor.EdgeDirection().ToVector();
                var halfExtent = Mathf.Abs(Vector2.Dot(half, outward));
                var inward = halfExtent - Vector2.Dot(local, outward);
                depth = (inward + grabDepth) * 0.5f;
            }
            else
            {
                var signs = anchor.CornerSigns();
                var u = half.x - signs.x * local.x;
                var v = half.y - signs.y * local.y;
                depth = (u + v) * 0.5f + grabDepth * 0.5f;
            }
            return Mathf.Max(0f, depth);
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
