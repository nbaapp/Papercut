using UnityEngine;

namespace Papercut
{
    /// <summary>Which of the player's loops should play.</summary>
    public enum SketchState
    {
        Idle,
        Running,
        /// <summary>Holding a block without pressing along its axis.</summary>
        Holding,
        /// <summary>Holding a block and pressing along its axis - pushing or pulling, whether or not it moves.</summary>
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
        public bool Holding => State == SketchState.Holding;
        public bool Pushing => State == SketchState.Pushing;
    }

    /// <summary>
    /// Picks the player's sketch animation - hold while holding a block still, push while pushing or pulling it,
    /// run while moving, the animator's idle otherwise - and mirrors the drawing to face the way the player is
    /// going, or the block they hold. This is the one place that decides what the player's
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

        [SerializeField, Tooltip("Played while the player holds a block without pushing or pulling it (needs a BlockPusher on the player). Optional: left empty, holding shows the idle.")]
        SketchAnimation hold;

        [SerializeField, Tooltip("Played while the player pushes or pulls a held block - pressing along its axis, whether or not it can move (needs a BlockPusher on the player). Optional: left empty, shows the run.")]
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
        /// The decision rule. Movement held (a player carried by a screen transition) is always Idle, facing kept.
        /// While <paramref name="holding"/> a block (<see cref="BlockPusher.Held"/>) the drawing faces it -
        /// <paramref name="heldX"/> is the sign of the direction from the player to the block, left or right; zero
        /// (above or below) keeps the facing - and it is Pushing when <paramref name="heldPressing"/>
        /// (<see cref="BlockPusher.HeldSense"/> ≠ 0: pressing along the block's axis, push or pull, whether or not
        /// the block could move) and Holding otherwise. Pushing also needs some raw input: the pusher's state is a
        /// physics step old, so on release it would otherwise play a push from a standstill for a frame. Otherwise
        /// Running needs input above <paramref name="threshold"/>, with facing from the horizontal input; zero
        /// horizontal input keeps <paramref name="facingLeft"/>. Pushing never arises without holding.
        /// </summary>
        public static SketchChoice Choose(Vector2 input, bool movementEnabled, bool holding, bool heldPressing, float heldX, float threshold, bool facingLeft)
        {
            if (!movementEnabled)
                return new SketchChoice(SketchState.Idle, facingLeft);

            var hasInput = input.sqrMagnitude > 0f;
            if (holding)
            {
                if (heldX < 0f)
                    facingLeft = true;
                else if (heldX > 0f)
                    facingLeft = false;
                return new SketchChoice(heldPressing && hasInput ? SketchState.Pushing : SketchState.Holding, facingLeft);
            }

            var moving = hasInput && input.sqrMagnitude > threshold * threshold;
            if (moving)
            {
                if (input.x < 0f)
                    facingLeft = true;
                else if (input.x > 0f)
                    facingLeft = false;
            }
            return new SketchChoice(moving ? SketchState.Running : SketchState.Idle, facingLeft);
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
            var holding = pusher != null && pusher.Held != null;
            var pressing = holding && pusher.HeldSense != 0;
            var heldX = holding ? pusher.HeldDirection.x : 0f;
            var choice = Choose(mover.MoveInput, mover.MovementEnabled, holding, pressing, heldX, runInputThreshold, FacingLeft);

            if (requested != choice.State)
            {
                requested = choice.State;
                switch (choice.State)
                {
                    case SketchState.Pushing:
                        // Hold and push are content of the cuttable block feature: with no drawing, they fall back.
                        animator.Play(push != null ? push : run);
                        break;
                    case SketchState.Holding:
                        if (hold != null)
                            animator.Play(hold);
                        else
                            animator.PlayIdle();
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
