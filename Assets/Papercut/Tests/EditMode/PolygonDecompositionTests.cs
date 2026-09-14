using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>Simple-polygon validation and convex decomposition (polygon terrain, 2026-09-10).</summary>
    public sealed class PolygonDecompositionTests
    {
        const float Eps = 1e-4f;

        static readonly Vector2[] Square = { new(0f, 0f), new(2f, 0f), new(2f, 2f), new(0f, 2f) };

        /// <summary>An L: a 3×3 square minus its top-right 2×2 corner. Area 5.</summary>
        static readonly Vector2[] L = { new(0f, 0f), new(3f, 0f), new(3f, 1f), new(1f, 1f), new(1f, 3f), new(0f, 3f) };

        /// <summary>A five-pointed star, ten vertices, concave at every inner vertex.</summary>
        static Vector2[] Star()
        {
            var points = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                var angle = Mathf.PI / 2f + i * Mathf.PI / 5f;
                var radius = i % 2 == 0 ? 2f : 0.8f;
                points[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return points;
        }

        static float Area(IReadOnlyList<Vector2> outline)
        {
            var twice = 0f;
            for (int i = 0; i < outline.Count; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Count];
                twice += a.x * b.y - b.x * a.y;
            }
            return Mathf.Abs(twice) * 0.5f;
        }

        static float TotalArea(List<ConvexPolygon> pieces)
        {
            var total = 0f;
            foreach (var piece in pieces)
                total += piece.Area;
            return total;
        }

        static void AssertConvex(ConvexPolygon piece)
        {
            var v = piece.Vertices;
            var sign = 0f;
            for (int i = 0; i < v.Count; i++)
            {
                var a = v[(i + v.Count - 1) % v.Count];
                var b = v[i];
                var c = v[(i + 1) % v.Count];
                var cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
                if (Mathf.Abs(cross) <= 1e-6f)
                    continue;
                if (sign == 0f)
                    sign = Mathf.Sign(cross);
                Assert.AreEqual(sign, Mathf.Sign(cross), "a piece must not turn both ways");
            }
        }

        static void AssertDisjoint(List<ConvexPolygon> pieces)
        {
            for (int i = 0; i < pieces.Count; i++)
                for (int j = i + 1; j < pieces.Count; j++)
                    Assert.IsFalse(pieces[i].Overlaps(pieces[j]), $"pieces {i} and {j} share area");
        }

        // ----- Validate -----

        [Test]
        public void Validate_AcceptsConvexConcaveAndEitherWinding()
        {
            Assert.IsTrue(PolygonDecomposition.Validate(Square, out _));
            Assert.IsTrue(PolygonDecomposition.Validate(L, out _));
            Assert.IsTrue(PolygonDecomposition.Validate(Star(), out _));
            var clockwise = new List<Vector2>(L);
            clockwise.Reverse();
            Assert.IsTrue(PolygonDecomposition.Validate(clockwise, out _));
        }

        [Test]
        public void Validate_RefusesTooFewPoints_ZeroArea_AndSelfCrossing()
        {
            Assert.IsFalse(PolygonDecomposition.Validate(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f) }, out var few));
            StringAssert.Contains("three", few);

            // Three collinear points: Clean drops the middle one, so the refusal is "too few vertices"; either way it is refused with a reason.
            Assert.IsFalse(PolygonDecomposition.Validate(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 0f) }, out var flat));
            Assert.IsNotEmpty(flat);

            var bowTie = new[] { new Vector2(0f, 0f), new Vector2(2f, 2f), new Vector2(2f, 0f), new Vector2(0f, 2f) };
            Assert.IsFalse(PolygonDecomposition.Validate(bowTie, out var crossing));
            StringAssert.Contains("crosses", crossing);
        }

        [Test]
        public void Validate_RefusesAnOutlineThatTouchesItself()
        {
            // A square whose fifth vertex touches the opposite edge from inside: two non-adjacent edges share a point.
            var touching = new[] { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 2f), new Vector2(1f, 0f), new Vector2(0f, 2f) };
            Assert.IsFalse(PolygonDecomposition.Validate(touching, out var reason));
            StringAssert.Contains("crosses", reason);
        }

        [Test]
        public void Clean_DropsDuplicatesAndBetweenPoints_ButKeepsASpike()
        {
            var noisy = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(2f, 0f), // (1,0) is strictly between
                new Vector2(2f, 2f), new Vector2(0f, 2f), new Vector2(0f, 0f), // repeated first vertex
            };
            var cleaned = PolygonDecomposition.Clean(noisy);
            Assert.AreEqual(4, cleaned.Count);

            // A spike: the outline goes out to (3,1) and straight back. Collinear, but NOT between its neighbours.
            var spike = new[] { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 1f), new Vector2(3f, 1f), new Vector2(2f, 1f), new Vector2(2f, 2f), new Vector2(0f, 2f) };
            var kept = PolygonDecomposition.Clean(spike);
            Assert.AreEqual(7, kept.Count, "a spike must survive Clean so Validate can refuse it");
            Assert.IsFalse(PolygonDecomposition.Validate(spike, out _));
        }

        // ----- Decompose -----

        [Test]
        public void Decompose_ConvexInput_IsOnePieceOfTheSameArea()
        {
            var pieces = PolygonDecomposition.Decompose(Square);
            Assert.AreEqual(1, pieces.Count);
            Assert.AreEqual(4f, pieces[0].Area, Eps);
            Assert.AreEqual(4, pieces[0].Count);
        }

        [Test]
        public void Decompose_L_IsTwoConvexDisjointPiecesSummingToItsArea()
        {
            var pieces = PolygonDecomposition.Decompose(L);
            Assert.AreEqual(2, pieces.Count, "an L merges to two rectangles/quads");
            foreach (var piece in pieces)
                AssertConvex(piece);
            AssertDisjoint(pieces);
            Assert.AreEqual(5f, TotalArea(pieces), Eps);
        }

        [Test]
        public void Decompose_Star_IsConvexDisjointAndAreaPreserving()
        {
            var star = Star();
            var pieces = PolygonDecomposition.Decompose(star);
            Assert.GreaterOrEqual(pieces.Count, 4, "five reflex vertices need at least ceil(5/2) + 1 pieces");
            Assert.LessOrEqual(pieces.Count, 8, "triangulation alone would be eight; merging must not add pieces");
            foreach (var piece in pieces)
                AssertConvex(piece);
            AssertDisjoint(pieces);
            Assert.AreEqual(Area(star), TotalArea(pieces), Eps);
        }

        [Test]
        public void Decompose_ClockwiseInput_GivesTheSameAreaAsCounterClockwise()
        {
            var clockwise = new List<Vector2>(L);
            clockwise.Reverse();
            Assert.AreEqual(TotalArea(PolygonDecomposition.Decompose(L)), TotalArea(PolygonDecomposition.Decompose(clockwise)), Eps);
        }

        [Test]
        public void Decompose_EveryPieceLiesInsideTheOutline()
        {
            // Sample each piece's centroid against the outline (even-odd) for the star: pieces never spill outside.
            var star = Star();
            foreach (var piece in PolygonDecomposition.Decompose(star))
            {
                var centroid = Vector2.zero;
                foreach (var v in piece.Vertices)
                    centroid += v;
                centroid /= piece.Count;
                Assert.IsTrue(PointInPolygon(centroid, star), $"centroid {centroid} is outside the star");
            }
        }

        static bool PointInPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
        {
            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if (a.y > point.y != b.y > point.y && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
