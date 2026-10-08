using System.Collections.Generic;

namespace Papercut
{
    /// <summary>
    /// The unfold rule of a <see cref="Paperweight"/> (Aaron, 2026-09-24): a paperweight is always on top of
    /// the sheet, so a fold whose Flap any part of it rests on cannot be undone until it is pushed off again -
    /// otherwise it would ride the Flap around to the Back like a block does, and a puzzle built around holding
    /// a fold short is trivially undone by pushing the weight onto the Flap. Any part counts, not just the
    /// centre (Aaron: a weight straddling the crease must be pushed fully clear). Pure and Unity-object-free
    /// so it can be unit-tested, like <see cref="PressureRules"/>.
    /// </summary>
    public static class PaperweightRules
    {
        /// <summary>
        /// True if any of <paramref name="pieces"/> (the weight's visible pieces on <paramref name="layers"/>)
        /// lies on a layer that committed fold <paramref name="foldIndex"/> moved - so undoing that fold would
        /// carry the piece with it. A piece climbing onto a Flap counts as on it.
        /// </summary>
        public static bool RidesFold(IReadOnlyList<SheetPlacement.Piece> pieces, SheetLayers layers, int foldIndex)
        {
            var stack = layers.Layers;
            foreach (var piece in pieces)
            {
                if (piece.SurfaceLayer >= 0 && piece.SurfaceLayer < stack.Count && stack[piece.SurfaceLayer].MovedBy == foldIndex)
                    return true;
            }
            return false;
        }
    }
}
