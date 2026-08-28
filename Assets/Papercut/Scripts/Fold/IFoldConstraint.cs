namespace Papercut
{
    /// <summary>
    /// Something under a Sheet that may refuse an unfold: <see cref="SheetFolds"/> asks every active constraint
    /// before undoing a fold. Keeps element rules (e.g. a block hanging past its Flap's edge) out of the fold
    /// system: deleting the element deletes the rule.
    /// </summary>
    public interface IFoldConstraint
    {
        /// <summary>
        /// Why committed fold <paramref name="foldIndex"/> may not be undone now, or <see cref="FoldRejection.None"/>.
        /// Called while <see cref="SheetFolds.Layers"/> and <see cref="SheetFolds.Effects"/> still hold the fold.
        /// </summary>
        FoldRejection RefuseUnfold(int foldIndex);
    }
}
