using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>Whether a fold can be made on the sheet as it is.</summary>
    public enum FoldOutcome
    {
        /// <summary>The fold lifts something and lands on the sheet.</summary>
        None,
        /// <summary>No part of the sheet lies on the Flap side of the crease.</summary>
        NothingToFold,
        /// <summary>Some landed piece would extend past the sheet rect (not allowed; see <see cref="SheetLayers.MaxDepth"/>).</summary>
        Overhangs,
    }

    /// <summary>
    /// One crease line as a mark on one face of the sheet, in that face's authored space (Front-space under
    /// Front, Back-space under Back), so that it moves with the sheet like the rest of the face. The renderer
    /// shades the paper along it through a crease normal map, centred on the line: each piece of the folded sheet
    /// shows its own half.
    /// </summary>
    public readonly struct CreaseMark
    {
        public SheetFace Face { get; }
        public Vector2 A { get; }
        public Vector2 B { get; }

        /// <summary>Unit vector perpendicular to AB, in the mark's face space, toward the side of the crease that lifted.</summary>
        public Vector2 LiftedSide { get; }

        /// <summary>True if this face was the cut layer's up face when the fold was made: the inside of the fold.</summary>
        public bool Inside { get; }

        public CreaseMark(SheetFace face, Vector2 a, Vector2 b, Vector2 liftedSide, bool inside)
        {
            Face = face;
            A = a;
            B = b;
            LiftedSide = liftedSide;
            Inside = inside;
        }
    }

    /// <summary>
    /// What one fold did to the sheet it was applied to, in sheet-local space: the pieces it lifted (where they
    /// were), where they landed, the crease where it cut the sheet, the Seam where the landed edge meets what
    /// it lies on, and the marks it left on both faces. Produced by <see cref="SheetLayers.Apply"/>.
    /// </summary>
    public readonly struct FoldEffect
    {
        static readonly ConvexPolygon[] NoPieces = System.Array.Empty<ConvexPolygon>();
        static readonly (Vector2, Vector2)[] NoSegments = System.Array.Empty<(Vector2, Vector2)>();
        static readonly CreaseMark[] NoMarks = System.Array.Empty<CreaseMark>();

        public Fold Fold { get; }
        public FoldOutcome Outcome { get; }

        /// <summary>The pieces that lift, where they lay before the fold.</summary>
        public IReadOnlyList<ConvexPolygon> Lifted { get; }

        /// <summary>The same pieces where they land, bottom to top.</summary>
        public IReadOnlyList<ConvexPolygon> Landed { get; }

        /// <summary>The crease line where it cuts the sheet.</summary>
        public IReadOnlyList<(Vector2 a, Vector2 b)> CreaseSegments { get; }

        /// <summary>The visible edges of the landed pieces other than the crease: where the tape would go.</summary>
        public IReadOnlyList<(Vector2 a, Vector2 b)> SeamSegments { get; }

        public IReadOnlyList<CreaseMark> CreaseMarks { get; }

        public FoldEffect(Fold fold, FoldOutcome outcome, IReadOnlyList<ConvexPolygon> lifted, IReadOnlyList<ConvexPolygon> landed,
            IReadOnlyList<(Vector2 a, Vector2 b)> creaseSegments, IReadOnlyList<(Vector2 a, Vector2 b)> seamSegments, IReadOnlyList<CreaseMark> creaseMarks)
        {
            Fold = fold;
            Outcome = outcome;
            Lifted = lifted ?? NoPieces;
            Landed = landed ?? NoPieces;
            CreaseSegments = creaseSegments ?? NoSegments;
            SeamSegments = seamSegments ?? NoSegments;
            CreaseMarks = creaseMarks ?? NoMarks;
        }

        public float DistanceToSeam(Vector2 p) => DistanceTo(SeamSegments, p);

        public float DistanceToCrease(Vector2 p) => DistanceTo(CreaseSegments, p);

        /// <summary>True if any lifted or landed piece shares area with <paramref name="rect"/>.</summary>
        public bool OverlapsRect(Rect rect)
        {
            foreach (var piece in Lifted)
                if (piece.Overlaps(rect)) return true;
            foreach (var piece in Landed)
                if (piece.Overlaps(rect)) return true;
            return false;
        }

        /// <summary>True if any lifted or landed piece of either fold shares area with one of the other's.</summary>
        public bool Overlaps(in FoldEffect other)
        {
            foreach (var mine in Lifted)
                if (other.Overlaps(mine)) return true;
            foreach (var mine in Landed)
                if (other.Overlaps(mine)) return true;
            return false;
        }

        /// <summary>True if any lifted or landed piece shares area with <paramref name="polygon"/>.</summary>
        public bool Overlaps(ConvexPolygon polygon)
        {
            foreach (var piece in Lifted)
                if (piece.Overlaps(polygon)) return true;
            foreach (var piece in Landed)
                if (piece.Overlaps(polygon)) return true;
            return false;
        }

        static float DistanceTo(IReadOnlyList<(Vector2 a, Vector2 b)> segments, Vector2 p)
        {
            var best = float.PositiveInfinity;
            foreach (var (a, b) in segments)
                best = Mathf.Min(best, FoldGeometry.DistanceToSegment(p, a, b));
            return best;
        }
    }
}
