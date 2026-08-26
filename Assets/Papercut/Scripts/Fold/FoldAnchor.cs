using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Where a fold starts: one of the four sheet edges (crease parallel to that edge) or one of the four
    /// corners (crease at exactly 45°, cutting the corner off). The two legal crease families of Bible §4.
    /// </summary>
    public enum FoldAnchor
    {
        EdgeNorth,
        EdgeEast,
        EdgeSouth,
        EdgeWest,
        CornerNorthEast,
        CornerNorthWest,
        CornerSouthEast,
        CornerSouthWest,
    }

    public static class FoldAnchorExtensions
    {
        public static bool IsCorner(this FoldAnchor anchor) => anchor >= FoldAnchor.CornerNorthEast;

        public static bool IsEdge(this FoldAnchor anchor) => !anchor.IsCorner();

        /// <summary>Outward normal of an edge anchor. Throws for corners.</summary>
        public static GridDirection EdgeDirection(this FoldAnchor anchor) => anchor switch
        {
            FoldAnchor.EdgeNorth => GridDirection.North,
            FoldAnchor.EdgeEast => GridDirection.East,
            FoldAnchor.EdgeSouth => GridDirection.South,
            FoldAnchor.EdgeWest => GridDirection.West,
            _ => throw new System.ArgumentException($"{anchor} is not an edge anchor.", nameof(anchor)),
        };

        /// <summary>Sign of each axis toward a corner anchor, e.g. (+1, +1) for north-east. Throws for edges.</summary>
        public static Vector2 CornerSigns(this FoldAnchor anchor) => anchor switch
        {
            FoldAnchor.CornerNorthEast => new Vector2(1f, 1f),
            FoldAnchor.CornerNorthWest => new Vector2(-1f, 1f),
            FoldAnchor.CornerSouthEast => new Vector2(1f, -1f),
            FoldAnchor.CornerSouthWest => new Vector2(-1f, -1f),
            _ => throw new System.ArgumentException($"{anchor} is not a corner anchor.", nameof(anchor)),
        };
    }
}
