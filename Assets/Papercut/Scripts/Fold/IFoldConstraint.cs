namespace Papercut
{
    /// <summary>
    /// Something under a Sheet that may refuse a fold or an unfold: <see cref="SheetFolds"/> asks every active
    /// constraint before making a fold and before undoing one. Keeps element rules (a block hanging past its
    /// Flap's edge; a block a Flap would carry under a universal wall) out of the fold system: deleting the
    /// element deletes the rule.
    /// </summary>
    public interface IFoldConstraint
    {
        /// <summary>
        /// Why <paramref name="effect"/> - the fold that would become committed fold <paramref name="foldIndex"/>,
        /// producing the stack <paramref name="after"/> - may not be made, or <see cref="FoldRejection.None"/>.
        /// Called while <see cref="SheetFolds.Layers"/> still holds the sheet as it lies before the fold.
        /// </summary>
        FoldRejection RefuseFold(in FoldEffect effect, SheetLayers after, int foldIndex);

        /// <summary>
        /// Why committed fold <paramref name="foldIndex"/> may not be undone now, or <see cref="FoldRejection.None"/>.
        /// Called while <see cref="SheetFolds.Layers"/> and <see cref="SheetFolds.Effects"/> still hold the fold.
        /// </summary>
        FoldRejection RefuseUnfold(int foldIndex);
    }
}
