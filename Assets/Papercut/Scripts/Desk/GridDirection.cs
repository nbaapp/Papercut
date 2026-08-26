using System;
using UnityEngine;

namespace Papercut
{
    /// <summary>A cardinal direction on the Desk grid. North is +y, East is +x.</summary>
    public enum GridDirection
    {
        North,
        East,
        South,
        West,
    }

    public static class GridDirectionExtensions
    {
        /// <summary>The grid offset one step in this direction.</summary>
        public static Vector2Int ToOffset(this GridDirection direction) => direction switch
        {
            GridDirection.North => Vector2Int.up,
            GridDirection.East => Vector2Int.right,
            GridDirection.South => Vector2Int.down,
            GridDirection.West => Vector2Int.left,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
        };

        /// <summary>Unit vector pointing in this direction.</summary>
        public static Vector2 ToVector(this GridDirection direction) => direction.ToOffset();

        public static GridDirection Opposite(this GridDirection direction) => direction switch
        {
            GridDirection.North => GridDirection.South,
            GridDirection.East => GridDirection.West,
            GridDirection.South => GridDirection.North,
            GridDirection.West => GridDirection.East,
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
        };
    }
}
