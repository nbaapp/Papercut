using System;

namespace Papercut
{
    /// <summary>
    /// One fold of a Sheet: where it starts and how deep it is. The data model of a fold; everything geometric
    /// is derived from it by <see cref="FoldGeometry"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Depth"/> is in sheet-local units: for an edge fold, the distance from the anchoring edge to
    /// the Crease; for a corner fold, the distance from the corner along each adjacent edge to where the Crease
    /// meets it. A Sheet holds a list of these even though only one is exposed at a time (Bible decision #1).
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

        /// <summary>This fold with its depth limited so the landed Flap stays on the sheet (Aaron, 2026-08-26: no overhang).</summary>
        public Fold Clamped => new(Anchor, System.Math.Min(Depth, FoldGeometry.MaxDepth(Anchor)));

        /// <summary>True if the landed Flap would extend past the sheet.</summary>
        public bool Overhangs => Depth > FoldGeometry.MaxDepth(Anchor) + FoldGeometry.DepthEpsilon;

        public bool Equals(Fold other) => Anchor == other.Anchor && Depth.Equals(other.Depth);
        public override bool Equals(object obj) => obj is Fold other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Anchor, Depth);
        public override string ToString() => $"{Anchor} depth {Depth:0.###}";
    }
}
