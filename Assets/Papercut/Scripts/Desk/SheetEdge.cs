using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// One exit-candidate edge of a Sheet's walkable area: a trigger strip just inside an outline segment that
    /// faces <see cref="Direction"/>. The player is carried to the neighbouring Sheet when inside it and
    /// pushing outward; whether there is a neighbour with room is <see cref="ScreenNavigator"/>'s call.
    /// Generated at runtime by <see cref="SheetBoundary"/>; nothing is authored (exits are any walkable ground
    /// that reaches an edge, Front or Back alike).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class SheetEdge : MonoBehaviour
    {
        /// <summary>Minimum component of the player's move input along the edge direction that counts as "moving toward it".</summary>
        const float MoveTowardThreshold = 0.01f;

        GridDirection direction;
        Sheet sheet;
        ScreenNavigator navigator;

        public GridDirection Direction => direction;
        public Sheet Sheet => sheet;

        internal void Initialise(Sheet owner, GridDirection facing, ScreenNavigator screenNavigator)
        {
            sheet = owner;
            direction = facing;
            navigator = screenNavigator;
        }

        void OnTriggerStay2D(Collider2D other)
        {
            if (navigator == null || navigator.IsTransitioning)
                return;

            var body = other.attachedRigidbody;
            if (body == null || !body.TryGetComponent(out PlayerMover player))
                return;

            // What the player is actually moving by: while holding a block, the input along the block's axis only,
            // so a sideways press beside an edge does not travel mid-hold (Aaron, 2026-09-24: sideways does nothing).
            if (Vector2.Dot(player.EffectiveMoveInput, direction.ToVector()) < MoveTowardThreshold)
                return;

            navigator.TravelThrough(this, player);
        }
    }
}
