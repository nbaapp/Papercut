using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// A paperweight: sheet content a Flap may neither lift nor land on, so a fold dragged toward it stops where
    /// the Flap would touch it (Aaron, 2026-09-14: <em>"an object ... that blocks a fold from folding past that
    /// point ... it just can't stretch beyond that point"</em>). It is a <see cref="PushableBlock"/> - so it is
    /// solid, folds, covers, exposes, presses plates and resets like one - and the two prefabs differ only in
    /// data: <c>Paperweight</c> has pushing off (fixed where it is authored), <c>Pushable Paperweight</c> pushes
    /// like a block so puzzles can be built around moving it.
    /// </summary>
    /// <remarks>
    /// Rules from Aaron (2026-09-14): a Flap may neither lift nor land on it - the player rule's shape - but the
    /// drag holds at the contact depth instead of turning red; it blocks only through the parts of it that are
    /// face-up (<em>"If its not on the same side, it doesn't block it"</em>; a covered part is inert and rides the
    /// sheet if that region is later lifted); both kinds press plates. The obstacle pieces are the block's
    /// visible pieces on the committed layers, so a weight on a Flap blocks further folds there and a weight
    /// ridden around to the Back blocks nothing until exposed again. Since 2026-09-24 a weight is always on top
    /// of the sheet: a fold whose Flap any part of it rests on cannot be unfolded until it is pushed off
    /// (<see cref="PaperweightRules"/>, <see cref="FoldRejection.PaperweightOnFlap"/>), so a pushable weight
    /// never rides a Flap around to the Back.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PushableBlock))]
    public sealed class Paperweight : MonoBehaviour, IFoldObstacle, IFoldConstraint
    {
        PushableBlock block;

        void Awake()
        {
            block = GetComponent<PushableBlock>();
        }

        public void AddObstaclePieces(List<ConvexPolygon> sheetLocal)
        {
            if (!isActiveAndEnabled || block == null || !block.isActiveAndEnabled || !block.IsVisible)
                return;
            foreach (var piece in block.VisiblePieces)
                sheetLocal.Add(piece.Desk);
        }

        /// <summary>A weight is an obstacle the drag clamps short of, never carried; the block half of it answers for carrying.</summary>
        public FoldRejection RefuseFold(in FoldEffect effect, SheetLayers after, int foldIndex) => FoldRejection.None;

        public FoldRejection RefuseUnfold(int foldIndex)
        {
            if (!isActiveAndEnabled || block == null || !block.isActiveAndEnabled || !block.IsVisible)
                return FoldRejection.None;
            return PaperweightRules.RidesFold(block.VisiblePieces, block.Sheet.Folds.Layers, foldIndex)
                ? FoldRejection.PaperweightOnFlap
                : FoldRejection.None;
        }
    }
}
