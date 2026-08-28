using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The one rule of a <see cref="PressurePlate"/>: it is pressed while something stands on it — on the same
    /// face of the sheet, with its centre inside the plate. A fact about the flat sheet, not about what is
    /// visible: a block under a Flap still holds the plate under it (Aaron, 2026-08-27).
    /// </summary>
    public static class PressureRules
    {
        public static bool IsPressed(Rect plateFlatRect, SheetFace plateSide, IReadOnlyList<SheetPoint> pressers)
        {
            foreach (var presser in pressers)
            {
                if (presser.Side == plateSide && plateFlatRect.Contains(presser.Point))
                    return true;
            }
            return false;
        }
    }
}
