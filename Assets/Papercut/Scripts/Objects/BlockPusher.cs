using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Lets the player push a <see cref="PushableBlock"/> by walking into it: each physics step the block the
    /// player is pressing against moves along the cardinal axis of the face they touch, at the player's speed
    /// scaled by the block's push factor, and the player walks at that same scaled speed while pushing.
    /// </summary>
    /// <remarks>
    /// Runs before <see cref="PlayerMover"/> so the speed scale applies to this step's velocity. Contacts are the
    /// previous step's (one step of latency on first touch). A block that cannot move (against a wall) still counts
    /// as being pushed, so the slowdown holds; a block the player may not push (ability) is just a wall.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMover), typeof(Rigidbody2D), typeof(PlayerAbilities))]
    [DefaultExecutionOrder(-10)]
    public sealed class BlockPusher : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f), Tooltip("Move input component into the block needed to count as pushing.")]
        float pushInputThreshold = 0.3f;

        [SerializeField, Range(0.5f, 1f), Tooltip("How axis-aligned the contact must be to count as a block face (1 = perfectly flat contact).")]
        float pushFaceAxisFraction = 0.9f;

        PlayerMover mover;
        Rigidbody2D body;
        PlayerAbilities abilities;
        readonly List<ContactPoint2D> contacts = new();

        /// <summary>The block being pushed this step, if any.</summary>
        public PushableBlock Pushing { get; private set; }

        void Awake()
        {
            mover = GetComponent<PlayerMover>();
            body = GetComponent<Rigidbody2D>();
            abilities = GetComponent<PlayerAbilities>();
        }

        void OnDisable()
        {
            Pushing = null;
            mover.SpeedScale = 1f;
        }

        void FixedUpdate()
        {
            Pushing = null;
            if (!mover.MovementEnabled)
            {
                mover.SpeedScale = 1f;
                return;
            }

            contacts.Clear();
            body.GetContacts(contacts);
            foreach (var contact in contacts)
            {
                var other = contact.collider != null ? contact.collider.attachedRigidbody : null;
                if (other == null || !other.TryGetComponent(out PushableBlock block) || !block.IsVisible)
                    continue;
                var into = PushRules.IntoBlock(contact.normal, body.position, other.position);
                if (!PushRules.TryCardinal(into, pushFaceAxisFraction, out var cardinal))
                    continue;
                if (Vector2.Dot(mover.MoveInput, cardinal) < pushInputThreshold)
                    continue;
                if (!block.CanBePushedBy(abilities))
                    continue; // A block the player may not push is just a wall: no slowdown.

                Pushing = block;
                mover.SpeedScale = block.PushSpeedFactor;
                var velocity = mover.MoveInput * (mover.MoveSpeed * mover.SpeedScale);
                var distance = PushRules.PushDistance(velocity, cardinal, Time.fixedDeltaTime);
                block.TryPush(cardinal, distance, abilities, body, out _);
                return;
            }
            mover.SpeedScale = 1f;
        }
    }
}
