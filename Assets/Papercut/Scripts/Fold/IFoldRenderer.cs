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
        /// <summary>A fold being dragged that would be refused (covers the player, overlaps another fold, overhangs).</summary>
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

    /// <summary>
    /// Everything a renderer needs to draw a sheet's folds. Owned by <see cref="SheetFolds"/>. The folds are in
    /// the order they apply (committed folds first, in commit order; then any retreating and preview fold), already
    /// replayed by <see cref="SheetFolds"/> into <see cref="Layers"/> (where every piece of the sheet lies) and
    /// <see cref="Effects"/>; a renderer draws from those and must not replay the folds itself.
    /// </summary>
    public readonly struct FoldDisplay
    {
        public IReadOnlyList<FoldVisual> Folds { get; }

        /// <summary>Marks left by folds that were undone; drawn faintly on both faces until the sheet resets.</summary>
        public IReadOnlyList<CreaseMark> RememberedCreases { get; }

        /// <summary>The sheet as it lies after every displayed fold (<see cref="Folds"/> replayed in order): where every piece is drawn.</summary>
        public SheetLayers Layers { get; }

        /// <summary>What each displayed fold did, index-aligned with <see cref="Folds"/>.</summary>
        public IReadOnlyList<FoldEffect> Effects { get; }

        public FoldDisplay(IReadOnlyList<FoldVisual> folds, IReadOnlyList<CreaseMark> rememberedCreases, SheetLayers layers, IReadOnlyList<FoldEffect> effects)
        {
            Folds = folds;
            RememberedCreases = rememberedCreases;
            Layers = layers;
            Effects = effects;
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

        /// <summary>
        /// Sheet-local z at which something resting on layer <paramref name="layerIndex"/> of the sheet (bottom = 0)
        /// is drawn above that layer and below the next. The renderer is the only thing that knows its depth layout.
        /// </summary>
        float SurfaceZ(int layerIndex);
    }
}
