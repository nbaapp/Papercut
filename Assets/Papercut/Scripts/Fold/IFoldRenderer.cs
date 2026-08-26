using System.Collections.Generic;

namespace Papercut
{
    /// <summary>How one fold should look right now.</summary>
    public enum FoldVisualState
    {
        /// <summary>A committed fold.</summary>
        Committed,
        /// <summary>A fold being dragged that could be committed.</summary>
        Preview,
        /// <summary>A fold being dragged that would cover the player and will be refused.</summary>
        PreviewInvalid,
        /// <summary>A fold that has just been undone, animating back flat. Visual only; physically it is gone.</summary>
        Retreating,
    }

    public readonly struct FoldVisual
    {
        public Fold Fold { get; }
        public FoldVisualState State { get; }

        public FoldVisual(Fold fold, FoldVisualState state)
        {
            Fold = fold;
            State = state;
        }
    }

    /// <summary>Everything a renderer needs to draw a sheet's folds. Owned by <see cref="SheetFolds"/>.</summary>
    public readonly struct FoldDisplay
    {
        public IReadOnlyList<FoldVisual> Folds { get; }

        /// <summary>Creases left by folds that were undone; drawn faintly until the sheet resets.</summary>
        public IReadOnlyList<Crease> RememberedCreases { get; }

        public FoldDisplay(IReadOnlyList<FoldVisual> folds, IReadOnlyList<Crease> rememberedCreases)
        {
            Folds = folds;
            RememberedCreases = rememberedCreases;
        }
    }

    /// <summary>
    /// The seam between what a fold does and how it is drawn (Bible §4). <see cref="SheetFolds"/> calls
    /// <see cref="Draw"/> whenever anything visual changes; the renderer never reads the model and the model
    /// never knows how it is drawn. Replacing the rendering approach means a new component implementing this.
    /// </summary>
    public interface IFoldRenderer
    {
        void Draw(in FoldDisplay display);
    }
}
