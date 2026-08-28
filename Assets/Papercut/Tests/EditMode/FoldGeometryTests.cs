using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class FoldGeometryTests
    {
        const float W = 11f, H = 8.5f, HW = 5.5f, HH = 4.25f;
        const float Eps = 1e-4f;

        // ----- creases -----

        [TestCase(FoldAnchor.EdgeNorth, 0f, HH - 2f, 0f, 1f)]
        [TestCase(FoldAnchor.EdgeSouth, 0f, -HH + 2f, 0f, -1f)]
        [TestCase(FoldAnchor.EdgeEast, HW - 2f, 0f, 1f, 0f)]
        [TestCase(FoldAnchor.EdgeWest, -HW + 2f, 0f, -1f, 0f)]
        public void EdgeCrease_LiesDepthInFromItsEdge_FlapTowardEdge(FoldAnchor anchor, float px, float py, float nx, float ny)
        {
            var crease = FoldGeometry.CreaseOf(new Fold(anchor, 2f));

            Assert.AreEqual(0f, crease.SignedDistance(new Vector2(px, py)), Eps, "point on the crease");
            Assert.AreEqual(nx, crease.FlapNormal.x, Eps);
            Assert.AreEqual(ny, crease.FlapNormal.y, Eps);
            Assert.AreEqual(0f, Vector2.Dot(crease.Direction, crease.FlapNormal), Eps, "direction is along the crease");
        }

        [TestCase(FoldAnchor.CornerNorthEast, 1f, 1f)]
        [TestCase(FoldAnchor.CornerNorthWest, -1f, 1f)]
        [TestCase(FoldAnchor.CornerSouthEast, 1f, -1f)]
        [TestCase(FoldAnchor.CornerSouthWest, -1f, -1f)]
        public void CornerCrease_At45Degrees_ThroughBothEdgePoints(FoldAnchor anchor, float sx, float sy)
        {
            var crease = FoldGeometry.CreaseOf(new Fold(anchor, 2f));
            var onEdgeX = new Vector2(sx * (HW - 2f), sy * HH); // 2 in from the corner along the horizontal edge
            var onEdgeY = new Vector2(sx * HW, sy * (HH - 2f)); // 2 in along the vertical edge

            Assert.AreEqual(0f, crease.SignedDistance(onEdgeX), Eps);
            Assert.AreEqual(0f, crease.SignedDistance(onEdgeY), Eps);
            Assert.Greater(crease.SignedDistance(new Vector2(sx * HW, sy * HH)), 0f, "corner is on the flap side");
            Assert.AreEqual(1f, Mathf.Abs(crease.Direction.x) / Mathf.Abs(crease.Direction.y), Eps, "45 degrees");
        }

        [Test]
        public void Reflect_MirrorsAcrossTheCrease()
        {
            var crease = FoldGeometry.CreaseOf(new Fold(FoldAnchor.EdgeSouth, 2f)); // y = -2.25
            var p = crease.Reflect(new Vector2(1f, -4f));
            Assert.AreEqual(1f, p.x, Eps);
            Assert.AreEqual(-0.5f, p.y, Eps);
        }

        // ----- flat limits -----

        [Test]
        public void MaxDepth_FlatSheet()
        {
            Assert.AreEqual(H / 2f, FoldGeometry.MaxDepth(FoldAnchor.EdgeNorth), Eps);
            Assert.AreEqual(W / 2f, FoldGeometry.MaxDepth(FoldAnchor.EdgeEast), Eps);
            Assert.AreEqual(H, FoldGeometry.MaxDepth(FoldAnchor.CornerSouthWest), Eps);
        }

        // ----- drag -----

        [Test]
        public void DepthForDragPoint_Edge_IsHalfInwardDistance()
        {
            Assert.AreEqual(1.5f, FoldGeometry.DepthForDragPoint(FoldAnchor.EdgeSouth, new Vector2(2f, -HH + 3f)), Eps);
            Assert.AreEqual(0f, FoldGeometry.DepthForDragPoint(FoldAnchor.EdgeSouth, new Vector2(2f, -HH - 1f)), Eps, "outside clamps to 0");
            Assert.AreEqual(50f + HH / 2f, FoldGeometry.DepthForDragPoint(FoldAnchor.EdgeSouth, new Vector2(0f, 100f)), Eps, "no upper limit here: the caller holds it at the live MaxDepth");
        }

        [Test]
        public void DepthForDragPoint_Corner_IsMeanInwardDistance()
        {
            Assert.AreEqual(2f, FoldGeometry.DepthForDragPoint(FoldAnchor.CornerNorthEast, new Vector2(HW - 3f, HH - 1f)), Eps);
            Assert.AreEqual(2f, FoldGeometry.DepthForDragPoint(FoldAnchor.CornerSouthWest, new Vector2(-HW + 2f, -HH + 2f)), Eps);
        }

        [Test]
        public void DepthForDragPoint_GrabbedCrease_PutsTheFoldLineUnderTheCursor()
        {
            // A south fold at depth 2 has its crease 2 in. Grab it and drag to 5 in: the line lands at 2d - 2 = 5 -> d = 3.5.
            var depth = FoldGeometry.DepthForDragPoint(FoldAnchor.EdgeSouth, new Vector2(0f, -HH + 5f), grabDepth: 2f);
            Assert.AreEqual(3.5f, depth, Eps);
            Assert.AreEqual(2f, FoldGeometry.DepthForDragPoint(FoldAnchor.EdgeSouth, new Vector2(0f, -HH + 2f), grabDepth: 2f), Eps, "cursor on the crease: unchanged");

            // Corner at depth 2: the crease midpoint is (1, 1) in; drag it to (3, 3) in -> d = 3 + 1 = 4.
            Assert.AreEqual(4f, FoldGeometry.DepthForDragPoint(FoldAnchor.CornerNorthEast, new Vector2(HW - 3f, HH - 3f), grabDepth: 2f), Eps);
        }

        // ----- helpers -----

        [Test]
        public void DistanceToSegment_MeasuresToTheNearestPoint()
        {
            var a = new Vector2(-5.5f, -2.25f);
            var b = new Vector2(5.5f, -2.25f);
            Assert.AreEqual(0f, FoldGeometry.DistanceToSegment(new Vector2(1f, -2.25f), a, b), Eps);
            Assert.AreEqual(0.5f, FoldGeometry.DistanceToSegment(new Vector2(1f, -1.75f), a, b), Eps);
            Assert.AreEqual(2f, FoldGeometry.DistanceToSegment(new Vector2(7.5f, -2.25f), a, b), Eps, "beyond the segment end");
        }

        [Test]
        public void Intersect_And_Contains()
        {
            var inter = FoldGeometry.Intersect(Rect.MinMaxRect(0f, 0f, 4f, 4f), Rect.MinMaxRect(2f, -1f, 6f, 1f));
            Assert.AreEqual(2f, inter.xMin, Eps);
            Assert.AreEqual(4f, inter.xMax, Eps);
            Assert.AreEqual(1f, inter.yMax, Eps);
            Assert.AreEqual(Rect.zero, FoldGeometry.Intersect(Rect.MinMaxRect(0f, 0f, 1f, 1f), Rect.MinMaxRect(2f, 2f, 3f, 3f)));
            Assert.IsTrue(FoldGeometry.Contains(FoldGeometry.Sheet, Rect.MinMaxRect(-HW, -HH, HW, HH)));
            Assert.IsFalse(FoldGeometry.Contains(FoldGeometry.Sheet, Rect.MinMaxRect(-HW, -HH, HW, HH + 0.1f)));
        }
    }
}
