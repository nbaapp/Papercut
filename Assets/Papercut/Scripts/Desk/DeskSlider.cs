using System.Collections;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Slides the Desk's sheet grid so that a Sheet sits under the view. Changing Screen moves the
    /// sheets, not the camera (Bible §7): the camera, the Desk, and its surface stay where they are.
    /// </summary>
    /// <remarks>
    /// Lives on the Desk alongside <see cref="Desk"/>. Only <see cref="Desk.SheetGrid"/> moves; everything
    /// under it (sheets, their content, the player) rides along as children. The grid carries many static
    /// colliders (sheet boundaries, terrain, exits) on a moving transform, which Unity re-syncs each physics
    /// step during a slide - cheap at this scale. Do not "fix" that by giving the grid a Rigidbody2D: it would
    /// compound every sheet collider onto one body and change <c>attachedRigidbody</c> for every trigger.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Desk))]
    public sealed class DeskSlider : MonoBehaviour
    {
        [SerializeField, Tooltip("Transform whose X/Y is where the current Screen is centred. Normally the Main Camera.")]
        Transform view;

        [Header("Slide")]
        [SerializeField, Min(0f), Tooltip("Seconds the slide between screens takes.")]
        float slideDuration = 0.4f;

        [SerializeField, Tooltip("Progress of the slide over normalised time: x is 0..1 of the duration, y is 0..1 of the distance.")]
        AnimationCurve slideEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        Desk desk;
        Coroutine slide;
        bool sliding;
        Vector3 slideTarget;

        public bool IsSliding => sliding;

        void Awake()
        {
            desk = GetComponent<Desk>();
            if (view == null)
                Debug.LogError("DeskSlider has no view transform assigned.", this);
        }

        void OnDisable()
        {
            // Finish rather than abandon: anyone waiting on IsSliding sees the slide complete.
            if (!sliding)
                return;
            CancelSlide();
            desk.SheetGrid.position = slideTarget;
        }

        /// <summary>Snaps the sheet grid so <paramref name="sheet"/> is under the view, cancelling any slide in progress.</summary>
        public void Centre(Sheet sheet)
        {
            if (!TryGetTarget(sheet, out var grid, out var target))
                return;
            CancelSlide();
            grid.position = target;
        }

        /// <summary>
        /// Slides the sheet grid so <paramref name="sheet"/> ends up under the view. Wait on <see cref="IsSliding"/>;
        /// it is false immediately if the slide cannot start.
        /// </summary>
        public void SlideTo(Sheet sheet)
        {
            if (!TryGetTarget(sheet, out var grid, out var target))
                return;
            CancelSlide();
            slideTarget = target;
            // Flag before starting: a zero-duration routine finishes inside StartCoroutine.
            sliding = true;
            slide = StartCoroutine(SlideRoutine(grid, target));
        }

        bool TryGetTarget(Sheet sheet, out Transform grid, out Vector3 target)
        {
            grid = desk.SheetGrid;
            target = default;
            if (sheet == null)
            {
                Debug.LogError("DeskSlider was asked to centre a null sheet.", this);
                return false;
            }
            if (view == null || grid == null)
            {
                Debug.LogError("DeskSlider cannot move the sheet grid: view or Desk.SheetGrid is missing.", this);
                return false;
            }

            var offset = (Vector2)view.position - sheet.Centre;
            target = grid.position + new Vector3(offset.x, offset.y, 0f);
            return true;
        }

        void CancelSlide()
        {
            sliding = false;
            if (slide == null)
                return;
            StopCoroutine(slide);
            slide = null;
        }

        IEnumerator SlideRoutine(Transform grid, Vector3 target)
        {
            var start = grid.position;
            var duration = slideDuration;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = slideEase.Evaluate(Mathf.Clamp01(elapsed / duration));
                grid.position = Vector3.LerpUnclamped(start, target, t);
                yield return null;
            }
            grid.position = target;
            sliding = false;
        }
    }
}
