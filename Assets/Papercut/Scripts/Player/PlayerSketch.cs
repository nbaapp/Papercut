using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// What the player's drawing should be doing this frame: which loop plays and which way it faces.
    /// Pure result of <see cref="PlayerSketch.Choose"/> so the rule can be tested without a scene.
    /// </summary>
    public readonly struct SketchChoice
    {
        public readonly bool Running;
        public readonly bool FacingLeft;

        public SketchChoice(bool running, bool facingLeft)
        {
            Running = running;
            FacingLeft = facingLeft;
        }
    }

    /// <summary>
    /// Picks the player's sketch animation - run while moving, the animator's idle otherwise - and mirrors the
    /// drawing to face the way the player is going. This is the one place that decides what the player's
    /// <see cref="SketchAnimator"/> plays; the animator itself stays generic for every other sketched object.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMover))]
    public sealed class PlayerSketch : MonoBehaviour
    {
        [Header("Drawing")]
        [SerializeField, Tooltip("The SketchAnimator on the player's Visual child. Its idle is the standing animation.")]
        SketchAnimator animator;

        [SerializeField, Tooltip("Played while the player is moving.")]
        SketchAnimation run;

        [Header("Feel")]
        [SerializeField, Range(0f, 0.95f), Tooltip("Move input magnitude above which the run plays. At 0 any input that moves the player runs (the Input System stick deadzone is the gate). Raise it to add a slow-stick band where Scuffy drifts without running.")]
        float runInputThreshold = 0f;

        [SerializeField, Tooltip("Flip the drawing horizontally when moving left. The drawings are made facing right. Off: always face right.")]
        bool mirrorToFaceLeft = true;

        PlayerMover mover;
        SpriteRenderer spriteRenderer;

        /// <summary>Which animation was last requested from the animator; null until the first request after enable.</summary>
        bool? requestedRunning;

        /// <summary>True when the drawing last moved left. Kept while idle or moving straight up/down.</summary>
        public bool FacingLeft { get; private set; }

        /// <summary>
        /// The decision rule. Running needs movement to be enabled (a player carried by a screen transition must not
        /// run on the spot) and input above <paramref name="threshold"/>. Facing follows the horizontal input only
        /// while running; zero horizontal input keeps <paramref name="facingLeft"/>.
        /// </summary>
        public static SketchChoice Choose(Vector2 input, bool movementEnabled, float threshold, bool facingLeft)
        {
            var running = movementEnabled && input.sqrMagnitude > threshold * threshold;
            if (running)
            {
                if (input.x < 0f)
                    facingLeft = true;
                else if (input.x > 0f)
                    facingLeft = false;
            }
            return new SketchChoice(running, facingLeft);
        }

        void Awake()
        {
            mover = GetComponent<PlayerMover>();

            if (animator == null)
            {
                Debug.LogError($"PlayerSketch on '{name}' has no SketchAnimator assigned.", this);
                enabled = false;
                return;
            }
            if (run == null)
            {
                Debug.LogError($"PlayerSketch on '{name}' has no run animation.", this);
                enabled = false;
                return;
            }

            // SketchAnimator requires a SpriteRenderer, so this cannot be null.
            spriteRenderer = animator.GetComponent<SpriteRenderer>();
        }

        void OnEnable()
        {
            // Re-assert on the next Update: while disabled something else may have changed what plays.
            requestedRunning = null;
        }

        void Update()
        {
            var choice = Choose(mover.MoveInput, mover.MovementEnabled, runInputThreshold, FacingLeft);

            if (requestedRunning != choice.Running)
            {
                requestedRunning = choice.Running;
                if (choice.Running)
                    animator.Play(run);
                else
                    animator.PlayIdle();
            }

            FacingLeft = choice.FacingLeft;
            spriteRenderer.flipX = mirrorToFaceLeft && FacingLeft;
        }
    }
}
