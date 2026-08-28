using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Applies a <see cref="CoverageResult"/> to one <see cref="BoxCollider2D"/>: whole → the box is on;
    /// nothing → off; partial → the box is off and a <see cref="PolygonCollider2D"/> on the same object carries
    /// one path per visible piece. Shared by every box-shaped occludee.
    /// </summary>
    public sealed class OccludedBoxCollider
    {
        readonly BoxCollider2D box;
        PolygonCollider2D polygon;

        public OccludedBoxCollider(BoxCollider2D box)
        {
            this.box = box;
        }

        /// <summary>The colliders currently standing in for the box (zero, one box, or one polygon).</summary>
        public IEnumerable<Collider2D> LiveColliders
        {
            get
            {
                if (box != null && box.enabled)
                    yield return box;
                if (polygon != null && polygon.enabled)
                    yield return polygon;
            }
        }

        public void Apply(in CoverageResult coverage, Transform space)
        {
            if (coverage.IsNone)
            {
                box.enabled = false;
                if (polygon != null)
                    polygon.enabled = false;
                return;
            }

            if (coverage.IsWhole)
            {
                if (polygon != null)
                    polygon.enabled = false;
                box.enabled = true;
                return;
            }

            if (polygon == null)
            {
                polygon = box.gameObject.AddComponent<PolygonCollider2D>();
                polygon.hideFlags = HideFlags.DontSave;
            }
            polygon.isTrigger = box.isTrigger;
            polygon.pathCount = coverage.VisibleParts.Count;
            var toLocal = box.transform.worldToLocalMatrix * space.localToWorldMatrix; // parts are in `space` (sheet-local): where each piece physically lies
            for (int i = 0; i < coverage.VisibleParts.Count; i++)
            {
                var verts = coverage.VisibleParts[i].Vertices;
                var path = new Vector2[verts.Count];
                for (int v = 0; v < verts.Count; v++)
                    path[v] = toLocal.MultiplyPoint3x4(verts[v]);
                polygon.SetPath(i, path);
            }
            box.enabled = false;
            polygon.enabled = true;
        }
    }
}
