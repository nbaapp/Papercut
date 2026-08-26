using UnityEngine;

namespace Papercut
{
    /// <summary>Footprints of box colliders in a face root's local space.</summary>
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
    }
}
