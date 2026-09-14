using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Applies a <see cref="CoverageResult"/> to one authored collider (a <see cref="BoxCollider2D"/> or a
    /// <see cref="PolygonCollider2D"/>): whole → the authored collider is on; nothing → off; partial → the
    /// authored collider is off and a runtime <see cref="PolygonCollider2D"/> on the same object carries one
    /// path per visible piece. Shared by every occludee with a single authored collider.
    /// </summary>
    /// <remarks>
    /// The runtime polygon is held by reference and never looked up, so an authored <see cref="PolygonCollider2D"/>
    /// on the same object is never confused with it. It is <see cref="HideFlags.DontSave"/>: never serialized,
    /// never seen by the Sheet Studio.
    /// </remarks>
    public sealed class OccludedCollider
    {
        readonly Collider2D authored;
        PolygonCollider2D runtime;

        public OccludedCollider(Collider2D authored)
        {
            this.authored = authored;
        }

        /// <summary>The colliders currently standing in for the authored one (zero, the authored one, or the runtime polygon).</summary>
        public IEnumerable<Collider2D> LiveColliders
        {
            get
            {
                if (authored != null && authored.enabled)
                    yield return authored;
                if (runtime != null && runtime.enabled)
                    yield return runtime;
            }
        }

        /// <summary>The runtime polygon, if one has been created; for tests.</summary>
        public PolygonCollider2D RuntimePolygon => runtime;

        public void Apply(in CoverageResult coverage, Transform space)
        {
            if (coverage.IsNone)
            {
                authored.enabled = false;
                if (runtime != null)
                    runtime.enabled = false;
                return;
            }

            if (coverage.IsWhole)
            {
                if (runtime != null)
                    runtime.enabled = false;
                authored.enabled = true;
                return;
            }

            if (runtime == null)
            {
                runtime = authored.gameObject.AddComponent<PolygonCollider2D>();
                runtime.hideFlags = HideFlags.DontSave;
            }
            runtime.isTrigger = authored.isTrigger;
            runtime.pathCount = coverage.VisibleParts.Count;
            var toLocal = authored.transform.worldToLocalMatrix * space.localToWorldMatrix; // parts are in `space` (sheet-local): where each piece physically lies
            for (int i = 0; i < coverage.VisibleParts.Count; i++)
            {
                var verts = coverage.VisibleParts[i].Vertices;
                var path = new Vector2[verts.Count];
                for (int v = 0; v < verts.Count; v++)
                    path[v] = toLocal.MultiplyPoint3x4(verts[v]);
                runtime.SetPath(i, path);
            }
            authored.enabled = false;
            runtime.enabled = true;
        }
    }
}
