using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Moves the player continuously across the sheet from a move-input vector. Movement is free,
    /// not tile-based (Bible §3 [LOCKED]); collision comes from the Rigidbody2D.
    /// </summary>
    /// <remarks>
    /// Movement scheme (analog vs 8-way, Bible decision #12) is open. This applies the input vector as-is,
    /// clamped to unit length; a quantising step would slot into <see cref="SetMoveInput"/>.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerMover : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("World units per second at full input.")]
        float moveSpeed = 4f;

        Rigidbody2D body;
        Vector2 moveInput;

        /// <summary>Direction the player is trying to move, magnitude 0..1. Kept even while movement is held.</summary>
        public Vector2 MoveInput => moveInput;

        bool movementEnabled = true;
        RigidbodyInterpolation2D heldInterpolation;

        /// <summary>
        /// When false the player stands still regardless of input (e.g. during a screen transition). While held
        /// the body is carried by the world (the sheet grid sliding), so its interpolation is switched off:
        /// an interpolated body rewrites the transform from its last physics pose every frame and would fight the
        /// parent's motion. The previous mode is restored when movement resumes.
        /// </summary>
        public bool MovementEnabled
        {
            get => movementEnabled;
            set
            {
                if (movementEnabled == value)
                    return;
                movementEnabled = value;
                if (value)
                {
                    body.interpolation = heldInterpolation;
                }
                else
                {
                    heldInterpolation = body.interpolation;
                    body.interpolation = RigidbodyInterpolation2D.None;
                }
            }
        }

        public Vector2 Position => body.position;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
        }

        public void SetMoveInput(Vector2 input) => moveInput = Vector2.ClampMagnitude(input, 1f);

        /// <summary>Teleports the player, discarding any velocity. Z is preserved.</summary>
        public void PlaceAt(Vector2 worldPosition)
        {
            body.linearVelocity = Vector2.zero;
            body.position = worldPosition;
            transform.position = new Vector3(worldPosition.x, worldPosition.y, transform.position.z);
        }

        void FixedUpdate()
        {
            body.linearVelocity = MovementEnabled ? moveInput * moveSpeed : Vector2.zero;
        }
    }
}
