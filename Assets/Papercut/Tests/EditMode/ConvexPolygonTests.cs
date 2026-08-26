using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class ConvexPolygonTests
    {
        static readonly Rect Unit = Rect.MinMaxRect(0f, 0f, 2f, 1f);

        [Test]
        public void FromRect_HasRectArea()
        {
            Assert.AreEqual(2f, ConvexPolygon.FromRect(Unit).Area, 1e-5f);
        }

        [Test]
        public void ClipToHalfPlane_KeepsPositiveSide()
        {
            var clipped = ConvexPolygon.FromRect(Unit).ClipToHalfPlane(new Vector2(0.5f, 0f), Vector2.right);

            Assert.AreEqual(1.5f, clipped.Area, 1e-5f);
            Assert.AreEqual(4, clipped.Count);
            Assert.AreEqual(0.5f, clipped.Bounds.xMin, 1e-5f);
        }

        [Test]
        public void ClipToHalfPlane_KeepNegative_IsComplement()
        {
            var clipped = ConvexPolygon.FromRect(Unit).ClipToHalfPlane(new Vector2(0.5f, 0f), Vector2.right, keepPositive: false);

            Assert.AreEqual(0.5f, clipped.Area, 1e-5f);
        }

        [Test]
        public void ClipToHalfPlane_EntirelyOutside_IsEmpty()
        {
            var clipped = ConvexPolygon.FromRect(Unit).ClipToHalfPlane(new Vector2(5f, 0f), Vector2.right);

            Assert.IsTrue(clipped.IsEmpty);
        }

        [Test]
        public void DiagonalClip_OfSquare_IsTriangleOfHalfArea()
        {
            var square = ConvexPolygon.FromRect(Rect.MinMaxRect(0f, 0f, 2f, 2f));
            var tri = square.ClipToHalfPlane(new Vector2(1f, 1f), new Vector2(1f, 1f));

            Assert.AreEqual(2f, tri.Area, 1e-5f);
            Assert.AreEqual(3, tri.Count);
        }

        [Test]
        public void Reflect_PreservesArea()
        {
            var crease = new Crease(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, -1f));
            var poly = ConvexPolygon.FromRect(Unit);

            Assert.AreEqual(poly.Area, poly.Reflect(crease).Area, 1e-5f);
        }

        [Test]
        public void Overlaps_TouchingEdges_IsFalse()
        {
            var poly = ConvexPolygon.FromRect(Unit);

            Assert.IsFalse(poly.Overlaps(Rect.MinMaxRect(2f, 0f, 3f, 1f)));
            Assert.IsTrue(poly.Overlaps(Rect.MinMaxRect(1.9f, 0f, 3f, 1f)));
        }

        [Test]
        public void ClipToRect_IsIntersection()
        {
            var poly = ConvexPolygon.FromRect(Unit).ClipToRect(Rect.MinMaxRect(1f, 0.5f, 5f, 5f));

            Assert.AreEqual(0.5f, poly.Area, 1e-5f);
            Assert.AreEqual(1f, poly.Bounds.xMin, 1e-5f);
            Assert.AreEqual(0.5f, poly.Bounds.yMin, 1e-5f);
            Assert.AreEqual(2f, poly.Bounds.xMax, 1e-5f);
            Assert.AreEqual(1f, poly.Bounds.yMax, 1e-5f);
        }
    }
}
