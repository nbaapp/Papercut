using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>
    /// <see cref="SheetLayers.Coverage(FaceFootprint, SheetFace)"/> for a footprint of several pieces (a
    /// polygon region), Front and Back, and the pooled-piece rules of Whole / Partial / Covered.
    /// </summary>
    public sealed class PolygonCoverageTests
    {
        const float Eps = 1e-4f;

        static SheetLayers Replay(params Fold[] folds)
        {
            var stack = SheetLayers.Flat;
            for (int i = 0; i < folds.Length; i++)
                stack = stack.Apply(folds[i], i, out _);
            return stack;
        }

        /// <summary>An L with its corner at <paramref name="origin"/>: 3×3 minus the top-right 2×2. Area 5.</summary>
        static FaceFootprint L(Vector2 origin)
        {
            var o = origin;
            var outline = new[]
            {
                o, o + new Vector2(3f, 0f), o + new Vector2(3f, 1f), o + new Vector2(1f, 1f), o + new Vector2(1f, 3f), o + new Vector2(0f, 3f),
            };
            Assert.IsTrue(FaceFootprint.TryFromOutline(outline, out var footprint, out var reason), reason);
            Assert.AreEqual(2, footprint.Pieces.Count);
            return footprint;
        }

        static float TotalArea(CoverageResult result)
        {
            var total = 0f;
            foreach (var part in result.VisibleParts)
                total += part.Area;
            return total;
        }

        [Test]
        public void Front_ClearOfTheFold_IsWholeWithBothPieces()
        {
            var footprint = L(new Vector2(0f, 0f));
            var result = Replay(new Fold(FoldAnchor.EdgeSouth, 1f)).Coverage(footprint, SheetFace.Front); // hidden y < -3.25

            Assert.IsTrue(result.IsWhole);
            Assert.AreEqual(FoldCoverage.Uncovered, result.Coverage);
            Assert.AreEqual(2, result.VisibleParts.Count);
            Assert.AreEqual(5f, TotalArea(result), Eps);
        }

        [Test]
        public void Front_StraddlingTheCrease_IsPartialWithTheVisibleArea()
        {
            // EdgeSouth depth 1: crease at y = -3.25; the lifted strip (y < -3.25) lands over y -3.25..-2.25. The L from
            // y = -4 to -1 loses its foot (lifted) and the arm's bottom (under the Flap); the arm's x 0..1 × y -2.25..-1 shows.
            var footprint = L(new Vector2(0f, -4f));
            var result = Replay(new Fold(FoldAnchor.EdgeSouth, 1f)).Coverage(footprint, SheetFace.Front);

            Assert.AreEqual(FoldCoverage.Partial, result.Coverage);
            Assert.IsFalse(result.IsWhole);
            Assert.AreEqual(1.25f, TotalArea(result), Eps);
            foreach (var part in result.VisibleParts)
                foreach (var v in part.Vertices)
                    Assert.GreaterOrEqual(v.y, -2.25f - Eps, "no visible part reaches under the landed Flap");
        }

        [Test]
        public void Front_EntirelyOnTheLiftedStrip_IsCoveredWithNoParts()
        {
            var footprint = L(new Vector2(0f, -4.25f)); // y from -4.25 to -1.25, all below the crease at -0.25
            var result = Replay(new Fold(FoldAnchor.EdgeSouth, 4f)).Coverage(footprint, SheetFace.Front);

            Assert.AreEqual(FoldCoverage.Covered, result.Coverage);
            Assert.IsTrue(result.IsNone);
        }

        [Test]
        public void Back_OnTheLiftedStrip_IsExposedOnTheLandedFlap_MirroredPieceByPiece()
        {
            // Back-space L at x 1..4, y -4..-1 lies under Front-space x -4..-1 (BackToFront). EdgeSouth depth 4 puts the
            // crease at y = -0.25, so the whole L is on the lifted strip and lands, mirrored, at y 0.5..3.5.
            var footprint = L(new Vector2(1f, -4f));
            var layers = Replay(new Fold(FoldAnchor.EdgeSouth, 4f));
            var result = layers.Coverage(footprint, SheetFace.Back);

            Assert.AreEqual(FoldCoverage.Covered, result.Coverage, "fully exposed");
            Assert.IsFalse(result.IsWhole, "never where it was authored");
            Assert.AreEqual(5f, TotalArea(result), Eps);
            var flap = layers.Layers[1].Desk;
            foreach (var part in result.VisibleParts)
                foreach (var v in part.Vertices)
                    Assert.IsTrue(flap.Contains(v), $"{v} is not on the landed Flap");
            var bounds = result.VisibleParts[0].Bounds;
            foreach (var part in result.VisibleParts)
            {
                var b = part.Bounds;
                bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, b.xMin), Mathf.Min(bounds.yMin, b.yMin), Mathf.Max(bounds.xMax, b.xMax), Mathf.Max(bounds.yMax, b.yMax));
            }
            Assert.AreEqual(-4f, bounds.xMin, Eps);
            Assert.AreEqual(-1f, bounds.xMax, Eps);
            Assert.AreEqual(0.5f, bounds.yMin, Eps);
            Assert.AreEqual(3.5f, bounds.yMax, Eps);
        }

        [Test]
        public void RectOverload_AgreesWithTheOnePieceFootprint()
        {
            var rect = Rect.MinMaxRect(0f, -3f, 1f, -1f);
            var layers = Replay(new Fold(FoldAnchor.EdgeSouth, 1f));
            var a = layers.Coverage(rect, SheetFace.Front);
            var b = layers.Coverage(FaceFootprint.FromRect(rect), SheetFace.Front);
            Assert.AreEqual(a.Coverage, b.Coverage);
            Assert.AreEqual(a.IsWhole, b.IsWhole);
            Assert.AreEqual(TotalArea(a), TotalArea(b), Eps);
        }
    }
}
