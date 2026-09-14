using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The rule for stepping onto a neighbouring Sheet: there must be room for the player where they arrive.
    /// Pure so it is testable; <see cref="ScreenNavigator"/> supplies the shapes.
    /// </summary>
    public static class TravelRules
    {
        /// <summary>True if <paramref name="playerBox"/> shares no area with any of <paramref name="solids"/> (touching edges is fine).</summary>
        public static bool HasRoom(Rect playerBox, IEnumerable<ConvexPolygon> solids)
        {
            foreach (var solid in solids)
            {
                if (solid.Overlaps(playerBox))
                    return false;
            }
            return true;
        }
    }
}
