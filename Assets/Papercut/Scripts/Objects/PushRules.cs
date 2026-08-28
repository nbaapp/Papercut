using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The pure rules of pushing a <see cref="PushableBlock"/>: which way a contact pushes, how far a step of the
    /// pusher's walk moves the block, how an obstacle clamps it, and who may push. <see cref="BlockPusher"/> and
    /// the block turn these into physics.
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

        /// <summary>How far the block moves this step: the pusher's velocity component into the block, over <paramref name="dt"/>. Never negative.</summary>
        public static float PushDistance(Vector2 velocity, Vector2 direction, float dt)
            => Mathf.Max(0f, Vector2.Dot(velocity, direction)) * dt;

        /// <summary>The move shortened so the block stops <paramref name="skin"/> short of an obstacle at <paramref name="hitDistance"/>.</summary>
        public static float ClampToHit(float wanted, float hitDistance, float skin)
            => Mathf.Min(wanted, Mathf.Max(0f, hitDistance - skin));

        /// <summary>True if a pusher holding <paramref name="owned"/> may push a block that needs <paramref name="required"/> only when <paramref name="requiresAbility"/>.</summary>
        public static bool CanPush(Ability owned, bool requiresAbility, Ability required)
            => !requiresAbility || (owned & required) == required;
    }
}
