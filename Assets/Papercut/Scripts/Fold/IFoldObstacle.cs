using System.Collections.Generic;

namespace Papercut
{
    /// <summary>
    /// Something under a Sheet that a Flap may neither lift nor land on (an element such as a paperweight): <see cref="SheetFolds"/>
    /// asks every active obstacle where it is before judging a fold, holds the drag short of it
    /// (<see cref="FoldObstacles.MaxDepth"/>) and refuses a fold that would reach it (<see cref="FoldRejection.CoversObstacle"/>).
    /// Keeps element rules out of the fold system, like <see cref="IFoldConstraint"/>: deleting the element deletes the rule.
    /// </summary>
    public interface IFoldObstacle
    {
        /// <summary>
        /// Appends the sheet-local convex pieces of this object that a Flap may neither lift nor land on: the parts of
        /// it that are face-up on the sheet as it lies now (<see cref="SheetFolds.Layers"/>). Adds nothing while none
        /// of it is up (covered, or on the face that is down).
        /// </summary>
        void AddObstaclePieces(List<ConvexPolygon> sheetLocal);
    }
}
