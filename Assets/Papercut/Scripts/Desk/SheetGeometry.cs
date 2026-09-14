using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Physical dimensions of one Sheet, in world units.
    /// </summary>
    /// <remarks>
    /// World scale: 1 unit = 1 inch of paper, so a landscape letter sheet is 11 x 8.5 units
    /// (Bible §3, open decision #9 — both candidate readings give these numbers).
    /// This is the only place sheet dimensions live. Never hard-code them elsewhere.
    /// </remarks>
    public static class SheetGeometry
    {
        public const float Width = 11f;
        public const float Height = 8.5f;

        public static readonly Vector2 Size = new(Width, Height);
        public static readonly Vector2 HalfSize = Size * 0.5f;

        /// <summary>World-space bounds of a sheet whose centre is at <paramref name="centre"/>.</summary>
        public static Rect BoundsAt(Vector2 centre) => new(centre - HalfSize, Size);

        /// <summary>
        /// Maps a point in Back-space to the Front-space point directly beneath it, in sheet-local units.
        /// </summary>
        /// <remarks>
        /// Back-side authoring convention (Bible §6, decision #5, chosen by Aaron): Back content is authored
        /// flat in its own space, as the sheet looks when turned over about its vertical edge. So Back point
        /// (x, y) lies beneath Front point (-x, y). Any mirroring across a crease is the fold system's job
        /// at runtime and composes with this. The mapping is its own inverse, so it also maps Front to Back.
        /// This is the only place the convention lives.
        /// </remarks>
        public static Vector2 BackToFront(Vector2 backLocal) => new(-backLocal.x, backLocal.y);

        /// <summary>The Front-space rect under a Back-space rect (its mirror in x); the same map the other way.</summary>
        public static Rect BackToFront(Rect back) => Rect.MinMaxRect(-back.xMax, back.yMin, -back.xMin, back.yMax);

        /// <summary>The Front-space polygon under a Back-space polygon (its mirror in x); the same map the other way.</summary>
        public static ConvexPolygon BackToFront(ConvexPolygon back) => back.MirroredX();
    }
}
