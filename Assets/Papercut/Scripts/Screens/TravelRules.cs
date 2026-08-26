using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The rule for stepping onto a neighbouring Sheet: there must be room for the player where they arrive.
    /// Pure so it is testable; <see cref="ScreenNavigator"/> supplies the rects.
    /// </summary>
    public static class TravelRules
    {
        /// <summary>True if <paramref name="playerBox"/> shares no area with any of <paramref name="solids"/> (touching edges is fine).</summary>
        public static bool HasRoom(Rect playerBox, IEnumerable<Rect> solids)
        {
            foreach (var solid in solids)
            {
                var inter = FoldGeometry.Intersect(playerBox, solid);
                if (inter.width > 1e-5f && inter.height > 1e-5f)
                    return false;
            }
            return true;
        }
    }
}
