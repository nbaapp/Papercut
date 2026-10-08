namespace Papercut
{
    /// <summary>
    /// The one place that says whether a player may cross a piece of terrain, and whether a block is stopped by
    /// it. Pure, so it is testable and so "what counts as passable" has exactly one home; <see cref="TerrainRegion"/>
    /// turns the answer into physics.
    /// </summary>
    public static class TerrainRules
    {
        /// <summary>
        /// The ability gate: a region that requires <see cref="Ability.None"/> is a wall, nothing lets you through;
        /// otherwise the player must hold every flag in <paramref name="required"/>.
        /// </summary>
        public static bool IsPassable(Ability owned, Ability required)
            => required != Ability.None && (owned & required) == required;

        /// <summary>
        /// Whether the player may cross a region solid to <paramref name="blocks"/>: a region that is not solid to
        /// the player at all (a filter wall, Aaron 2026-09-28) is always passable; otherwise the ability gate decides.
        /// </summary>
        public static bool IsPassable(TerrainBlocks blocks, Ability owned, Ability required)
            => (blocks & TerrainBlocks.Player) == 0 || IsPassable(owned, required);

        /// <summary>True if a region solid to <paramref name="blocks"/> stops pushable blocks.</summary>
        public static bool StopsBlocks(TerrainBlocks blocks) => (blocks & TerrainBlocks.Blocks) != 0;
    }
}
