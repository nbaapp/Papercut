using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The sheet as a stack of layers: what a sequence of folds has done to it. Each layer is a convex piece of
    /// the flat sheet (in Front-space, the texture space) with the rigid transform that puts it where it now
    /// lies and which face is up. Pure and immutable; every fold produces a new stack via <see cref="Apply"/>.
    /// The one place fold-on-fold physics lives (Bible §4: multiple folds and stacking, behind
    /// <see cref="SheetFolds"/>'s toggles).
    /// </summary>
    /// <remarks>
    /// A fold lifts <em>everything</em> on the Flap side of its crease — however many layers deep — and lays it
    /// over across the crease on top of what is there, so the lifted pile lands in reverse order. Lifted pieces
    /// must land on the sheet rect (no overhang — Aaron, 2026-08-26); the largest depth that satisfies that
    /// depends on where the sheet lies now, see <see cref="MaxDepth"/>. Sheet-local space throughout; a
    /// "desk" polygon is a piece where it lies now, an "original" polygon the same piece on the flat sheet.
    /// </remarks>
    public sealed class SheetLayers
    {
        public readonly struct Layer
        {
            /// <summary>The piece on the flat sheet, Front-space.</summary>
            public ConvexPolygon Original { get; }

            /// <summary>Front-space → where the piece lies now (sheet-local).</summary>
            public Isometry2D ToDesk { get; }

            /// <summary>True if the Front face of this piece faces up.</summary>
            public bool FrontUp { get; }

            /// <summary>Index of the last fold that moved this piece; −1 for pieces of the Base that never moved.</summary>
            public int MovedBy { get; }

            /// <summary>Where the piece lies now.</summary>
            public ConvexPolygon Desk { get; }

            public Layer(ConvexPolygon original, Isometry2D toDesk, bool frontUp, int movedBy)
            {
                Original = original;
                ToDesk = toDesk;
                FrontUp = frontUp;
                MovedBy = movedBy;
                Desk = original.Transform(toDesk);
            }

            public bool IsBase => MovedBy < 0;
        }

        /// <summary>Tolerance for "lands inside the sheet", in sheet units.</summary>
        const float ContainmentTolerance = 1e-4f;

        /// <summary>Segments shorter than this are not crease or Seam lines.</summary>
        const float MinSegmentLength = 1e-4f;

        readonly List<Layer> layers;
        readonly List<ConvexPolygon> footprint;

        /// <summary>The unfolded sheet: one Front-up layer.</summary>
        public static readonly SheetLayers Flat = new(new List<Layer>
        {
            new(ConvexPolygon.FromRect(FoldGeometry.Sheet), Isometry2D.Identity, frontUp: true, movedBy: -1),
        });

        SheetLayers(List<Layer> layers)
        {
            this.layers = layers;
            footprint = new List<ConvexPolygon>(layers.Count);
            foreach (var layer in layers)
                footprint.Add(layer.Desk);
        }

        /// <summary>Bottom to top.</summary>
        public IReadOnlyList<Layer> Layers => layers;

        /// <summary>Every layer where it lies: the sheet's footprint on the Desk, as convex pieces (they may overlap).</summary>
        public IReadOnlyList<ConvexPolygon> Footprint => footprint;

        public bool AnyBackUp
        {
            get
            {
                foreach (var layer in layers)
                    if (!layer.FrontUp) return true;
                return false;
            }
        }

        /// <summary>
        /// The stack after <paramref name="fold"/>. <paramref name="effect"/> describes what it did; its
        /// <see cref="FoldEffect.Outcome"/> says whether the fold is possible — the returned stack is still
        /// the geometric result either way, so callers refuse before using it.
        /// </summary>
        public SheetLayers Apply(Fold fold, int foldIndex, out FoldEffect effect)
        {
            var crease = FoldGeometry.CreaseOf(fold);
            var mirror = Isometry2D.Reflection(crease);
            var stays = new List<Layer>(layers.Count + 2);
            var lifted = new List<Layer>();
            var liftedDesk = new List<ConvexPolygon>();
            var creaseSegments = new List<(Vector2, Vector2)>();
            var marks = new List<CreaseMark>();

            foreach (var layer in layers)
            {
                var stay = layer.Desk.ClipToHalfPlane(crease.Point, crease.FlapNormal, keepPositive: false);
                var lift = layer.Desk.ClipToHalfPlane(crease.Point, crease.FlapNormal, keepPositive: true);
                var inverse = layer.ToDesk.Inverse;
                if (!stay.IsEmpty)
                    stays.Add(new Layer(stay.Transform(inverse), layer.ToDesk, layer.FrontUp, layer.MovedBy));
                if (!lift.IsEmpty)
                {
                    liftedDesk.Add(lift);
                    lifted.Add(new Layer(lift.Transform(inverse), layer.ToDesk.Then(mirror), !layer.FrontUp, foldIndex));
                }
                if (!stay.IsEmpty && !lift.IsEmpty && layer.Desk.TryClipLine(crease.Point, crease.Direction, out var a, out var b)
                    && Vector2.Distance(a, b) > MinSegmentLength)
                {
                    creaseSegments.Add((a, b));
                    AddMarks(marks, layer, inverse, a, b, crease.FlapNormal);
                }
            }

            lifted.Reverse(); // the top of the lifted pile lands at the bottom
            var next = new List<Layer>(stays.Count + lifted.Count);
            next.AddRange(stays);
            next.AddRange(lifted);

            var landed = new List<ConvexPolygon>(lifted.Count);
            foreach (var layer in lifted)
                landed.Add(layer.Desk);

            var outcome = FoldOutcome.None;
            if (lifted.Count == 0)
                outcome = FoldOutcome.NothingToFold;
            else
            {
                foreach (var piece in landed)
                {
                    if (!piece.ContainedIn(FoldGeometry.Sheet, ContainmentTolerance))
                    {
                        outcome = FoldOutcome.Overhangs;
                        break;
                    }
                }
            }

            effect = new FoldEffect(fold, outcome, liftedDesk, landed, creaseSegments, SeamOf(landed, crease), marks);
            return new SheetLayers(next);
        }

        /// <summary>
        /// The largest depth at which every piece <paramref name="anchor"/>'s fold would lift lands on the sheet
        /// rect, given where the sheet lies now. The minimum over every layer vertex of a per-vertex bound: a
        /// vertex at coordinate s along an edge anchor's outward normal (half-extent h) lifts once d &gt; h − s and
        /// lands at 2(h − d) − s, inside iff d ≤ (3h − s)/2; a vertex at inward corner coordinates (u, v) lands
        /// at (X − v, X − u) with X = hx + hy − d, inside iff d ≤ min(2hx + hy − v, hx + 2hy − u). Each bound is
        /// at least the depth at which the vertex lifts, so the valid depths are exactly [0, min bound]; the
        /// pieces are convex and a crossing vertex on the crease reflects to itself, so vertices suffice.
        /// Flat, this equals <see cref="FoldGeometry.MaxDepth"/>.
        /// </summary>
        public float MaxDepth(FoldAnchor anchor)
        {
            var half = SheetGeometry.HalfSize;
            var best = float.PositiveInfinity;
            if (anchor.IsEdge())
            {
                var outward = anchor.EdgeDirection().ToVector();
                var h = Mathf.Abs(Vector2.Dot(half, outward));
                foreach (var layer in layers)
                    foreach (var p in layer.Desk.Vertices)
                        best = Mathf.Min(best, (3f * h - Vector2.Dot(p, outward)) * 0.5f);
            }
            else
            {
                var signs = anchor.CornerSigns();
                foreach (var layer in layers)
                    foreach (var p in layer.Desk.Vertices)
                    {
                        var u = signs.x * p.x;
                        var v = signs.y * p.y;
                        best = Mathf.Min(best, Mathf.Min(2f * half.x + half.y - v, half.x + 2f * half.y - u));
                    }
            }
            return Mathf.Max(0f, best);
        }

        /// <summary>
        /// What is left of a face-content footprint: the parts of it that lie on a top-visible layer showing
        /// that face, each placed where that layer lies now (sheet-local). <paramref name="faceFootprint"/> is
        /// in the face's authored space (Front-space or Back-space).
        /// </summary>
        public CoverageResult Coverage(Rect faceFootprint, SheetFace face)
            => Coverage(FaceFootprint.FromRect(faceFootprint), face);

        /// <summary>
        /// <see cref="Coverage(Rect, SheetFace)"/> for a footprint of several convex pieces (a polygon region):
        /// each piece is clipped on its own and the parts are pooled. Whole means every piece is present in one
        /// part on the Base, unmoved.
        /// </summary>
        public CoverageResult Coverage(FaceFootprint footprint, SheetFace face)
        {
            if (footprint.IsEmpty)
                return CoverageResult.None(FoldCoverage.Uncovered);

            var wantFrontUp = face == SheetFace.Front;
            var parts = new List<ConvexPolygon>();
            var everyPieceWholeOnBase = true;
            foreach (var authored in footprint.Pieces)
            {
                var query = face == SheetFace.Front ? authored : SheetGeometry.BackToFront(authored);
                var partsBefore = parts.Count;
                var fromBase = false;
                for (int i = 0; i < layers.Count; i++)
                {
                    var layer = layers[i];
                    if (layer.FrontUp != wantFrontUp)
                        continue;
                    var piece = query.Intersect(layer.Original);
                    if (piece.IsEmpty)
                        continue;
                    var pieces = new List<ConvexPolygon> { piece.Transform(layer.ToDesk) };
                    for (int j = i + 1; j < layers.Count && pieces.Count > 0; j++)
                    {
                        var above = layers[j].Desk;
                        var remaining = new List<ConvexPolygon>();
                        foreach (var p in pieces)
                            remaining.AddRange(p.Subtract(above));
                        pieces = remaining;
                    }
                    if (pieces.Count == 0)
                        continue;
                    fromBase = parts.Count == partsBefore && pieces.Count == 1 && layer.IsBase;
                    parts.AddRange(pieces);
                }
                if (parts.Count != partsBefore + 1 || !fromBase)
                    everyPieceWholeOnBase = false;
            }

            if (parts.Count == 0)
                return CoverageResult.None(face == SheetFace.Front ? FoldCoverage.Covered : FoldCoverage.Uncovered);

            var total = 0f;
            foreach (var p in parts)
                total += p.Area;
            var whole = total >= footprint.Area - ContainmentTolerance;
            if (whole && everyPieceWholeOnBase)
                return CoverageResult.Whole(FoldCoverage.Uncovered, footprint);
            var coverage = whole ? (face == SheetFace.Front ? FoldCoverage.Uncovered : FoldCoverage.Covered) : FoldCoverage.Partial;
            return CoverageResult.Clipped(parts, coverage);
        }

        /// <summary>
        /// What is left of content that sits <em>above</em> the sheet (a universal region, Aaron 2026-09-28): the
        /// parts of a sheet-space footprint that lie on the sheet's footprint - the union of every layer where it
        /// lies - as disjoint pieces (each layer's part minus the layers above it). Above content never moves, so
        /// it is Whole exactly when the surviving area is the footprint's, whatever lies under it; None (Covered)
        /// when nothing of it is over the sheet; Partial otherwise. Flat, this trims a footprint hanging past the
        /// sheet's edge - the same rule as when folded.
        /// </summary>
        public CoverageResult CoverageAbove(FaceFootprint footprint)
        {
            if (footprint.IsEmpty)
                return CoverageResult.None(FoldCoverage.Uncovered);

            var parts = new List<ConvexPolygon>();
            foreach (var authored in footprint.Pieces)
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    var piece = authored.Intersect(layers[i].Desk);
                    if (piece.IsEmpty)
                        continue;
                    var pieces = new List<ConvexPolygon> { piece };
                    for (int j = i + 1; j < layers.Count && pieces.Count > 0; j++)
                    {
                        var above = layers[j].Desk;
                        var remaining = new List<ConvexPolygon>();
                        foreach (var p in pieces)
                            remaining.AddRange(p.Subtract(above));
                        pieces = remaining;
                    }
                    parts.AddRange(pieces);
                }
            }

            if (parts.Count == 0)
                return CoverageResult.None(FoldCoverage.Covered);
            var total = 0f;
            foreach (var p in parts)
                total += p.Area;
            return total >= footprint.Area - ContainmentTolerance
                ? CoverageResult.Whole(FoldCoverage.Uncovered, footprint)
                : CoverageResult.Clipped(parts);
        }

        /// <summary>
        /// Crease marks for a layer the crease cuts, one per face, each in that face's authored space: the side that
        /// lifted (the Back mark is the Front-space mark through <see cref="SheetGeometry.BackToFront"/>), and which
        /// face was inside the fold — the one that was up.
        /// </summary>
        static void AddMarks(List<CreaseMark> marks, in Layer layer, Isometry2D inverse, Vector2 a, Vector2 b, Vector2 flapNormal)
        {
            var frontA = inverse.Apply(a);
            var frontB = inverse.Apply(b);
            var towardLift = inverse.ApplyVector(flapNormal);
            marks.Add(new CreaseMark(SheetFace.Front, frontA, frontB, towardLift, layer.FrontUp));
            marks.Add(new CreaseMark(SheetFace.Back, SheetGeometry.BackToFront(frontA), SheetGeometry.BackToFront(frontB), SheetGeometry.BackToFront(towardLift), !layer.FrontUp));
        }

        static bool OnSheetBorder(Vector2 a, Vector2 b)
        {
            var half = SheetGeometry.HalfSize;
            return (Mathf.Abs(Mathf.Abs(a.x) - half.x) < MinSegmentLength && Mathf.Abs(Mathf.Abs(b.x) - half.x) < MinSegmentLength && Mathf.Sign(a.x) == Mathf.Sign(b.x))
                || (Mathf.Abs(Mathf.Abs(a.y) - half.y) < MinSegmentLength && Mathf.Abs(Mathf.Abs(b.y) - half.y) < MinSegmentLength && Mathf.Sign(a.y) == Mathf.Sign(b.y));
        }

        /// <summary>
        /// The Seam: every edge of every landed piece that is not on the crease line or the sheet's border, minus the landed pieces
        /// above it (the pile lands reversed, so lower pieces' edges are hidden by upper ones).
        /// </summary>
        static List<(Vector2 a, Vector2 b)> SeamOf(List<ConvexPolygon> landed, Crease crease)
        {
            var seam = new List<(Vector2, Vector2)>();
            for (int i = 0; i < landed.Count; i++)
            {
                var piece = landed[i];
                for (int e = 0; e < piece.Count; e++)
                {
                    var a = piece.Vertices[e];
                    var b = piece.Vertices[(e + 1) % piece.Count];
                    if (Vector2.Distance(a, b) <= MinSegmentLength)
                        continue;
                    if (Mathf.Abs(crease.SignedDistance(a)) < MinSegmentLength && Mathf.Abs(crease.SignedDistance(b)) < MinSegmentLength)
                        continue;
                    if (OnSheetBorder(a, b))
                        continue; // The landed edge lies along the sheet's own edge: it meets nothing there.
                    var pieces = new List<(Vector2 a, Vector2 b)> { (a, b) };
                    for (int j = i + 1; j < landed.Count && pieces.Count > 0; j++)
                    {
                        var remaining = new List<(Vector2 a, Vector2 b)>();
                        foreach (var (p, q) in pieces)
                            remaining.AddRange(ConvexPolygon.SubtractFromSegment(p, q, landed[j], closed: true)); // an edge shared with an upper piece is that piece's Seam
                        pieces = remaining;
                    }
                    seam.AddRange(pieces);
                }
            }
            return seam;
        }
    }
}
