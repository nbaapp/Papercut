using System;

namespace Papercut
{
    /// <summary>
    /// Abilities the player can hold (Design Doc: Unlockables). A flag set: the player's current abilities are
    /// a combination of these, stored on <see cref="PlayerAbilities"/>. Elements consult the flags; they never
    /// branch on "which unlockable is this".
    /// </summary>
    /// <remarks>
    /// <see cref="None"/> is the empty set. Note the one place it reads differently: a <see cref="TerrainRegion"/>
    /// whose required ability is <see cref="None"/> is a wall that nothing lets you cross — see
    /// <see cref="TerrainRules.IsPassable"/>. Adding an unlockable is adding a member here.
    /// </remarks>
    [Flags]
    public enum Ability
    {
        None = 0,
        Swim = 1 << 0,
        /// <summary>Lets the player push blocks that ask for it (Design Doc: push/pull as a possible early unlockable). Unused by content for now (Aaron, 2026-08-27).</summary>
        Push = 1 << 1,
    }
}
