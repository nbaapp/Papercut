using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Which committed fold a press addresses, from the folds' effects (index-aligned with the fold list, in
    /// apply order). Pure, so the precedence rules are testable without a Sheet. Sheet-local space.
    /// </summary>
    /// <remarks>
    /// A fold is <em>pinned</em> when a later fold lifted or landed on anything it lifted or landed: it cannot be
    /// unfolded or refolded first, and its Seam and crease are where they were at commit time — possibly under
    /// or along a later fold. So a pinned fold's Seam is never a hit; the nearest unpinned one, latest first, is.
    /// </remarks>
    public static class FoldHitTest
    {
        /// <summary>True if a later fold lifted or landed on anything fold <paramref name="index"/> lifted or landed.</summary>
        public static bool IsPinned(IReadOnlyList<FoldEffect> effects, int index)
        {
            for (int j = index + 1; j < effects.Count; j++)
            {
                if (!FoldValidity.Independent(effects[index], effects[j]))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Index of the latest unpinned fold whose Seam is within <paramref name="grabDistance"/> of
        /// <paramref name="point"/> and which the player does not stand on; −1 otherwise, with
        /// <paramref name="rejection"/> = <see cref="FoldRejection.PlayerOnFlap"/> if the player is on that fold
        /// (the search stops there), <see cref="FoldRejection.CoveredByLaterFold"/> if only pinned Seams were near,
        /// <see cref="FoldRejection.None"/> if none was.
        /// </summary>
        public static int SeamAt(IReadOnlyList<FoldEffect> effects, Vector2 point, float grabDistance, Rect playerFootprint, out FoldRejection rejection)
        {
            rejection = FoldRejection.None;
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                if (effects[i].DistanceToSeam(point) > grabDistance)
                    continue;
                if (IsPinned(effects, i))
                {
                    rejection = FoldRejection.CoveredByLaterFold;
                    continue;
                }
                if (FoldValidity.PlayerOverlapsFlap(effects[i], playerFootprint))
                {
                    rejection = FoldRejection.PlayerOnFlap;
                    return -1;
                }
                return i;
            }
            return -1;
        }

        /// <summary>Index of the latest unpinned fold whose crease is within <paramref name="grabDistance"/> of <paramref name="point"/>; −1 if none.</summary>
        public static int CreaseAt(IReadOnlyList<FoldEffect> effects, Vector2 point, float grabDistance)
        {
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                if (!IsPinned(effects, i) && effects[i].DistanceToCrease(point) <= grabDistance)
                    return i;
            }
            return -1;
        }
    }
}
