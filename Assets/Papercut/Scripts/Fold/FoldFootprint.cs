using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>Footprints of authored colliders in a face root's local space.</summary>
    public static class FoldFootprint
    {
        /// <summary>
        /// The axis-aligned face-local rect of <paramref name="box"/>: its four corners taken through
        /// faceRoot.worldToLocal × box.localToWorld. Exact whatever the face root's runtime pose, and for any
        /// scale on the object; authored content is never rotated relative to its face root, so the result is
        /// the box itself.
        /// </summary>
        public static Rect FaceLocalRect(BoxCollider2D box, Transform faceRoot)
        {
            var toFace = faceRoot.worldToLocalMatrix * box.transform.localToWorldMatrix;
            var half = box.size * 0.5f;
            var c = box.offset;
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in new[]
                     {
                         new Vector2(c.x - half.x, c.y - half.y), new Vector2(c.x + half.x, c.y - half.y),
                         new Vector2(c.x + half.x, c.y + half.y), new Vector2(c.x - half.x, c.y + half.y),
                     })
            {
                Vector2 p = toFace.MultiplyPoint3x4(corner);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>
        /// The face-local outline of <paramref name="polygon"/>'s single path (plus its offset), through the
        /// same matrix. False, with <paramref name="outline"/> cleared, if the collider does not have exactly
        /// one path of at least three points.
        /// </summary>
        public static bool FaceLocalOutline(PolygonCollider2D polygon, Transform faceRoot, List<Vector2> outline)
        {
            outline.Clear();
            if (polygon.pathCount != 1)
                return false;
            var toFace = faceRoot.worldToLocalMatrix * polygon.transform.localToWorldMatrix;
            foreach (var point in polygon.GetPath(0))
                outline.Add(toFace.MultiplyPoint3x4(point + polygon.offset));
            return outline.Count >= 3;
        }

        /// <summary>
        /// The footprint of an authored collider: a box is one rect piece; a polygon is its convex
        /// decomposition. <see cref="FaceFootprint.Empty"/> with an <paramref name="error"/> sentence for a
        /// polygon that is not one simple outline or a collider of another kind.
        /// </summary>
        public static FaceFootprint Of(Collider2D authored, Transform faceRoot, out string error)
        {
            error = null;
            switch (authored)
            {
                case BoxCollider2D box:
                    return FaceFootprint.FromRect(FaceLocalRect(box, faceRoot));
                case PolygonCollider2D polygon:
                {
                    var outline = new List<Vector2>();
                    if (!FaceLocalOutline(polygon, faceRoot, outline))
                    {
                        error = "the PolygonCollider2D must have exactly one outline of at least three points";
                        return FaceFootprint.Empty;
                    }
                    return FaceFootprint.TryFromOutline(outline, out var footprint, out error) ? footprint : FaceFootprint.Empty;
                }
                default:
                    error = $"a {authored.GetType().Name} has no supported footprint (use a BoxCollider2D or a PolygonCollider2D)";
                    return FaceFootprint.Empty;
            }
        }
    }
}
