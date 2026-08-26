using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class WalkableOutlineTests
    {
        const float HW = 5.5f, HH = 4.25f, H = 8.5f;
        const float Eps = 1e-4f;

        static List<OutlineSegment> Outline(params Fold[] folds) => WalkableOutline.Segments(folds);

        static void AssertClosed(List<OutlineSegment> segments)
        {
            // Every endpoint is shared by exactly two segments: the boundary is a closed loop.
            var endpoints = segments.SelectMany(s => new[] { s.A, s.B }).ToList();
            foreach (var p in endpoints)
                Assert.AreEqual(2, endpoints.Count(q => Vector2.Distance(p, q) < 1e-3f), $"endpoint {p}");
        }

        [Test]
        public void Flat_IsFourSheetEdges()
        {
            var segments = Outline();

            Assert.AreEqual(4, segments.Count);
            Assert.IsTrue(segments.All(s => s.Kind == OutlineKind.SheetEdge));
            CollectionAssert.AreEquivalent(
                new[] { GridDirection.North, GridDirection.East, GridDirection.South, GridDirection.West },
                segments.Select(s => s.Direction));
            AssertClosed(segments);
        }

        [Test]
        public void EdgeFold_CreaseIsWall_FarAndSideEdgesUnchanged()
        {
            var segments = Outline(new Fold(FoldAnchor.EdgeSouth, 3f)); // crease y = -1.25; landed [-1.25, 1.75] inside

            var crease = segments.Single(s => s.Kind == OutlineKind.Crease);
            Assert.AreEqual(-1.25f, crease.A.y, Eps);
            Assert.AreEqual(-1.25f, crease.B.y, Eps);
            Assert.AreEqual(2f * HW, crease.Length, Eps);
            Assert.AreEqual(Vector2.down, crease.Outward);

            Assert.IsFalse(segments.Any(s => s.Direction == GridDirection.South && s.Kind == OutlineKind.SheetEdge), "south edge is lifted");
            var north = segments.Single(s => s.Kind == OutlineKind.SheetEdge && s.Direction == GridDirection.North);
            Assert.AreEqual(HH, north.A.y, Eps);
            var east = segments.Single(s => s.Kind == OutlineKind.SheetEdge && s.Direction == GridDirection.East);
            Assert.AreEqual(HH - (-1.25f), east.Length, Eps);
            AssertClosed(segments);
        }

        [Test]
        public void EdgeFold_DeeperThanHalf_ExtendsSidesAndUsesReflectedFarEdge()
        {
            var segments = Outline(new Fold(FoldAnchor.EdgeSouth, 6f)); // crease y = 1.75; landed [1.75, 7.75]

            var north = segments.Single(s => s.Kind == OutlineKind.SheetEdge && s.Direction == GridDirection.North);
            Assert.AreEqual(7.75f, north.A.y, Eps, "the reflected south edge faces north");
            var east = segments.Where(s => s.Kind == OutlineKind.SheetEdge && s.Direction == GridDirection.East).ToList();
            Assert.AreEqual(1, east.Count, "coincident side edges merged");
            Assert.AreEqual(7.75f - 1.75f, east[0].Length, Eps);
            AssertClosed(segments);
        }

        [Test]
        public void AtMaxDepth_OutlineIsClosedWithNoDegenerateSegments()
        {
            foreach (var anchor in new[] { FoldAnchor.EdgeSouth, FoldAnchor.EdgeEast, FoldAnchor.CornerNorthEast })
            {
                var segments = Outline(new Fold(anchor, FoldGeometry.MaxDepth(anchor)));
                Assert.IsTrue(segments.All(s => s.Length > 1e-4f), anchor + ": zero-length segment");
                for (int i = 0; i < segments.Count; i++)
                for (int j = i + 1; j < segments.Count; j++)
                    Assert.IsFalse(Vector2.Distance(segments[i].A, segments[j].A) < 1e-4f && Vector2.Distance(segments[i].B, segments[j].B) < 1e-4f, anchor + ": duplicate segment");
                AssertClosed(segments);
            }
        }

        [Test]
        public void CornerFold_IsPentagonWithDiagonalCreaseWall()
        {
            var segments = Outline(new Fold(FoldAnchor.CornerNorthEast, 2f));

            Assert.AreEqual(5, segments.Count);
            var crease = segments.Single(s => s.Kind == OutlineKind.Crease);
            Assert.AreEqual(2f * Mathf.Sqrt(2f), crease.Length, Eps);
            Assert.AreEqual(2f * HH - 2f, segments.Single(s => s.Kind == OutlineKind.SheetEdge && s.Direction == GridDirection.East).Length, Eps, "east edge runs up to where the crease meets it");
            AssertClosed(segments);
        }

        static int ConvexCornerCount(List<OutlineSegment> segments)
        {
            var convex = 0;
            foreach (var s in segments)
            {
                if (WalkableOutline.IsConvexAt(segments, s, s.A)) convex++;
                if (WalkableOutline.IsConvexAt(segments, s, s.B)) convex++;
            }
            return convex / 2; // each vertex is an endpoint of two segments
        }

        [Test]
        public void Convexity_Flat_AllFourCornersConvex()
        {
            var segments = Outline();
            Assert.AreEqual(4, ConvexCornerCount(segments));
        }

        [Test]
        public void Convexity_EdgeFold_RectangleStaysConvex()
        {
            Assert.AreEqual(4, ConvexCornerCount(Outline(new Fold(FoldAnchor.EdgeSouth, 3f))));
            Assert.AreEqual(4, ConvexCornerCount(Outline(new Fold(FoldAnchor.EdgeSouth, 6f))));
        }

        [Test]
        public void Convexity_CornerFold_PentagonAllConvex()
        {
            Assert.AreEqual(5, ConvexCornerCount(Outline(new Fold(FoldAnchor.CornerNorthEast, 2f))));
        }

        [Test]
        public void Convexity_Overhang_HasOneConcaveCornerWhereItMeetsTheSheet()
        {
            // Geometry only: unreachable through input (no overhang).
            var fold = new Fold(FoldAnchor.CornerNorthEast, 10f);
            var segments = Outline(fold);
            var distinct = new List<Vector2>();
            foreach (var v in segments.SelectMany(s => new[] { s.A, s.B }))
                if (!distinct.Any(d => Vector2.Distance(d, v) < 1e-3f)) distinct.Add(v);
            var vertices = distinct.Count;
            Assert.AreEqual(7, vertices, "NW, top/crease, crease/east, east/bottom, bottom/west, west/sheet-bottom (concave), SW");
            var concaveAt = new Vector2(HW - 10f, -HH); // where the overhang's west edge leaves the sheet's south edge

            Assert.AreEqual(vertices - 1, ConvexCornerCount(segments));
            foreach (var s in segments)
            {
                if (Vector2.Distance(s.A, concaveAt) < 1e-3f) Assert.IsFalse(WalkableOutline.IsConvexAt(segments, s, s.A), s.ToString());
                if (Vector2.Distance(s.B, concaveAt) < 1e-3f) Assert.IsFalse(WalkableOutline.IsConvexAt(segments, s, s.B), s.ToString());
            }
        }

        [Test]
        public void CornerFold_DeeperThanHeight_HasWalkableOverhang()
        {
            // Geometry only: such depths cannot be made through input (Aaron: no overhang).
            var segments = Outline(new Fold(FoldAnchor.CornerNorthEast, 10f));

            var minY = segments.Min(s => Mathf.Min(s.A.y, s.B.y));
            Assert.AreEqual(HH - 10f, minY, Eps, "the overhang's reflected edge is on the outline");
            Assert.IsTrue(segments.Any(s => s.Kind == OutlineKind.SheetEdge && s.Direction == GridDirection.South && Mathf.Abs(s.A.y - (HH - 10f)) < Eps), "overhang bottom faces south");
            Assert.IsTrue(segments.Any(s => s.Kind == OutlineKind.Crease));
            AssertClosed(segments);

            // No segment lies strictly inside the walkable area: each is a boundary of sheet ∪ reflected sheet.
            var sheet = FoldGeometry.Sheet;
            var mirrored = FoldGeometry.ReflectedSheet(new Fold(FoldAnchor.CornerNorthEast, 10f));
            foreach (var s in segments.Where(s => s.Kind == OutlineKind.SheetEdge))
            {
                var probe = s.Midpoint + s.Outward * 0.01f;
                Assert.IsFalse(sheet.Contains(probe) || mirrored.Contains(probe), $"segment {s.A}-{s.B} has walkable ground outside it");
            }
        }
    }
}
