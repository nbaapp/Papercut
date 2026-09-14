using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// What a piece of face content occupies, in its face's authored space: disjoint convex pieces. A box is one
    /// piece; a polygon region is its <see cref="PolygonDecomposition"/>. This is the seam between <em>what an
    /// occludee occupies</em> and <em>what its collider is</em>: folding and the arrival room test know only
    /// pieces.
    /// </summary>
    /// <remarks>
    /// <c>default(FaceFootprint)</c> is <see cref="Empty"/>: every accessor treats a null backing list as no
    /// pieces, so a footprint handed out by a failed <c>out</c> path is safe to use.
    /// </remarks>
    public readonly struct FaceFootprint
    {
        static readonly ConvexPolygon[] NoPieces = System.Array.Empty<ConvexPolygon>();

        readonly IReadOnlyList<ConvexPolygon> pieces;

        FaceFootprint(IReadOnlyList<ConvexPolygon> pieces)
        {
            this.pieces = pieces;
        }

        public static readonly FaceFootprint Empty = default;

        /// <summary>Disjoint convex pieces in the face's authored space. Empty when there is nothing.</summary>
        public IReadOnlyList<ConvexPolygon> Pieces => pieces ?? NoPieces;

        public bool IsEmpty => Pieces.Count == 0;

        /// <summary>Sum of the pieces' areas.</summary>
        public float Area
        {
            get
            {
                var total = 0f;
                foreach (var piece in Pieces)
                    total += piece.Area;
                return total;
            }
        }

        /// <summary>Axis-aligned bounds of every piece; a zero rect when empty.</summary>
        public Rect Bounds
        {
            get
            {
                if (IsEmpty)
                    return Rect.zero;
                var bounds = Pieces[0].Bounds;
                for (int i = 1; i < Pieces.Count; i++)
                {
                    var b = Pieces[i].Bounds;
                    bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, b.xMin), Mathf.Min(bounds.yMin, b.yMin),
                        Mathf.Max(bounds.xMax, b.xMax), Mathf.Max(bounds.yMax, b.yMax));
                }
                return bounds;
            }
        }

        /// <summary>One rect piece; <see cref="Empty"/> for a rect without area.</summary>
        public static FaceFootprint FromRect(Rect rect)
            => rect.width <= 0f || rect.height <= 0f ? Empty : new FaceFootprint(new[] { ConvexPolygon.FromRect(rect) });

        /// <summary>The given pieces, minus any that are empty.</summary>
        public static FaceFootprint FromPieces(IReadOnlyList<ConvexPolygon> pieces)
        {
            var kept = new List<ConvexPolygon>(pieces.Count);
            foreach (var piece in pieces)
            {
                if (!piece.IsEmpty)
                    kept.Add(piece);
            }
            return kept.Count == 0 ? Empty : new FaceFootprint(kept);
        }

        /// <summary>
        /// The footprint of a simple polygon outline (any winding, concave allowed). False, with a reason and
        /// <see cref="Empty"/>, if the outline is not a simple polygon.
        /// </summary>
        public static bool TryFromOutline(IReadOnlyList<Vector2> outline, out FaceFootprint footprint, out string reason)
        {
            if (!PolygonDecomposition.Validate(outline, out reason))
            {
                footprint = Empty;
                return false;
            }
            footprint = FromPieces(PolygonDecomposition.Decompose(outline));
            return true;
        }
    }
}
