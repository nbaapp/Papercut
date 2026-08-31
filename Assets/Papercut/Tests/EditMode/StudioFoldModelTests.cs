using NUnit.Framework;
using Papercut.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>
    /// The Studio's fold-preview model against the real fold math: replay truth, the editor rule scope
    /// (allow-everything folds, geometric guards, ghost-only player rule), list-edit refusals, and the
    /// name-coupled settings reads.
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
        public void Evaluate_AllowsOverlappingFolds_TogglesAreIgnored()
        {
            var model = Model();
            Assert.IsTrue(model.TryCommit(EdgeEast(2f), MinDepth, out _));
            // A deeper second fold lifts pieces of the first fold's flap: the game would refuse this without
            // allowStacking (and without allowMultipleFolds at all); the editor allows any combination.
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

        // ----- Name-coupled settings reads (a rename must fail here, not silently degrade) -----

        [Test]
        public void SettingsFieldNames_ResolveOnTheRealComponents()
        {
            var sheetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Papercut/Prefabs/Sheet.prefab");
            Assert.IsNotNull(sheetPrefab);

            var folds = new SerializedObject(sheetPrefab.GetComponent<SheetFolds>());
            Assert.IsNotNull(folds.FindProperty(StudioFoldSettings.MinDepthField), StudioFoldSettings.MinDepthField);
            Assert.IsNotNull(folds.FindProperty(StudioFoldSettings.RememberCreasesField), StudioFoldSettings.RememberCreasesField);

            var renderer = new SerializedObject(sheetPrefab.GetComponent<RenderTextureFoldRenderer>());
            foreach (var name in new[]
                     {
                         StudioFoldSettings.PreviewTintField, StudioFoldSettings.InvalidTintField,
                         StudioFoldSettings.CreaseColorField, StudioFoldSettings.RememberedCreaseColorField,
                         StudioFoldSettings.CreaseWidthField, StudioFoldSettings.SeamWidthField,
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
    }
}
