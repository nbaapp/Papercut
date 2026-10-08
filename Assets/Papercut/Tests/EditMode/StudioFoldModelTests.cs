using NUnit.Framework;
using Papercut.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>
    /// The Studio's fold-preview model against the real fold math: replay truth, the editor rule scope
    /// (any number of folds, stacking as the sheet says, geometric guards, ghost-only player rule), list-edit
    /// refusals, and the name-coupled settings reads.
    /// </summary>
    public sealed class StudioFoldModelTests
    {
        const float MinDepth = 0.25f;

        static StudioFoldModel Model() => new();

        static Fold EdgeEast(float depth) => new(FoldAnchor.EdgeEast, depth);
        static Fold EdgeWest(float depth) => new(FoldAnchor.EdgeWest, depth);

        // ----- Replay truth -----

        [Test]
        public void Commit_ReplaysThroughSheetLayers()
        {
            var model = Model();
            Assert.IsTrue(model.TryCommit(EdgeEast(2f), MinDepth, out _));
            Assert.AreEqual(1, model.Folds.Count);
            Assert.AreEqual(2, model.Layers.Layers.Count, "base + one landed flap");
            Assert.IsTrue(model.Layers.AnyBackUp, "the landed flap shows the Back");

            // Truth check against SheetLayers directly.
            var truth = SheetLayers.Flat.Apply(EdgeEast(2f), 0, out var effect);
            Assert.AreEqual(FoldOutcome.None, effect.Outcome);
            Assert.AreEqual(truth.Layers.Count, model.Layers.Layers.Count);
        }

        [Test]
        public void Preview_AppearsInDisplayLayers_ButNotInCommitted()
        {
            var model = Model();
            model.SetPreview(EdgeEast(2f));
            Assert.AreEqual(0, model.Folds.Count);
            Assert.AreEqual(1, model.Layers.Layers.Count, "committed stack stays flat");
            Assert.AreEqual(2, model.DisplayLayers.Layers.Count, "display includes the preview");
            model.SetPreview(null);
            Assert.AreEqual(1, model.DisplayLayers.Layers.Count);
        }

        // ----- Editor rule scope -----

        [Test]
        public void Evaluate_AllowsOverlappingFolds_WithStackingOn()
        {
            var model = Model();
            Assert.IsTrue(model.AllowStacking, "stacking is on until the window mirrors the sheet's toggle");
            Assert.IsTrue(model.TryCommit(EdgeEast(2f), MinDepth, out _));
            // A deeper second fold lifts pieces of the first fold's flap: the game would refuse this with
            // allowStacking off (mirrored, see below) and without allowMultipleFolds (ignored: any number previews).
            Assert.IsTrue(model.TryCommit(EdgeEast(3f), MinDepth, out var rejection), rejection.ToString());
            Assert.AreEqual(2, model.Folds.Count);
        }

        [Test]
        public void DragClamp_MaxDepthPreventsOverhang()
        {
            var model = Model();
            var clamped = Mathf.Min(100f, model.MaxDepth(FoldAnchor.EdgeEast));
            Assert.AreEqual(FoldRejection.None, model.Evaluate(EdgeEast(clamped), out _), "at MaxDepth the fold is legal");
            Assert.AreEqual(FoldRejection.Overhangs, model.Evaluate(EdgeEast(clamped + 0.5f), out _), "past it, it overhangs");
        }

        [Test]
        public void Commit_DropsBelowMinDepth()
        {
            var model = Model();
            Assert.IsFalse(model.TryCommit(EdgeEast(0.1f), MinDepth, out var rejection));
            Assert.AreEqual(FoldRejection.TooShallow, rejection);
            Assert.AreEqual(0, model.Folds.Count);
        }

        // ----- Ghost -----

        [Test]
        public void GhostOff_NeverRefusesForThePlayer()
        {
            var model = Model();
            Assert.AreNotEqual(FoldRejection.CoversPlayer, model.Evaluate(EdgeEast(5f), out _));
            Assert.IsTrue(model.TryCommit(EdgeEast(3f), MinDepth, out _));
            Assert.IsTrue(model.TryUnfoldAt(SeamPointOf(model, 0), 0.3f, out var rejection), rejection.ToString());
        }

        [Test]
        public void Ghost_RefusesCoveringFold_ExactlyLikeFoldValidity()
        {
            var model = Model();
            model.SetGhostEnabled(true); // Sheet centre.
            var fold = EdgeEast(4f);     // Crease at x = 1.5; lifts the east side, lands over the centre.
            SheetLayers.Flat.Apply(fold, 0, out var effect);
            Assert.IsFalse(FoldValidity.IsValid(effect, model.Ghost.Value), "sanity: this fold covers the centre ghost");
            Assert.AreEqual(FoldRejection.CoversPlayer, model.Evaluate(fold, out _));

            var shallow = EdgeEast(1f); // Lands nowhere near the centre.
            Assert.AreEqual(FoldRejection.None, model.Evaluate(shallow, out _));
        }

        [Test]
        public void Ghost_OnFlap_RefusesUnfold()
        {
            var model = Model();
            Assert.IsTrue(model.TryCommit(EdgeEast(2f), MinDepth, out _));
            model.SetGhostEnabled(true);
            model.MoveGhost(new Vector2(2.5f, 0f)); // On the landed flap (crease x=3.5, flap covers [1.5, 3.5]).
            Assert.IsFalse(model.TryUnfoldAt(SeamPointOf(model, 0), 0.3f, out var rejection));
            Assert.AreEqual(FoldRejection.PlayerOnFlap, rejection);
        }

        [Test]
        public void Ghost_IsNonDegenerate_AndWholeRectClampsOntoFootprint()
        {
            var size = StudioFoldSettings.ReadPlayerFootprintSize();
            Assert.Greater(size.x, 0.01f, "ghost width from Player.prefab must be real (serialized size, not physics bounds)");
            Assert.Greater(size.y, 0.01f);

            var model = Model();
            model.SetGhostSize(size);
            model.SetGhostEnabled(true);
            model.MoveGhost(new Vector2(100f, 100f)); // Way off the sheet.
            // Aaron (2026-08-31): the WHOLE rect must fit on the paper, so the centre clamps a half-size in.
            var ghost = model.Ghost.Value;
            Assert.LessOrEqual(ghost.xMax, SheetGeometry.HalfSize.x + 1e-3f, "whole ghost must stay on the sheet");
            Assert.LessOrEqual(ghost.yMax, SheetGeometry.HalfSize.y + 1e-3f);
            Assert.GreaterOrEqual(ghost.xMin, -SheetGeometry.HalfSize.x - 1e-3f);
            Assert.GreaterOrEqual(ghost.yMin, -SheetGeometry.HalfSize.y - 1e-3f);
        }

        [Test]
        public void Ghost_RelocatesWhenAListEditEmptiesTheSheetUnderIt()
        {
            // A live commit can never strand the ghost (the covers-player rule refuses lifting under it),
            // but list edits use geometric guards only (§3), so a depth edit can empty the Sheet under it.
            var model = Model();
            model.SetGhostEnabled(true);
            model.MoveGhost(new Vector2(2f, 0f));
            Assert.IsTrue(model.TryCommit(EdgeEast(1f), MinDepth, out var rejection), rejection.ToString());
            var before = model.Ghost.Value.center;

            // Depth 4: crease at x = 1.5, lifting [1.5, 5.5] — the ghost's piece of the Sheet — and landing at [-2.5, 1.5].
            Assert.IsTrue(model.TrySetDepth(0, 4f, MinDepth, out var reason), reason);
            Assert.IsTrue(model.GhostWasRelocated, "the ghost must move off bare desk");
            Assert.AreNotEqual(before, model.Ghost.Value.center);
            Assert.LessOrEqual(model.Ghost.Value.xMax, 1.5f + 1e-3f,
                "the WHOLE relocated ghost must fit on the remaining footprint (centre-clamp would leave xMax past the edge)");
        }

        // ----- List-edit guards -----

        [Test]
        public void TryRemoveAt_RefusesPinnedFolds_MirroringGameUnfoldOrder()
        {
            // Fold 1 (East, half the width) piles the sheet onto the west half; fold 2 stacks on it, pinning
            // it. Aaron (2026-08-31): ✕ mirrors the game's unfold ordering — a pinned fold cannot go first.
            var model = Model();
            Assert.IsTrue(model.TryCommit(EdgeEast(SheetGeometry.Width * 0.5f), MinDepth, out _));
            var deep = model.MaxDepth(FoldAnchor.EdgeEast);
            Assert.Greater(deep, SheetGeometry.Width * 0.5f, "sanity: stacked MaxDepth exceeds the flat limit");
            Assert.IsTrue(model.TryCommit(EdgeEast(deep - 0.1f), MinDepth, out var r2), r2.ToString());
            Assert.IsTrue(model.IsPinned(0), "sanity: fold 1 is pinned by fold 2");

            Assert.IsFalse(model.TryRemoveAt(0, out var reason), "removing the pinned support must be refused");
            StringAssert.Contains("pinned", reason);
            Assert.AreEqual(2, model.Folds.Count, "state unchanged after refusal");

            Assert.IsTrue(model.TryRemoveAt(1, out _), "removing the later fold first is fine");
            Assert.IsTrue(model.TryRemoveAt(0, out _), "then the support");
            Assert.AreEqual(0, model.Folds.Count);
        }

        [Test]
        public void RememberCreases_HonoursTheSheetsAuthoredToggle()
        {
            var model = Model();
            model.RememberCreasesEnabled = false;
            Assert.IsTrue(model.TryCommit(EdgeEast(2f), MinDepth, out _));
            Assert.IsTrue(model.TryUnfoldAt(SeamPointOf(model, 0), 0.3f, out _));
            Assert.AreEqual(0, model.RememberedCreases.Count, "rememberCreases off leaves no marks");
        }

        [Test]
        public void TrySetDepth_RefusesImpossibleAndTooShallowValues()
        {
            var model = Model();
            Assert.IsTrue(model.TryCommit(EdgeEast(2f), MinDepth, out _));

            Assert.IsFalse(model.TrySetDepth(0, 0.05f, MinDepth, out var shallowReason));
            StringAssert.Contains("minimum", shallowReason);

            Assert.IsFalse(model.TrySetDepth(0, 100f, MinDepth, out var deepReason));
            StringAssert.Contains("overhang", deepReason);

            Assert.AreEqual(2f, model.Folds[0].Depth, 1e-5f, "state unchanged after refusals");
            Assert.IsTrue(model.TrySetDepth(0, 3f, MinDepth, out _));
            Assert.AreEqual(3f, model.Folds[0].Depth, 1e-5f);
        }

        // ----- Unfold / creases -----

        [Test]
        public void Unfold_RespectsCoveredByLaterFold_AndRemembersCreases()
        {
            var model = Model();
            Assert.IsTrue(model.TryCommit(EdgeEast(3f), MinDepth, out _));
            // Fold 1 emptied the east side (footprint now ends at x = 2.5), so a second east fold must reach
            // past that to lift anything: depth 4 puts its crease at x = 1.5, cutting fold 1's landed flap.
            Assert.IsTrue(model.TryCommit(EdgeEast(4f), MinDepth, out var r2), r2.ToString());
            Assert.IsTrue(model.IsPinned(0), "sanity: fold 1 is pinned by fold 2");

            // Fold 1's seam is pinned: unfolding at it must not remove fold 1.
            Assert.IsFalse(model.TryUnfoldAt(SeamPointOf(model, 0), 0.05f, out var rejection));
            Assert.AreEqual(FoldRejection.CoveredByLaterFold, rejection);
            Assert.AreEqual(2, model.Folds.Count);

            Assert.IsTrue(model.TryUnfoldAt(SeamPointOf(model, 1), 0.3f, out _), "fold 2 unfolds by its seam");
            Assert.AreEqual(1, model.Folds.Count);
            Assert.Greater(model.RememberedCreases.Count, 0, "unfolding leaves a remembered crease");

            model.Clear();
            Assert.AreEqual(0, model.RememberedCreases.Count, "clear forgets creases");
        }

        [Test]
        public void GrabCrease_FindsUnpinnedFoldOnly()
        {
            var model = Model();
            Assert.IsTrue(model.TryCommit(EdgeEast(2f), MinDepth, out _));
            var crease = model.Effects[0].CreaseSegments[0];
            var mid = (crease.a + crease.b) * 0.5f;
            Assert.IsTrue(model.TryGrabCreaseAt(mid, 0.3f, out var grabbed));
            Assert.AreEqual(model.Folds[0], grabbed);
        }

        static Vector2 SeamPointOf(StudioFoldModel model, int index)
        {
            var seam = model.Effects[index].SeamSegments[0];
            return (seam.a + seam.b) * 0.5f;
        }

        // ----- Obstacles (paperweights, as authored) -----

        static readonly Rect WeightRect = new(-0.37f, -0.35f, 0.74f, 0.7f); // The Block footprint, at the centre.

        static (FaceFootprint, SheetFace)[] WeightAtCentre(SheetFace face)
            => new[] { (FaceFootprint.FromRect(WeightRect), face) };

        [Test]
        public void Obstacle_ClampsTheDragAndRefusesADeeperFold_LikeFoldObstacles()
        {
            var model = Model();
            model.SetObstacles(WeightAtCentre(SheetFace.Front));
            Assert.AreEqual(1, model.ObstaclePieces.Count, "the weight is face-up on the flat sheet");

            var expected = FoldObstacles.MaxDepth(FoldAnchor.EdgeEast, SheetLayers.Flat, model.ObstaclePieces);
            Assert.AreEqual((SheetGeometry.HalfSize.x - WeightRect.xMax) * 0.5f, expected, 1e-3f, "sanity: the flat closed form");
            Assert.AreEqual(expected, model.MaxDepth(FoldAnchor.EdgeEast), 1e-6f);

            Assert.AreEqual(FoldRejection.None, model.Evaluate(EdgeEast(expected), out _), "at the clamp the fold is legal");
            Assert.AreEqual(FoldRejection.CoversObstacle, model.Evaluate(EdgeEast(expected + 0.1f), out _), "past it, the safety net");
            model.SetPreview(EdgeEast(5f));
            Assert.AreEqual(expected, model.Preview.Value.Depth, 1e-6f, "the preview is held at the clamp");
            Assert.IsTrue(model.PreviewValid, "and is not red");
        }

        [Test]
        public void Obstacle_BelowMinDepth_MakesTheAnchorInert()
        {
            var model = Model();
            model.MinDepth = MinDepth;
            var nearEdge = new Rect(SheetGeometry.HalfSize.x - 0.4f - 0.74f, -0.35f, 0.74f, 0.7f); // 0.4 in from the east edge: clamp 0.2 < minDepth.
            model.SetObstacles(new[] { (FaceFootprint.FromRect(nearEdge), SheetFace.Front) });
            Assert.AreEqual(0f, model.MaxDepth(FoldAnchor.EdgeEast), "a sliver that would drop as TooShallow is not offered");
            Assert.Greater(model.MaxDepth(FoldAnchor.EdgeWest), 1f, "other anchors are unaffected");
        }

        [Test]
        public void Obstacle_OnTheBack_IsInertFlat_AndBindsOnceExposed()
        {
            var model = Model();
            // Authored on the Back at Back-space x in [3, 3.74]: beneath Front-space x in [-3.74, -3] (Bible §6).
            var backRect = new Rect(3f, -0.35f, 0.74f, 0.7f);
            model.SetObstacles(new[] { (FaceFootprint.FromRect(backRect), SheetFace.Back) });
            Assert.AreEqual(0, model.ObstaclePieces.Count, "face-down: not on the same side (Aaron)");
            Assert.IsTrue(float.IsPositiveInfinity(FoldObstacles.MaxDepth(FoldAnchor.EdgeEast, model.Layers, model.ObstaclePieces)));

            // West fold depth 2.5 (crease x = -3) lifts x in [-5.5, -3] and lands it Back-up on [-3, -0.5]: the
            // weight's region comes around to its mirror across the crease, x in [-3, -2.26].
            Assert.IsTrue(model.TryCommit(EdgeWest(2.5f), MinDepth, out var rejection), rejection.ToString());
            Assert.AreEqual(1, model.ObstaclePieces.Count, "exposed on the landed flap");
            var bounds = model.ObstaclePieces[0].Bounds;
            Assert.AreEqual(-3f, bounds.xMin, 1e-3f);
            Assert.AreEqual(-2.26f, bounds.xMax, 1e-3f);

            // An east fold's flap reaching x = -2.26 would land on it: 2d = 5.5 + 2.26.
            Assert.AreEqual((SheetGeometry.HalfSize.x + 2.26f) * 0.5f, model.MaxDepth(FoldAnchor.EdgeEast), 1e-3f);
        }

        [Test]
        public void Obstacle_GuardsListEdits_WhereItIsAuthored()
        {
            var model = Model();
            model.SetObstacles(WeightAtCentre(SheetFace.Front));
            Assert.IsTrue(model.TryCommit(EdgeEast(1f), MinDepth, out _));

            Assert.IsFalse(model.TrySetDepth(0, 4f, MinDepth, out var reason), "depth 4 lands on the centre weight");
            StringAssert.Contains("paperweight", reason);
            Assert.AreEqual(1f, model.Folds[0].Depth, 1e-6f, "unchanged after the refusal");

            model.Clear();
            Assert.AreEqual(1, model.ObstaclePieces.Count, "obstacles are authored content: Clear keeps them");
        }

        [Test]
        public void SetObstacles_SameAgain_IsANoOp()
        {
            var model = Model();
            var changes = 0;
            model.Changed += () => changes++;
            model.SetObstacles(WeightAtCentre(SheetFace.Front));
            Assert.AreEqual(1, changes);
            model.SetObstacles(WeightAtCentre(SheetFace.Front));
            Assert.AreEqual(1, changes, "an unchanged hand-off does not re-render");
            model.SetObstacles(System.Array.Empty<(FaceFootprint, SheetFace)>());
            Assert.AreEqual(2, changes);
            Assert.AreEqual(0, model.ObstaclePieces.Count);
        }

        // ----- Stacking off (Aaron, 2026-09-16: the drag stops against another Flap, as at a paperweight) -----

        [Test]
        public void StackingOff_HoldsTheDragShortOfTheOtherFlap_AndCommitsThere()
        {
            var model = Model();
            model.AllowStacking = false;
            Assert.IsTrue(model.TryCommit(EdgeWest(2f), MinDepth, out _));

            // The west Flap lies on x in [-3.5, -1.5]; an east Flap's far edge is at 5.5 - 2d: it meets the west Flap at d = 3.5.
            var clamp = model.MaxDepth(FoldAnchor.EdgeEast);
            Assert.AreEqual(3.5f - FoldObstacles.ContactTolerance, clamp, 1e-3f);

            model.SetPreview(EdgeEast(5f));
            Assert.AreEqual(clamp, model.Preview.Value.Depth, 1e-6f, "the preview holds at the clamp, not at the cursor");
            Assert.IsTrue(model.PreviewValid, "held short of the other Flap: not red");

            Assert.IsTrue(model.TryCommit(EdgeEast(clamp), MinDepth, out var rejection), rejection.ToString());
            Assert.AreEqual(2, model.Folds.Count);
            Assert.IsTrue(FoldValidity.Independent(model.Effects[0], model.Effects[1]), "side by side, not stacked");
        }

        [Test]
        public void StackingOff_RefusesAnOverlap_AsTheSafetyNet()
        {
            var model = Model();
            model.AllowStacking = false;
            Assert.IsTrue(model.TryCommit(EdgeWest(2f), MinDepth, out _));

            Assert.AreEqual(FoldRejection.OverlapsFold, model.Evaluate(EdgeEast(4f), out _));
            Assert.IsFalse(model.TryCommit(EdgeEast(4f), MinDepth, out var rejection));
            Assert.AreEqual(FoldRejection.OverlapsFold, rejection);
            Assert.AreEqual(1, model.Folds.Count);
        }

        [Test]
        public void StackingOff_GuardsListEdits()
        {
            var model = Model();
            model.AllowStacking = false;
            Assert.IsTrue(model.TryCommit(EdgeWest(2f), MinDepth, out _));
            Assert.IsTrue(model.TryCommit(EdgeEast(3f), MinDepth, out var r2), r2.ToString());

            // West at depth 3 would lie on x in [-2.5, 0.5]; the east Flap lies on x in [-0.5, 2.5]. The trial replays
            // in order, so it is fold 2 (east) that is found overlapping fold 1 (west) at the new depth.
            Assert.IsFalse(model.TrySetDepth(0, 3f, MinDepth, out var reason), "deepening the west fold runs it into the east Flap");
            StringAssert.Contains("Fold 2 would overlap fold 1", reason);
            StringAssert.Contains("if fold 1 were 3 deep", reason);
            Assert.AreEqual(2f, model.Folds[0].Depth, 1e-6f, "unchanged after the refusal");

            model.AllowStacking = true;
            Assert.IsTrue(model.TrySetDepth(0, 3f, MinDepth, out var stackedReason), stackedReason);
        }

        // ----- Name-coupled settings reads (a rename must fail here, not silently degrade) -----

        [Test]
        public void SettingsFieldNames_ResolveOnTheRealComponents()
        {
            var sheetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Papercut/Prefabs/Sheet.prefab");
            Assert.IsNotNull(sheetPrefab);

            var folds = new SerializedObject(sheetPrefab.GetComponent<SheetFolds>());
            Assert.IsNotNull(folds.FindProperty(StudioFoldSettings.MinDepthField), StudioFoldSettings.MinDepthField);
            Assert.IsNotNull(folds.FindProperty(StudioFoldSettings.RememberCreasesField), StudioFoldSettings.RememberCreasesField);
            Assert.IsNotNull(folds.FindProperty(StudioFoldSettings.AllowStackingField), StudioFoldSettings.AllowStackingField);

            var renderer = new SerializedObject(sheetPrefab.GetComponent<RenderTextureFoldRenderer>());
            foreach (var name in new[]
                     {
                         StudioFoldSettings.PreviewTintField, StudioFoldSettings.InvalidTintField,
                         StudioFoldSettings.SeamWidthField,
                         StudioFoldSettings.SeamColorField, StudioFoldSettings.PixelsPerUnitField,
                         StudioFoldSettings.FaceMaterialField,
                     })
                Assert.IsNotNull(renderer.FindProperty(name), name);

            // FoldDragInput has RequireComponent chains, so probe its serialized field names by reflection
            // instead of instantiating it.
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            foreach (var name in new[]
                     {
                         StudioFoldSettings.CornerGrabRadiusField, StudioFoldSettings.EdgeGrabMarginField,
                         StudioFoldSettings.UnfoldGrabDistanceField, StudioFoldSettings.CreaseGrabDistanceField,
                         StudioFoldSettings.DepthSnapField,
                     })
                Assert.IsNotNull(typeof(FoldDragInput).GetField(name, flags), name);
        }

        // ----- A Flap may not carry a block under a universal wall (Aaron, 2026-09-28) -----

        /// <summary>A Back-side block near the south edge, which a south fold of depth 1 turns face-up onto y in [-2.8, -2.3] - where the wall is.</summary>
        static readonly Rect BlockNearSouthEdge = new(-0.25f, -4.2f, 0.5f, 0.5f);
        static readonly ConvexPolygon[] WallAtLanding = { ConvexPolygon.FromRect(new Rect(-1f, -3f, 2f, 1f)) };

        [Test]
        public void BlockUnderWall_RefusesTheCarryingFold_LikeTheGame()
        {
            var model = Model();
            model.SetBlocks(new[] { (BlockNearSouthEdge, SheetFace.Back) });
            model.SetBlockWalls(WallAtLanding);

            model.SetPreview(new Fold(FoldAnchor.EdgeSouth, 1f));
            Assert.IsFalse(model.PreviewValid, "red, like a fold over the player");
            Assert.IsFalse(model.TryCommit(new Fold(FoldAnchor.EdgeSouth, 1f), MinDepth, out var rejection));
            Assert.AreEqual(FoldRejection.CarriesBlockUnderWall, rejection);
            Assert.AreEqual(0, model.Folds.Count);

            model.SetBlockWalls(System.Array.Empty<ConvexPolygon>());
            Assert.IsTrue(model.TryCommit(new Fold(FoldAnchor.EdgeSouth, 1f), MinDepth, out _), "without the wall the same fold commits");
        }

        [Test]
        public void BlockUnderWall_GuardsListEdits()
        {
            var model = Model();
            model.SetBlocks(new[] { (BlockNearSouthEdge, SheetFace.Back) });
            model.SetBlockWalls(WallAtLanding);
            Assert.IsTrue(model.TryCommit(new Fold(FoldAnchor.EdgeSouth, 0.5f), MinDepth, out _), "shallow: the block lands short of the wall");

            Assert.IsFalse(model.TrySetDepth(0, 1f, MinDepth, out var reason));
            StringAssert.Contains("carry a block under a universal wall", reason);
            Assert.AreEqual(0.5f, model.Folds[0].Depth, 1e-5f, "unchanged");
        }

        [Test]
        public void SetBlocksAndWalls_SameAgain_AreNoOps()
        {
            var model = Model();
            var changes = 0;
            model.Changed += () => changes++;
            model.SetBlocks(new[] { (BlockNearSouthEdge, SheetFace.Back) });
            model.SetBlockWalls(WallAtLanding);
            Assert.AreEqual(2, changes);
            model.SetBlocks(new[] { (BlockNearSouthEdge, SheetFace.Back) });
            model.SetBlockWalls(WallAtLanding);
            Assert.AreEqual(2, changes, "unchanged hand-offs change nothing");
        }
    }
}
