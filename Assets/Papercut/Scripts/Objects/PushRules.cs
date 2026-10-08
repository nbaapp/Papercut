using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The pure rules of holding and moving a <see cref="PushableBlock"/>: which face a contact is on, what the
    /// player's input does along that face's axis (push, pull or nothing), how far a step moves the block, how an
    /// obstacle clamps it, how the player follows the block, when a hold has been broken, and who may push.
    /// <see cref="BlockPusher"/> and the block turn these into physics.
    /// </summary>
    public static class PushRules
    {
        /// <summary>
        /// The dominant axis of <paramref name="v"/> as a unit vector, if that axis carries at least
        /// <paramref name="minAxisFraction"/> of its length (a diagonal contact is not a block face). False for zero.
        /// </summary>
        public static bool TryCardinal(Vector2 v, float minAxisFraction, out Vector2 cardinal)
        {
            cardinal = Vector2.zero;
            var length = v.magnitude;
            if (length <= 1e-6f)
                return false;
            var ax = Mathf.Abs(v.x);
            var ay = Mathf.Abs(v.y);
            if (ax >= ay)
            {
                if (ax < minAxisFraction * length)
                    return false;
                cardinal = new Vector2(Mathf.Sign(v.x), 0f);
            }
            else
            {
                if (ay < minAxisFraction * length)
                    return false;
                cardinal = new Vector2(0f, Mathf.Sign(v.y));
            }
            return true;
        }

        /// <summary>
        /// <paramref name="contactNormal"/> oriented from the pusher into the block. Physics2D does not promise
        /// which way a contact normal points, so the sign is settled from the two centres.
        /// </summary>
        public static Vector2 IntoBlock(Vector2 contactNormal, Vector2 pusherCentre, Vector2 blockCentre)
            => Vector2.Dot(contactNormal, blockCentre - pusherCentre) >= 0f ? contactNormal : -contactNormal;

        /// <summary>
        /// The player's input along the hold axis. <paramref name="axis"/> is the unit cardinal from the player into
        /// the held block. Returns the input to move by: the component along the axis (<c>axis * a</c>, with
        /// <c>a = Dot(input, axis)</c>) when |a| reaches <paramref name="threshold"/>; otherwise zero - the dead band, in
        /// which the player stands still and the block does not move. Input across the axis is dropped either way
        /// (Aaron, 2026-09-24: sideways does nothing while holding). <paramref name="sense"/> is +1 pressing into the
        /// block (a push), −1 pressing away from it (a pull), 0 in the dead band.
        /// </summary>
        public static Vector2 HoldInput(Vector2 input, Vector2 axis, float threshold, out int sense)
        {
            var along = Vector2.Dot(input, axis);
            if (along == 0f || Mathf.Abs(along) < threshold)
            {
                sense = 0;
                return Vector2.zero;
            }
            sense = along > 0f ? 1 : -1;
            return axis * along;
        }

        /// <summary>How far the block moves this step: the pusher's velocity component into the block, over <paramref name="dt"/>. Never negative.</summary>
        public static float PushDistance(Vector2 velocity, Vector2 direction, float dt)
            => Mathf.Max(0f, Vector2.Dot(velocity, direction)) * dt;

        /// <summary>The move shortened so the block stops <paramref name="skin"/> short of an obstacle at <paramref name="hitDistance"/>.</summary>
        public static float ClampToHit(float wanted, float hitDistance, float skin)
            => Mathf.Min(wanted, Mathf.Max(0f, hitDistance - skin));

        /// <summary>
        /// The move input (magnitude 0..1) that carries the player exactly <paramref name="moved"/> units along
        /// <paramref name="direction"/> this step, at <paramref name="speed"/> units per second over <paramref name="dt"/>
        /// seconds - so the player goes as far as the held block went and no farther. Zero when the block did not
        /// move or nothing could be moved (speed or dt not positive); never longer than a unit input.
        /// </summary>
        public static Vector2 FollowInput(Vector2 direction, float moved, float speed, float dt)
        {
            if (moved <= 0f || speed <= 0f || dt <= 0f)
                return Vector2.zero;
            return Vector2.ClampMagnitude(direction * (moved / (speed * dt)), 1f);
        }

        /// <summary>
        /// True when the separation between player and block along the hold axis, <paramref name="gapNow"/>, has
        /// drifted more than <paramref name="tolerance"/> from what it was when the hold began,
        /// <paramref name="gapAtHold"/>: something other than the hold moved one of them (a fold re-placed the
        /// block; the player was stopped by a crease the block rolled across), so the hold lets go rather than
        /// dragging across the gap.
        /// </summary>
        public static bool HoldBroken(float gapNow, float gapAtHold, float tolerance)
            => Mathf.Abs(gapNow - gapAtHold) > tolerance;

        /// <summary>
        /// True if a pusher holding <paramref name="owned"/> may push a block that needs <paramref name="required"/>
        /// only when <paramref name="requiresAbility"/>. A block that is not <paramref name="pushable"/> at all
        /// (a fixed paperweight) is never pushed, whatever the pusher holds.
        /// </summary>
        public static bool CanPush(bool pushable, Ability owned, bool requiresAbility, Ability required)
            => pushable && (!requiresAbility || (owned & required) == required);
    }
}
