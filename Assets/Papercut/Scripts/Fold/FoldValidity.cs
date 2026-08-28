using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The rules about where a fold may be relative to the player and to other folds (Bible §4). Sheet-local.
    /// The player rule is [LOCKED]: a fold may not cover the player, wholly or partially, and the player is never
    /// carried by the Flap.
    /// </summary>
    public static class FoldValidity
    {
        /// <summary>True if the player touches neither what the fold lifts nor where it lands.</summary>
        public static bool IsValid(in FoldEffect effect, Rect playerFootprint)
            => !PlayerOverlapsFlap(effect, playerFootprint);

        /// <summary>
        /// True if the player stands on a piece the fold lifts or on a piece where it lands. Used both to reject
        /// a fold and to refuse an unfold while the player is on the landed Flap.
        /// </summary>
        public static bool PlayerOverlapsFlap(in FoldEffect effect, Rect playerFootprint)
            => effect.OverlapsRect(playerFootprint);

        /// <summary>
        /// True if the two folds never touch each other's pieces: neither lifts or lands on anything the other
        /// lifted or landed. Independent folds can be made and undone in any order; with stacking off every
        /// fold must be independent of every other.
        /// </summary>
        public static bool Independent(in FoldEffect a, in FoldEffect b) => !a.Overlaps(b);
    }
}
