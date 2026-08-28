using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Front content of a Sheet that is solid to the player where they would arrive on it, for the room test of
    /// a screen transition (<see cref="TravelRules.HasRoom"/>). Asked only while the sheet is not the Screen, so
    /// the sheet is flat and face-local coordinates are sheet-local.
    /// </summary>
    public interface IArrivalObstacle
    {
        /// <summary>True with the sheet-local rect this object blocks for <paramref name="player"/>; false if it does not block them.</summary>
        bool TryGetSolidFootprint(PlayerAbilities player, out Rect sheetLocal);
    }
}
