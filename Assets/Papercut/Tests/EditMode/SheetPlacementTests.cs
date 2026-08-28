using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>Placement of sheet-resident movable content (blocks) from the layer stack. Managed math only.</summary>
    public sealed class SheetPlacementTests
    {
        const float HW = 5.5f, HH = 4.25f;
        const float Eps = 1e-4f;

        static SheetLayers Replay(params Fold[] folds)
        {
            var stack = SheetLayers.Flat;
            for (int i = 0; i < folds.Length; i++)
                stack = stack.Apply(folds[i], i, out _);
            return stack;
        }

        /// <summary>South edge fold of depth 3: crease y = −1.25; the Flap lands over y ∈ [−1.25, 1.75], Back-up, index 1.</summary>
        static SheetLayers SouthFold() => Replay(new Fold(FoldAnchor.EdgeSouth, 3f));

        static float TotalArea(List<SheetPlacement.Piece> pieces)
        {
            var total = 0f;
            foreach (var p in pieces)
                total += p.Desk.Area;
            return total;
        }

        static void AssertRect(Rect expected, Rect actual)
        {
            Assert.AreEqual(expected.xMin, actual.xMin, Eps, "xMin");
            Assert.AreEqual(expected.yMin, actual.yMin, Eps, "yMin");
            Assert.AreEqual(expected.xMax, actual.xMax, Eps, "xMax");
            Assert.AreEqual(expected.yMax, actual.yMax, Eps, "yMax");
        }

        // ----- layers under points -----

        [Test]
        public void Flat_EveryPointIsOnTheBase()
        {
            var flat = SheetLayers.Flat;
            Assert.AreEqual(0, SheetPlacement.LayerContaining(flat, Vector2.zero));
            Assert.AreEqual(0, SheetPlacement.TopLayerAt(flat, new Vector2(5f, -4f)));
            Assert.AreEqual(-1, SheetPlacement.LayerContaining(flat, new Vector2(6f, 0f)));
            Assert.AreEqual(-1, SheetPlacement.TopLayerAt(flat, new Vector2(0f, 5f)));
        }

        [Test]
        public void EdgeFold_TopLayerIsTheFlapWhereItLanded_LiftedRegionIsEmptyDesk()
        {
            var stack = SouthFold();
            Assert.AreEqual(1, SheetPlacement.TopLayerAt(stack, new Vector2(0f, 0f)), "under the landed Flap");
            Assert.AreEqual(0, SheetPlacement.TopLayerAt(stack, new Vector2(0f, 3f)), "bare base");
            Assert.AreEqual(-1, SheetPlacement.TopLayerAt(stack, new Vector2(0f, -3f)), "lifted away");
            Assert.AreEqual(1, SheetPlacement.LayerContaining(stack, new Vector2(0f, -3f)), "flat point in the lifted region");
            Assert.AreEqual(0, SheetPlacement.LayerContaining(stack, new Vector2(0f, 0f)));
        }

        [Test]
        public void CornerFold_LayersAreFoundAndAxisAligned()
        {
            var stack = Replay(new Fold(FoldAnchor.CornerSouthWest, 2f));
            Assert.AreEqual(2, stack.Layers.Count);
            Assert.IsTrue(SheetPlacement.IsAxisAligned(stack.Layers[1].ToDesk));
            Assert.IsTrue(stack.Layers[1].ToDesk.IsMirror);
            var flap = stack.Layers[1];
            var inside = flap.Original.Bounds.center;
            Assert.AreEqual(1, SheetPlacement.LayerContaining(stack, inside));
        }

        // ----- rects through isometries -----

        [Test]
        public void DeskRect_FlatRect_RoundTrip_EdgeAndCorner()
        {
            var flat = new Rect(-1f, -3.5f, 0.8f, 0.6f);
            foreach (var stack in new[] { SouthFold(), Replay(new Fold(FoldAnchor.CornerSouthWest, 3f)) })
            {
                var iso = stack.Layers[1].ToDesk;
                var desk = SheetPlacement.DeskRect(flat, iso);
                Assert.AreEqual(flat.width * flat.height, desk.width * desk.height, Eps, "area kept");
                AssertRect(flat, SheetPlacement.FlatRect(desk, iso));
            }
        }

        [Test]
        public void DeskRect_EdgeFold_IsTheMirrorAcrossTheCrease()
        {
            var stack = SouthFold();
            var flat = new Rect(-1f, -3.5f, 0.8f, 0.6f); // y ∈ [−3.5, −2.9] → mirrored across y = −1.25 → [0.4, 1.0]
            var desk = SheetPlacement.DeskRect(flat, stack.Layers[1].ToDesk);
            AssertRect(new Rect(-1f, 0.4f, 0.8f, 0.6f), desk);
        }

        // ----- visible pieces -----

        [Test]
        public void Block_UnderLandedFlap_IsPartiallyCovered_OnTheBase()
        {
            var stack = SouthFold();
            var block = new Rect(-0.5f, 1.5f, 1f, 1f); // y ∈ [1.5, 2.5]; Flap covers up to 1.75
            var pieces = SheetPlacement.VisiblePieces(block, SheetFace.Front, stack);
            Assert.AreEqual(1, pieces.Count);
            Assert.AreEqual(0, pieces[0].LayerIndex);
            Assert.AreEqual(0, pieces[0].SurfaceLayer);
            Assert.AreEqual(0.75f, pieces[0].Desk.Area, Eps);
            AssertRect(new Rect(-0.5f, 1.75f, 1f, 0.75f), pieces[0].Desk.Bounds);
        }

        [Test]
        public void Block_WhollyUnderLandedFlap_HasNoPieces()
        {
            var pieces = SheetPlacement.VisiblePieces(new Rect(-0.5f, -0.5f, 1f, 1f), SheetFace.Front, SouthFold());
            Assert.AreEqual(0, pieces.Count);
        }

        [Test]
        public void BackBlock_OnLiftedPaper_IsWholeOnTheFlap()
        {
            var stack = SouthFold();
            var block = new Rect(-0.5f, -3.5f, 1f, 1f); // in the lifted region, on the Back: exposed
            var pieces = SheetPlacement.VisiblePieces(block, SheetFace.Back, stack);
            Assert.AreEqual(1, pieces.Count);
            Assert.AreEqual(1, pieces[0].LayerIndex);
            Assert.AreEqual(1f, pieces[0].Desk.Area, Eps);
            AssertRect(new Rect(-0.5f, 0f, 1f, 1f), pieces[0].Desk.Bounds); // mirrored across y = −1.25
        }

        [Test]
        public void FrontBlock_OnLiftedPaper_IsFaceDown()
        {
            var pieces = SheetPlacement.VisiblePieces(new Rect(-0.5f, -3.5f, 1f, 1f), SheetFace.Front, SouthFold());
            Assert.AreEqual(0, pieces.Count);
        }

        [Test]
        public void CreaseThroughFrontBlock_NothingVisible()
        {
            // y ∈ [−1.75, −0.75] straddles the crease at −1.25: the lifted half is face-down, the staying half is under the Flap.
            var pieces = SheetPlacement.VisiblePieces(new Rect(-0.5f, -1.75f, 1f, 1f), SheetFace.Front, SouthFold());
            Assert.AreEqual(0, pieces.Count);
        }

        [Test]
        public void CreaseThroughBackBlock_TheFlapHalfIsShown()
        {
            var stack = SouthFold();
            var block = new Rect(-0.5f, -1.75f, 1f, 1f);
            var pieces = SheetPlacement.VisiblePieces(block, SheetFace.Back, stack);
            Assert.AreEqual(1, pieces.Count);
            Assert.AreEqual(1, pieces[0].LayerIndex);
            Assert.AreEqual(0.5f, pieces[0].Desk.Area, Eps);
            AssertRect(new Rect(-0.5f, -1.25f, 1f, 0.5f), pieces[0].Desk.Bounds);
            Assert.AreEqual(1, SheetPlacement.PushLayer(block, SheetFace.Back, stack), "pushed through the Flap");
        }

        [Test]
        public void PushLayer_GlueLayerWhenUp_MinusOneWhenHidden()
        {
            var stack = SouthFold();
            Assert.AreEqual(0, SheetPlacement.PushLayer(new Rect(-0.5f, 2f, 1f, 1f), SheetFace.Front, stack));
            Assert.AreEqual(1, SheetPlacement.PushLayer(new Rect(-0.5f, -3.5f, 1f, 1f), SheetFace.Back, stack));
            Assert.AreEqual(-1, SheetPlacement.PushLayer(new Rect(-0.5f, -3.5f, 1f, 1f), SheetFace.Front, stack));
        }

        [Test]
        public void ClimbedBlock_PastTheSeam_HasASheetPieceAndAnAirPiece_BothOnTheFlap()
        {
            var stack = SouthFold();
            // On top of the Flap (Back side), straddling the Seam at y = 1.75 on the desk: desk y ∈ [1.25, 2.25].
            var desk = new Rect(-0.5f, 1.25f, 1f, 1f);
            var flat = SheetPlacement.FlatRect(desk, stack.Layers[1].ToDesk); // y ∈ [−4.75, −3.75]: half past the south edge
            Assert.IsTrue(SheetPlacement.OverhangsSheet(flat));
            Assert.AreEqual(1, SheetPlacement.LayerContaining(stack, flat.center), "centre still on the Flap's paper");

            var pieces = SheetPlacement.VisiblePieces(flat, SheetFace.Back, stack);
            Assert.AreEqual(2, pieces.Count);
            Assert.AreEqual(1f, TotalArea(pieces), Eps);
            foreach (var piece in pieces)
            {
                Assert.AreEqual(1, piece.LayerIndex);
                Assert.AreEqual(1, piece.SurfaceLayer);
            }
        }

        [Test]
        public void ClimbingBlock_OverlapWithTheFlap_IsOnTheFlapsSurface_NotCovered()
        {
            var stack = SouthFold();
            var block = new Rect(-0.5f, 1.5f, 1f, 1f); // base block, its lower quarter over the Flap
            var pieces = SheetPlacement.VisiblePieces(block, SheetFace.Front, stack, climbTarget: 1);
            Assert.AreEqual(2, pieces.Count);
            Assert.AreEqual(1f, TotalArea(pieces), Eps);
            var onFlap = pieces.Find(p => p.SurfaceLayer == 1);
            Assert.AreEqual(0, onFlap.LayerIndex, "still the base's content");
            Assert.AreEqual(0.25f, onFlap.Desk.Area, Eps);
        }

        [Test]
        public void HigherLayerOverlapping_And_CentreOn()
        {
            var stack = SouthFold();
            Assert.AreEqual(1, SheetPlacement.HigherLayerOverlapping(new Rect(-0.5f, 1.5f, 1f, 1f), stack, 0));
            Assert.AreEqual(-1, SheetPlacement.HigherLayerOverlapping(new Rect(-0.5f, 2f, 1f, 1f), stack, 0));
            Assert.IsTrue(SheetPlacement.CentreOn(new Vector2(0f, 1f), stack.Layers[1]));
            Assert.IsFalse(SheetPlacement.CentreOn(new Vector2(0f, 2f), stack.Layers[1]));
        }

        [Test]
        public void Reglue_AfterClimb_KeepsTheFlatCentreOnTheSheet()
        {
            var stack = SouthFold();
            var flap = stack.Layers[1];
            var desk = new Rect(-0.5f, 1.2f, 1f, 1f); // centre y = 1.7 < 1.75: just inside the Flap
            Assert.IsTrue(SheetPlacement.CentreOn(desk.center, flap));
            var flat = SheetPlacement.FlatRect(desk, flap.ToDesk);
            Assert.IsTrue(FoldGeometry.Sheet.Contains(flat.center));
            Assert.AreEqual(1, SheetPlacement.LayerContaining(stack, flat.center));
        }

        // ----- the ride rule -----

        [Test]
        public void UnfoldRide_BlockOnTheFlap_KeepsItsFace_AndIsOnTheBackAtTheMirroredPlace()
        {
            var folded = SouthFold();
            var flap = folded.Layers[1];
            var desk = new Rect(-0.5f, 0f, 1f, 1f); // on top of the Flap
            var flat = SheetPlacement.FlatRect(desk, flap.ToDesk);
            var side = SheetPlacement.UpSide(flap); // Back
            Assert.AreEqual(SheetFace.Back, side);

            // Unfold: the flat rect and side do not change - the block rides its piece of the sheet all the way
            // around (Aaron, 2026-08-27): it is now on the Back at the mirrored place, hidden.
            var unfolded = SheetLayers.Flat;
            Assert.AreEqual(0, SheetPlacement.VisiblePieces(flat, side, unfolded).Count);
            AssertRect(new Rect(-0.5f, -3.5f, 1f, 1f), flat); // mirrored across y = -1.25
            // Folding that region again exposes it on the Flap where it was.
            var pieces = SheetPlacement.VisiblePieces(flat, side, SouthFold());
            Assert.AreEqual(1, pieces.Count);
            AssertRect(desk, pieces[0].Desk.Bounds);
        }

        // ----- staying on the sheet -----

        [Test]
        public void MaxTravelInside_StopsAtTheCrease_FromUnderAThinFlap()
        {
            var stack = Replay(new Fold(FoldAnchor.EdgeSouth, 0.6f)); // crease y = -3.65; Flap lands over y in [-3.65, -3.05]
            var block = new Rect(-0.5f, -3.4f, 0.8f, 0.8f);            // partly under the Flap, on the Base
            var travel = SheetPlacement.MaxTravelInside(block, Vector2.down, stack.Layers[0].Original);
            Assert.AreEqual(-3.4f - -3.65f, travel, Eps, "to the crease");
            Assert.AreEqual(HW - 0.3f, SheetPlacement.MaxTravelInside(block, Vector2.right, stack.Layers[0].Original), Eps, "sideways: to the sheet's east edge");
            Assert.AreEqual(0f, SheetPlacement.MaxTravelInside(new Rect(-0.5f, -4f, 0.8f, 0.8f), Vector2.down, stack.Layers[0].Original), Eps, "already past it");
        }

        [Test]
        public void PushBound_CoveredStaysOnItsPiece_BaseStaysOnSheet_FlapTopIsFree()
        {
            var stack = SouthFold();
            // Partly under the landed Flap: pushed on the Base (layer 0) with the Flap over it -> bound is the Base piece.
            Assert.AreSame(stack.Layers[0].Original, SheetPlacement.PushBound(new Rect(-0.5f, 1.5f, 1f, 1f), stack, 0, 0));
            // Clear on the Base -> the sheet rect.
            var bound = SheetPlacement.PushBound(new Rect(-0.5f, 2.5f, 1f, 1f), stack, 0, 0);
            Assert.IsNotNull(bound);
            Assert.AreEqual(FoldGeometry.Sheet.width * FoldGeometry.Sheet.height, bound.Area, Eps);
            // On top of the Flap -> free (centre rule).
            var onFlap = SheetPlacement.FlatRect(new Rect(-0.5f, 0f, 1f, 1f), stack.Layers[1].ToDesk);
            Assert.IsNull(SheetPlacement.PushBound(onFlap, stack, 1, 1));
        }

        [Test]
        public void ClimbOnto_FromClearBase_NotFromRolledAround_NotFromCovered()
        {
            var stack = SouthFold();
            var clear = new Rect(-0.5f, 2f, 1f, 1f);
            var touching = new Rect(-0.5f, 1.5f, 1f, 1f);
            Assert.AreEqual(1, SheetPlacement.ClimbOnto(clear, touching, stack, 0, SheetFace.Front), "clear Base block sliding into the Flap climbs");
            Assert.AreEqual(-1, SheetPlacement.ClimbOnto(touching, new Rect(-0.5f, 1.4f, 1f, 1f), stack, 0, SheetFace.Front), "already partly under: goes under");
            // Rolled around the crease onto the Base's underside: glue is the Base, side Back (not up there) -> never climbs,
            // even though its identity-placed desk rect overlaps the landed Flap.
            var rolled = new Rect(-0.5f, -1.45f, 1f, 1f);
            Assert.AreEqual(0, SheetPlacement.LayerContaining(stack, rolled.center));
            Assert.AreEqual(1, SheetPlacement.PushLayer(rolled, SheetFace.Back, stack));
            Assert.AreEqual(-1, SheetPlacement.ClimbOnto(rolled, new Rect(-0.5f, -1.3f, 1f, 1f), stack, 0, SheetFace.Back));
        }

        [Test]
        public void SameLayer_FindsAnUntouchedFlapInAPreviewedStack_NotACutOne()
        {
            var committed = SouthFold();
            var flap = committed.Layers[1];
            // A preview that lifts nothing near it (a north strip): the Flap is still there, re-indexed.
            var previewed = Replay(new Fold(FoldAnchor.EdgeSouth, 3f), new Fold(FoldAnchor.EdgeNorth, 1f));
            var found = SheetPlacement.SameLayer(previewed, flap);
            Assert.GreaterOrEqual(found, 0);
            Assert.AreEqual(0, previewed.Layers[found].MovedBy);
            Assert.AreEqual(flap.Original.Area, previewed.Layers[found].Original.Area, Eps);
            // A preview that cuts through it (a west strip across the Flap): no whole match.
            var cut = Replay(new Fold(FoldAnchor.EdgeSouth, 3f), new Fold(FoldAnchor.EdgeWest, 2f));
            Assert.AreEqual(-1, SheetPlacement.SameLayer(cut, flap));
        }

        [Test]
        public void MaxTravelInside_SheetRect_StopsAtTheEdge()
        {
            var sheet = ConvexPolygon.FromRect(FoldGeometry.Sheet);
            Assert.AreEqual(HW - 1.5f, SheetPlacement.MaxTravelInside(new Rect(0.5f, 0f, 1f, 1f), Vector2.right, sheet), Eps);
            Assert.AreEqual(HH - 1f, SheetPlacement.MaxTravelInside(new Rect(0.5f, 0f, 1f, 1f), Vector2.up, sheet), Eps);
        }

        [Test]
        public void IsUp_UpSide()
        {
            var stack = SouthFold();
            Assert.IsTrue(SheetPlacement.IsUp(stack.Layers[0], SheetFace.Front));
            Assert.IsFalse(SheetPlacement.IsUp(stack.Layers[0], SheetFace.Back));
            Assert.IsTrue(SheetPlacement.IsUp(stack.Layers[1], SheetFace.Back));
            Assert.AreEqual(SheetFace.Back, SheetPlacement.UpSide(stack.Layers[1]));
        }
    }
}
