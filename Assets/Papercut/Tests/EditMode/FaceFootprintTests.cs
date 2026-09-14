using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class FaceFootprintTests
    {
        const float Eps = 1e-4f;

        static readonly Vector2[] L = { new(0f, 0f), new(3f, 0f), new(3f, 1f), new(1f, 1f), new(1f, 3f), new(0f, 3f) };

        [Test]
        public void Default_IsEmptyAndSafe()
        {
            FaceFootprint footprint = default;
            Assert.IsTrue(footprint.IsEmpty);
            Assert.AreEqual(0, footprint.Pieces.Count);
            Assert.AreEqual(0f, footprint.Area);
            Assert.AreEqual(Rect.zero, footprint.Bounds);
            Assert.IsTrue(FaceFootprint.Empty.IsEmpty);
        }

        [Test]
        public void FromRect_IsOnePieceWithTheRectsAreaAndBounds()
        {
            var rect = Rect.MinMaxRect(1f, 2f, 4f, 3f);
            var footprint = FaceFootprint.FromRect(rect);
            Assert.AreEqual(1, footprint.Pieces.Count);
            Assert.AreEqual(3f, footprint.Area, Eps);
            Assert.AreEqual(rect, footprint.Bounds);
            Assert.IsTrue(FaceFootprint.FromRect(new Rect(0f, 0f, 0f, 2f)).IsEmpty, "no width → empty");
        }

        [Test]
        public void TryFromOutline_L_IsDisjointPiecesWithTheLsAreaAndBounds()
        {
            Assert.IsTrue(FaceFootprint.TryFromOutline(L, out var footprint, out var reason), reason);
            Assert.AreEqual(5f, footprint.Area, Eps);
            Assert.AreEqual(Rect.MinMaxRect(0f, 0f, 3f, 3f), footprint.Bounds);
            for (int i = 0; i < footprint.Pieces.Count; i++)
                for (int j = i + 1; j < footprint.Pieces.Count; j++)
                    Assert.IsFalse(footprint.Pieces[i].Overlaps(footprint.Pieces[j]));
        }

        [Test]
        public void TryFromOutline_Invalid_IsFalseEmptyWithAReason()
        {
            var bowTie = new[] { new Vector2(0f, 0f), new Vector2(2f, 2f), new Vector2(2f, 0f), new Vector2(0f, 2f) };
            Assert.IsFalse(FaceFootprint.TryFromOutline(bowTie, out var footprint, out var reason));
            Assert.IsTrue(footprint.IsEmpty);
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void CoverageResult_WholeOfEmpty_IsNone()
        {
            var result = CoverageResult.Whole(FoldCoverage.Uncovered, FaceFootprint.Empty);
            Assert.IsTrue(result.IsNone);
            Assert.IsFalse(result.IsWhole);
        }
    }
}
