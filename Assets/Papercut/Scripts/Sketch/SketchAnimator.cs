using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Loops a <see cref="SketchAnimation"/> on this object's <see cref="SpriteRenderer"/>. Add it to anything
    /// hand-drawn that should wobble and assign its idle. Other animations are started with <see cref="Play"/>
    /// by whatever owns the object's state - this component never decides what should be playing.
    /// An animation with <see cref="SketchAnimation.SyncToClock"/> on takes its frame from the Desk's
    /// <see cref="SketchClock"/> beat, so every such drawing flips together; one without runs on its own playhead.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SketchAnimator : MonoBehaviour
    {
        [Header("Animation")]
        [SerializeField, Tooltip("Played on start and whenever nothing else is playing.")]
        SketchAnimation idle;

        [SerializeField, Tooltip("Unsynced animations only: start each at a random frame and phase so several such drawings don't wobble in lockstep. " +
            "Synced animations always follow the clock.")]
        bool randomiseStart = true;

        SpriteRenderer spriteRenderer;
        SketchPlayhead playhead;
        bool reportedNoClock;

        /// <summary>
        /// True while the current animation follows the Desk's clock: it asks to be synced and a
        /// <see cref="SketchClock"/> is enabled. Resolved every frame, so a clock that enables after this animator
        /// (scene load order) or is added later is picked up, and one that goes away drops the drawing back to its own rate.
        /// </summary>
        public bool IsSynced => Current != null && Current.SyncToClock && SketchClock.Active != null;

        /// <summary>The animation currently playing, or null if nothing is.</summary>
        public SketchAnimation Current { get; private set; }

        void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (idle == null)
                Debug.LogError($"SketchAnimator on '{name}' has no idle animation.", this);
        }

        void OnEnable()
        {
            if (Current == null)
                PlayIdle();
            else
                ApplyFrame();
        }

        /// <summary>Plays the idle animation. No-op if it is already playing, or if no idle is assigned (Awake reports that).</summary>
        public void PlayIdle()
        {
            if (idle == null)
                return;
            Play(idle);
        }

        /// <summary>
        /// Switches to <paramref name="animation"/> from its first frame (or a random one, see <c>randomiseStart</c>).
        /// No-op if it is already playing. A null or unplayable animation is an error: nothing plays and the
        /// renderer keeps its last drawing.
        /// </summary>
        public void Play(SketchAnimation animation)
        {
            if (animation != null && animation == Current)
                return;

            if (animation == null)
            {
                Debug.LogError($"SketchAnimator on '{name}' was asked to play nothing.", this);
                Current = null;
                return;
            }
            if (!animation.IsPlayable)
            {
                Debug.LogError($"SketchAnimator on '{name}': '{animation.name}' has no frames or an empty frame slot.", this);
                Current = null;
                return;
            }

            Current = animation;
            var frame = 0;
            var elapsed = 0f;
            var clock = animation.SyncToClock ? SketchClock.Active : null;
            if (clock != null)
                frame = SketchClock.FrameOf(clock.Beat, animation.FrameCount);
            else if (randomiseStart)
            {
                // Also the start of a synced animation whose clock has not enabled yet: Update snaps it to the beat.
                frame = Random.Range(0, animation.FrameCount);
                if (animation.FramesPerSecond > 0f)
                    elapsed = Random.value / animation.FramesPerSecond;
            }
            playhead.Reset(frame, elapsed);
            ApplyFrame();
        }

        /// <summary>Reports a synced animation running without a clock, once. Checked from Update, after every OnEnable has run.</summary>
        void ReportNoClock()
        {
            if (reportedNoClock)
                return;
            reportedNoClock = true;
            Debug.LogError($"SketchAnimator on '{name}': '{Current.name}' is synced to the clock but no SketchClock is enabled (put one on the Desk); playing on its own rate.", this);
        }

        void Update()
        {
            if (Current == null)
                return;

            if (!Current.IsPlayable)
            {
                Debug.LogError($"SketchAnimator on '{name}': '{Current.name}' lost its frames while playing; stopping.", this);
                Current = null;
                return;
            }

            var before = playhead.Frame;
            var clock = Current.SyncToClock ? SketchClock.Active : null;
            if (clock != null)
            {
                playhead.Frame = SketchClock.FrameOf(clock.Beat, Current.FrameCount);
            }
            else
            {
                if (Current.SyncToClock)
                    ReportNoClock();
                playhead.Advance(Time.deltaTime, Current.FramesPerSecond, Current.FrameCount);
            }
            if (playhead.Frame != before)
                ApplyFrame();
        }

        void ApplyFrame()
        {
            if (Current == null)
                return;
            // Play may be called by another component's Awake before ours has run.
            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.sprite = Current.GetFrame(playhead.Frame);
        }
    }
}
