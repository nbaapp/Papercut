using System;

namespace Papercut
{
    /// <summary>
    /// Who a <see cref="TerrainRegion"/> is solid to (Aaron, 2026-09-28: a "filter wall" blocks blocks but not the
    /// player). Independent of where the region lives (a face, or above the sheet); the two compose as data.
    /// </summary>
    [Flags]
    public enum TerrainBlocks
    {
        None = 0,
        /// <summary>The player - unless they hold the region's required ability (the gate rule).</summary>
        Player = 1 << 0,
        /// <summary>Every pushable block and paperweight (<see cref="PushableBlock"/>).</summary>
        Blocks = 1 << 1,
    }
}
