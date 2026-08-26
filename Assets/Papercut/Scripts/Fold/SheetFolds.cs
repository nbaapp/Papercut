using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    public enum FoldRejection
    {
        None,
        /// <summary>The Flap would cover the player, or the player would lift with it (Bible §4 [LOCKED]).</summary>
        CoversPlayer,
        /// <summary>Only one fold is exposed at a time; unfold first.</summary>
        AlreadyFolded,
        /// <summary>Shallower than the minimum depth: not a fold.</summary>
        TooShallow,
        /// <summary>The player is standing on the landed Flap; unfolding fails.</summary>
        PlayerOnFlap,
        /// <summary>The landed Flap would extend past the sheet (not allowed; see <see cref="FoldGeometry.MaxDepth"/>).</summary>
        Overhangs,
    }

    /// <summary>
    /// The fold state of one Sheet: the committed folds (a list, though only one is exposed — Bible decision #1),
    /// the drag preview, the unfold animation and the creases left behind. Owns the ordering commit → occlusion
    /// → render, and the feel of unfolding.
    /// </summary>
    /// <remarks>
    /// Sheet-local space throughout; callers pass the player's footprint in sheet-local space. Folds reset when
    /// the player leaves the sheet (Bible §8 [LOCKED]) — creases too, per Aaron. Rendering goes through
    /// <see cref="IFoldRenderer"/>; physics through <see cref="SheetOcclusion"/> and <see cref="SheetBoundary"/>,
    /// which listen to <see cref="Changed"/>.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Sheet))]
    public sealed class SheetFolds : MonoBehaviour
    {
        [Header("Commit")]
        [SerializeField, Min(0f), Tooltip("A released drag shallower than this (sheet units) is not a fold and is dropped.")]
        float minDepth = 0.25f;

        [Header("Unfold")]
        [SerializeField, Min(0f), Tooltip("Seconds the Flap takes to swing back flat after unfolding. 0 = instant.")]
        float unfoldDuration = 0.25f;

        [SerializeField, Tooltip("Progress of the retreat over normalised time: x is 0..1 of the duration, y is 0..1 of the way back to flat.")]
        AnimationCurve unfoldEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Creases")]
        [SerializeField, Tooltip("Draw a faint crease where a fold used to be, until the player leaves the sheet.")]
        bool rememberCreases = true;

        readonly List<Fold> folds = new();
        readonly List<Crease> creases = new();
        readonly List<FoldVisual> visuals = new();

        Sheet sheet;
        IFoldRenderer foldRenderer;
        Fold? preview;
        bool previewValid;
        Fold? retreating;
        Coroutine retreat;

        /// <summary>Committed folds. Only one at a time is created by input; the list is the data model.</summary>
        public IReadOnlyList<Fold> Folds => folds;

        public bool IsFolded => folds.Count > 0;

        public Fold? Preview => preview;

        /// <summary>Raised after the committed folds change (commit, unfold, reset).</summary>
        public event Action Changed;

        void Awake()
        {
            sheet = GetComponent<Sheet>();
            foldRenderer = GetComponent<IFoldRenderer>(); // A missing renderer is reported by Sheet.
        }

        void OnEnable()
        {
            sheet.PlayerLeft += Reset;
        }

        void OnDisable()
        {
            sheet.PlayerLeft -= Reset;
        }

        void Start()
        {
            Draw();
        }

        public bool IsValid(Fold fold, Rect playerLocal) => FoldValidity.IsValid(fold, playerLocal);

        /// <summary>Shows <paramref name="fold"/> as a drag preview (or clears it with null), depth clamped to the sheet. Visual only.</summary>
        public void SetPreview(Fold? fold, Rect playerLocal)
        {
            var clamped = fold?.Clamped;
            preview = clamped;
            previewValid = clamped.HasValue && IsValid(clamped.Value, playerLocal);
            Draw();
        }

        /// <summary>Makes <paramref name="fold"/> real: physics and occlusion follow immediately.</summary>
        public bool TryCommit(Fold fold, Rect playerLocal, out FoldRejection rejection)
        {
            rejection = IsFolded ? FoldRejection.AlreadyFolded
                : fold.Depth < minDepth ? FoldRejection.TooShallow
                : fold.Overhangs ? FoldRejection.Overhangs
                : !IsValid(fold, playerLocal) ? FoldRejection.CoversPlayer
                : FoldRejection.None;
            if (rejection != FoldRejection.None)
                return false;

            StopRetreat();
            folds.Add(fold);
            Changed?.Invoke();
            Draw();
            return true;
        }

        /// <summary>
        /// Unfolds if <paramref name="sheetLocal"/> is within <paramref name="grabDistance"/> of a fold's crease or
        /// Seam (where the Flap's edge meets the Front) and the player is not standing on that Flap. False with the
        /// reason otherwise.
        /// </summary>
        public bool TryUnfoldAt(Vector2 sheetLocal, Rect playerLocal, float grabDistance, out FoldRejection rejection)
        {
            rejection = FoldRejection.None;
            for (int i = folds.Count - 1; i >= 0; i--)
            {
                var distance = Mathf.Min(FoldGeometry.DistanceToCrease(folds[i], sheetLocal), FoldGeometry.DistanceToSeam(folds[i], sheetLocal));
                if (distance > grabDistance)
                    continue;
                if (FoldValidity.PlayerOverlapsFlap(folds[i], playerLocal))
                {
                    rejection = FoldRejection.PlayerOnFlap;
                    return false;
                }
                Unfold(i);
                return true;
            }
            return false;
        }

        /// <summary>Removes every fold (no animation) and forgets creases and preview. Called when the player leaves.</summary>
        public void Reset()
        {
            StopRetreat();
            var hadFolds = folds.Count > 0;
            folds.Clear();
            creases.Clear();
            preview = null;
            if (hadFolds)
                Changed?.Invoke();
            Draw();
        }

        void Unfold(int index)
        {
            var fold = folds[index];
            folds.RemoveAt(index);
            if (rememberCreases)
                creases.Add(FoldGeometry.CreaseOf(fold));
            Changed?.Invoke();

            StopRetreat();
            if (unfoldDuration > 0f)
                retreat = StartCoroutine(Retreat(fold));
            else
                Draw();
        }

        IEnumerator Retreat(Fold fold)
        {
            var elapsed = 0f;
            while (elapsed < unfoldDuration)
            {
                elapsed += Time.deltaTime;
                var t = unfoldEase.Evaluate(Mathf.Clamp01(elapsed / unfoldDuration));
                retreating = fold.WithDepth(Mathf.LerpUnclamped(fold.Depth, 0f, t));
                Draw();
                yield return null;
            }
            retreating = null;
            retreat = null;
            Draw();
        }

        void StopRetreat()
        {
            if (retreat != null)
                StopCoroutine(retreat);
            retreat = null;
            retreating = null;
        }

        void Draw()
        {
            if (foldRenderer == null)
                return;

            visuals.Clear();
            foreach (var fold in folds)
                visuals.Add(new FoldVisual(fold, FoldVisualState.Committed));
            if (retreating.HasValue && retreating.Value.Depth > 0f)
                visuals.Add(new FoldVisual(retreating.Value, FoldVisualState.Retreating));
            if (preview.HasValue && preview.Value.Depth > 0f)
                visuals.Add(new FoldVisual(preview.Value, previewValid ? FoldVisualState.Preview : FoldVisualState.PreviewInvalid));

            foldRenderer.Draw(new FoldDisplay(visuals, creases));
        }
    }
}
