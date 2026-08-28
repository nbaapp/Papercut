using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class SheetGeometryTests
    {
        [Test]
        public void BackToFront_MirrorsXOnly()
        {
            var front = SheetGeometry.BackToFront(new Vector2(2.5f, -1.25f));

            Assert.AreEqual(-2.5f, front.x, 1e-6f);
            Assert.AreEqual(-1.25f, front.y, 1e-6f);
        }

        [Test]
        public void BackToFront_IsItsOwnInverse()
        {
            var point = new Vector2(-3.7f, 4.1f);

            Assert.AreEqual(point, SheetGeometry.BackToFront(SheetGeometry.BackToFront(point)));
        }

        [Test]
        public void BackToFront_EastEdgeOfBackIsWestEdgeOfFront()
        {
            var backEastMid = new Vector2(SheetGeometry.HalfSize.x, 0f);

            var front = SheetGeometry.BackToFront(backEastMid);

            Assert.AreEqual(-SheetGeometry.HalfSize.x, front.x, 1e-6f);
            Assert.AreEqual(0f, front.y, 1e-6f);
        }

        [TestCase(-5.5f, -4.25f, 5.5f, -4.25f)]   // south-west corner -> south-east
        [TestCase(5.5f, -4.25f, -5.5f, -4.25f)]   // south-east corner -> south-west
        [TestCase(-5.5f, 4.25f, 5.5f, 4.25f)]     // north-west corner -> north-east
        [TestCase(5.5f, 4.25f, -5.5f, 4.25f)]     // north-east corner -> north-west
        [TestCase(1.5f, 3f, -1.5f, 3f)]           // asymmetric interior point
        public void BackToFront_MapsCornersAndInteriorPointsExactly(float bx, float by, float fx, float fy)
        {
            var front = SheetGeometry.BackToFront(new Vector2(bx, by));

            Assert.AreEqual(fx, front.x, 1e-6f);
            Assert.AreEqual(fy, front.y, 1e-6f);
        }

        [Test]
        public void BackToFront_Rect_MirrorsXAndSwapsEdges()
        {
            var back = Rect.MinMaxRect(1f, -2f, 3f, 0.5f);
            var front = SheetGeometry.BackToFront(back);

            Assert.AreEqual(-3f, front.xMin, 1e-6f);
            Assert.AreEqual(-1f, front.xMax, 1e-6f);
            Assert.AreEqual(-2f, front.yMin, 1e-6f);
            Assert.AreEqual(0.5f, front.yMax, 1e-6f);
            Assert.AreEqual(back.width, front.width, 1e-6f);
            var roundTrip = SheetGeometry.BackToFront(front); // the same map is its own inverse
            Assert.AreEqual(back.xMin, roundTrip.xMin, 1e-6f);
            Assert.AreEqual(back.xMax, roundTrip.xMax, 1e-6f);
        }
    }
}
