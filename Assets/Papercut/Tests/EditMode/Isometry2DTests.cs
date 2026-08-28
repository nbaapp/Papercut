using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class Isometry2DTests
    {
        const float Eps = 1e-4f;

        static void AssertVector(Vector2 expected, Vector2 actual, string label = "")
        {
            Assert.AreEqual(expected.x, actual.x, Eps, label + " x");
            Assert.AreEqual(expected.y, actual.y, Eps, label + " y");
        }

        static readonly Crease Horizontal = new(new Vector2(0f, 1f), Vector2.right, Vector2.up);      // y = 1
        static readonly Crease Diagonal = new(new Vector2(2f, 0f), new Vector2(-1f, 1f), new Vector2(1f, 1f)); // x + y = 2

        [Test]
        public void Reflection_MirrorsAcrossTheCrease()
        {
            var m = Isometry2D.Reflection(Horizontal);
            AssertVector(new Vector2(3f, -1f), m.Apply(new Vector2(3f, 3f)));
            AssertVector(new Vector2(3f, 1f), m.Apply(new Vector2(3f, 1f)), "on the line");
            Assert.IsTrue(m.IsMirror);

            var d = Isometry2D.Reflection(Diagonal);
            AssertVector(new Vector2(2f, 2f), d.Apply(Vector2.zero));
            AssertVector(new Vector2(-1f, 1f), d.Apply(new Vector2(1f, 3f)));
        }

        [Test]
        public void Reflection_IsAnInvolution()
        {
            var m = Isometry2D.Reflection(Diagonal);
            var twice = m.Then(m);
            AssertVector(new Vector2(0.3f, -4f), twice.Apply(new Vector2(0.3f, -4f)));
            Assert.IsFalse(twice.IsMirror);
        }

        [Test]
        public void Then_AppliesInOrder_TwoMirrorsAreARotation()
        {
            var a = Isometry2D.Reflection(Horizontal);
            var b = Isometry2D.Reflection(new Crease(new Vector2(-2.5f, 0f), Vector2.up, Vector2.left)); // x = -2.5
            var both = a.Then(b);
            var p = new Vector2(-5f, 4f);
            AssertVector(b.Apply(a.Apply(p)), both.Apply(p));
            // Perpendicular mirrors: a half turn about their intersection (-2.5, 1).
            AssertVector(new Vector2(0f, -2f), both.Apply(p));
            Assert.IsFalse(both.IsMirror);
        }

        [Test]
        public void Inverse_RoundTrips()
        {
            var m = Isometry2D.Reflection(Diagonal).Then(Isometry2D.Reflection(Horizontal));
            var p = new Vector2(1.7f, -0.4f);
            AssertVector(p, m.Inverse.Apply(m.Apply(p)));
            AssertVector(p, m.Apply(m.Inverse.Apply(p)));
            AssertVector(Vector2.up, m.Inverse.ApplyVector(m.ApplyVector(Vector2.up)), "vector");
        }
    }
}
