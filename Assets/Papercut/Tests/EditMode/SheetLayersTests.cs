using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class SheetLayersTests
    {
        const float W = 11f, H = 8.5f, HW = 5.5f, HH = 4.25f;
        const float Eps = 1e-4f;

        static void AssertRect(Rect expected, Rect actual, string label = "")
        {
            Assert.AreEqual(expected.xMin, actual.xMin, Eps, label + " xMin");
            Assert.AreEqual(expected.yMin, actual.yMin, Eps, label + " yMin");
            Assert.AreEqual(expected.xMax, actual.xMax, Eps, label + " xMax");
            Assert.AreEqual(expected.yMax, actual.yMax, Eps, label + " yMax");
        }

        /// <summary>Applies folds in order; the last effect is returned.</summary>
        static SheetLayers Replay(out FoldEffect last, params Fold[] folds)
        {
            var stack = SheetLayers.Flat;
            last = default;
            for (int i = 0; i < folds.Length; i++)
                stack = stack.Apply(folds[i], i, out last);
            return stack;
        }

        static SheetLayers Replay(params Fold[] folds) => Replay(out _, folds);

        // ----- one fold -----

        [Test]
        public void EdgeFold_LiftsStrip_LandsMirrorStrip_BackUpOnTop()
        {
            var stack = Replay(out var effect, new Fold(FoldAnchor.EdgeSouth, 3f)); // crease y = -1.25

            Assert.AreEqual(FoldOutcome.None, effect.Outcome);
            Assert.AreEqual(2, stack.Layers.Count);
            var flap = stack.Layers[1];
            Assert.IsFalse(flap.FrontUp);
            Assert.AreEqual(0, flap.MovedBy);
            Assert.IsTrue(stack.Layers[0].FrontUp);
            Assert.IsTrue(stack.Layers[0].IsBase);
            AssertRect(Rect.MinMaxRect(-HW, -HH, HW, -1.25f), flap.Original.Bounds, "original");
            AssertRect(Rect.MinMaxRect(-HW, -1.25f, HW, 1.75f), flap.Desk.Bounds, "landed");
            AssertRect(Rect.MinMaxRect(-HW, -1.25f, HW, HH), stack.Layers[0].Desk.Bounds, "base");
            Assert.AreEqual(1, effect.Lifted.Count);
            Assert.AreEqual(3f * W, effect.Lifted[0].Area, Eps);
            Assert.AreEqual(3f * W, effect.Landed[0].Area, Eps);
            Assert.IsTrue(stack.AnyBackUp);
        }

        [Test]
        public void EdgeFold_CreaseSegmentAndSeam()
        {
            Replay(out var effect, new Fold(FoldAnchor.EdgeSouth, 3f));

            Assert.AreEqual(1, effect.CreaseSegments.Count);
            Assert.AreEqual(W, Vector2.Distance(effect.CreaseSegments[0].a, effect.CreaseSegments[0].b), Eps);
            Assert.AreEqual(-1.25f, effect.CreaseSegments[0].a.y, Eps);

            Assert.AreEqual(1, effect.SeamSegments.Count, "only the reflected south edge; the sides lie on the sheet's own edges");
            Assert.AreEqual(1.75f, effect.SeamSegments[0].a.y, Eps);
            Assert.AreEqual(1.75f, effect.SeamSegments[0].b.y, Eps);
            Assert.AreEqual(W, Vector2.Distance(effect.SeamSegments[0].a, effect.SeamSegments[0].b), Eps);

            Assert.AreEqual(0f, effect.DistanceToSeam(new Vector2(1f, 1.75f)), Eps);
            Assert.AreEqual(0.5f, effect.DistanceToCrease(new Vector2(1f, -0.75f)), Eps);
            Assert.AreEqual(2f, effect.DistanceToCrease(new Vector2(7.5f, -1.25f)), Eps, "beyond the segment end");
        }

        [Test]
        public void EdgeFold_CreaseMarks_LiftedSideSouth_FrontInside()
        {
            Replay(out var effect, new Fold(FoldAnchor.EdgeSouth, 3f));

            var front = effect.CreaseMarks.Single(m => m.Face == SheetFace.Front);
            var back = effect.CreaseMarks.Single(m => m.Face == SheetFace.Back);
            Assert.AreEqual(-1.25f, front.A.y, Eps);
            Assert.AreEqual(-1.25f, back.A.y, Eps);
            Assert.AreEqual(0f, front.LiftedSide.x, Eps);
            Assert.AreEqual(-1f, front.LiftedSide.y, Eps, "the south strip lifted");
            Assert.AreEqual(0f, back.LiftedSide.x, Eps);
            Assert.AreEqual(-1f, back.LiftedSide.y, Eps, "an x mirror leaves south as south in Back-space");
            Assert.IsTrue(front.Inside, "Front was up: the inside of the fold");
            Assert.IsFalse(back.Inside);
            Assert.AreEqual(-front.A.x, back.A.x, Eps, "Back-space is x-mirrored");
        }

        [Test]
        public void CornerFold_CreaseMarks_LiftedSideTowardTheCorner_MirroredOnBack()
        {
            Replay(out var effect, new Fold(FoldAnchor.CornerNorthEast, 2f));

            var front = effect.CreaseMarks.Single(m => m.Face == SheetFace.Front);
            var back = effect.CreaseMarks.Single(m => m.Face == SheetFace.Back);
            var diagonal = Mathf.Sqrt(0.5f);
            Assert.AreEqual(diagonal, front.LiftedSide.x, Eps, "toward the north-east corner");
            Assert.AreEqual(diagonal, front.LiftedSide.y, Eps);
            Assert.AreEqual(-diagonal, back.LiftedSide.x, Eps, "Back-space is x-mirrored");
            Assert.AreEqual(diagonal, back.LiftedSide.y, Eps);
            Assert.AreEqual(0f, Vector2.Dot(front.LiftedSide, front.B - front.A), Eps, "perpendicular to the crease");
            Assert.IsTrue(front.Inside);
            Assert.IsFalse(back.Inside);
        }

        [Test]
        public void CornerFold_LiftsTriangle_LandsInsideCorner()
        {
            var stack = Replay(out var effect, new Fold(FoldAnchor.CornerNorthEast, 2f));

            Assert.AreEqual(2, stack.Layers.Count);
            Assert.AreEqual(2f, effect.Lifted[0].Area, Eps);
            AssertRect(Rect.MinMaxRect(HW - 2f, HH - 2f, HW, HH), effect.Landed[0].Bounds);
            Assert.AreEqual(2, effect.SeamSegments.Count, "two legs meeting at the reflected corner");
            Assert.IsTrue(effect.SeamSegments.All(s => Vector2.Distance(s.a, new Vector2(HW - 2f, HH - 2f)) < Eps || Vector2.Distance(s.b, new Vector2(HW - 2f, HH - 2f)) < Eps));
            Assert.AreEqual(2f * Mathf.Sqrt(2f), Vector2.Distance(effect.CreaseSegments[0].a, effect.CreaseSegments[0].b), Eps);
        }

        // ----- limits -----

        [Test]
        public void MaxDepth_Flat_EqualsTheStaticBound()
        {
            foreach (FoldAnchor anchor in System.Enum.GetValues(typeof(FoldAnchor)))
                Assert.AreEqual(FoldGeometry.MaxDepth(anchor), SheetLayers.Flat.MaxDepth(anchor), Eps, anchor.ToString());
        }

        [Test]
        public void MaxDepth_AfterAFold_FollowsTheSheetAsItLies()
        {
            var stack = Replay(new Fold(FoldAnchor.EdgeSouth, 2f)); // sheet now spans y in [-2.25, 4.25]

            Assert.AreEqual(5.25f, stack.MaxDepth(FoldAnchor.EdgeSouth), Eps, "(3h - s)/2 with s = 2.25 for the nearest vertex");
            Assert.AreEqual(HH, stack.MaxDepth(FoldAnchor.EdgeNorth), Eps, "north side unchanged");

            var deep = stack.Apply(new Fold(FoldAnchor.EdgeSouth, 5.25f), 1, out var atMax);
            Assert.AreEqual(FoldOutcome.None, atMax.Outcome);
            Assert.AreEqual(HH, atMax.Landed.Max(p => p.Bounds.yMax), Eps, "lands exactly on the far edge");
            stack.Apply(new Fold(FoldAnchor.EdgeSouth, 5.35f), 1, out var past);
            Assert.AreEqual(FoldOutcome.Overhangs, past.Outcome);
            Assert.AreEqual(3, deep.Layers.Count, "the base is cut and the old Flap lifts whole");
        }

        [Test]
        public void MaxDepth_Corner_AfterAnEdgeFold_FollowsTheNarrowerSheet()
        {
            // West strip folded away: the sheet spans x in [-3.5, 5.5]. For the south-west corner the x-bound
            // hx + 2hy - u with u = -x is tightest at x = -3.5: 14 - 3.5 = 10.5 (flat it is 8.5, at the corner itself).
            var stack = Replay(new Fold(FoldAnchor.EdgeWest, 2f));

            Assert.AreEqual(10.5f, stack.MaxDepth(FoldAnchor.CornerSouthWest), Eps);
            stack.Apply(new Fold(FoldAnchor.CornerSouthWest, 10.5f), 1, out var atMax);
            Assert.AreEqual(FoldOutcome.None, atMax.Outcome);
            Assert.IsTrue(atMax.Landed.All(p => p.ContainedIn(FoldGeometry.Sheet, 1e-3f)));
            stack.Apply(new Fold(FoldAnchor.CornerSouthWest, 10.6f), 1, out var past);
            Assert.AreEqual(FoldOutcome.Overhangs, past.Outcome);
        }

        [Test]
        public void Overhangs_JustPastTheFlatBound()
        {
            foreach (var anchor in new[] { FoldAnchor.EdgeSouth, FoldAnchor.EdgeEast, FoldAnchor.CornerNorthEast })
            {
                SheetLayers.Flat.Apply(new Fold(anchor, FoldGeometry.MaxDepth(anchor) + 0.1f), 0, out var past);
                Assert.AreEqual(FoldOutcome.Overhangs, past.Outcome, anchor.ToString());
                SheetLayers.Flat.Apply(new Fold(anchor, FoldGeometry.MaxDepth(anchor)), 0, out var atMax);
                Assert.AreEqual(FoldOutcome.None, atMax.Outcome, anchor.ToString());
            }
        }

        [Test]
        public void NothingToFold_WhenTheCreaseIsBeyondTheSheet()
        {
            var stack = Replay(new Fold(FoldAnchor.EdgeNorth, 2f)); // nothing lies at y > 2.25
            var same = stack.Apply(new Fold(FoldAnchor.EdgeNorth, 1f), 1, out var effect);

            Assert.AreEqual(FoldOutcome.NothingToFold, effect.Outcome);
            Assert.AreEqual(0, effect.Lifted.Count);
            Assert.AreEqual(stack.Layers.Count, same.Layers.Count);
        }

        // ----- stacking -----

        [Test]
        public void Stacking_LandingIntoAnEmptiedRegion_IsThreeLayersOverTheDesk()
        {
            var stack = Replay(out var effect, new Fold(FoldAnchor.EdgeNorth, 2f), new Fold(FoldAnchor.EdgeSouth, 4f));

            Assert.AreEqual(FoldOutcome.None, effect.Outcome);
            Assert.AreEqual(3, stack.Layers.Count);
            var top = stack.Layers[2];
            Assert.AreEqual(1, top.MovedBy);
            Assert.IsFalse(top.FrontUp);
            AssertRect(Rect.MinMaxRect(-HW, -0.25f, HW, 3.75f), top.Desk.Bounds, "lands over the base and on into the emptied strip");
            var union = stack.Footprint.Select(p => p.Bounds).ToList();
            Assert.AreEqual(3.75f, union.Max(r => r.yMax), Eps);
            Assert.AreEqual(-0.25f, union.Min(r => r.yMin), Eps);
        }

        [Test]
        public void FoldOnFold_LiftsBothLayers_LandsThemReversed_DoubleReflectedPieceIsFrontUp()
        {
            var stack = Replay(out var effect, new Fold(FoldAnchor.EdgeNorth, 2f), new Fold(FoldAnchor.EdgeWest, 3f)); // creases y = 2.25, x = -2.5

            Assert.AreEqual(FoldOutcome.None, effect.Outcome);
            Assert.AreEqual(2, effect.Lifted.Count, "the base and the north Flap both lie west of the crease");
            Assert.AreEqual(4, stack.Layers.Count);

            var twice = stack.Layers[2];
            var once = stack.Layers[3];
            Assert.IsTrue(twice.FrontUp, "the north Flap piece is turned over again");
            Assert.AreEqual(1, twice.MovedBy);
            Assert.IsFalse(twice.ToDesk.IsMirror, "two mirrors make a rotation");
            AssertRect(Rect.MinMaxRect(-2.5f, 0.25f, 0.5f, 2.25f), twice.Desk.Bounds, "half turn about (-2.5, 2.25)");
            AssertRect(Rect.MinMaxRect(-HW, 2.25f, -2.5f, HH), twice.Original.Bounds, "it came from the north-west corner");
            Assert.IsFalse(once.FrontUp);
            Assert.AreEqual(1, once.MovedBy);
            AssertRect(Rect.MinMaxRect(-2.5f, -HH, 0.5f, 2.25f), once.Desk.Bounds, "the base piece lands on top of the pile");
            Assert.AreEqual(2, effect.CreaseSegments.Count, "the crease cuts both layers");
            Assert.AreEqual(4, effect.CreaseMarks.Count);
        }

        [Test]
        public void FoldOnFold_CreaseMarks_OnTheBackUpLayer_BackIsInside()
        {
            Replay(out var effect, new Fold(FoldAnchor.EdgeNorth, 2f), new Fold(FoldAnchor.EdgeWest, 3f));

            // The north Flap (Back up) is cut along x = -2.5 for y in [0.25, 2.25] (desk). Its Front-space original
            // is the mirror across y = 2.25: x = -2.5, y in [2.25, 4.25].
            var onFlap = effect.CreaseMarks.Where(m => m.A.y > 2.25f - Eps && m.B.y > 2.25f - Eps).ToList();
            Assert.AreEqual(2, onFlap.Count, "one mark per face on the flap piece");
            var front = onFlap.Single(m => m.Face == SheetFace.Front);
            var back = onFlap.Single(m => m.Face == SheetFace.Back);
            Assert.AreEqual(-2.5f, front.A.x, Eps);
            Assert.AreEqual(2.5f, back.A.x, Eps, "Back-space is x-mirrored");
            // The west side lifted: desk -x, which is Front-space -x here (the north fold mirrors y only) and
            // Back-space +x. The Back face is up on this piece, so it is the inside of the fold.
            Assert.AreEqual(-1f, front.LiftedSide.x, Eps, "toward the lifted (west) side, in Front-space");
            Assert.AreEqual(1f, back.LiftedSide.x, Eps, "toward the lifted (west) side, in Back-space");
            Assert.IsFalse(front.Inside, "Front was down on the Flap");
            Assert.IsTrue(back.Inside, "Back was up on the Flap: the inside of this fold");
        }

        [Test]
        public void FoldOnFold_APieceOverEmptyDesk_ComesBackFrontUpOnTop()
        {
            // North strip lifted, south Flap lands past it over the desk, then that overhanging piece is folded back.
            var stack = Replay(out var effect, new Fold(FoldAnchor.EdgeNorth, 2f), new Fold(FoldAnchor.EdgeSouth, 4f), new Fold(FoldAnchor.EdgeNorth, 1.25f));

            Assert.AreEqual(FoldOutcome.None, effect.Outcome);
            Assert.AreEqual(1, effect.Lifted.Count, "only the south Flap lies north of y = 3");
            var top = stack.Layers[^1];
            Assert.IsTrue(top.FrontUp);
            Assert.AreEqual(2, top.MovedBy);
            AssertRect(Rect.MinMaxRect(-HW, 2.25f, HW, 3f), top.Desk.Bounds);
            AssertRect(Rect.MinMaxRect(-HW, -HH, HW, -3.5f), top.Original.Bounds, "it is the far south strip of the flat sheet");
        }

        [Test]
        public void IndependentFolds_Commute()
        {
            var pairs = new[]
            {
                (new Fold(FoldAnchor.EdgeNorth, 1f), new Fold(FoldAnchor.EdgeSouth, 2f)), // a south fold of depth 3 would land on the north Flap
                (new Fold(FoldAnchor.EdgeEast, 2f), new Fold(FoldAnchor.EdgeWest, 1.5f)),
                (new Fold(FoldAnchor.CornerNorthEast, 2f), new Fold(FoldAnchor.CornerSouthWest, 3f)),
                (new Fold(FoldAnchor.CornerNorthEast, 2f), new Fold(FoldAnchor.EdgeWest, 1f)),
            };
            foreach (var (a, b) in pairs)
            {
                SheetLayers.Flat.Apply(a, 0, out var ea);
                SheetLayers.Flat.Apply(b, 0, out var eb);
                Assert.IsTrue(FoldValidity.Independent(ea, eb), $"{a} / {b} should be independent");

                var ab = Replay(a, b);
                var ba = Replay(b, a);
                Assert.AreEqual(ab.Layers.Count, ba.Layers.Count);
                var abKeys = ab.Layers.Select(Key).OrderBy(k => k).ToList();
                var baKeys = ba.Layers.Select(Key).OrderBy(k => k).ToList();
                CollectionAssert.AreEqual(abKeys, baKeys, $"{a} / {b}");
            }

            static string Key(SheetLayers.Layer l)
            {
                var r = l.Desk.Bounds;
                return $"{(l.FrontUp ? "F" : "B")} {r.xMin:0.###} {r.yMin:0.###} {r.xMax:0.###} {r.yMax:0.###} {l.Desk.Area:0.###}";
            }
        }

        [Test]
        public void DependentFolds_AreNotIndependent()
        {
            SheetLayers.Flat.Apply(new Fold(FoldAnchor.EdgeNorth, 2f), 0, out var north);
            Replay(new Fold(FoldAnchor.EdgeNorth, 2f)).Apply(new Fold(FoldAnchor.EdgeWest, 3f), 1, out var west);
            Replay(new Fold(FoldAnchor.EdgeNorth, 2f)).Apply(new Fold(FoldAnchor.EdgeSouth, 4f), 1, out var south);

            Assert.IsFalse(FoldValidity.Independent(north, west), "west lifts part of the north Flap");
            Assert.IsFalse(FoldValidity.Independent(north, south), "south lands where north lifted from and on its Flap");
        }

        [Test]
        public void AnEarlierFoldsEffect_IsUnchangedByALaterFold_ItsSeamMayNowLieUnderOrAlongTheLater()
        {
            // Effects describe a fold at the moment it was made; replaying the same prefix reproduces them exactly.
            // So a pinned fold's Seam can coincide with a later fold's crease - which is why the input tries a
            // crease grab before reporting CoveredByLaterFold (SheetFolds.TryUnfoldAt skips pinned Seams).
            SheetLayers.Flat.Apply(new Fold(FoldAnchor.EdgeSouth, 2f), 0, out var alone);
            var stack = SheetLayers.Flat.Apply(new Fold(FoldAnchor.EdgeSouth, 2f), 0, out var first);
            stack.Apply(new Fold(FoldAnchor.EdgeSouth, 4f), 1, out var second);

            Assert.AreEqual(alone.SeamSegments.Count, first.SeamSegments.Count);
            for (int i = 0; i < alone.SeamSegments.Count; i++)
            {
                Assert.AreEqual(0f, Vector2.Distance(alone.SeamSegments[i].a, first.SeamSegments[i].a), Eps);
                Assert.AreEqual(0f, Vector2.Distance(alone.SeamSegments[i].b, first.SeamSegments[i].b), Eps);
            }
            Assert.IsFalse(FoldValidity.Independent(first, second), "the second fold lifts the first Flap: the first is pinned");
            Assert.AreEqual(0f, first.DistanceToSeam(new Vector2(0f, -0.25f)), Eps, "the first Seam...");
            Assert.AreEqual(0f, second.DistanceToCrease(new Vector2(0f, -0.25f)), Eps, "...lies exactly on the second crease");
        }

        [Test]
        public void Seam_OfAStackedLanding_HidesLowerEdgesUnderUpperPieces()
        {
            Replay(out var effect, new Fold(FoldAnchor.EdgeNorth, 2f), new Fold(FoldAnchor.EdgeWest, 3f));

            // The double-reflected piece (x in [-2.5, 0.5], y in [0.25, 2.25]) lands under the base piece
            // (x in [-2.5, 0.5], y in [-4.25, 2.25]); none of its edges are Seam. The base piece's east and north edges are.
            Assert.AreEqual(2, effect.SeamSegments.Count, string.Join("; ", effect.SeamSegments));
            var east = effect.SeamSegments.Single(s => Mathf.Abs(s.a.x - 0.5f) < Eps && Mathf.Abs(s.b.x - 0.5f) < Eps);
            var north = effect.SeamSegments.Single(s => Mathf.Abs(s.a.y - 2.25f) < Eps && Mathf.Abs(s.b.y - 2.25f) < Eps);
            Assert.AreEqual(2.25f + HH, Vector2.Distance(east.a, east.b), Eps);
            Assert.AreEqual(3f, Vector2.Distance(north.a, north.b), Eps);
        }

        // ----- coverage -----

        [Test]
        public void Coverage_Front_ClearOfEveryFold_IsWhole()
        {
            var result = Replay(new Fold(FoldAnchor.EdgeSouth, 1f)).Coverage(Rect.MinMaxRect(0f, 0f, 1f, 1f), SheetFace.Front);

            Assert.AreEqual(FoldCoverage.Uncovered, result.Coverage);
            Assert.IsTrue(result.IsWhole);
        }

        [Test]
        public void Coverage_Front_LiftedOrUnderTheFlap_IsNone()
        {
            var result = Replay(new Fold(FoldAnchor.EdgeSouth, 1f)).Coverage(Rect.MinMaxRect(0f, -4f, 1f, -3f), SheetFace.Front);

            Assert.AreEqual(FoldCoverage.Covered, result.Coverage);
            Assert.IsTrue(result.IsNone);
        }

        [Test]
        public void Coverage_Front_Straddling_KeepsTheVisibleRemainder()
        {
            var result = Replay(new Fold(FoldAnchor.EdgeSouth, 1f)).Coverage(Rect.MinMaxRect(0f, -3f, 1f, -1f), SheetFace.Front); // hidden y < -2.25

            Assert.AreEqual(FoldCoverage.Partial, result.Coverage);
            Assert.AreEqual(1, result.VisibleParts.Count);
            AssertRect(Rect.MinMaxRect(0f, -2.25f, 1f, -1f), result.VisibleParts[0].Bounds);
        }

        [Test]
        public void Coverage_Front_CornerFold_RemainderHasTheRightArea()
        {
            var result = Replay(new Fold(FoldAnchor.CornerNorthEast, 3f)).Coverage(Rect.MinMaxRect(2f, 1f, 5f, 4f), SheetFace.Front); // hidden square x > 2.5, y > 1.25

            Assert.AreEqual(FoldCoverage.Partial, result.Coverage);
            Assert.AreEqual(9f - 2.5f * 2.75f, result.VisibleParts.Sum(p => p.Area), Eps);
        }

        [Test]
        public void Coverage_Back_OnTheFlap_IsExposedWhereItLands()
        {
            // South fold d = 2: Front y < -2.25 lifts. Back (1..2, -4..-3) lies beneath Front (-2..-1, -4..-3),
            // which lands at y = -4.5 - y: (-2..-1, -1.5..-0.5).
            var result = Replay(new Fold(FoldAnchor.EdgeSouth, 2f)).Coverage(Rect.MinMaxRect(1f, -4f, 2f, -3f), SheetFace.Back);

            Assert.AreEqual(FoldCoverage.Covered, result.Coverage, "fully exposed");
            Assert.IsFalse(result.IsWhole, "but never where it was authored: it needs a placed polygon");
            Assert.AreEqual(1, result.VisibleParts.Count);
            AssertRect(Rect.MinMaxRect(-2f, -1.5f, -1f, -0.5f), result.VisibleParts[0].Bounds);
        }

        [Test]
        public void Coverage_Back_OffTheFlap_IsNothing()
        {
            var result = Replay(new Fold(FoldAnchor.EdgeSouth, 2f)).Coverage(Rect.MinMaxRect(1f, 0f, 2f, 1f), SheetFace.Back);

            Assert.AreEqual(FoldCoverage.Uncovered, result.Coverage);
            Assert.IsTrue(result.IsNone);
        }

        [Test]
        public void Coverage_Back_Straddling_IsClippedToTheFlapSide()
        {
            // East fold d = 2: Front x > 3.5 lifts -> Back x < -3.5. Landed x = 7 - x_front.
            var result = Replay(new Fold(FoldAnchor.EdgeEast, 2f)).Coverage(Rect.MinMaxRect(-4.5f, 0f, -2.5f, 1f), SheetFace.Back);

            Assert.AreEqual(FoldCoverage.Partial, result.Coverage);
            Assert.AreEqual(1f, result.VisibleParts[0].Area, Eps);
            AssertRect(Rect.MinMaxRect(2.5f, 0f, 3.5f, 1f), result.VisibleParts[0].Bounds, "Back x in [-4.5, -3.5] -> Front x in [3.5, 4.5] -> landed x in [2.5, 3.5]");
        }

        [Test]
        public void Coverage_Front_UnderAStackedFlap_IsHidden()
        {
            var stack = Replay(new Fold(FoldAnchor.EdgeNorth, 2f), new Fold(FoldAnchor.EdgeSouth, 4f)); // the south Flap covers y in [-0.25, 3.75]
            var result = stack.Coverage(Rect.MinMaxRect(0f, 0f, 1f, 1f), SheetFace.Front);

            Assert.IsTrue(result.IsNone);
            Assert.AreEqual(FoldCoverage.Covered, result.Coverage);
        }

        [Test]
        public void Coverage_Front_OnADoubleFoldedPieceOnTop_AppearsWhereItLies()
        {
            var stack = Replay(new Fold(FoldAnchor.EdgeNorth, 2f), new Fold(FoldAnchor.EdgeSouth, 4f), new Fold(FoldAnchor.EdgeNorth, 1.25f));
            // The flat sheet's far south strip (y in [-4.25, -3.5]) is now Front-up on top at y in [2.25, 3]: y' = y + 6.5.
            var result = stack.Coverage(Rect.MinMaxRect(0f, -4f, 1f, -3.6f), SheetFace.Front);

            Assert.AreEqual(FoldCoverage.Uncovered, result.Coverage, "all of it is visible");
            Assert.IsFalse(result.IsWhole, "but not where it was authored");
            Assert.AreEqual(1, result.VisibleParts.Count);
            AssertRect(Rect.MinMaxRect(0f, 2.5f, 1f, 2.9f), result.VisibleParts[0].Bounds);
        }

        // ----- Above the sheet (universal regions, 2026-09-28): clipped to the union of the layers -----

        static float TotalArea(CoverageResult result)
        {
            var total = 0f;
            foreach (var p in result.VisibleParts)
                total += p.Area;
            return total;
        }

        [Test]
        public void CoverageAbove_Flat_InsideTheSheet_IsWhole()
        {
            var result = SheetLayers.Flat.CoverageAbove(FaceFootprint.FromRect(Rect.MinMaxRect(0f, 0f, 1f, 1f)));
            Assert.IsTrue(result.IsWhole);
            Assert.AreEqual(FoldCoverage.Uncovered, result.Coverage);
        }

        [Test]
        public void CoverageAbove_Flat_PastTheEdge_IsTrimmedToTheSheet()
        {
            var result = SheetLayers.Flat.CoverageAbove(FaceFootprint.FromRect(Rect.MinMaxRect(5f, 0f, 6f, 1f)));
            Assert.IsFalse(result.IsWhole);
            Assert.AreEqual(FoldCoverage.Partial, result.Coverage);
            Assert.AreEqual(0.5f, TotalArea(result), Eps);
        }

        [Test]
        public void CoverageAbove_EdgeFold_AcrossTheRemovedStrip_KeepsWhatIsOverTheSheet()
        {
            // South fold of depth 1: the sheet's footprint ends at y = -3.25 (a strip of 2d is gone from that edge).
            var result = Replay(new Fold(FoldAnchor.EdgeSouth, 1f)).CoverageAbove(FaceFootprint.FromRect(Rect.MinMaxRect(0f, -4f, 1f, -3f)));
            Assert.AreEqual(FoldCoverage.Partial, result.Coverage);
            Assert.AreEqual(0.25f, TotalArea(result), Eps);
            foreach (var part in result.VisibleParts)
                Assert.GreaterOrEqual(part.Bounds.yMin, -3.25f - Eps);
        }

        [Test]
        public void CoverageAbove_OverTheLandedFlap_IsWhole_AndUnmoved()
        {
            // The Flap lands under the region; the region stays put, whole, so the authored collider stays live.
            var result = Replay(new Fold(FoldAnchor.EdgeSouth, 1f)).CoverageAbove(FaceFootprint.FromRect(Rect.MinMaxRect(0f, -3f, 1f, -2.5f)));
            Assert.IsTrue(result.IsWhole);
            Assert.AreEqual(FoldCoverage.Uncovered, result.Coverage);
        }

        [Test]
        public void CoverageAbove_InTheLiftedCorner_IsNone()
        {
            // North-east corner fold of depth 3 lifts x + y >= 6.75; nothing of the sheet is left there.
            var result = Replay(new Fold(FoldAnchor.CornerNorthEast, 3f)).CoverageAbove(FaceFootprint.FromRect(Rect.MinMaxRect(5f, 3.75f, 5.5f, 4.25f)));
            Assert.IsTrue(result.IsNone);
            Assert.AreEqual(FoldCoverage.Covered, result.Coverage);
        }

        [Test]
        public void CoverageAbove_CornerFold_RemainderHasTheRightArea()
        {
            // Of x in [3, 5.5], y in [3, 4.25], only the triangle with x + y <= 6.75 is still over the sheet.
            var result = Replay(new Fold(FoldAnchor.CornerNorthEast, 3f)).CoverageAbove(FaceFootprint.FromRect(Rect.MinMaxRect(3f, 3f, 5.5f, 4.25f)));
            Assert.AreEqual(FoldCoverage.Partial, result.Coverage);
            Assert.AreEqual(0.28125f, TotalArea(result), 1e-3f);
        }

        [Test]
        public void CoverageAbove_TwoFolds_PartsAreDisjointAndSumToTheRemainder()
        {
            // South then west, depth 1 each: each fold's lifted strip is gone and its landed strip is still sheet, so
            // the footprint is x >= -4.5, y >= -3.25 whatever lies on top of what.
            var result = Replay(new Fold(FoldAnchor.EdgeSouth, 1f), new Fold(FoldAnchor.EdgeWest, 1f))
                .CoverageAbove(FaceFootprint.FromRect(Rect.MinMaxRect(-5f, -4f, -3f, -3f)));
            Assert.AreEqual(0.375f, TotalArea(result), 1e-3f);
            Assert.IsTrue(result.VisibleParts.Count >= 2, "several layers under it: pieced together");
            var parts = result.VisibleParts;
            for (int i = 0; i < parts.Count; i++)
                for (int j = i + 1; j < parts.Count; j++)
                    Assert.LessOrEqual(parts[i].Intersect(parts[j]).Area, 1e-4f, "parts overlap");
        }
    }
}
