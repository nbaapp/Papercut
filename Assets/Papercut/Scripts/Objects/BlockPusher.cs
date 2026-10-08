using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Lets the player take hold of a <see cref="PushableBlock"/> and push or pull it: while the interact key is
    /// down (<see cref="PlayerMover.InteractHeld"/>) and the player touches one of a block's faces, that block is
    /// held; the player then moves only along the axis through that face - toward the block to push it ahead of
    /// them, away from it to pull it after them - at the block's push speed, and input across the axis does
    /// nothing. Releasing the key lets go. Without the key a block is just a wall (Aaron, 2026-09-24: hold Space to
    /// push, so that pulling is possible too; two directions only, perpendicular to the face; sideways ignored).
    /// </summary>
    /// <remarks>
    /// Runs before <see cref="PlayerMover"/> so the speed scale and move override apply to this step's velocity.
    /// The player's movement while holding is derived from what the block actually moved
    /// (<see cref="PushRules.FollowInput"/>), so the two stay locked together in both directions: a block stopped
    /// by a wall stops the player, and a pull never opens a gap. A pull first checks how far the player can walk
    /// back (their own cast), so a wall behind the player stops the block too. Contacts are the previous step's
    /// (one step of latency on first touch); a resting contact that has lapsed at a standstill is covered by a
    /// contact-offset-length probe. A block the player may not push (ability, or a fixed paperweight) cannot be
    /// held and stays a wall with no slowdown. Blocks are content that may be cut: nothing else depends on this.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMover), typeof(Rigidbody2D), typeof(PlayerAbilities))]
    [DefaultExecutionOrder(-10)]
    public sealed class BlockPusher : MonoBehaviour
    {
        [Header("Hold")]
        [SerializeField, Range(0f, 1f), Tooltip("Move input component along the held block's axis, toward or away from it, needed to move it. Below this the player and block stand still.")]
        float pushInputThreshold = 0.3f;

        [SerializeField, Range(0.5f, 1f), Tooltip("How axis-aligned the contact must be to count as a block face (1 = perfectly flat contact).")]
        float pushFaceAxisFraction = 0.9f;

        [SerializeField, Min(0f), Tooltip("If the held block ends up this much nearer or farther along the hold axis than when it was taken hold of " +
            "(something else moved one of them: a fold re-placed the block, the player was stopped by a crease the block rolled across), the hold lets go instead of dragging across the gap. World units.")]
        float holdBreakDistance = 0.1f;

        /// <summary>The order the standstill probe tries after the pressed direction.</summary>
        static readonly Vector2[] Cardinals = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };

        /// <summary>Everything solid: no triggers, every layer. The held block and passable terrain are skipped per hit.</summary>
        static readonly ContactFilter2D SolidFilter = new() { useTriggers = false, useLayerMask = false };

        /// <summary>
        /// A pull stops the block this far short of where the player's cast hits: two default contact offsets, the
        /// gap physics itself keeps the player from a wall, so a pull into a wall never wedges the block into them.
        /// A physics-engine fact, hence derived; the block's own Inspector skin is its authoring gap from walls.
        /// </summary>
        static float PullSkin => 2f * Physics2D.defaultContactOffset;

        PlayerMover mover;
        Rigidbody2D body;
        PlayerAbilities abilities;
        readonly List<ContactPoint2D> contacts = new();
        readonly List<RaycastHit2D> hits = new();

        Rigidbody2D heldBody;
        /// <summary>Separation from the player to the held block along the hold axis when the hold began.</summary>
        float holdGap;

        /// <summary>The block being held, if any.</summary>
        public PushableBlock Held { get; private set; }

        /// <summary>Unit cardinal from the player into the held block (desk space). Zero when nothing is held.</summary>
        public Vector2 HeldDirection { get; private set; }

        /// <summary>
        /// This step's input along the hold axis: +1 pressing into the block (a push), −1 away from it (a pull),
        /// 0 in the dead band or when nothing is held. Set whether or not the block could move (a blocked push still
        /// reads as pushing - Aaron, 2026-09-24: the push loop plays as a strain).
        /// </summary>
        public int HeldSense { get; private set; }

        void Awake()
        {
            mover = GetComponent<PlayerMover>();
            body = GetComponent<Rigidbody2D>();
            abilities = GetComponent<PlayerAbilities>();
        }

        void OnDisable()
        {
            Release();
        }

        void FixedUpdate()
        {
            HeldSense = 0;
            if (!mover.MovementEnabled || !mover.InteractHeld)
            {
                Release();
                return;
            }

            if (Held == null && !TryTakeHold())
            {
                Release();
                return;
            }

            if (!StillHeld())
            {
                Release();
                return;
            }

            mover.SpeedScale = Held.PushSpeedFactor;
            var speed = mover.MoveSpeed * mover.SpeedScale;
            var move = PushRules.HoldInput(mover.MoveInput, HeldDirection, pushInputThreshold, out var sense);
            HeldSense = sense;
            if (sense == 0)
            {
                mover.MoveOverride = Vector2.zero;
                return;
            }

            var direction = HeldDirection * sense;
            var dt = Time.fixedDeltaTime;
            var wanted = PushRules.PushDistance(move * speed, direction, dt);
            if (sense < 0)
                wanted = PushRules.ClampToHit(wanted, PlayerFreeDistance(direction, wanted), PullSkin);
            Held.TryPush(direction, wanted, abilities, body, out var moved);
            mover.MoveOverride = PushRules.FollowInput(direction, moved, speed, dt);
        }

        /// <summary>
        /// Looks for a block face the player is touching - the previous step's contacts first, then a
        /// contact-offset-length probe along the cardinals (the pressed direction's dominant axis first) for a resting contact
        /// that has lapsed at a standstill - and takes hold of the first that qualifies.
        /// </summary>
        bool TryTakeHold()
        {
            contacts.Clear();
            body.GetContacts(contacts);
            foreach (var contact in contacts)
            {
                var other = contact.collider != null ? contact.collider.attachedRigidbody : null;
                if (TryQualify(other, contact.normal, out var block, out var cardinal))
                    return TakeHold(block, other, cardinal);
            }

            // The dominant axis of the pressed direction is tried first (a diagonal press has no preference: x wins);
            // then the rest in a fixed order.
            var reach = Physics2D.defaultContactOffset;
            if (PushRules.TryCardinal(mover.MoveInput, 0f, out var pressed) && TryProbe(pressed, reach))
                return true;
            foreach (var cardinal in Cardinals)
            {
                if (cardinal != pressed && TryProbe(cardinal, reach))
                    return true;
            }
            return false;
        }

        bool TryProbe(Vector2 cardinal, float reach)
        {
            hits.Clear();
            body.Cast(cardinal, SolidFilter, hits, reach);
            foreach (var hit in hits)
            {
                if (TryQualify(hit.rigidbody, hit.normal, out var block, out var face) && face == cardinal)
                    return TakeHold(block, hit.rigidbody, face);
            }
            return false;
        }

        /// <summary>True if <paramref name="other"/> is a block the player may hold, touched on a face; the face's cardinal points from the player into it.</summary>
        bool TryQualify(Rigidbody2D other, Vector2 normal, out PushableBlock block, out Vector2 cardinal)
        {
            block = null;
            cardinal = Vector2.zero;
            if (other == null || !other.TryGetComponent(out block) || !block.IsVisible || !block.CanBePushedBy(abilities))
                return false;
            var into = PushRules.IntoBlock(normal, body.position, other.position);
            return PushRules.TryCardinal(into, pushFaceAxisFraction, out cardinal);
        }

        bool TakeHold(PushableBlock block, Rigidbody2D blockBody, Vector2 cardinal)
        {
            Held = block;
            heldBody = blockBody;
            HeldDirection = cardinal;
            holdGap = Vector2.Dot(heldBody.position - body.position, cardinal);
            return true;
        }

        /// <summary>The held block is still there to be held: present, visible, pushable by this player, on the Screen, and where the hold left it.</summary>
        bool StillHeld()
        {
            if (Held == null || heldBody == null || !Held.isActiveAndEnabled || !Held.IsVisible || !Held.CanBePushedBy(abilities) || !Held.Sheet.IsScreen)
                return false;
            var gap = Vector2.Dot(heldBody.position - body.position, HeldDirection);
            return !PushRules.HoldBroken(gap, holdGap, holdBreakDistance);
        }

        /// <summary>
        /// How far the player can move along <paramref name="direction"/> before something solid stops them, up to
        /// <paramref name="distance"/>. The held block is not in the way (it moves with them), and neither is terrain
        /// the player may pass: passability is applied with Physics2D.IgnoreCollision pairs, which a cast does not
        /// honour, so a swimmer pulling a block back into Water must not be stopped as if by a wall.
        /// </summary>
        float PlayerFreeDistance(Vector2 direction, float distance)
        {
            hits.Clear();
            body.Cast(direction, SolidFilter, hits, distance + PullSkin);
            var nearest = float.PositiveInfinity;
            foreach (var hit in hits)
            {
                if (hit.rigidbody == heldBody)
                    continue;
                var region = hit.collider.GetComponentInParent<TerrainRegion>();
                if (region != null && region.IsPassableBy(abilities))
                    continue;
                nearest = Mathf.Min(nearest, hit.distance);
            }
            return nearest;
        }

        void Release()
        {
            Held = null;
            heldBody = null;
            HeldDirection = Vector2.zero;
            HeldSense = 0;
            mover.MoveOverride = null;
            mover.SpeedScale = 1f;
        }
    }
}
