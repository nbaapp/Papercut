using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>
    /// The carried-under-a-wall rule (Aaron, 2026-09-28: a fold is refused when its Flap would carry a block under a
    /// universal wall that stops blocks) against the real fold math. Sheet 11 × 8.5, half 5.5 × 4.25.
    /// </summary>
    public sealed class FoldLandingRulesTests
    {
        /// <summary>
        /// A block near the south edge: y in [-4.2, -3.7]. A south edge fold of depth 1 (crease y = -3.25) lands it at
        /// y in [-2.8, -2.3]. A Front block carried there rides face-down, under the Flap; only a Back-side block is
        /// turned face-up onto the landing - that is the block a wall there can catch.
        /// </summary>
        static readonly Rect BlockNearSouthEdge = new(-0.25f, -4.2f, 0.5f, 0.5f);

        /// <summary>A wall across where that block lands: y in [-3, -2].</summary>
        static readonly ConvexPolygon[] WallAtLanding = { ConvexPolygon.FromRect(new Rect(-1f, -3f, 2f, 1f)) };

        static readonly ConvexPolygon[] WallElsewhere = { ConvexPolygon.FromRect(new Rect(2f, 2f, 1f, 1f)) };

        static SheetLayers After(out int foldIndex, params Fold[] folds)
        {
            var stack = SheetLayers.Flat;
            for (int i = 0; i < folds.Length; i++)
            {
                stack = stack.Apply(folds[i], i, out var effect);
                Assert.AreEqual(FoldOutcome.None, effect.Outcome, $"fold {i} must be possible");
            }
            foldIndex = folds.Length - 1;
            return stack;
        }

        [Test]
        public void CarriedFaceUpOntoAWall_IsRefused()
        {
            var after = After(out var index, new Fold(FoldAnchor.EdgeSouth, 1f));
            Assert.IsTrue(FoldLandingRules.CarriesUnderWall(BlockNearSouthEdge, SheetFace.Back, after, index, WallAtLanding));
        }

        [Test]
        public void AFrontBlockTheFlapCarries_RidesFaceDown_NotIntoTheWall()
        {
            // The Flap flips: a Front block on the lifted strip ends under the paper, with the sheet between it and the wall.
            var after = After(out var index, new Fold(FoldAnchor.EdgeSouth, 1f));
            Assert.IsFalse(FoldLandingRules.CarriesUnderWall(BlockNearSouthEdge, SheetFace.Front, after, index, WallAtLanding));
        }

        [Test]
        public void CarriedClearOfEveryWall_IsFine()
        {
            var after = After(out var index, new Fold(FoldAnchor.EdgeSouth, 1f));
            Assert.IsFalse(FoldLandingRules.CarriesUnderWall(BlockNearSouthEdge, SheetFace.Back, after, index, WallElsewhere));
            Assert.IsFalse(FoldLandingRules.CarriesUnderWall(BlockNearSouthEdge, SheetFace.Back, after, index, System.Array.Empty<ConvexPolygon>()), "no walls, nothing to refuse");
        }

        [Test]
        public void ABlockTheFlapCovers_IsNotCarried()
        {
            // On the Base under where the Flap lands: it goes under the Flap, not into the wall.
            var covered = new Rect(-0.25f, -2.9f, 0.5f, 0.5f);
            var after = After(out var index, new Fold(FoldAnchor.EdgeSouth, 1f));
            Assert.IsFalse(FoldLandingRules.CarriesUnderWall(covered, SheetFace.Front, after, index, WallAtLanding));
        }

        [Test]
        public void ABlockLeftUntouched_IsNotCarried()
        {
            var far = new Rect(3f, 2f, 0.5f, 0.5f);
            var after = After(out var index, new Fold(FoldAnchor.EdgeSouth, 1f));
            var wallOverIt = new[] { ConvexPolygon.FromRect(new Rect(2.5f, 1.5f, 2f, 2f)) };
            Assert.IsFalse(FoldLandingRules.CarriesUnderWall(far, SheetFace.Front, after, index, wallOverIt), "already overlapping a wall, but this fold does not carry it");
        }

        [Test]
        public void ABackSideBlock_TurnedFaceUpUnderAWall_IsRefused()
        {
            // Invisible before the fold (its face is down); the fold exposes it exactly where the wall is.
            var after = After(out var index, new Fold(FoldAnchor.EdgeSouth, 1f));
            Assert.IsTrue(FoldLandingRules.CarriesUnderWall(BlockNearSouthEdge, SheetFace.Back, after, index, WallAtLanding));
        }

        [Test]
        public void ABackBlockTheFoldDoesNotReach_StaysFaceDown()
        {
            var after = After(out var index, new Fold(FoldAnchor.EdgeSouth, 1f));
            var wallEverywhere = new[] { ConvexPolygon.FromRect(new Rect(-5.5f, -4.25f, 11f, 8.5f)) };
            Assert.IsFalse(FoldLandingRules.CarriesUnderWall(new Rect(3f, 2f, 0.5f, 0.5f), SheetFace.Back, after, index, wallEverywhere),
                "a Back block away from the Flap is still face-down: nothing of it is carried");
        }

        [Test]
        public void OnlyTheJudgedFoldsOwnLayersCount()
        {
            // Fold 0 carries the block; fold 1 (north) does not touch it. Judged as fold 1, nothing is carried under the wall.
            var after = After(out var index, new Fold(FoldAnchor.EdgeSouth, 1f), new Fold(FoldAnchor.EdgeNorth, 1f));
            Assert.AreEqual(1, index);
            Assert.IsFalse(FoldLandingRules.CarriesUnderWall(BlockNearSouthEdge, SheetFace.Back, after, index, WallAtLanding));
            Assert.IsTrue(FoldLandingRules.CarriesUnderWall(BlockNearSouthEdge, SheetFace.Back, after, 0, WallAtLanding), "judged as fold 0 it is");
        }

        [Test]
        public void CornerFold_CarriesTheCornerBlock()
        {
            // North-east corner fold of depth 3 lifts x + y >= 6.75; a block at (5, 3.75) lands mirrored across the crease.
            var block = new Rect(4.75f, 3.5f, 0.5f, 0.5f);
            var after = After(out var index, new Fold(FoldAnchor.CornerNorthEast, 3f));
            var stack = after.Layers;
            var landed = SheetPlacement.VisiblePieces(block, SheetFace.Back, after);
            Assert.IsTrue(landed.Count > 0, "the Back of the corner block is exposed by the corner fold");
            var wallOnLanding = new[] { landed[0].Desk };
            Assert.IsTrue(stack[landed[0].LayerIndex].MovedBy == index);
            Assert.IsTrue(FoldLandingRules.CarriesUnderWall(block, SheetFace.Back, after, index, wallOnLanding));
        }
    }
}
