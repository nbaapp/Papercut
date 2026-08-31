using System;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// One loop of hand-drawn frames: the sketchy "wobble" that anything drawn on a sheet cycles through.
    /// Pure data; <see cref="SketchAnimator"/> plays it.
    /// </summary>
    /// <remarks>
    /// Create via Assets > Create > Papercut > Sketch Animation, drag the drawings into <c>frames</c> in loop order,
    /// and assign the asset to a <see cref="SketchAnimator"/>.
    /// </remarks>
    [CreateAssetMenu(menuName = "Papercut/Sketch Animation", fileName = "New Sketch Animation")]
    public sealed class SketchAnimation : ScriptableObject
    {
        [SerializeField, Tooltip("Hand-drawn frames, in loop order. Two or more give the sketch wobble; one is a static drawing.")]
        Sprite[] frames = Array.Empty<Sprite>();

        [SerializeField, Min(0f), Tooltip("How fast the drawing cycles through its frames. 0 holds the first frame.")]
        float framesPerSecond = 6f;

        public int FrameCount => frames.Length;

        public float FramesPerSecond => framesPerSecond;

        /// <summary>True when there is at least one frame and every frame slot is filled.</summary>
        public bool IsPlayable
        {
            get
            {
                if (frames.Length == 0)
                    return false;
                foreach (var frame in frames)
                    if (frame == null)
                        return false;
                return true;
            }
        }

        public Sprite GetFrame(int index) => frames[index];
    }

    /// <summary>
    /// Where a <see cref="SketchAnimation"/> playback is: the current frame and how far into it, in seconds.
    /// Pure timing so it can be tested without a scene. The frame rate is passed in on every advance so a live
    /// change to the asset takes effect without resetting.
    /// </summary>
    public struct SketchPlayhead
    {
        public int Frame;

        /// <summary>Seconds elapsed within the current frame.</summary>
        public float Elapsed;

        public void Reset(int frame, float elapsedSeconds)
        {
            Frame = frame;
            Elapsed = elapsedSeconds;
        }

        /// <summary>
        /// Moves the playhead forward by <paramref name="deltaSeconds"/>. A non-positive frame rate holds the
        /// current frame. A delta spanning several frames advances several frames without drift.
        /// </summary>
        public void Advance(float deltaSeconds, float framesPerSecond, int frameCount)
        {
            if (frameCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "Need at least one frame.");

            // The frame list may have shrunk since the last advance.
            Frame = ((Frame % frameCount) + frameCount) % frameCount;

            if (framesPerSecond <= 0f)
            {
                Elapsed = 0f;
                return;
            }

            var secondsPerFrame = 1f / framesPerSecond;
            Elapsed += deltaSeconds;
            if (Elapsed < secondsPerFrame)
                return;

            // Step all whole frames at once: O(1) whatever the rate, and no loop that could fail to
            // terminate when a period is smaller than float precision on Elapsed. The remainder comes
            // from IEEE '%', which is exact, so Elapsed always lands in [0, secondsPerFrame) - subtracting
            // steps * secondsPerFrame would leave rounding noise larger than a tiny period. The whole-frame
            // count is kept in double so an absurd delta / rate cannot overflow an int and push Frame negative.
            var steps = Math.Floor((double)Elapsed / secondsPerFrame);
            Elapsed %= secondsPerFrame;
            Frame = (int)((Frame + steps % frameCount) % frameCount);
        }
    }
}
