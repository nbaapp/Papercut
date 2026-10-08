using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The rule about where a fold may carry movable content relative to the walls above the sheet (Aaron,
    /// 2026-09-28: <em>"Deny the fold if a block falls under a universal wall, like with folding over the
    /// player"</em>): a Flap may not carry a block to a place under a universal wall that stops blocks. Sheet-local;
    /// pure and Unity-object-free so it can be unit-tested, like <see cref="FoldValidity"/>.
    /// </summary>
    public static class FoldLandingRules
    {
        /// <summary>
        /// True if the fold that became fold <paramref name="foldIndex"/> of <paramref name="after"/> carries any
        /// face-up piece of the content with this flat rect and side onto a wall piece. Carried pieces are the
        /// content's visible pieces on the stack after the fold (<see cref="SheetPlacement.VisiblePieces"/>) whose
        /// layer that fold moved; pieces the fold merely covers, or that ride underneath face-down, are not carried
        /// and do not count - a later fold that turns them face-up is judged then. Pre-fold visibility is irrelevant:
        /// a Back-side block a fold exposes under a wall is exactly what this refuses.
        /// </summary>
        public static bool CarriesUnderWall(Rect flatRect, SheetFace side, SheetLayers after, int foldIndex, IReadOnlyList<ConvexPolygon> walls)
        {
            if (walls.Count == 0 || flatRect.width <= 0f || flatRect.height <= 0f)
                return false;
            var stack = after.Layers;
            foreach (var piece in SheetPlacement.VisiblePieces(flatRect, side, after))
            {
                if (piece.LayerIndex < 0 || piece.LayerIndex >= stack.Count || stack[piece.LayerIndex].MovedBy != foldIndex)
                    continue;
                if (piece.Desk.IsEmpty)
                    continue;
                foreach (var wall in walls)
                {
                    if (!wall.IsEmpty && piece.Desk.Overlaps(wall))
                        return true;
                }
            }
            return false;
        }
    }
}
