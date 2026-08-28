using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// A rigid 2D transform (rotation, reflection, translation): a 2×2 orthogonal linear part stored as two
    /// columns plus a translation. Every pose a piece of the sheet can take under folding is one of these —
    /// a composition of reflections across creases. Managed math only, so it is unit-testable.
    /// </summary>
    public readonly struct Isometry2D
    {
        /// <summary>Image of the x axis (first column of the linear part).</summary>
        public Vector2 Ax { get; }

        /// <summary>Image of the y axis (second column of the linear part).</summary>
        public Vector2 Ay { get; }

        /// <summary>Translation.</summary>
        public Vector2 T { get; }

        public Isometry2D(Vector2 ax, Vector2 ay, Vector2 t)
        {
            Ax = ax;
            Ay = ay;
            T = t;
        }

        public static readonly Isometry2D Identity = new(Vector2.right, Vector2.up, Vector2.zero);

        /// <summary>The mirror across <paramref name="crease"/>: p ↦ p − 2((p − P)·n)n.</summary>
        public static Isometry2D Reflection(Crease crease)
        {
            var n = crease.FlapNormal;
            var ax = new Vector2(1f - 2f * n.x * n.x, -2f * n.x * n.y);
            var ay = new Vector2(-2f * n.x * n.y, 1f - 2f * n.y * n.y);
            var t = 2f * Vector2.Dot(crease.Point, n) * n;
            return new Isometry2D(ax, ay, t);
        }

        public Vector2 Apply(Vector2 p) => Ax * p.x + Ay * p.y + T;

        /// <summary>Linear part only: for directions and normals.</summary>
        public Vector2 ApplyVector(Vector2 v) => Ax * v.x + Ay * v.y;

        /// <summary>This transform followed by <paramref name="next"/>.</summary>
        public Isometry2D Then(Isometry2D next)
            => new(next.ApplyVector(Ax), next.ApplyVector(Ay), next.Apply(T));

        /// <summary>The inverse: the linear part is orthogonal, so its inverse is its transpose.</summary>
        public Isometry2D Inverse
        {
            get
            {
                var ax = new Vector2(Ax.x, Ay.x);
                var ay = new Vector2(Ax.y, Ay.y);
                var t = -(ax * T.x + ay * T.y);
                return new Isometry2D(ax, ay, t);
            }
        }

        /// <summary>True if this transform turns the sheet over (an odd number of reflections).</summary>
        public bool IsMirror => Ax.x * Ay.y - Ay.x * Ax.y < 0f;

        public override string ToString() => $"[{Ax} {Ay} + {T}]";
    }
}
