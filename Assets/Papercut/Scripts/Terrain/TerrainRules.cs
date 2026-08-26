namespace Papercut
{
    /// <summary>
    /// The one place that says whether a player may cross a piece of terrain. Pure, so it is testable and so
    /// "what counts as passable" has exactly one home; <see cref="TerrainRegion"/> turns the answer into physics.
    /// </summary>
    public static class TerrainRules
    {
        /// <summary>
        /// A region that requires <see cref="Ability.None"/> is a wall: nothing lets you through. Otherwise the
        /// player must hold every flag in <paramref name="required"/>.
        /// </summary>
        public static bool IsPassable(Ability owned, Ability required)
            => required != Ability.None && (owned & required) == required;
    }
}
