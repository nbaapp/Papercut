using System;
using System.Collections.Generic;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// The fold-preview pane's fold stack: committed preview folds in order, replayed through the same pure
    /// runtime pieces the game uses (<see cref="SheetLayers"/>, <see cref="FoldValidity"/>,
    /// <see cref="FoldHitTest"/>), plus the drag preview and the test-player ghost. Editor state only —
    /// nothing here is ever written to the prefab (folds do not persist, Bible §8 [LOCKED]).
    /// </summary>
    /// <remarks>
    /// Rule scope, per Aaron (2026-08-31): the preview ignores the sheet's allowMultipleFolds playtest toggle
    /// (any number of folds previews), so unlike <c>SheetFolds.Evaluate</c> there is no AlreadyFolded refusal
    /// here — if that method's semantics change, revisit <see cref="Evaluate"/>. The allowStacking toggle
    /// <em>is</em> mirrored (<see cref="AllowStacking"/>; Aaron, 2026-09-16, when the game's stacking-off drag
    /// became a clamp): off, the other folds' lifted and landed pieces hold the drag (<see cref="MaxDepth"/>),
    /// refuse a live fold as a safety net (<see cref="FoldRejection.OverlapsFold"/>) and guard the list edits,
    /// exactly like paperweights. Geometric truths still hold (no overhang, nothing-to-fold), and the [LOCKED]
    /// covers-player rule applies through the ghost — to live actions only (new folds, seam unfolds), never
    /// to fold-list trial replays, because in-game the player moves between folds. Fold obstacles (the sheet's
    /// paperweights, given by <see cref="SetObstacles"/> as authored) are different: the editor cannot move them
    /// between folds, so they hold the drag (<see cref="MaxDepth"/>), refuse a live fold
    /// (<see cref="FoldRejection.CoversObstacle"/>) <em>and</em> guard the list edits, exactly like the game's
    /// <c>SheetFolds</c>. Stated limit: in the game a pushable paperweight may be pushed away between folds, so
    /// the list can refuse a stack the game could reach after a push; the editor shows only stacks it can vouch for.
    /// </remarks>
    public sealed class StudioFoldModel
    {
        /// <summary>The player stand-in when the ghost is off: far off-sheet, so no rule ever fires.</summary>
        static readonly Rect FarAway = new(1e6f, 1e6f, 0.1f, 0.1f);

        readonly List<Fold> folds = new();
        readonly List<FoldEffect> effects = new();
        readonly List<FoldEffect> displayEffects = new();
        readonly List<(Vector2 a, Vector2 b)> rememberedCreases = new();
        readonly List<(FaceFootprint footprint, SheetFace face)> obstacles = new();
        readonly List<ConvexPolygon> obstaclePieces = new();
        readonly List<(Rect flatRect, SheetFace side)> blocks = new();
        readonly List<ConvexPolygon> blockWalls = new();
        readonly List<ConvexPolygon> foldPieces = new(); // The committed folds' lifted and landed pieces (mirrors SheetFolds).

        SheetLayers layers = SheetLayers.Flat;
        SheetLayers displayLayers = SheetLayers.Flat;
        Fold? preview;
        bool previewValid = true;
        Rect? ghost;
        Vector2 ghostSize = new(0.5f, 0.5f);

        /// <summary>Raised whenever anything that affects the composite or overlays changes.</summary>
        public event Action Changed;

        public IReadOnlyList<Fold> Folds => folds;

        /// <summary>Effects of the committed folds, index-aligned with <see cref="Folds"/>.</summary>
        public IReadOnlyList<FoldEffect> Effects => effects;

        /// <summary>The sheet as it lies after the committed folds.</summary>
        public SheetLayers Layers => layers;

        /// <summary>The sheet as drawn: committed folds plus any drag preview on top (mirrors SheetFolds.Draw).</summary>
        public SheetLayers DisplayLayers => displayLayers;

        /// <summary>Effects of the displayed folds; indices ≥ <see cref="Folds"/>.Count belong to the preview.</summary>
        public IReadOnlyList<FoldEffect> DisplayEffects => displayEffects;

        public Fold? Preview => preview;

        /// <summary>False while the dragged fold would be refused (covers the ghost) — the red tint.</summary>
        public bool PreviewValid => previewValid;

        /// <summary>The test-player footprint in sheet-local space, or null while the ghost is off.</summary>
        public Rect? Ghost => ghost;

        /// <summary>Desk-space crease segments captured when folds were unfolded/removed, drawn faint.</summary>
        public IReadOnlyList<(Vector2 a, Vector2 b)> RememberedCreases => rememberedCreases;

        /// <summary>
        /// The sheet's authored minimum fold depth, mirrored from settings by the window (like
        /// <see cref="RememberCreasesEnabled"/>): an obstacle bound below it makes the anchor inert, as in the game.
        /// </summary>
        public float MinDepth { get; set; }

        /// <summary>The obstacles' face-up pieces on the committed stack, sheet-local (test seam).</summary>
        public IReadOnlyList<ConvexPolygon> ObstaclePieces => obstaclePieces;

        /// <summary>
        /// The sheet's authored stacking toggle, mirrored from settings by the window (like <see cref="MinDepth"/>).
        /// Off: a new fold must be independent of every committed fold, and the drag holds short of them.
        /// </summary>
        public bool AllowStacking { get; set; } = true;

        /// <summary>
        /// The largest depth a fold from <paramref name="anchor"/> can have now: no overhang, short of every
        /// obstacle and, with stacking off, short of every other fold; 0 when that bound is below
        /// <see cref="MinDepth"/> (mirrors <c>SheetFolds.MaxDepth</c>).
        /// </summary>
        public float MaxDepth(FoldAnchor anchor)
        {
            var depth = Mathf.Min(layers.MaxDepth(anchor), FoldObstacles.MaxDepth(anchor, layers, obstaclePieces));
            if (!AllowStacking)
                depth = Mathf.Min(depth, FoldObstacles.MaxDepth(anchor, layers, foldPieces));
            return depth < MinDepth ? 0f : depth;
        }

        /// <summary>
        /// The sheet's fold obstacles as authored (each footprint in its face's space). Where they lie on the
        /// folded sheet is recomputed with every stack change through <see cref="SheetLayers.Coverage"/> — the
        /// runtime's own rule, which equals a block's visible pieces while nothing has been pushed. A call with the
        /// same obstacles as before changes nothing (unrelated edits re-gather too).
        /// </summary>
        public void SetObstacles(IReadOnlyList<(FaceFootprint footprint, SheetFace face)> authored)
        {
            if (SameObstacles(authored))
                return;
            obstacles.Clear();
            obstacles.AddRange(authored);
            RebuildObstaclePieces();
            Changed?.Invoke();
        }

        bool SameObstacles(IReadOnlyList<(FaceFootprint footprint, SheetFace face)> authored)
        {
            if (authored.Count != obstacles.Count)
                return false;
            for (int i = 0; i < authored.Count; i++)
            {
                var (footprint, face) = authored[i];
                var (current, currentFace) = obstacles[i];
                if (face != currentFace || footprint.Pieces.Count != current.Pieces.Count)
                    return false;
                for (int p = 0; p < footprint.Pieces.Count; p++)
                {
                    var a = footprint.Pieces[p].Vertices;
                    var b = current.Pieces[p].Vertices;
                    if (a.Count != b.Count)
                        return false;
                    for (int v = 0; v < a.Count; v++)
                        if (a[v] != b[v]) return false;
                }
            }
            return true;
        }

        void RebuildObstaclePieces() => ObstaclePiecesOn(layers, obstaclePieces);

        /// <summary>
        /// The sheet's blocks as authored (flat Front-space rect plus the face they are on) and the universal walls
        /// that stop blocks (sheet-space pieces), for the game's carried-under-a-wall refusal
        /// (<see cref="FoldRejection.CarriesBlockUnderWall"/>, Aaron 2026-09-28). Editor limit, like the
        /// paperweights': blocks are judged where they are authored, while in the game a block pushed elsewhere
        /// first may make a stack reachable that the preview refuses, or the reverse. A hand-off that changes
        /// nothing changes nothing.
        /// </summary>
        public void SetBlocks(IReadOnlyList<(Rect flatRect, SheetFace side)> authored)
        {
            if (SameBlocks(authored))
                return;
            blocks.Clear();
            blocks.AddRange(authored);
            Changed?.Invoke();
        }

        /// <summary>See <see cref="SetBlocks"/>: the block-stopping universal walls, sheet-local.</summary>
        public void SetBlockWalls(IReadOnlyList<ConvexPolygon> walls)
        {
            if (SamePieces(walls, blockWalls))
                return;
            blockWalls.Clear();
            blockWalls.AddRange(walls);
            Changed?.Invoke();
        }

        /// <summary>The blocks and walls last handed over (test seams).</summary>
        public IReadOnlyList<(Rect flatRect, SheetFace side)> Blocks => blocks;
        public IReadOnlyList<ConvexPolygon> BlockWalls => blockWalls;

        bool SameBlocks(IReadOnlyList<(Rect flatRect, SheetFace side)> authored)
        {
            if (authored.Count != blocks.Count)
                return false;
            for (int i = 0; i < authored.Count; i++)
                if (authored[i].flatRect != blocks[i].flatRect || authored[i].side != blocks[i].side)
                    return false;
            return true;
        }

        static bool SamePieces(IReadOnlyList<ConvexPolygon> a, IReadOnlyList<ConvexPolygon> b)
        {
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; i++)
            {
                var va = a[i].Vertices;
                var vb = b[i].Vertices;
                if (va.Count != vb.Count)
                    return false;
                for (int v = 0; v < va.Count; v++)
                    if (va[v] != vb[v]) return false;
            }
            return true;
        }

        /// <summary>True if the fold that became fold <paramref name="foldIndex"/> of <paramref name="after"/> carries any authored block under a block-stopping universal wall.</summary>
        bool CarriesABlockUnderAWall(SheetLayers after, int foldIndex)
        {
            foreach (var (flatRect, side) in blocks)
            {
                if (FoldLandingRules.CarriesUnderWall(flatRect, side, after, foldIndex, blockWalls))
                    return true;
            }
            return false;
        }

        void ObstaclePiecesOn(SheetLayers stack, List<ConvexPolygon> pieces)
        {
            pieces.Clear();
            foreach (var (footprint, face) in obstacles)
                pieces.AddRange(stack.Coverage(footprint, face).VisibleParts);
        }

        /// <summary>True if a later committed fold pins fold <paramref name="index"/> (its seam/crease are dead).</summary>
        public bool IsPinned(int index) => FoldHitTest.IsPinned(effects, index);

        Rect GhostOrFar => ghost ?? FarAway;

        // ----- Live fold actions (ghost rule applies) -----

        /// <summary>
        /// Why <paramref name="fold"/> cannot be made on the sheet as it lies. Editor scope: no AlreadyFolded
        /// (see class remarks); the player rule tests the ghost; obstacles as authored; OverlapsFold only with
        /// stacking off, as the safety net behind <see cref="MaxDepth"/>.
        /// </summary>
        public FoldRejection Evaluate(Fold fold, out FoldEffect effect)
        {
            var after = layers.Apply(fold, folds.Count, out effect);
            if (effect.Outcome == FoldOutcome.NothingToFold)
                return FoldRejection.NothingToFold;
            if (effect.Outcome == FoldOutcome.Overhangs)
                return FoldRejection.Overhangs;
            if (!FoldValidity.IsValid(effect, GhostOrFar))
                return FoldRejection.CoversPlayer;
            if (!FoldObstacles.Clear(effect, obstaclePieces))
                return FoldRejection.CoversObstacle;
            if (CarriesABlockUnderAWall(after, folds.Count))
                return FoldRejection.CarriesBlockUnderWall;
            if (!AllowStacking && !FoldObstacles.Clear(effect, foldPieces))
                return FoldRejection.OverlapsFold;
            return FoldRejection.None;
        }

        /// <summary>
        /// Shows a drag preview (null clears it), depth clamped to <see cref="MaxDepth"/>. Visual only.
        /// A fold that lifts nothing shows nothing and is not red (mirrors SheetFolds.SetPreview).
        /// </summary>
        public void SetPreview(Fold? fold)
        {
            preview = fold.HasValue ? fold.Value.WithDepth(Mathf.Min(fold.Value.Depth, MaxDepth(fold.Value.Anchor))) : null;
            previewValid = true;
            if (preview.HasValue)
            {
                var rejection = Evaluate(preview.Value, out _);
                previewValid = rejection == FoldRejection.None || rejection == FoldRejection.NothingToFold;
            }
            RebuildDisplay();
            Changed?.Invoke();
        }

        /// <summary>Commits a fold (and clears the preview). <paramref name="minDepth"/> is the sheet's authored value.</summary>
        public bool TryCommit(Fold fold, float minDepth, out FoldRejection rejection)
        {
            rejection = fold.Depth < minDepth ? FoldRejection.TooShallow : Evaluate(fold, out _);
            preview = null;
            previewValid = true;
            if (rejection != FoldRejection.None)
            {
                RebuildDisplay();
                Changed?.Invoke();
                return false;
            }
            folds.Add(fold);
            AfterStackChange();
            return true;
        }

        /// <summary>
        /// Unfolds the latest unpinned fold whose Seam is near <paramref name="sheetLocal"/>, refusing when the
        /// ghost stands on that flap (<see cref="FoldRejection.PlayerOnFlap"/>) — the game's rule. No object
        /// constraints: nothing rides preview folds in the editor.
        /// </summary>
        public bool TryUnfoldAt(Vector2 sheetLocal, float grabDistance, out FoldRejection rejection)
        {
            var index = FoldHitTest.SeamAt(effects, sheetLocal, grabDistance, GhostOrFar, out rejection);
            if (index < 0)
                return false;
            RememberCreases(index);
            folds.RemoveAt(index);
            AfterStackChange();
            return true;
        }

        /// <summary>The unpinned committed fold whose crease is near the point — grab it to fold further.</summary>
        public bool TryGrabCreaseAt(Vector2 sheetLocal, float grabDistance, out Fold fold)
        {
            var index = FoldHitTest.CreaseAt(effects, sheetLocal, grabDistance);
            fold = index >= 0 ? folds[index] : default;
            return index >= 0;
        }

        // ----- Fold-list edits (geometric guards only; see class remarks) -----

        /// <summary>
        /// Removes a committed fold if every remaining fold stays legal without it (Aaron 2026-08-31: refuse
        /// with reason). MaxDepth is stack-dependent, so an unguarded remove could leave a later fold
        /// overhanging — a state the game can never produce.
        /// </summary>
        public bool TryRemoveAt(int index, out string reason)
        {
            if (index < 0 || index >= folds.Count)
            {
                reason = "No such fold.";
                return false;
            }
            // Aaron (2026-08-31, code-review Q2): ✕ mirrors the game's unfold ordering exactly — a pinned
            // fold (one a later fold stacked on) cannot be removed first.
            if (IsPinned(index))
            {
                var pinner = index + 1;
                for (int j = index + 1; j < effects.Count; j++)
                {
                    if (!FoldValidity.Independent(effects[index], effects[j]))
                    {
                        pinner = j;
                        break;
                    }
                }
                reason = $"Fold {index + 1} is pinned by fold {pinner + 1}; remove that first.";
                return false;
            }
            var trial = new List<Fold>(folds);
            trial.RemoveAt(index);
            if (!TrialIsLegal(trial, out var badTrialIndex, out var problem))
            {
                var original = badTrialIndex < index ? badTrialIndex : badTrialIndex + 1;
                reason = $"Fold {original + 1} would {problem} without fold {index + 1}; remove it first.";
                return false;
            }
            RememberCreases(index);
            folds.RemoveAt(index);
            AfterStackChange();
            reason = null;
            return true;
        }

        /// <summary>
        /// Changes a committed fold's depth if the whole stack stays legal at the new value, and the value is
        /// not below the sheet's authored minimum (the game would drop it as TooShallow).
        /// </summary>
        public bool TrySetDepth(int index, float depth, float minDepth, out string reason)
        {
            if (index < 0 || index >= folds.Count)
            {
                reason = "No such fold.";
                return false;
            }
            if (depth < minDepth)
            {
                reason = $"Depth {depth:0.###} is below the sheet's minimum fold depth ({minDepth:0.###}).";
                return false;
            }
            var trial = new List<Fold>(folds);
            trial[index] = trial[index].WithDepth(depth);
            if (!TrialIsLegal(trial, out var badIndex, out var problem))
            {
                reason = badIndex == index
                    ? $"Fold {index + 1} would {problem} at depth {depth:0.###}."
                    : $"Fold {badIndex + 1} would {problem} if fold {index + 1} were {depth:0.###} deep.";
                return false;
            }
            folds[index] = folds[index].WithDepth(depth);
            AfterStackChange();
            reason = null;
            return true;
        }

        /// <summary>Removes every fold, remembered crease, and preview. Obstacles are authored content and stay.</summary>
        public void Clear()
        {
            folds.Clear();
            rememberedCreases.Clear();
            preview = null;
            previewValid = true;
            AfterStackChange();
        }

        /// <summary>
        /// Replays <paramref name="trial"/> from flat: every fold must be geometrically possible, clear of the
        /// obstacles as they lie just before it, and (stacking off) independent of every fold before it (the
        /// game's rules minus the player, who moves between folds). <paramref name="problem"/> is the verb
        /// phrase for the refusal message.
        /// </summary>
        bool TrialIsLegal(List<Fold> trial, out int badIndex, out string problem)
        {
            var stack = SheetLayers.Flat;
            var pieces = new List<ConvexPolygon>();
            var earlier = new List<FoldEffect>();
            for (int i = 0; i < trial.Count; i++)
            {
                ObstaclePiecesOn(stack, pieces);
                stack = stack.Apply(trial[i], i, out var effect);
                if (effect.Outcome != FoldOutcome.None)
                {
                    badIndex = i;
                    problem = Describe(effect.Outcome);
                    return false;
                }
                if (!FoldObstacles.Clear(effect, pieces))
                {
                    badIndex = i;
                    problem = "cover a paperweight where it is authored";
                    return false;
                }
                if (CarriesABlockUnderAWall(stack, i))
                {
                    badIndex = i;
                    problem = "carry a block under a universal wall";
                    return false;
                }
                if (!AllowStacking)
                {
                    for (int j = 0; j < earlier.Count; j++)
                    {
                        if (FoldValidity.Independent(effect, earlier[j]))
                            continue;
                        badIndex = i;
                        problem = $"overlap fold {j + 1} with stacking off";
                        return false;
                    }
                }
                earlier.Add(effect);
            }
            badIndex = -1;
            problem = null;
            return true;
        }

        static string Describe(FoldOutcome outcome)
            => outcome == FoldOutcome.Overhangs ? "overhang the sheet" : "lift nothing";

        // ----- Ghost (faithful stand-in: always on the sheet's current footprint) -----

        public void SetGhostSize(Vector2 size)
        {
            ghostSize = new Vector2(Mathf.Max(size.x, 0.05f), Mathf.Max(size.y, 0.05f));
            if (ghost.HasValue)
                MoveGhost(ghost.Value.center);
        }

        /// <summary>Turns the ghost on at (or nearest to) the sheet centre, or off.</summary>
        public void SetGhostEnabled(bool enabled)
        {
            ghost = enabled ? RectAt(NearestFitCentre(Vector2.zero)) : null;
            Changed?.Invoke();
        }

        /// <summary>Moves the ghost, clamped so the whole rect stays on the sheet's current footprint.</summary>
        public void MoveGhost(Vector2 centre)
        {
            if (!ghost.HasValue)
                return;
            ghost = RectAt(NearestFitCentre(centre));
            Changed?.Invoke();
        }

        Rect RectAt(Vector2 centre) => new(centre - ghostSize * 0.5f, ghostSize);

        /// <summary>
        /// The nearest centre (to <paramref name="point"/>) at which the whole ghost rect fits on the sheet's
        /// current footprint (Aaron, 2026-08-31: the in-game player's whole collider is stopped by boundary
        /// walls, so the stand-in must fit entirely on the Sheet). Each footprint polygon is eroded by the ghost's
        /// half-size (exact for a convex polygon: every edge's half-plane shifted inward by the rect's support
        /// distance); the nearest point in any eroded polygon wins. Per-polygon erosion is conservative — a
        /// spot straddling two footprint pieces may be refused — erring on the faithful side. If the ghost
        /// fits nowhere (deep stacks of tiny pieces), it falls back to nearest-footprint-under-centre.
        /// </summary>
        public Vector2 NearestFitCentre(Vector2 point)
        {
            var half = ghostSize * 0.5f;
            var best = point;
            var bestDistance = float.PositiveInfinity;
            var anyFit = false;
            foreach (var polygon in layers.Footprint)
            {
                if (polygon.IsEmpty)
                    continue;
                var eroded = Erode(polygon, half);
                if (eroded.IsEmpty)
                    continue;
                anyFit = true;
                if (eroded.Contains(point))
                    return point;
                var vertices = eroded.Vertices;
                for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
                {
                    var candidate = ClosestOnSegment(point, vertices[j], vertices[i]);
                    var distance = Vector2.SqrMagnitude(candidate - point);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }
            return anyFit ? best : NearestFootprintPoint(point);
        }

        static ConvexPolygon Erode(ConvexPolygon polygon, Vector2 half)
        {
            var result = polygon;
            var vertices = polygon.Vertices;
            for (int i = 0; i < vertices.Count && !result.IsEmpty; i++)
            {
                var normal = polygon.OutwardNormal(i);
                var support = Mathf.Abs(normal.x) * half.x + Mathf.Abs(normal.y) * half.y;
                result = result.ClipToHalfPlane(vertices[i] - normal * support, normal, keepPositive: false);
            }
            return result;
        }

        /// <summary>Fallback: the nearest point with footprint under it (centre-based, mirrors PressureRules).</summary>
        Vector2 NearestFootprintPoint(Vector2 point)
        {
            var best = point;
            var bestDistance = float.PositiveInfinity;
            foreach (var polygon in layers.Footprint)
            {
                if (polygon.IsEmpty)
                    continue;
                if (polygon.Contains(point))
                    return point;
                var vertices = polygon.Vertices;
                for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
                {
                    var candidate = ClosestOnSegment(point, vertices[j], vertices[i]);
                    var distance = Vector2.SqrMagnitude(candidate - point);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }
            }
            return best;
        }

        static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var len2 = ab.sqrMagnitude;
            if (len2 < 1e-12f)
                return a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return a + ab * t;
        }

        /// <summary>True if the ghost had to relocate because a fold edit emptied the Sheet under it.</summary>
        public bool GhostWasRelocated { get; private set; }

        // ----- Internals -----

        /// <summary>Honours the sheet's authored rememberCreases toggle; the window sets it from settings.</summary>
        public bool RememberCreasesEnabled { get; set; } = true;

        void AfterStackChange()
        {
            Replay();
            GhostWasRelocated = false;
            if (ghost.HasValue)
            {
                var nearest = NearestFitCentre(ghost.Value.center);
                if (Vector2.SqrMagnitude(nearest - ghost.Value.center) > 1e-8f)
                {
                    ghost = RectAt(nearest);
                    GhostWasRelocated = true;
                }
            }
            RebuildDisplay();
            Changed?.Invoke();
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
            RebuildObstaclePieces();
        }

        void RebuildDisplay()
        {
            var stack = SheetLayers.Flat;
            displayEffects.Clear();
            for (int i = 0; i < folds.Count; i++)
            {
                stack = stack.Apply(folds[i], i, out var effect);
                displayEffects.Add(effect);
            }
            if (preview.HasValue && preview.Value.Depth > 0f)
            {
                stack = stack.Apply(preview.Value, folds.Count, out var effect);
                displayEffects.Add(effect);
            }
            displayLayers = stack;
        }

        void RememberCreases(int index)
        {
            if (RememberCreasesEnabled)
                rememberedCreases.AddRange(effects[index].CreaseSegments);
        }
    }
}
