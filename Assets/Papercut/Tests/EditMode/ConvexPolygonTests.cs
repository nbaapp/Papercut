using System.Collections.Generic;
using System.Linq;
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

        [Test]
        public void Intersect_OfOverlappingRects_IsTheirIntersection()
        {
            var a = ConvexPolygon.FromRect(Rect.MinMaxRect(0f, 0f, 4f, 4f));
            var b = ConvexPolygon.FromRect(Rect.MinMaxRect(2f, 2f, 6f, 6f));
            var i = a.Intersect(b);

            Assert.AreEqual(4f, i.Area, 1e-5f);
            Assert.AreEqual(2f, i.Bounds.xMin, 1e-5f);
            Assert.AreEqual(4f, i.Bounds.xMax, 1e-5f);
            Assert.IsTrue(a.Overlaps(b));
            Assert.IsFalse(a.Overlaps(ConvexPolygon.FromRect(Rect.MinMaxRect(4f, 0f, 5f, 1f))), "touching is not overlapping");
        }

        [Test]
        public void Subtract_RectMinusInnerRect_CoversExactlyTheDifference()
        {
            var parts = ConvexPolygon.FromRect(Rect.MinMaxRect(0f, 0f, 4f, 4f)).Subtract(ConvexPolygon.FromRect(Rect.MinMaxRect(1f, 1f, 2f, 2f)));

            Assert.AreEqual(4, parts.Count);
            Assert.AreEqual(15f, parts.Sum(p => p.Area), 1e-5f);
            for (int i = 0; i < parts.Count; i++)
            for (int j = i + 1; j < parts.Count; j++)
                Assert.IsFalse(parts[i].Overlaps(parts[j]), "pieces are disjoint");
        }

        [Test]
        public void Subtract_Disjoint_IsItself_Contained_IsEmpty()
        {
            var a = ConvexPolygon.FromRect(Unit);
            var far = a.Subtract(ConvexPolygon.FromRect(Rect.MinMaxRect(5f, 5f, 6f, 6f)));
            Assert.AreEqual(1, far.Count);
            Assert.AreEqual(a.Area, far[0].Area, 1e-5f);
            Assert.AreEqual(0, a.Subtract(ConvexPolygon.FromRect(Rect.MinMaxRect(-1f, -1f, 3f, 2f))).Count);
        }

        [Test]
        public void Subtract_Triangle_AreaIsDifference()
        {
            var square = ConvexPolygon.FromRect(Rect.MinMaxRect(0f, 0f, 2f, 2f));
            var tri = square.ClipToHalfPlane(new Vector2(1f, 1f), new Vector2(1f, 1f)); // upper-right half
            var parts = square.Subtract(tri);
            Assert.AreEqual(2f, parts.Sum(p => p.Area), 1e-5f);
        }

        [Test]
        public void Transform_AppliesTheIsometry_AndReversesWindingForAMirror()
        {
            var poly = ConvexPolygon.FromRect(Unit);
            var mirror = Isometry2D.Reflection(new Crease(new Vector2(0f, 1f), Vector2.right, Vector2.up)); // y = 1
            var moved = poly.Transform(mirror);
            Assert.AreEqual(poly.Area, moved.Area, 1e-5f);
            Assert.AreEqual(1f, moved.Bounds.yMin, 1e-5f);
            Assert.AreEqual(2f, moved.Bounds.yMax, 1e-5f);
            Assert.IsTrue(moved.Contains(new Vector2(1f, 1.5f)));
            Assert.IsFalse(moved.Contains(new Vector2(1f, 0.5f)));
            Assert.AreEqual(-moved.OutwardNormal(0).y, poly.OutwardNormal(0).y, 1e-5f, "outward normals stay outward");
        }

        [Test]
        public void ContainedIn_Rect()
        {
            var poly = ConvexPolygon.FromRect(Unit);
            Assert.IsTrue(poly.ContainedIn(Rect.MinMaxRect(0f, 0f, 2f, 1f), 1e-5f));
            Assert.IsFalse(poly.ContainedIn(Rect.MinMaxRect(0f, 0f, 1.9f, 1f), 1e-5f));
        }

        [Test]
        public void TryClipLine_GivesTheChordInsideThePolygon()
        {
            var poly = ConvexPolygon.FromRect(Rect.MinMaxRect(0f, 0f, 2f, 2f));
            Assert.IsTrue(poly.TryClipLine(new Vector2(-1f, 1f), Vector2.right, out var a, out var b));
            Assert.AreEqual(2f, Vector2.Distance(a, b), 1e-5f);
            Assert.IsFalse(poly.TryClipLine(new Vector2(-1f, 5f), Vector2.right, out _, out _), "misses");
            Assert.IsFalse(poly.TryClipLine(new Vector2(-1f, 2f), Vector2.right, out _, out _), "grazes an edge");
        }

        [Test]
        public void SubtractFromSegment_RemovesTheInteriorStretch_KeepsEdgesUnlessClosed()
        {
            var poly = ConvexPolygon.FromRect(Rect.MinMaxRect(1f, 0f, 2f, 3f));
            var pieces = ConvexPolygon.SubtractFromSegment(new Vector2(0f, 1f), new Vector2(3f, 1f), poly);
            Assert.AreEqual(2, pieces.Count);
            Assert.AreEqual(1f, pieces[0].b.x, 1e-5f);
            Assert.AreEqual(2f, pieces[1].a.x, 1e-5f);

            var along = ConvexPolygon.SubtractFromSegment(new Vector2(0f, 0f), new Vector2(3f, 0f), poly);
            Assert.AreEqual(1, along.Count, "along the boundary: kept whole (open)");
            Assert.AreEqual(3f, Vector2.Distance(along[0].a, along[0].b), 1e-5f);

            var closed = ConvexPolygon.SubtractFromSegment(new Vector2(0f, 0f), new Vector2(3f, 0f), poly, closed: true);
            Assert.AreEqual(2, closed.Count, "closed: the shared stretch is removed");
            Assert.AreEqual(1f, closed[0].b.x, 1e-5f);
            Assert.AreEqual(2f, closed[1].a.x, 1e-5f);

            var outside = ConvexPolygon.SubtractFromSegment(new Vector2(0f, 5f), new Vector2(3f, 5f), poly);
            Assert.AreEqual(1, outside.Count);
        }
    }
}
