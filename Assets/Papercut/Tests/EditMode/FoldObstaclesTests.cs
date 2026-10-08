using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>
    /// The paperweight rules (Aaron, 2026-09-14): a Flap may neither lift nor land on a fold obstacle, and the
    /// drag holds at the first-contact depth. The analytic <see cref="FoldObstacles.MaxDepth"/> is checked
    /// against closed forms on the flat sheet and against a brute-force scan of <see cref="FoldObstacles.Clear"/>
    /// on stacked sheets - the scan is the source of truth. With stacking off the other folds' pieces are the
    /// obstacles (Aaron, 2026-09-16); there the scan is of <see cref="FoldValidity.Independent"/>.
    /// </summary>
    public sealed class FoldObstaclesTests
    {
        const float HW = 5.5f, HH = 4.25f;
        const float Eps = 1e-3f;
        /// <summary>Just past the contact tolerance: the clamp is Clear, this much deeper is not.</summary>
        const float PastContact = FoldObstacles.ContactTolerance * 2f + 1e-4f;

        /// <summary>
        /// Past a corner-on contact: where a Flap's edge meets another piece's corner, the overlap grows with the
        /// square of the penetration, so <see cref="PastContact"/> deeper is still under
        /// <see cref="ConvexPolygon.AreaEpsilon"/> and counts as independent. This much deeper (edge folds move
        /// the landed edge twice this) overlaps a 45-degree corner by ~8e-4 of area, well past the epsilon.
        /// </summary>
        const float PastCorner = 0.02f;

        static readonly Vector2 WeightSize = new(0.74f, 0.7f); // The Block footprint.

        static ConvexPolygon WeightAt(float x, float y) => ConvexPolygon.FromRect(new Rect(x - WeightSize.x * 0.5f, y - WeightSize.y * 0.5f, WeightSize.x, WeightSize.y));

        static SheetLayers Stack(params Fold[] folds)
        {
            var stack = SheetLayers.Flat;
            for (int i = 0; i < folds.Length; i++)
                stack = stack.Apply(folds[i], i, out _);
            return stack;
        }

        static FoldEffect Effect(SheetLayers stack, Fold fold)
        {
            stack.Apply(fold, stack.Layers.Count, out var effect);
            return effect;
        }

        static List<ConvexPolygon> Weights(params ConvexPolygon[] weights) => new(weights);

        // ----- Clear -----

        [Test]
        public void Clear_NoObstacles_IsAlwaysClear()
        {
            Assert.IsTrue(FoldObstacles.Clear(Effect(SheetLayers.Flat, new Fold(FoldAnchor.EdgeEast, 5f)), Weights()));
        }

        [Test]
        public void Clear_LiftedOverObstacle_IsNotClear()
        {
            // East fold depth 2 lifts x in [3.5, 5.5]; a weight at x = 4.5 is on it.
            Assert.IsFalse(FoldObstacles.Clear(Effect(SheetLayers.Flat, new Fold(FoldAnchor.EdgeEast, 2f)), Weights(WeightAt(4.5f, 0f))));
        }

        [Test]
        public void Clear_LandedOnObstacle_IsNotClear()
        {
            // The same fold lands on x in [1.5, 3.5]; a weight at x = 2.5 is under it.
            Assert.IsFalse(FoldObstacles.Clear(Effect(SheetLayers.Flat, new Fold(FoldAnchor.EdgeEast, 2f)), Weights(WeightAt(2.5f, 0f))));
        }

        [Test]
        public void Clear_ObstacleAway_IsClear()
        {
            Assert.IsTrue(FoldObstacles.Clear(Effect(SheetLayers.Flat, new Fold(FoldAnchor.EdgeEast, 2f)), Weights(WeightAt(0f, 0f), WeightAt(-4f, 3f))));
        }

        // ----- MaxDepth, flat sheet, closed forms -----

        [Test]
        public void MaxDepth_NoObstacles_IsInfinite()
        {
            Assert.IsTrue(float.IsPositiveInfinity(FoldObstacles.MaxDepth(FoldAnchor.EdgeEast, SheetLayers.Flat, Weights())));
            Assert.IsTrue(float.IsPositiveInfinity(FoldObstacles.MaxDepth(FoldAnchor.CornerNorthEast, SheetLayers.Flat, Weights())));
        }

        [TestCase(FoldAnchor.EdgeEast, 1.5f, 0f)]
        [TestCase(FoldAnchor.EdgeWest, -2f, 1f)]
        [TestCase(FoldAnchor.EdgeNorth, 0f, 2.25f)]
        [TestCase(FoldAnchor.EdgeSouth, 3f, -1f)]
        public void MaxDepth_EdgeFold_Flat_IsHalfTheDistanceToTheWeightsNearEdge(FoldAnchor anchor, float wx, float wy)
        {
            // The flap's far edge is at 2d in from the sheet edge: contact when 2d = distance to the weight's near edge.
            var weight = WeightAt(wx, wy);
            var outward = anchor.EdgeDirection().ToVector();
            var halfExtent = Mathf.Abs(Vector2.Dot(SheetGeometry.HalfSize, outward));
            var nearEdge = float.PositiveInfinity;
            foreach (var v in weight.Vertices)
                nearEdge = Mathf.Min(nearEdge, halfExtent - Vector2.Dot(v, outward));
            var expected = nearEdge * 0.5f;

            var clamp = FoldObstacles.MaxDepth(anchor, SheetLayers.Flat, Weights(weight));
            Assert.AreEqual(expected, clamp, Eps);
            AssertClampIsFirstContact(anchor, SheetLayers.Flat, Weights(weight), clamp);
        }

        [TestCase(FoldAnchor.CornerNorthEast, 1f, 1f)]
        [TestCase(FoldAnchor.CornerNorthEast, 0f, 0f)]
        [TestCase(FoldAnchor.CornerNorthWest, -3f, 2f)]
        [TestCase(FoldAnchor.CornerSouthEast, 2f, 0.5f)]
        [TestCase(FoldAnchor.CornerSouthWest, -4f, -3f)]
        public void MaxDepth_CornerFold_Flat_IsTheLargerOfTheTwoDistancesToTheWeightsNearestCorner(FoldAnchor anchor, float wx, float wy)
        {
            // The landed triangle's outer edges are the legs x = hx - d and y = hy - d (plan review B1): the first leg
            // to reach the weight's nearest corner, (a, b) in from the sheet corner, decides: d = max(a, b).
            var weight = WeightAt(wx, wy);
            var signs = anchor.CornerSigns();
            var a = float.PositiveInfinity;
            var b = float.PositiveInfinity;
            foreach (var v in weight.Vertices)
            {
                a = Mathf.Min(a, HW - signs.x * v.x);
                b = Mathf.Min(b, HH - signs.y * v.y);
            }
            var expected = Mathf.Max(a, b);

            var clamp = FoldObstacles.MaxDepth(anchor, SheetLayers.Flat, Weights(weight));
            Assert.AreEqual(expected, clamp, Eps);
            AssertClampIsFirstContact(anchor, SheetLayers.Flat, Weights(weight), clamp);
        }

        [Test]
        public void MaxDepth_WeightBeyondACornerFoldsReach_DoesNotBindBeforeTheOverhangBound()
        {
            // The SW weight is 9.5 + 7.6 = 17.1 in from the NE corner along the legs; the corner fold overhangs at 8.5 first.
            var weights = Weights(WeightAt(-4f, -3f));
            var clamp = FoldObstacles.MaxDepth(FoldAnchor.CornerNorthEast, SheetLayers.Flat, weights);
            Assert.GreaterOrEqual(clamp, SheetLayers.Flat.MaxDepth(FoldAnchor.CornerNorthEast), "finite, but the overhang bound is the smaller of the two");
            AssertClampIsFirstContact(FoldAnchor.CornerNorthEast, SheetLayers.Flat, weights, clamp);
        }

        [Test]
        public void MaxDepth_WeightTouchingTheAnchoringEdge_IsZero()
        {
            var weight = ConvexPolygon.FromRect(Rect.MinMaxRect(HW - 0.7f, -0.35f, HW, 0.35f)); // Against the east edge.
            Assert.AreEqual(0f, FoldObstacles.MaxDepth(FoldAnchor.EdgeEast, SheetLayers.Flat, Weights(weight)), Eps);
        }

        [Test]
        public void MaxDepth_TakesTheNearestOfSeveralWeights()
        {
            var weights = Weights(WeightAt(-3f, 0f), WeightAt(2f, 2f), WeightAt(4f, -3f));
            var clamp = FoldObstacles.MaxDepth(FoldAnchor.EdgeEast, SheetLayers.Flat, weights);
            Assert.AreEqual((HW - 4.37f) * 0.5f, clamp, Eps, "the weight at x = 4 is nearest the east edge");
            AssertClampIsFirstContact(FoldAnchor.EdgeEast, SheetLayers.Flat, weights, clamp);
        }

        // ----- MaxDepth against the scan oracle, stacked -----

        [Test]
        public void MaxDepth_AfterAWestFold_ClampsAnEastFoldBeforeItLandsOnTheWeight()
        {
            // West fold depth 1: base x in [-4.5, 5.5], west flap landed on x in [-4.5, -3.5]. Weight at x = 1.
            var stack = Stack(new Fold(FoldAnchor.EdgeWest, 1f));
            var weights = Weights(WeightAt(1f, 0f));
            var clamp = FoldObstacles.MaxDepth(FoldAnchor.EdgeEast, stack, weights);
            Assert.AreEqual((HW - 1.37f) * 0.5f, clamp, Eps, "the base still reaches the east edge, so the flat rule holds");
            AssertClampIsFirstContact(FoldAnchor.EdgeEast, stack, weights, clamp);
        }

        [Test]
        public void MaxDepth_FoldFurtherFromAnEmptiedEdge_LandsTheStackNotTheBand()
        {
            // East fold depth 1 emptied x in [4.5, 5.5]; the stack near the east edge starts at x = 4.5 (base + flap).
            // A second east fold at crease c lifts [4.5, c]... i.e. x in [c, 4.5] and lands it on [2c - 4.5, c]:
            // contact with a weight at x in [1.63, 2.37] when 2c - 4.5 = 2.37, c = 3.435, depth = 5.5 - 3.435 = 2.065.
            // The conservative "within 2d of the edge" band would stop at d = (5.5 - 2.37) / 2 = 1.565.
            var stack = Stack(new Fold(FoldAnchor.EdgeEast, 1f));
            var weights = Weights(WeightAt(2f, 0f));
            var clamp = FoldObstacles.MaxDepth(FoldAnchor.EdgeEast, stack, weights);
            Assert.AreEqual(2.065f, clamp, Eps);
            AssertClampIsFirstContact(FoldAnchor.EdgeEast, stack, weights, clamp);
        }

        [Test]
        public void MaxDepth_WeightOnALandedFlap_IsLiftedWithIt()
        {
            // North fold depth 2: the flap lies on y in [0.25, 2.25]. A weight sitting on that flap (its visible piece
            // is where the flap lies) must not be lifted by a west fold reaching it: x in [-4.37, -3.63].
            var stack = Stack(new Fold(FoldAnchor.EdgeNorth, 2f));
            var weights = Weights(WeightAt(-4f, 1.25f));
            var clamp = FoldObstacles.MaxDepth(FoldAnchor.EdgeWest, stack, weights);
            Assert.AreEqual((HW - 4.37f) * 0.5f, clamp, Eps, "landing binds first: the flap's far edge reaches the weight at 2d");
            AssertClampIsFirstContact(FoldAnchor.EdgeWest, stack, weights, clamp);
        }

        [TestCase(FoldAnchor.CornerNorthEast)]
        [TestCase(FoldAnchor.CornerSouthWest)]
        [TestCase(FoldAnchor.EdgeSouth)]
        [TestCase(FoldAnchor.EdgeEast)]
        public void MaxDepth_MixedStack_MatchesTheScan(FoldAnchor anchor)
        {
            // North fold, then a NW corner fold, then a shallow east fold: pieces of every kind on the desk.
            var stack = Stack(new Fold(FoldAnchor.EdgeNorth, 1.5f), new Fold(FoldAnchor.CornerNorthWest, 2.5f), new Fold(FoldAnchor.EdgeEast, 0.75f));
            var weights = Weights(WeightAt(0.5f, -0.5f), WeightAt(-3f, 1f), WeightAt(3.5f, -2.5f));
            var clamp = FoldObstacles.MaxDepth(anchor, stack, weights);
            AssertClampIsFirstContact(anchor, stack, weights, clamp);
        }

        [Test]
        public void MaxDepth_IsNeverNegative_AndClearAtTheClamp()
        {
            var weight = ConvexPolygon.FromRect(Rect.MinMaxRect(HW - 0.0001f, -0.35f, HW, 0.35f)); // A hair inside the edge.
            var clamp = FoldObstacles.MaxDepth(FoldAnchor.EdgeEast, SheetLayers.Flat, Weights(weight));
            Assert.GreaterOrEqual(clamp, 0f);
            Assert.IsTrue(FoldObstacles.Clear(Effect(SheetLayers.Flat, new Fold(FoldAnchor.EdgeEast, clamp)), Weights(weight)));
        }

        // ----- Other folds' pieces as obstacles (stacking off; Aaron, 2026-09-16) -----

        /// <summary>The stack after <paramref name="folds"/>, their effects, and every lifted and landed piece - what SheetFolds hands MaxDepth with stacking off.</summary>
        static (SheetLayers stack, List<FoldEffect> effects, List<ConvexPolygon> pieces) Committed(params Fold[] folds)
        {
            var stack = SheetLayers.Flat;
            var effects = new List<FoldEffect>();
            var pieces = new List<ConvexPolygon>();
            for (int i = 0; i < folds.Length; i++)
            {
                stack = stack.Apply(folds[i], i, out var effect);
                effects.Add(effect);
                pieces.AddRange(effect.Lifted);
                pieces.AddRange(effect.Landed);
            }
            return (stack, effects, pieces);
        }

        [Test]
        public void MaxDepth_OtherFlap_EastFoldStopsWhereItWouldMeetTheWestFlap()
        {
            // West fold depth 2 lands on x in [-3.5, -1.5]; an east Flap's far edge is at 5.5 - 2d: contact at d = 3.5.
            var (stack, effects, pieces) = Committed(new Fold(FoldAnchor.EdgeWest, 2f));
            var clamp = FoldObstacles.MaxDepth(FoldAnchor.EdgeEast, stack, pieces);
            Assert.AreEqual(3.5f - FoldObstacles.ContactTolerance, clamp, Eps);
            AssertClampIsFirstOverlap(FoldAnchor.EdgeEast, stack, effects, clamp);
        }

        [Test]
        public void MaxDepth_OtherFlap_CornerFoldStopsAtTheOtherCornerFlapsEdge()
        {
            // NW corner fold depth 4: its hole and its Flap both lie against x = -1.5. The NE Flap's leg x = 5.5 - d reaches it at d = 7.
            var (stack, effects, pieces) = Committed(new Fold(FoldAnchor.CornerNorthWest, 4f));
            var clamp = FoldObstacles.MaxDepth(FoldAnchor.CornerNorthEast, stack, pieces);
            Assert.AreEqual(7f - FoldObstacles.ContactTolerance, clamp, Eps);
            AssertClampIsFirstOverlap(FoldAnchor.CornerNorthEast, stack, effects, clamp);
        }

        [Test]
        public void MaxDepth_OtherFlap_AFoldAcrossTheOtherFlapIsDead()
        {
            // North fold depth 1 lies across the full width; any east fold would lift part of it, so the east edge is inert.
            var (stack, _, pieces) = Committed(new Fold(FoldAnchor.EdgeNorth, 1f));
            Assert.AreEqual(0f, FoldObstacles.MaxDepth(FoldAnchor.EdgeEast, stack, pieces), Eps);
        }

        [Test]
        public void MaxDepth_OtherFold_TheHoleItLeftCountsToo()
        {
            // The hole alone (the west fold's lifted strip, x in [-5.5, -3.5]) binds an east fold where its far edge reaches x = -3.5: d = 4.5.
            var (stack, effects, _) = Committed(new Fold(FoldAnchor.EdgeWest, 2f));
            var holeOnly = new List<ConvexPolygon>(effects[0].Lifted);
            Assert.AreEqual(4.5f - FoldObstacles.ContactTolerance, FoldObstacles.MaxDepth(FoldAnchor.EdgeEast, stack, holeOnly), Eps);
        }

        [TestCase(FoldAnchor.EdgeEast)]
        [TestCase(FoldAnchor.EdgeSouth)]
        [TestCase(FoldAnchor.CornerSouthWest)]
        [TestCase(FoldAnchor.CornerNorthWest)]
        [TestCase(FoldAnchor.CornerSouthEast)]
        public void MaxDepth_OtherFolds_IndependentStack_MatchesTheIndependenceScan(FoldAnchor anchor)
        {
            // A west fold and a NE corner fold, independent of each other - a stack stacking-off allows.
            var (stack, effects, pieces) = Committed(new Fold(FoldAnchor.EdgeWest, 1.5f), new Fold(FoldAnchor.CornerNorthEast, 2f));
            Assert.IsTrue(FoldValidity.Independent(effects[0], effects[1]));
            var clamp = FoldObstacles.MaxDepth(anchor, stack, pieces);
            AssertClampIsFirstOverlap(anchor, stack, effects, clamp);
        }

        /// <summary>
        /// The oracle for other folds as obstacles: scanning depth from 0 up to the overhang bound, the first depth
        /// at which the fold is not independent of every committed fold must be the clamp - and the clamped fold
        /// is independent of them all while a hair deeper is not.
        /// </summary>
        static void AssertClampIsFirstOverlap(FoldAnchor anchor, SheetLayers stack, List<FoldEffect> committed, float clamp)
        {
            const float step = 0.005f;
            var bound = stack.MaxDepth(anchor);
            var firstOverlap = float.PositiveInfinity;
            for (var d = 0f; d <= bound; d += step)
            {
                if (!IndependentOfAll(Effect(stack, new Fold(anchor, d)), committed))
                {
                    firstOverlap = d;
                    break;
                }
            }

            if (float.IsPositiveInfinity(firstOverlap))
            {
                Assert.GreaterOrEqual(clamp, bound - FoldObstacles.ContactTolerance, "no overlap before the overhang bound: the clamp must not bind first");
                return;
            }
            Assert.AreEqual(firstOverlap, clamp, step * 2f, "the clamp is the first overlap");
            Assert.IsTrue(IndependentOfAll(Effect(stack, new Fold(anchor, clamp)), committed), "the clamped fold is independent of every committed fold");
            Assert.IsFalse(IndependentOfAll(Effect(stack, new Fold(anchor, clamp + PastCorner)), committed), "a little deeper overlaps one (corner-on contacts need more than a hair to show area)");
        }

        static bool IndependentOfAll(in FoldEffect effect, List<FoldEffect> committed)
        {
            foreach (var other in committed)
            {
                if (!FoldValidity.Independent(effect, other))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The oracle: scanning depth from 0 up to the overhang bound, the first depth at which the fold is not
        /// Clear must be the clamp (within a step) - and the clamp itself is Clear while a hair deeper is not.
        /// </summary>
        static void AssertClampIsFirstContact(FoldAnchor anchor, SheetLayers stack, List<ConvexPolygon> weights, float clamp)
        {
            const float step = 0.005f;
            var bound = stack.MaxDepth(anchor);
            var firstContact = float.PositiveInfinity;
            for (var d = 0f; d <= bound; d += step)
            {
                if (!FoldObstacles.Clear(Effect(stack, new Fold(anchor, d)), weights))
                {
                    firstContact = d;
                    break;
                }
            }

            if (float.IsPositiveInfinity(firstContact))
            {
                Assert.GreaterOrEqual(clamp, bound - FoldObstacles.ContactTolerance, "no contact before the overhang bound: the clamp must not bind first");
                return;
            }
            // The first failing sample lies up to one step past the true contact; the clamp sits ContactTolerance under it.
            Assert.AreEqual(firstContact, clamp, step * 2f, "the clamp is the first contact");
            Assert.IsTrue(FoldObstacles.Clear(Effect(stack, new Fold(anchor, clamp)), weights), "the clamped fold is clear");
            Assert.IsFalse(FoldObstacles.Clear(Effect(stack, new Fold(anchor, clamp + PastContact)), weights), "a hair deeper touches");
        }
    }
}
