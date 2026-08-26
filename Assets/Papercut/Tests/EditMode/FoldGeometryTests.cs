using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class FoldGeometryTests
    {
        const float W = 11f, H = 8.5f, HW = 5.5f, HH = 4.25f;
        const float Eps = 1e-4f;

        static void AssertVector(Vector2 expected, Vector2 actual, string label = "")
        {
            Assert.AreEqual(expected.x, actual.x, Eps, label + " x");
            Assert.AreEqual(expected.y, actual.y, Eps, label + " y");
        }

        static void AssertRect(Rect expected, Rect actual, string label = "")
        {
            AssertVector(expected.min, actual.min, label + " min");
            AssertVector(expected.max, actual.max, label + " max");
        }

        /// <summary>Managed 2D rotation (Quaternion.Euler is native and unavailable to the CLI test runner).</summary>
        static Vector2 Rotate(Vector2 p, float degrees)
        {
            var r = degrees * Mathf.Deg2Rad;
            var c = Mathf.Cos(r);
            var s = Mathf.Sin(r);
            return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y);
        }

        // ----- creases -----

        [TestCase(FoldAnchor.EdgeNorth, 0f, HH - 2f, 0f, 1f)]
        [TestCase(FoldAnchor.EdgeSouth, 0f, -HH + 2f, 0f, -1f)]
        [TestCase(FoldAnchor.EdgeEast, HW - 2f, 0f, 1f, 0f)]
        [TestCase(FoldAnchor.EdgeWest, -HW + 2f, 0f, -1f, 0f)]
        public void EdgeCrease_LiesDepthInFromItsEdge_FlapTowardEdge(FoldAnchor anchor, float px, float py, float nx, float ny)
        {
            var crease = FoldGeometry.CreaseOf(new Fold(anchor, 2f));

            AssertVector(new Vector2(px, py), crease.Point, "point");
            AssertVector(new Vector2(nx, ny), crease.FlapNormal, "normal");
            Assert.AreEqual(0f, Vector2.Dot(crease.Direction, crease.FlapNormal), Eps);
        }

        [TestCase(FoldAnchor.CornerNorthEast, 1f, 1f)]
        [TestCase(FoldAnchor.CornerNorthWest, -1f, 1f)]
        [TestCase(FoldAnchor.CornerSouthEast, 1f, -1f)]
        [TestCase(FoldAnchor.CornerSouthWest, -1f, -1f)]
        public void CornerCrease_At45Degrees_ThroughBothEdgePoints(FoldAnchor anchor, float sx, float sy)
        {
            const float d = 3f;
            var crease = FoldGeometry.CreaseOf(new Fold(anchor, d));
            var onEdgeX = new Vector2(sx * HW - sx * d, sy * HH);
            var onEdgeY = new Vector2(sx * HW, sy * HH - sy * d);

            Assert.AreEqual(0f, crease.SignedDistance(onEdgeX), Eps);
            Assert.AreEqual(0f, crease.SignedDistance(onEdgeY), Eps);
            Assert.Greater(crease.SignedDistance(new Vector2(sx * HW, sy * HH)), 0f, "corner is on the flap side");
            Assert.AreEqual(1f, Mathf.Abs(crease.Direction.x) / Mathf.Abs(crease.Direction.y), Eps, "45 degrees");
        }

        // ----- regions -----

        [Test]
        public void EdgeFold_FlapArea_IsDepthTimesWidth()
        {
            Assert.AreEqual(3f * W, FoldGeometry.FlapRegion(new Fold(FoldAnchor.EdgeSouth, 3f)).Area, Eps);
            Assert.AreEqual(3f * H, FoldGeometry.FlapRegion(new Fold(FoldAnchor.EdgeEast, 3f)).Area, Eps);
        }

        [Test]
        public void CornerFold_FlapArea_IsHalfSquare()
        {
            Assert.AreEqual(2f * 2f / 2f, FoldGeometry.FlapRegion(new Fold(FoldAnchor.CornerNorthEast, 2f)).Area, Eps);
        }

        [Test]
        public void EdgeFold_LandedRegion_IsMirrorStrip()
        {
            var landed = FoldGeometry.LandedRegion(new Fold(FoldAnchor.EdgeSouth, 3f)).Bounds;

            AssertRect(Rect.MinMaxRect(-HW, -HH + 3f, HW, -HH + 6f), landed);
        }

        [Test]
        public void CornerFold_LandedRegion_HasReflectedCornerInside()
        {
            var landed = FoldGeometry.LandedRegion(new Fold(FoldAnchor.CornerNorthEast, 2f));

            Assert.AreEqual(2f, landed.Area, Eps);
            AssertRect(Rect.MinMaxRect(HW - 2f, HH - 2f, HW, HH), landed.Bounds);
        }

        [Test]
        public void MaxDepth_KeepsLandedFlapOnTheSheet()
        {
            foreach (FoldAnchor anchor in System.Enum.GetValues(typeof(FoldAnchor)))
            {
                var fold = new Fold(anchor, FoldGeometry.MaxDepth(anchor));
                var landed = FoldGeometry.LandedRegion(fold).Bounds;
                Assert.IsTrue(FoldGeometry.Contains(FoldGeometry.Sheet, landed), $"{anchor}: {landed}");
                Assert.IsTrue(new Fold(anchor, FoldGeometry.MaxDepth(anchor) + 0.1f).Overhangs, $"{anchor} just past max overhangs");
                Assert.IsFalse(fold.Overhangs);
            }
            Assert.AreEqual(H / 2f, FoldGeometry.MaxDepth(FoldAnchor.EdgeNorth), Eps);
            Assert.AreEqual(W / 2f, FoldGeometry.MaxDepth(FoldAnchor.EdgeEast), Eps);
            Assert.AreEqual(H, FoldGeometry.MaxDepth(FoldAnchor.CornerSouthWest), Eps);
        }

        [Test]
        public void Clamped_LimitsDepthToMax()
        {
            Assert.AreEqual(H / 2f, new Fold(FoldAnchor.EdgeSouth, 7f).Clamped.Depth, Eps);
            Assert.AreEqual(2f, new Fold(FoldAnchor.EdgeSouth, 2f).Clamped.Depth, Eps);
        }

        // ----- seam -----

        [Test]
        public void Seam_EdgeFold_IsTheReflectedAnchorEdge()
        {
            var seams = FoldGeometry.SeamSegments(new Fold(FoldAnchor.EdgeSouth, 3f)); // edge lands at y = -4.25 + 6

            Assert.AreEqual(1, seams.Count);
            Assert.AreEqual(1.75f, seams[0].a.y, Eps);
            Assert.AreEqual(1.75f, seams[0].b.y, Eps);
            Assert.AreEqual(W, Vector2.Distance(seams[0].a, seams[0].b), Eps);
        }

        [Test]
        public void Seam_CornerFold_IsTwoLegsMeetingAtTheReflectedCorner()
        {
            var seams = FoldGeometry.SeamSegments(new Fold(FoldAnchor.CornerNorthEast, 3f));
            var reflectedCorner = new Vector2(HW - 3f, HH - 3f);

            Assert.AreEqual(2, seams.Count);
            foreach (var (a, b) in seams)
            {
                Assert.IsTrue(Vector2.Distance(a, reflectedCorner) < Eps || Vector2.Distance(b, reflectedCorner) < Eps, "each leg ends at the reflected corner");
                Assert.AreEqual(3f, Vector2.Distance(a, b), Eps);
            }
            var far = seams.Select(s => Vector2.Distance(s.a, reflectedCorner) < Eps ? s.b : s.a).ToList();
            CollectionAssert.AreEquivalent(new[] { new Vector2(HW - 3f, HH), new Vector2(HW, HH - 3f) }.Select(v => v.ToString()), far.Select(v => v.ToString()));
        }

        [Test]
        public void Seam_AtMaxDepth_CoincidesWithTheFarEdge()
        {
            var seams = FoldGeometry.SeamSegments(new Fold(FoldAnchor.EdgeSouth, H / 2f));

            Assert.AreEqual(1, seams.Count);
            Assert.AreEqual(HH, seams[0].a.y, Eps);
        }

        [Test]
        public void DistanceToSeam_MeasuresToTheNearestLeg()
        {
            var fold = new Fold(FoldAnchor.CornerNorthEast, 3f); // legs: x = 2.5 for y in [1.25, 4.25]; y = 1.25 for x in [2.5, 5.5]
            Assert.AreEqual(0f, FoldGeometry.DistanceToSeam(fold, new Vector2(2.5f, 3f)), Eps);
            Assert.AreEqual(0.5f, FoldGeometry.DistanceToSeam(fold, new Vector2(4f, 0.75f)), Eps);
            Assert.AreEqual(0f, FoldGeometry.DistanceToSeam(new Fold(FoldAnchor.EdgeSouth, 3f), new Vector2(0f, 1.75f)), Eps);
        }

        [Test]
        public void CornerFold_DeeperThanHeight_Overhangs()
        {
            // Geometry only: depths past MaxDepth cannot be made through input (Aaron: no overhang).
            var landed = FoldGeometry.LandedRegion(new Fold(FoldAnchor.CornerNorthEast, 10f)).Bounds;

            Assert.Less(landed.yMin, -HH, "hangs below the sheet");
            Assert.AreEqual(HW - 10f, landed.xMin, Eps);
            Assert.AreEqual(HW + H - 10f, landed.xMax, Eps);
        }

        [Test]
        public void ReflectedSheet_IsAxisAlignedWithSameArea()
        {
            foreach (var anchor in new[] { FoldAnchor.EdgeNorth, FoldAnchor.CornerSouthWest })
            {
                var poly = ConvexPolygon.FromRect(FoldGeometry.Sheet).Reflect(FoldGeometry.CreaseOf(new Fold(anchor, 3f)));
                Assert.AreEqual(W * H, poly.Bounds.width * poly.Bounds.height, Eps, anchor.ToString());
            }
        }

        // ----- hidden rect -----

        [Test]
        public void HiddenRect_EdgeFold_IsStripOfTwiceDepth()
        {
            AssertRect(Rect.MinMaxRect(-HW, HH - 6f, HW, HH), FoldGeometry.HiddenRect(new Fold(FoldAnchor.EdgeNorth, 3f)));
            AssertRect(Rect.MinMaxRect(-HW, -HH, -HW + 4f, HH), FoldGeometry.HiddenRect(new Fold(FoldAnchor.EdgeWest, 2f)));
        }

        [Test]
        public void HiddenRect_EdgeFold_ClampsToSheet()
        {
            AssertRect(FoldGeometry.Sheet, FoldGeometry.HiddenRect(new Fold(FoldAnchor.EdgeNorth, 5f)));
        }

        [Test]
        public void HiddenRect_CornerFold_IsSquare()
        {
            AssertRect(Rect.MinMaxRect(HW - 3f, HH - 3f, HW, HH), FoldGeometry.HiddenRect(new Fold(FoldAnchor.CornerNorthEast, 3f)));
            AssertRect(Rect.MinMaxRect(-HW, -HH, -HW + 3f, -HH + 3f), FoldGeometry.HiddenRect(new Fold(FoldAnchor.CornerSouthWest, 3f)));
        }

        [Test]
        public void HiddenRect_CornerFold_EqualsFlapUnionLanded_SampledPoints()
        {
            // Sample the sheet: a point is hidden iff it is on the flap side or under the landed region.
            foreach (var d in new[] { 2f, 6f, 10f })
            {
                var fold = new Fold(FoldAnchor.CornerNorthEast, d);
                var crease = FoldGeometry.CreaseOf(fold);
                var landed = FoldGeometry.LandedRegion(fold);
                var hidden = FoldGeometry.HiddenRect(fold);
                for (float x = -HW + 0.25f; x < HW; x += 0.5f)
                for (float y = -HH + 0.25f; y < HH; y += 0.5f)
                {
                    var p = new Vector2(x, y);
                    var onFlap = crease.SignedDistance(p) > 0f;
                    var underLanded = landed.Overlaps(new Rect(p - Vector2.one * 0.01f, Vector2.one * 0.02f));
                    Assert.AreEqual(onFlap || underLanded, hidden.Contains(p), $"d={d} p={p}");
                }
            }
        }

        // ----- back pose -----

        [Test]
        public void BackPose_HorizontalCrease_RotatesHalfTurnAboutCreaseHeight()
        {
            var fold = new Fold(FoldAnchor.EdgeSouth, 2f); // crease at y = -2.25
            var (position, rotation) = FoldGeometry.BackPose(fold);

            Assert.AreEqual(180f, Mathf.Repeat(rotation, 360f), Eps);
            AssertVector(new Vector2(0f, -4.5f), position);

            // Back (x, y) -> Front (-x, y) -> reflected across y = -2.25 -> (-x, -4.5 - y).
            var b = new Vector2(1f, -3f);
            AssertVector(new Vector2(-1f, -1.5f), FoldGeometry.BackToLanded(fold, b));
            AssertVector(new Vector2(-1f, -1.5f), Apply(position, rotation, b));
        }

        [Test]
        public void BackPose_VerticalCrease_IsPureTranslation()
        {
            var fold = new Fold(FoldAnchor.EdgeEast, 2f); // crease at x = 3.5
            var (position, rotation) = FoldGeometry.BackPose(fold);

            Assert.AreEqual(0f, Mathf.Repeat(rotation + 0.5f, 360f) - 0.5f, Eps);
            AssertVector(new Vector2(7f, 0f), position);
            var b = new Vector2(-4f, 1f); // beneath Front (4, 1) -> reflected to (3, 1)
            AssertVector(new Vector2(3f, 1f), FoldGeometry.BackToLanded(fold, b));
            AssertVector(new Vector2(3f, 1f), Apply(position, rotation, b));
        }

        [Test]
        public void BackPose_CornerCrease_IsQuarterTurn()
        {
            var fold = new Fold(FoldAnchor.CornerNorthEast, 3f);
            var (position, rotation) = FoldGeometry.BackPose(fold);

            Assert.AreEqual(90f, Mathf.Abs(Mathf.DeltaAngle(0f, rotation)), Eps);
            foreach (var b in new[] { new Vector2(-5f, 4f), new Vector2(-3.5f, 2.5f), new Vector2(1f, -1f) })
                AssertVector(FoldGeometry.BackToLanded(fold, b), Apply(position, rotation, b), b.ToString());
        }

        [Test]
        public void LandedToBack_InvertsBackToLanded()
        {
            var fold = new Fold(FoldAnchor.CornerSouthWest, 4f);
            var b = new Vector2(2f, -1f);

            AssertVector(b, FoldGeometry.LandedToBack(fold, FoldGeometry.BackToLanded(fold, b)));
        }

        static Vector2 Apply(Vector2 position, float rotation, Vector2 p) => position + Rotate(p, rotation);

        // ----- drag -----

        [Test]
        public void DepthForDragPoint_Edge_IsHalfInwardDistance()
        {
            Assert.AreEqual(1.5f, FoldGeometry.DepthForDragPoint(FoldAnchor.EdgeSouth, new Vector2(2f, -HH + 3f)), Eps);
            Assert.AreEqual(0f, FoldGeometry.DepthForDragPoint(FoldAnchor.EdgeSouth, new Vector2(2f, -HH - 1f)), Eps, "outside clamps to 0");
            Assert.AreEqual(H / 2f, FoldGeometry.DepthForDragPoint(FoldAnchor.EdgeSouth, new Vector2(0f, 100f)), Eps, "clamps so the Flap's edge stops at the far edge");
        }

        [Test]
        public void DepthForDragPoint_Corner_IsMeanInwardDistance()
        {
            Assert.AreEqual(2f, FoldGeometry.DepthForDragPoint(FoldAnchor.CornerNorthEast, new Vector2(HW - 3f, HH - 1f)), Eps);
            Assert.AreEqual(2f, FoldGeometry.DepthForDragPoint(FoldAnchor.CornerSouthWest, new Vector2(-HW + 2f, -HH + 2f)), Eps);
        }

        [Test]
        public void DistanceToCrease_MeasuresToSegmentInsideSheet()
        {
            var fold = new Fold(FoldAnchor.EdgeSouth, 2f); // crease y = -2.25, x in [-5.5, 5.5]
            Assert.AreEqual(0f, FoldGeometry.DistanceToCrease(fold, new Vector2(1f, -2.25f)), Eps);
            Assert.AreEqual(0.5f, FoldGeometry.DistanceToCrease(fold, new Vector2(1f, -1.75f)), Eps);
            Assert.AreEqual(2f, FoldGeometry.DistanceToCrease(fold, new Vector2(7.5f, -2.25f)), Eps, "beyond the segment end");
        }

        // ----- coverage -----

        [Test]
        public void Coverage_Front_Disjoint_IsUncoveredWhole()
        {
            var result = FoldGeometry.Coverage(Rect.MinMaxRect(0f, 0f, 1f, 1f), new Fold(FoldAnchor.EdgeSouth, 1f), SheetFace.Front);

            Assert.AreEqual(FoldCoverage.Uncovered, result.Coverage);
            Assert.IsTrue(result.IsWhole);
        }

        [Test]
        public void Coverage_Front_Inside_IsCoveredNone()
        {
            var result = FoldGeometry.Coverage(Rect.MinMaxRect(0f, -4f, 1f, -3f), new Fold(FoldAnchor.EdgeSouth, 1f), SheetFace.Front);

            Assert.AreEqual(FoldCoverage.Covered, result.Coverage);
            Assert.IsTrue(result.IsNone);
        }

        [Test]
        public void Coverage_Front_Straddling_KeepsVisibleRemainder()
        {
            var footprint = Rect.MinMaxRect(0f, -3f, 1f, -1f); // hidden strip is y < -2.25
            var result = FoldGeometry.Coverage(footprint, new Fold(FoldAnchor.EdgeSouth, 1f), SheetFace.Front);

            Assert.AreEqual(FoldCoverage.Partial, result.Coverage);
            Assert.AreEqual(1, result.VisibleParts.Count);
            AssertRect(Rect.MinMaxRect(0f, -2.25f, 1f, -1f), result.VisibleParts[0].Bounds);
        }

        [Test]
        public void Coverage_Front_CornerCutsL_ShapedRemainderIntoRects()
        {
            var footprint = Rect.MinMaxRect(2f, 1f, 5f, 4f); // square hidden region: x > 2.5, y > 1.25
            var result = FoldGeometry.Coverage(footprint, new Fold(FoldAnchor.CornerNorthEast, 3f), SheetFace.Front);

            Assert.AreEqual(FoldCoverage.Partial, result.Coverage);
            var total = 0f;
            foreach (var part in result.VisibleParts)
                total += part.Area;
            Assert.AreEqual(9f - 2.5f * 2.75f, total, Eps);
            Assert.AreEqual(2, result.VisibleParts.Count);
        }

        [Test]
        public void Coverage_Back_OnFlap_IsExposedWhole()
        {
            // South edge fold d = 2: flap is Front y < -2.25; Back (x, y) lies beneath Front (-x, y), so the same y range.
            var result = FoldGeometry.Coverage(Rect.MinMaxRect(1f, -4f, 2f, -3f), new Fold(FoldAnchor.EdgeSouth, 2f), SheetFace.Back);

            Assert.AreEqual(FoldCoverage.Covered, result.Coverage);
            Assert.IsTrue(result.IsWhole);
        }

        [Test]
        public void Coverage_Back_OffFlap_IsNothing()
        {
            var result = FoldGeometry.Coverage(Rect.MinMaxRect(1f, 0f, 2f, 1f), new Fold(FoldAnchor.EdgeSouth, 2f), SheetFace.Back);

            Assert.AreEqual(FoldCoverage.Uncovered, result.Coverage);
            Assert.IsTrue(result.IsNone);
        }

        [Test]
        public void Coverage_Back_Straddling_IsClippedToFlapSide()
        {
            // East edge fold d = 2: flap is Front x > 3.5 -> Back x < -3.5.
            var result = FoldGeometry.Coverage(Rect.MinMaxRect(-4.5f, 0f, -2.5f, 1f), new Fold(FoldAnchor.EdgeEast, 2f), SheetFace.Back);

            Assert.AreEqual(FoldCoverage.Partial, result.Coverage);
            Assert.AreEqual(1f, result.VisibleParts[0].Area, Eps);
            Assert.AreEqual(-3.5f, result.VisibleParts[0].Bounds.xMax, Eps);
        }

        [Test]
        public void Subtract_RectMinusRect_CoversExactlyTheDifference()
        {
            var parts = FoldGeometry.Subtract(Rect.MinMaxRect(0f, 0f, 4f, 4f), Rect.MinMaxRect(1f, 1f, 2f, 2f));

            Assert.AreEqual(4, parts.Count);
            var total = 0f;
            foreach (var p in parts) total += p.Area;
            Assert.AreEqual(15f, total, Eps);
        }
    }
}
