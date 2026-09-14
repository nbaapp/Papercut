using System;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The one beat every synced <see cref="SketchAnimation"/> flips on, so the whole Desk boils at once rather than
    /// each drawing wobbling on its own clock (Aaron, 2026-09-03: <em>"it should look like the entire page is boiling
    /// at the same time, even between different objects"</em>; a single shared beat, not per-drawing rates with a
    /// shared start). Lives on the Desk; <see cref="SketchAnimator"/> reads it for animations with
    /// <see cref="SketchAnimation.SyncToClock"/> on.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)] // Enabled before the drawings that read it; they also re-resolve it every frame, so order is a nicety, not a requirement.
    public sealed class SketchClock : MonoBehaviour
    {
        [Header("Beat")]
        [SerializeField, Min(0f), Tooltip("How many times per second every synced drawing on the Desk flips to its next frame - the whole page boils " +
            "on this one beat. Unsynced animations keep their own Frames Per Second. 0 holds every synced drawing on its first frame.")]
        float framesPerSecond = 3f;

        static SketchClock active;

        /// <summary>The enabled clock, or null if the scene has none.</summary>
        public static SketchClock Active => active;

        public float FramesPerSecond => framesPerSecond;

        /// <summary>Beats since the game started. Scaled time, so pausing the game holds the boil.</summary>
        public int Beat => BeatAt(Time.time, framesPerSecond);

        /// <summary>The beat count at <paramref name="seconds"/> for a beat rate; 0 forever at a rate of 0. Pure.</summary>
        public static int BeatAt(float seconds, float framesPerSecond)
            => framesPerSecond > 0f ? (int)Math.Floor((double)seconds * framesPerSecond) : 0;

        /// <summary>The frame an animation of <paramref name="frameCount"/> frames shows on <paramref name="beat"/>. Pure.</summary>
        public static int FrameOf(int beat, int frameCount)
        {
            if (frameCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "Need at least one frame.");
            return ((beat % frameCount) + frameCount) % frameCount;
        }

        void OnEnable()
        {
            if (active != null && active != this)
                Debug.LogError($"Two SketchClocks are enabled ('{active.name}' and '{name}'); synced drawings follow '{name}' from now on. Keep one, on the Desk.", this);
            active = this;
        }

        void OnDisable()
        {
            if (active == this)
                active = null;
        }
    }
}
