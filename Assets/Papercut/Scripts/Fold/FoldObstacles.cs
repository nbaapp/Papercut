using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The rules about where a fold may be relative to fold obstacles (<see cref="IFoldObstacle"/>; Aaron,
    /// 2026-09-14): a Flap may neither lift nor land on one, and a drag holds at the depth where it would first
    /// touch one rather than turning red. The obstacles are any sheet-local pieces: paperweights, and - with
    /// stacking off - the other folds' lifted and landed pieces, so that a Flap stops against another Flap the
    /// same way (Aaron, 2026-09-16); against those pieces <see cref="Clear"/> is exactly
    /// <see cref="FoldValidity.Independent"/>. Sheet-local; pure and Unity-object-free so it can be unit-tested,
    /// like <see cref="FoldValidity"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="MaxDepth"/> is the first-contact depth, found analytically rather than by searching
    /// <see cref="Clear"/>: a search assumes the set of clear depths is one interval from zero, which holds only
    /// when the sheet's layers leave no gap along the fold's direction, and that is not proven for every stack of
    /// edge and corner folds. The first contact is exact whatever the stack. Work is done in the anchor's frame:
    /// <c>u</c> is the distance inward from the anchoring edge (or corner) along the fold's normal, <c>t</c> runs
    /// along the crease. A crease at <c>u = c</c> lifts every layer point with <c>u ≤ c</c> and lands it at
    /// <c>2c − u</c>; edge folds have depth <c>d = c</c>, corner folds <c>d = c·√2</c> (see <see cref="FoldGeometry.CreaseOf"/>).
    /// </remarks>
    public static class FoldObstacles
    {
        /// <summary>
        /// How far short of contact the clamp stops, in sheet units: at exact contact the landed edge lies along the
        /// obstacle's edge and float error in the reflection could leave a sliver with more area than
        /// <see cref="ConvexPolygon.AreaEpsilon"/>, making the clamped fold refuse. Invisible (a tenth of a mil).
        /// </summary>
        public const float ContactTolerance = 1e-4f;

        /// <summary>Tolerance along the crease for "this edge spans t".</summary>
        const float SpanEpsilon = 1e-6f;

        // Scratch for MaxDepth, which runs every drag frame: pure, single-threaded code, so shared buffers are safe.
        static readonly List<Vector2> layerPoints = new();
        static readonly List<Vector2> obstaclePoints = new();
        static readonly List<float> candidates = new();

        /// <summary>True if no lifted or landed piece of the fold shares area with any obstacle piece.</summary>
        public static bool Clear(in FoldEffect effect, IReadOnlyList<ConvexPolygon> obstacles)
        {
            foreach (var obstacle in obstacles)
            {
                if (!obstacle.IsEmpty && effect.Overlaps(obstacle))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The largest depth a fold from <paramref name="anchor"/> can have before its Flap touches an obstacle,
        /// given where the sheet lies now — less <see cref="ContactTolerance"/>, so the returned depth is always
        /// <see cref="Clear"/>. Positive infinity with no obstacles in reach; never negative. The overhang bound
        /// (<see cref="SheetLayers.MaxDepth"/>) is the caller's to combine.
        /// </summary>
        public static float MaxDepth(FoldAnchor anchor, SheetLayers layers, IReadOnlyList<ConvexPolygon> obstacles)
        {
            if (obstacles.Count == 0)
                return float.PositiveInfinity;

            var frame = new AnchorFrame(anchor);
            var best = float.PositiveInfinity;
            foreach (var layer in layers.Layers)
            {
                if (layer.Desk.IsEmpty)
                    continue;
                frame.ToFrame(layer.Desk, layerPoints);
                foreach (var obstacle in obstacles)
                {
                    if (obstacle.IsEmpty)
                        continue;
                    frame.ToFrame(obstacle, obstaclePoints);
                    best = Mathf.Min(best, FirstContact(layerPoints, obstaclePoints));
                }
            }
            if (float.IsPositiveInfinity(best))
                return best;
            return Mathf.Max(0f, frame.DepthOf(best) - ContactTolerance);
        }

        /// <summary>
        /// The least crease distance <c>c</c> at which a fold of layer piece <paramref name="layer"/> touches
        /// obstacle piece <paramref name="obstacle"/> (both in frame coordinates, x = t, y = u). Contact needs a
        /// layer point p and an obstacle point b on the same t with u_p ≤ u_b landing on it: c = (u_p + u_b)/2;
        /// p = b covers lifting the sheet under the obstacle. Per t the least such c is
        /// (pLo + max(bLo, pLo))/2 where pLo ≤ bHi (pLo/bLo/bHi: the pieces' extents at that t), a piecewise-linear
        /// function whose breakpoints are the pieces' vertex t's and their edge crossings — so the minimum is at
        /// one of those, and they are all that is evaluated. Positive infinity if the pair can never touch.
        /// </summary>
        static float FirstContact(List<Vector2> layer, List<Vector2> obstacle)
        {
            var best = float.PositiveInfinity;
            CandidateTs(layer, obstacle, candidates);
            foreach (var t in candidates)
            {
                if (!Span(layer, t, out var pLo, out _) || !Span(obstacle, t, out var bLo, out var bHi))
                    continue;
                if (pLo > bHi + SpanEpsilon)
                    continue; // The obstacle lies wholly nearer the anchor than this layer here: lifting the layer lands it further out, never on it.
                best = Mathf.Min(best, (pLo + Mathf.Max(bLo, pLo)) * 0.5f);
            }
            return best;
        }

        static void CandidateTs(List<Vector2> a, List<Vector2> b, List<float> ts)
        {
            ts.Clear();
            foreach (var p in a)
                ts.Add(p.x);
            foreach (var p in b)
                ts.Add(p.x);
            for (int i = 0; i < a.Count; i++)
            {
                var a0 = a[i];
                var a1 = a[(i + 1) % a.Count];
                for (int j = 0; j < b.Count; j++)
                {
                    if (TryIntersect(a0, a1, b[j], b[(j + 1) % b.Count], out var t))
                        ts.Add(t);
                }
            }
        }

        /// <summary>The t of the crossing of two segments, if they cross (touching counts; parallel and collinear do not — those add no breakpoint).</summary>
        static bool TryIntersect(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, out float t)
        {
            var da = a1 - a0;
            var db = b1 - b0;
            var denom = da.x * db.y - da.y * db.x;
            t = 0f;
            if (Mathf.Abs(denom) < 1e-12f)
                return false;
            var ab = b0 - a0;
            var s = (ab.x * db.y - ab.y * db.x) / denom;
            var r = (ab.x * da.y - ab.y * da.x) / denom;
            if (s < -SpanEpsilon || s > 1f + SpanEpsilon || r < -SpanEpsilon || r > 1f + SpanEpsilon)
                return false;
            t = a0.x + da.x * s;
            return true;
        }

        /// <summary>The u-extent of a convex piece at <paramref name="t"/>: the least and greatest u of its edges there. False if no edge spans t.</summary>
        static bool Span(List<Vector2> polygon, float t, out float lo, out float hi)
        {
            lo = float.PositiveInfinity;
            hi = float.NegativeInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                var tMin = Mathf.Min(a.x, b.x);
                var tMax = Mathf.Max(a.x, b.x);
                if (t < tMin - SpanEpsilon || t > tMax + SpanEpsilon)
                    continue;
                if (tMax - tMin <= SpanEpsilon)
                {
                    Include(a.y, ref lo, ref hi); // An edge along u: both ends count.
                    Include(b.y, ref lo, ref hi);
                    continue;
                }
                var f = Mathf.Clamp01((t - a.x) / (b.x - a.x));
                Include(a.y + (b.y - a.y) * f, ref lo, ref hi);
            }
            return lo <= hi;
        }

        static void Include(float u, ref float lo, ref float hi)
        {
            lo = Mathf.Min(lo, u);
            hi = Mathf.Max(hi, u);
        }

        /// <summary>The anchor's frame: sheet-local → (t along the crease, u inward from the anchor), and depth from crease distance.</summary>
        readonly struct AnchorFrame
        {
            readonly Vector2 tAxis;
            readonly Vector2 uAxis;
            readonly float uOffset;
            readonly float depthPerU;

            public AnchorFrame(FoldAnchor anchor)
            {
                var half = SheetGeometry.HalfSize;
                if (anchor.IsEdge())
                {
                    var outward = anchor.EdgeDirection().ToVector();
                    uAxis = -outward;
                    uOffset = Mathf.Abs(Vector2.Dot(half, outward));
                    tAxis = new Vector2(-outward.y, outward.x);
                    depthPerU = 1f;
                }
                else
                {
                    var signs = anchor.CornerSigns();
                    var root2 = Mathf.Sqrt(2f);
                    uAxis = -signs / root2;
                    uOffset = (half.x + half.y) / root2;
                    tAxis = new Vector2(-signs.y, signs.x) / root2;
                    depthPerU = root2;
                }
            }

            public void ToFrame(ConvexPolygon polygon, List<Vector2> points)
            {
                points.Clear();
                foreach (var v in polygon.Vertices)
                    points.Add(new Vector2(Vector2.Dot(v, tAxis), Vector2.Dot(v, uAxis) + uOffset));
            }

            public float DepthOf(float creaseDistance) => creaseDistance * depthPerU;
        }
    }
}
