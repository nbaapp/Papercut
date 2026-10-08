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
        /// <summary>Multiple folds are off and the sheet is already folded; unfold first.</summary>
        AlreadyFolded,
        /// <summary>Shallower than the minimum depth: not a fold.</summary>
        TooShallow,
        /// <summary>The player is standing on the landed Flap; unfolding fails.</summary>
        PlayerOnFlap,
        /// <summary>Some landed piece would extend past the sheet (not allowed; see <see cref="SheetLayers.MaxDepth"/>).</summary>
        Overhangs,
        /// <summary>No part of the sheet lies on the Flap side of the crease.</summary>
        NothingToFold,
        /// <summary>The fold would lift or land on another fold's Flap, and stacking is off. A drag holds short of one (<see cref="SheetFolds.MaxDepth"/>); this is the safety net.</summary>
        OverlapsFold,
        /// <summary>A later fold lies on or through this one; unfold that first.</summary>
        CoveredByLaterFold,
        /// <summary>An object riding the Flap hangs past its edge; unfolding would carry it off the sheet (an <see cref="IFoldConstraint"/>).</summary>
        ObjectOnEdge,
        /// <summary>The Flap would lift or land on an <see cref="IFoldObstacle"/>. A drag holds short of one (<see cref="SheetFolds.MaxDepth"/>); this is the safety net.</summary>
        CoversObstacle,
        /// <summary>A <see cref="Paperweight"/> rests on the landed Flap; a weight is always on top of the sheet, so push it off first (an <see cref="IFoldConstraint"/>).</summary>
        PaperweightOnFlap,
        /// <summary>The Flap would carry a block under a universal wall that stops blocks (an <see cref="IFoldConstraint"/>): the block would end inside the wall, so the fold is refused like one that covers the player (Aaron, 2026-09-28).</summary>
        CarriesBlockUnderWall,
    }

    /// <summary>
    /// The fold state of one Sheet: the committed folds in order (the data model — Bible decision #1), the stack
    /// of layers they produce (<see cref="Layers"/>), the drag preview, the unfold animation and the creases left
    /// behind. Owns the ordering commit → occlusion → render, the two playtest toggles (multiple folds,
    /// stacking) and the feel of unfolding.
    /// </summary>
    /// <remarks>
    /// Sheet-local space throughout; callers pass the player's footprint in sheet-local space. Folds reset when
    /// the player leaves the sheet (Bible §8 [LOCKED]) — creases too, per Aaron. Rendering goes through
    /// <see cref="IFoldRenderer"/>; physics through <see cref="SheetOcclusion"/> and <see cref="SheetBoundary"/>,
    /// which listen to <see cref="Changed"/> and read <see cref="Layers"/>. Unfolding is by the Seam only —
    /// where the tape would go (Aaron, 2026-08-26); a press on a crease grabs that fold line to fold it further
    /// (<see cref="TryGrabCreaseAt"/>). Fold obstacles under the sheet (<see cref="IFoldObstacle"/>; Aaron,
    /// 2026-09-14) hold a drag short of them through <see cref="MaxDepth"/> and, as a safety net, refuse a fold
    /// that would reach them; with stacking off the other folds' Flaps hold a drag the same way (Aaron,
    /// 2026-09-16); unfold constraints (<see cref="IFoldConstraint"/>) are asked before an unfold.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Sheet))]
    public sealed class SheetFolds : MonoBehaviour
    {
        [Header("Multiple folds")]
        [SerializeField, Tooltip("Allow more than one fold on the sheet at a time. Off: the sheet must be unfolded before it can be folded again.")]
        bool allowMultipleFolds = true;

        [SerializeField, Tooltip("Allow a fold to lift or land on another fold's Flap (the Flap side of the crease is folded over, however many " +
            "layers deep). Off: every fold must be independent of the others - its lifted and landed regions may not touch theirs, " +
            "and a drag stops where the Flap would first touch another fold, like at a paperweight. Ignored when multiple folds are off.")]
        bool allowStacking = true;

        [Header("Commit")]
        [SerializeField, Min(0f), Tooltip("A released drag shallower than this (sheet units) is not a fold and is dropped.")]
        float minDepth = 0.25f;

        [Header("Unfold")]
        [SerializeField, Min(0f), Tooltip("Seconds the Flap takes to swing back flat after unfolding. 0 = instant.")]
        float unfoldDuration = 0.25f;

        [SerializeField, Tooltip("Progress of the retreat over normalised time: x is 0..1 of the duration, y is 0..1 of the way back to flat.")]
        AnimationCurve unfoldEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Creases")]
        [SerializeField, Tooltip("Draw a faint crease where a fold used to be - one undone by the player or by the sheet unfolding when the player leaves. Creases stay for the play session.")]
        bool rememberCreases = true;

        readonly List<Fold> folds = new();
        readonly List<FoldEffect> effects = new();
        readonly List<CreaseMark> remembered = new();
        readonly List<FoldVisual> visuals = new();
        readonly List<IFoldConstraint> constraints = new();
        readonly List<IFoldObstacle> obstacles = new();
        readonly List<ConvexPolygon> obstaclePieces = new();
        readonly List<ConvexPolygon> foldPieces = new(); // Every committed fold's lifted and landed pieces: what a new fold must stay clear of with stacking off.

        Sheet sheet;
        IFoldRenderer foldRenderer;
        SheetLayers layers = SheetLayers.Flat; // Valid before Start, whatever the execution order of the readers.
        Fold? preview;
        bool previewValid;
        Fold? retreating;
        Coroutine retreat;

        /// <summary>Committed folds, in the order they apply.</summary>
        public IReadOnlyList<Fold> Folds => folds;

        /// <summary>What each committed fold did, index-aligned with <see cref="Folds"/>.</summary>
        public IReadOnlyList<FoldEffect> Effects => effects;

        /// <summary>The sheet as it lies after every committed fold.</summary>
        public SheetLayers Layers => layers;

        /// <summary>Creases left by folds that have been undone, oldest first. Kept for the play session.</summary>
        public IReadOnlyList<CreaseMark> RememberedCreases => remembered;

        /// <summary>True if a new fold may be started now (always, unless multiple folds are off and one exists).</summary>
        public bool CanStartFold => allowMultipleFolds || folds.Count == 0;

        /// <summary>Raised after the committed folds change (commit, unfold, reset).</summary>
        public event Action Changed;

        /// <summary>
        /// Raised after the sheet is drawn - every frame of a drag preview or an unfold retreat as well as on every
        /// commit - so content drawn by the main camera (a <see cref="PushableBlock"/>) can follow the displayed
        /// Flap (<see cref="DisplayLayers"/>) rather than pop into place on commit. Physics stays on <see cref="Layers"/>.
        /// </summary>
        public event Action Displayed;

        SheetLayers displayLayers;
        bool displayIsCommitted = true;
        readonly List<FoldEffect> displayEffects = new();

        /// <summary>The sheet as it is currently drawn: <see cref="Layers"/> plus any retreating or previewed fold on top.</summary>
        public SheetLayers DisplayLayers => displayLayers ?? layers;

        /// <summary>True when what is drawn is exactly the committed folds (no preview, no retreat): layer indices agree with <see cref="Layers"/>.</summary>
        public bool DisplayIsCommitted => displayIsCommitted;

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

        /// <summary>
        /// The largest depth a fold from <paramref name="anchor"/> can have now: no overhang, the Flap short of
        /// every fold obstacle (Aaron, 2026-09-14: the drag "just can't stretch beyond that point") and, with
        /// stacking off, short of every other fold's Flap and of the sheet it lifted away (Aaron, 2026-09-16: the
        /// Flap "stops in place when it bumps into another flap", so two Flaps can be laid directly side by side
        /// and walked across). Zero when that bound is below the minimum commit depth, so the edge is inert
        /// rather than lifting a sliver that would drop on release.
        /// </summary>
        public float MaxDepth(FoldAnchor anchor)
        {
            GatherObstacles();
            return MaxDepthGathered(anchor);
        }

        float MaxDepthGathered(FoldAnchor anchor)
        {
            var depth = Mathf.Min(layers.MaxDepth(anchor), FoldObstacles.MaxDepth(anchor, layers, obstaclePieces));
            if (!allowStacking)
                depth = Mathf.Min(depth, FoldObstacles.MaxDepth(anchor, layers, foldPieces));
            return depth < minDepth ? 0f : depth;
        }

        /// <summary>
        /// Why <paramref name="fold"/> cannot be made on the sheet as it lies, or <see cref="FoldRejection.None"/>.
        /// <paramref name="effect"/> is what the fold would do (meaningful whatever the answer).
        /// </summary>
        public FoldRejection Evaluate(Fold fold, Rect playerLocal, out FoldEffect effect)
        {
            GatherObstacles();
            return EvaluateGathered(fold, playerLocal, out effect);
        }

        /// <summary><see cref="Evaluate"/> against the obstacles as last gathered (one gather per drag frame).</summary>
        FoldRejection EvaluateGathered(Fold fold, Rect playerLocal, out FoldEffect effect)
        {
            var after = layers.Apply(fold, folds.Count, out effect);
            if (!CanStartFold)
                return FoldRejection.AlreadyFolded;
            if (effect.Outcome == FoldOutcome.NothingToFold)
                return FoldRejection.NothingToFold;
            if (effect.Outcome == FoldOutcome.Overhangs)
                return FoldRejection.Overhangs;
            if (!FoldValidity.IsValid(effect, playerLocal))
                return FoldRejection.CoversPlayer;
            if (!FoldObstacles.Clear(effect, obstaclePieces))
                return FoldRejection.CoversObstacle;
            foreach (var constraint in constraints)
            {
                var refusal = constraint.RefuseFold(effect, after, folds.Count);
                if (refusal != FoldRejection.None)
                    return refusal;
            }
            if (!allowStacking)
            {
                foreach (var committed in effects)
                {
                    if (!FoldValidity.Independent(effect, committed))
                        return FoldRejection.OverlapsFold;
                }
            }
            return FoldRejection.None;
        }

        /// <summary>
        /// Shows <paramref name="fold"/> as a drag preview (or clears it with null), depth limited to
        /// <see cref="MaxDepth"/>. Visual only. A fold that lifts nothing shows nothing, and is not red.
        /// </summary>
        public void SetPreview(Fold? fold, Rect playerLocal)
        {
            GatherObstacles();
            preview = fold.HasValue ? fold.Value.WithDepth(Mathf.Min(fold.Value.Depth, MaxDepthGathered(fold.Value.Anchor))) : null;
            previewValid = true;
            if (preview.HasValue)
            {
                var rejection = EvaluateGathered(preview.Value, playerLocal, out _);
                previewValid = rejection == FoldRejection.None || rejection == FoldRejection.NothingToFold;
            }
            Draw();
        }

        /// <summary>Makes <paramref name="fold"/> real: physics and occlusion follow immediately.</summary>
        public bool TryCommit(Fold fold, Rect playerLocal, out FoldRejection rejection)
        {
            rejection = fold.Depth < minDepth ? FoldRejection.TooShallow : Evaluate(fold, playerLocal, out _);
            if (rejection != FoldRejection.None)
                return false;

            StopRetreat();
            folds.Add(fold);
            Replay();
            Changed?.Invoke();
            Draw();
            return true;
        }

        /// <summary>
        /// Unfolds the latest unpinned fold whose Seam (where the landed Flap's edge meets what it lies on) is
        /// within <paramref name="grabDistance"/> of <paramref name="sheetLocal"/>. False with
        /// <see cref="FoldRejection.PlayerOnFlap"/> if the player stands on that fold; false with
        /// <see cref="FoldRejection.CoveredByLaterFold"/> if the only Seams near are of pinned folds — their
        /// Seams are where they were at commit time and may since have been folded over, so the caller should
        /// treat that as advice, not a hit, and go on to try a crease or edge grab; false with
        /// <see cref="FoldRejection.None"/> if no Seam is near; false with what an <see cref="IFoldConstraint"/> under the sheet
        /// answers (e.g. <see cref="FoldRejection.ObjectOnEdge"/>, <see cref="FoldRejection.PaperweightOnFlap"/>) if one refuses.
        /// </summary>
        public bool TryUnfoldAt(Vector2 sheetLocal, Rect playerLocal, float grabDistance, out FoldRejection rejection)
        {
            var index = FoldHitTest.SeamAt(effects, sheetLocal, grabDistance, playerLocal, out rejection);
            if (index < 0)
                return false;
            constraints.Clear();
            GetComponentsInChildren(false, constraints);
            foreach (var constraint in constraints)
            {
                rejection = constraint.RefuseUnfold(index);
                if (rejection != FoldRejection.None)
                    return false;
            }
            Unfold(index);
            return true;
        }

        /// <summary>
        /// The committed fold whose crease is within <paramref name="grabDistance"/> of <paramref name="sheetLocal"/>
        /// and is not pinned by a later fold, latest first: a press there grabs that fold line to fold it further.
        /// </summary>
        public bool TryGrabCreaseAt(Vector2 sheetLocal, float grabDistance, out Fold fold)
        {
            var index = FoldHitTest.CreaseAt(effects, sheetLocal, grabDistance);
            fold = index >= 0 ? folds[index] : default;
            return index >= 0;
        }

        /// <summary>
        /// Removes every fold (no animation) and the preview. Called when the player leaves. Creases persist (Aaron,
        /// 2026-10-07): the folds undone here leave their creases like any unfold, and earlier ones stay.
        /// </summary>
        public void Reset()
        {
            StopRetreat();
            var hadFolds = folds.Count > 0;
            if (rememberCreases)
            {
                foreach (var effect in effects)
                    remembered.AddRange(effect.CreaseMarks);
            }
            folds.Clear();
            preview = null;
            Replay();
            if (hadFolds)
                Changed?.Invoke();
            Draw();
        }

        /// <summary>
        /// Where every active fold obstacle under the sheet is right now (sheet-local pieces). Asked fresh each time
        /// rather than cached: a block moves between drags, and the cost is a few polygons per drag frame on one sheet.
        /// </summary>
        void GatherObstacles()
        {
            obstacles.Clear();
            obstaclePieces.Clear();
            GetComponentsInChildren(false, obstacles);
            foreach (var obstacle in obstacles)
                obstacle.AddObstaclePieces(obstaclePieces);
            // The fold constraints are asked per evaluation too (a block may be carried under a universal wall).
            constraints.Clear();
            GetComponentsInChildren(false, constraints);
        }

        void Replay()
        {
            layers = SheetLayers.Flat;
            effects.Clear();
            foldPieces.Clear();
            for (int i = 0; i < folds.Count; i++)
            {
                layers = layers.Apply(folds[i], i, out var effect);
                effects.Add(effect);
                foldPieces.AddRange(effect.Lifted);
                foldPieces.AddRange(effect.Landed);
            }
        }

        void Unfold(int index)
        {
            var fold = folds[index];
            if (rememberCreases)
                remembered.AddRange(effects[index].CreaseMarks);
            folds.RemoveAt(index);
            Replay();
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
            // Display order = apply order. The retreating fold was independent of every fold after it (or it could
            // not have been unfolded), and independent folds commute, so replaying it last is exact at full depth;
            // at intermediate depths it is drawn on top, visual only. A preview begun during a retreat is likewise
            // drawn over the retreating fold but was evaluated against the committed stack alone.
            visuals.Clear();
            foreach (var fold in folds)
                visuals.Add(new FoldVisual(fold, FoldVisualState.Committed));
            if (retreating.HasValue && retreating.Value.Depth > 0f)
                visuals.Add(new FoldVisual(retreating.Value, FoldVisualState.Retreating));
            if (preview.HasValue && preview.Value.Depth > 0f)
                visuals.Add(new FoldVisual(preview.Value, previewValid ? FoldVisualState.Preview : FoldVisualState.PreviewInvalid));

            var stack = SheetLayers.Flat;
            displayEffects.Clear();
            for (int i = 0; i < visuals.Count; i++)
            {
                stack = stack.Apply(visuals[i].Fold, i, out var effect);
                displayEffects.Add(effect);
            }
            displayLayers = stack;
            displayIsCommitted = visuals.Count == folds.Count;

            foldRenderer?.Draw(new FoldDisplay(visuals, remembered, stack, displayEffects));
            Displayed?.Invoke();
        }
    }
}
