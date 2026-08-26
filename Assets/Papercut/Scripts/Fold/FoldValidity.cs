using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The one rule about where the player may be relative to a fold (Bible §4 [LOCKED]): a fold may not cover
    /// the player, wholly or partially, and the player is never carried by the Flap. Sheet-local space.
    /// </summary>
    public static class FoldValidity
    {
        /// <summary>True if <paramref name="playerFootprint"/> touches neither the lifting Flap nor where it lands.</summary>
        public static bool IsValid(Fold fold, Rect playerFootprint)
            => !PlayerOverlapsFlap(fold, playerFootprint);

        /// <summary>
        /// True if the player stands on the Flap or its landing place (overhang included). Used both to reject
        /// a fold and to refuse an unfold while the player is on the landed Flap.
        /// </summary>
        public static bool PlayerOverlapsFlap(Fold fold, Rect playerFootprint)
            => FoldGeometry.FlapRegion(fold).Overlaps(playerFootprint)
            || FoldGeometry.LandedRegion(fold).Overlaps(playerFootprint);
    }
}
