using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The line a fold pivots on, in sheet-local space, plus which side of it is the Flap.
    /// </summary>
    public readonly struct Crease
    {
        /// <summary>A point on the crease line.</summary>
        public Vector2 Point { get; }

        /// <summary>Unit vector along the crease line.</summary>
        public Vector2 Direction { get; }

        /// <summary>Unit vector perpendicular to the crease, pointing into the Flap side.</summary>
        public Vector2 FlapNormal { get; }

        public Crease(Vector2 point, Vector2 direction, Vector2 flapNormal)
        {
            Point = point;
            Direction = direction.normalized;
            FlapNormal = flapNormal.normalized;
        }

        /// <summary>Signed distance from the crease; positive on the Flap side.</summary>
        public float SignedDistance(Vector2 p) => Vector2.Dot(p - Point, FlapNormal);

        /// <summary>Mirror image of <paramref name="p"/> across the crease.</summary>
        public Vector2 Reflect(Vector2 p) => p - 2f * SignedDistance(p) * FlapNormal;
    }
}
