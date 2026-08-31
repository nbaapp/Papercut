using UnityEngine;

namespace Papercut
{
    /// <summary>Which of the player's loops should play.</summary>
    public enum SketchState
    {
        Idle,
        Running,
        Pushing,
    }

    /// <summary>
    /// What the player's drawing should be doing this frame: which loop plays and which way it faces.
    /// Pure result of <see cref="PlayerSketch.Choose"/> so the rule can be tested without a scene.
    /// </summary>
    public readonly struct SketchChoice
    {
        public readonly SketchState State;
        public readonly bool FacingLeft;

        public SketchChoice(SketchState state, bool facingLeft)
        {
            State = state;
            FacingLeft = facingLeft;
        }

        public bool Running => State == SketchState.Running;
        public bool Pushing => State == SketchState.Pushing;
    }

    /// <summary>
    /// Picks the player's sketch animation - push while pushing a block, run while moving, the animator's idle
    /// otherwise - and mirrors the drawing to face the way the player is going. This is the one place that decides what the player's
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

        [SerializeField, Tooltip("Played while the player is pushing a block (needs a BlockPusher on the player). Optional: left empty, pushing shows the run.")]
        SketchAnimation push;

        [Header("Feel")]
        [SerializeField, Range(0f, 0.95f), Tooltip("Move input magnitude above which the run plays. At 0 any input that moves the player runs (the Input System stick deadzone is the gate). Raise it to add a slow-stick band where Scuffy drifts without running.")]
        float runInputThreshold = 0f;

        [SerializeField, Tooltip("Flip the drawing horizontally when moving left. The drawings are made facing right. Off: always face right.")]
        bool mirrorToFaceLeft = true;

        PlayerMover mover;
        SpriteRenderer spriteRenderer;

        /// <summary>Optional: the block feature is content that may be cut, so this is never required.</summary>
        BlockPusher pusher;

        /// <summary>Which state was last requested from the animator; null until the first request after enable.</summary>
        SketchState? requested;

        /// <summary>True when the drawing last moved left. Kept while idle or moving straight up/down.</summary>
        public bool FacingLeft { get; private set; }

        /// <summary>
        /// The decision rule. Running needs movement to be enabled (a player carried by a screen transition must not
        /// run on the spot) and input above <paramref name="threshold"/>. Pushing (<paramref name="pushing"/>, from
        /// <see cref="BlockPusher.Pushing"/>) wins over running; it is gated by <see cref="BlockPusher"/>'s own input
        /// threshold rather than <paramref name="threshold"/>, so a block that is moving always shows the push. It
        /// still needs movement enabled and some input: the flag is a physics step old, so on release it would
        /// otherwise play a push from a standstill for a frame. (Input redirected along the block face can still show
        /// the push for that one step.) Facing follows the horizontal input only while moving; zero horizontal
        /// input keeps <paramref name="facingLeft"/>.
        /// </summary>
        public static SketchChoice Choose(Vector2 input, bool movementEnabled, bool pushing, float threshold, bool facingLeft)
        {
            var hasInput = movementEnabled && input.sqrMagnitude > 0f;
            var moving = hasInput && input.sqrMagnitude > threshold * threshold;
            var pushingNow = hasInput && pushing;
            if (moving || pushingNow)
            {
                if (input.x < 0f)
                    facingLeft = true;
                else if (input.x > 0f)
                    facingLeft = false;
            }
            var state = pushingNow ? SketchState.Pushing : moving ? SketchState.Running : SketchState.Idle;
            return new SketchChoice(state, facingLeft);
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

            TryGetComponent(out pusher);
        }

        void OnEnable()
        {
            // Re-assert on the next Update: while disabled something else may have changed what plays.
            requested = null;
        }

        void Update()
        {
            var pushing = pusher != null && pusher.Pushing != null;
            var choice = Choose(mover.MoveInput, mover.MovementEnabled, pushing, runInputThreshold, FacingLeft);

            if (requested != choice.State)
            {
                requested = choice.State;
                switch (choice.State)
                {
                    case SketchState.Pushing:
                        // Push is content of the cuttable block feature: with no push drawing, pushing shows the run.
                        animator.Play(push != null ? push : run);
                        break;
                    case SketchState.Running:
                        animator.Play(run);
                        break;
                    default:
                        animator.PlayIdle();
                        break;
                }
            }

            FacingLeft = choice.FacingLeft;
            spriteRenderer.flipX = mirrorToFaceLeft && FacingLeft;
        }
    }
}
