using System;

namespace Papercut
{
    /// <summary>
    /// One fold of a Sheet: where it starts and how deep it is. The data model of a fold; the crease it defines
    /// is <see cref="FoldGeometry.CreaseOf"/>, and what it does to the sheet as it lies is <see cref="SheetLayers.Apply"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Depth"/> is in sheet-local units: for an edge fold, the distance from the anchoring edge to
    /// the Crease; for a corner fold, the distance from the corner along each adjacent edge to where the Crease
    /// meets it. A Sheet holds an ordered list of these (Bible decision #1); order matters once folds stack.
    /// The largest usable depth depends on where the sheet lies (<see cref="SheetLayers.MaxDepth"/>), so it is
    /// not enforced here.
    /// </remarks>
    public readonly struct Fold : IEquatable<Fold>
    {
        public FoldAnchor Anchor { get; }
        public float Depth { get; }

        public Fold(FoldAnchor anchor, float depth)
        {
            Anchor = anchor;
            Depth = depth;
        }

        public bool IsCorner => Anchor.IsCorner();
        public bool IsEdge => Anchor.IsEdge();

        public Fold WithDepth(float depth) => new(Anchor, depth);

        public bool Equals(Fold other) => Anchor == other.Anchor && Depth.Equals(other.Depth);
        public override bool Equals(object obj) => obj is Fold other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Anchor, Depth);
        public override string ToString() => $"{Anchor} depth {Depth:0.###}";
    }
}
