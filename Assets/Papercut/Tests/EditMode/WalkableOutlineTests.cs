using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class WalkableOutlineTests
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

        static List<OutlineSegment> Outline(params Fold[] folds) => WalkableOutline.Segments(Replay(folds).Footprint);

        static void AssertClosed(List<OutlineSegment> segments)
        {
            // Every endpoint is shared by exactly two segments: the boundary is a closed loop.
            var endpoints = segments.SelectMany(s => new[] { s.A, s.B }).ToList();
            foreach (var p in endpoints)
                Assert.AreEqual(2, endpoints.Count(q => Vector2.Distance(p, q) < 1e-3f), $"endpoint {p}");
        }

        static void AssertNoDegenerateOrDuplicate(List<OutlineSegment> segments, string label)
        {
            Assert.IsTrue(segments.All(s => s.Length > 1e-4f), label + ": zero-length segment");
            for (int i = 0; i < segments.Count; i++)
            for (int j = i + 1; j < segments.Count; j++)
                Assert.IsFalse(Vector2.Distance(segments[i].A, segments[j].A) < 1e-4f && Vector2.Distance(segments[i].B, segments[j].B) < 1e-4f, label + ": duplicate segment");
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

            Assert.AreEqual(4, segments.Count);
            var crease = segments.Single(s => s.Kind == OutlineKind.Wall);
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
        public void AtMaxDepth_OutlineIsClosedWithNoDegenerateSegments()
        {
            foreach (var anchor in new[] { FoldAnchor.EdgeSouth, FoldAnchor.EdgeEast, FoldAnchor.CornerNorthEast })
            {
                var segments = Outline(new Fold(anchor, FoldGeometry.MaxDepth(anchor)));
                AssertNoDegenerateOrDuplicate(segments, anchor.ToString());
                AssertClosed(segments);
            }
        }

        [Test]
        public void CornerFold_IsPentagonWithDiagonalCreaseWall()
        {
            var segments = Outline(new Fold(FoldAnchor.CornerNorthEast, 2f));

            Assert.AreEqual(5, segments.Count);
            var crease = segments.Single(s => s.Kind == OutlineKind.Wall);
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
            Assert.AreEqual(4, ConvexCornerCount(Outline()));
        }

        [Test]
        public void Convexity_EdgeFold_RectangleStaysConvex()
        {
            Assert.AreEqual(4, ConvexCornerCount(Outline(new Fold(FoldAnchor.EdgeSouth, 3f))));
        }

        [Test]
        public void Convexity_CornerFold_PentagonAllConvex()
        {
            Assert.AreEqual(5, ConvexCornerCount(Outline(new Fold(FoldAnchor.CornerNorthEast, 2f))));
        }

        // ----- several folds -----

        [Test]
        public void TwoIndependentFolds_TwoCreaseWalls()
        {
            var segments = Outline(new Fold(FoldAnchor.EdgeNorth, 1f), new Fold(FoldAnchor.EdgeSouth, 2f));

            Assert.AreEqual(4, segments.Count);
            var walls = segments.Where(s => s.Kind == OutlineKind.Wall).ToList();
            Assert.AreEqual(2, walls.Count);
            CollectionAssert.AreEquivalent(new[] { 3.25f, -2.25f }, walls.Select(w => Mathf.Round(w.A.y * 1000f) / 1000f));
            CollectionAssert.AreEquivalent(new[] { GridDirection.East, GridDirection.West }, segments.Where(s => s.Kind == OutlineKind.SheetEdge).Select(s => s.Direction));
            AssertClosed(segments);
        }

        [Test]
        public void Stacked_LandingIntoAnEmptiedStrip_FarEdgeIsAWallNotAnExit()
        {
            // North strip lifted (y > 2.25 empty), then the south Flap lands up to y = 3.75 over base and desk alike.
            var segments = Outline(new Fold(FoldAnchor.EdgeNorth, 2f), new Fold(FoldAnchor.EdgeSouth, 4f));

            Assert.AreEqual(4, segments.Count, string.Join("; ", segments.Select(s => $"{s.Kind} {s.A}-{s.B}")));
            var top = segments.Single(s => Mathf.Abs(s.A.y - 3.75f) < Eps && Mathf.Abs(s.B.y - 3.75f) < Eps);
            Assert.AreEqual(OutlineKind.Wall, top.Kind, "a landed edge inside the sheet rect is not an exit");
            Assert.AreEqual(Vector2.up, top.Outward);
            var bottom = segments.Single(s => Mathf.Abs(s.A.y + 0.25f) < Eps && Mathf.Abs(s.B.y + 0.25f) < Eps);
            Assert.AreEqual(OutlineKind.Wall, bottom.Kind);
            CollectionAssert.AreEquivalent(new[] { GridDirection.East, GridDirection.West }, segments.Where(s => s.Kind == OutlineKind.SheetEdge).Select(s => s.Direction));
            Assert.AreEqual(4f, segments.Single(s => s.Direction == GridDirection.East && s.Kind == OutlineKind.SheetEdge).Length, Eps);
            AssertClosed(segments);
            Assert.AreEqual(4, ConvexCornerCount(segments));
        }

        [Test]
        public void FoldOnFold_OutlineIsTheUnion_NoInteriorWalls()
        {
            // North d=2 then west d=3: everything west of x = -2.5 (two layers) lands on x in [-2.5, 0.5].
            var stack = Replay(new Fold(FoldAnchor.EdgeNorth, 2f), new Fold(FoldAnchor.EdgeWest, 3f));
            var segments = WalkableOutline.Segments(stack.Footprint);

            Assert.AreEqual(4, segments.Count, string.Join("; ", segments.Select(s => $"{s.Kind} {s.A}-{s.B}")));
            AssertClosed(segments);
            AssertNoDegenerateOrDuplicate(segments, "fold on fold");
            foreach (var s in segments.Where(s => s.Kind == OutlineKind.SheetEdge))
            {
                var n = s.Direction.ToVector();
                var extent = Mathf.Abs(Vector2.Dot(SheetGeometry.HalfSize, n));
                Assert.AreEqual(extent, Vector2.Dot(s.A, n), Eps, "sheet edges lie on the sheet rect");
                Assert.AreEqual(extent, Vector2.Dot(s.B, n), Eps);
            }
            foreach (var s in segments)
            {
                var outside = s.Midpoint + s.Outward * 0.01f;
                var inside = s.Midpoint - s.Outward * 0.01f;
                Assert.IsFalse(stack.Footprint.Any(p => p.Contains(outside)), $"{s.Kind} {s.A}-{s.B}: sheet on the outward side");
                Assert.IsTrue(stack.Footprint.Any(p => p.Contains(inside)), $"{s.Kind} {s.A}-{s.B}: no sheet on the inward side");
            }
            Assert.AreEqual(2, segments.Count(s => s.Kind == OutlineKind.Wall), "the west crease and the north edge of the stack");
        }

        [Test]
        public void Stacked_ThreeFolds_OutlineIsClosedAndTight()
        {
            var stack = Replay(new Fold(FoldAnchor.EdgeNorth, 2f), new Fold(FoldAnchor.EdgeSouth, 4f), new Fold(FoldAnchor.CornerNorthEast, 3f));
            var segments = WalkableOutline.Segments(stack.Footprint);

            AssertClosed(segments);
            AssertNoDegenerateOrDuplicate(segments, "three folds");
            foreach (var s in segments)
            {
                Assert.IsFalse(stack.Footprint.Any(p => p.Contains(s.Midpoint + s.Outward * 0.01f)), $"{s.Kind} {s.A}-{s.B}: sheet on the outward side");
                Assert.IsTrue(stack.Footprint.Any(p => p.Contains(s.Midpoint - s.Outward * 0.01f)), $"{s.Kind} {s.A}-{s.B}: no sheet on the inward side");
            }
        }
    }
}
