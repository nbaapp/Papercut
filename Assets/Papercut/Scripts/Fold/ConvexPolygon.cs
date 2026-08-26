using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// An immutable convex polygon in 2D. Everything folding touches is convex: the sheet rect, a Flap
    /// (rect clipped by the Crease), its mirror image, and box footprints.
    /// </summary>
    /// <remarks>
    /// Vertices are stored in the order they were given; clipping preserves winding. Pure C#, no Unity
    /// objects, so it is unit-testable.
    /// </remarks>
    public sealed class ConvexPolygon
    {
        /// <summary>Areas at or below this are treated as empty (square units).</summary>
        public const float AreaEpsilon = 1e-5f;

        /// <summary>Distances within this of a clipping line count as on it.</summary>
        const float OnLineEpsilon = 1e-6f;

        public static readonly ConvexPolygon Empty = new(new List<Vector2>());

        readonly List<Vector2> vertices;

        public IReadOnlyList<Vector2> Vertices => vertices;
        public int Count => vertices.Count;
        public bool IsEmpty => vertices.Count < 3 || Area <= AreaEpsilon;

        public ConvexPolygon(List<Vector2> vertices)
        {
            this.vertices = vertices;
        }

        public static ConvexPolygon FromRect(Rect rect) => new(new List<Vector2>
        {
            new(rect.xMin, rect.yMin),
            new(rect.xMax, rect.yMin),
            new(rect.xMax, rect.yMax),
            new(rect.xMin, rect.yMax),
        });

        /// <summary>Unsigned area (shoelace).</summary>
        public float Area
        {
            get
            {
                float twice = 0f;
                for (int i = 0, n = vertices.Count; i < n; i++)
                {
                    var a = vertices[i];
                    var b = vertices[(i + 1) % n];
                    twice += a.x * b.y - b.x * a.y;
                }
                return Mathf.Abs(twice) * 0.5f;
            }
        }

        /// <summary>Axis-aligned bounds; a zero rect when empty.</summary>
        public Rect Bounds
        {
            get
            {
                if (vertices.Count == 0)
                    return Rect.zero;
                var min = vertices[0];
                var max = vertices[0];
                foreach (var v in vertices)
                {
                    min = Vector2.Min(min, v);
                    max = Vector2.Max(max, v);
                }
                return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }

        /// <summary>
        /// The part of this polygon on the side of the line through <paramref name="point"/> where
        /// dot(p - point, normal) is ≥ 0 (or ≤ 0 when <paramref name="keepPositive"/> is false).
        /// Sutherland–Hodgman against one plane.
        /// </summary>
        public ConvexPolygon ClipToHalfPlane(Vector2 point, Vector2 normal, bool keepPositive = true)
        {
            var sign = keepPositive ? 1f : -1f;
            var result = new List<Vector2>(vertices.Count + 2);
            for (int i = 0, n = vertices.Count; i < n; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % n];
                var da = sign * Vector2.Dot(a - point, normal);
                var db = sign * Vector2.Dot(b - point, normal);
                var aIn = da >= -OnLineEpsilon;
                var bIn = db >= -OnLineEpsilon;

                if (aIn)
                    result.Add(a);
                // A vertex on the line is kept as itself; only a true crossing adds a new vertex.
                if (aIn != bIn && Mathf.Abs(da) > OnLineEpsilon && Mathf.Abs(db) > OnLineEpsilon)
                {
                    var t = da / (da - db);
                    result.Add(a + (b - a) * t);
                }
            }
            return new ConvexPolygon(result);
        }

        /// <summary>This polygon clipped to <paramref name="rect"/>.</summary>
        public ConvexPolygon ClipToRect(Rect rect) => this
            .ClipToHalfPlane(new Vector2(rect.xMin, 0f), Vector2.right)
            .ClipToHalfPlane(new Vector2(rect.xMax, 0f), Vector2.left)
            .ClipToHalfPlane(new Vector2(0f, rect.yMin), Vector2.up)
            .ClipToHalfPlane(new Vector2(0f, rect.yMax), Vector2.down);

        /// <summary>Mirror image across <paramref name="crease"/>. A mirror reverses winding; the result is still convex.</summary>
        public ConvexPolygon Reflect(Crease crease)
        {
            var result = new List<Vector2>(vertices.Count);
            foreach (var v in vertices)
                result.Add(crease.Reflect(v));
            return new ConvexPolygon(result);
        }

        /// <summary>True if this polygon and <paramref name="rect"/> share any area.</summary>
        public bool Overlaps(Rect rect) => !ClipToRect(rect).IsEmpty;
    }
}
