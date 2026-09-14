using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Draws a <see cref="TerrainRegion"/>'s authored collider as a solid fill while its Sheet's
    /// <see cref="Sheet.ShowCollision"/> is on. A testing view of the collision (Aaron, 2026-09-14): static terrain
    /// has no drawing of its own - the map art is it - so a sheet whose art is not drawn yet has invisible walls.
    /// Off, the region is invisible as shipped.
    /// </summary>
    /// <remarks>
    /// The fill is a hidden, never-saved child mesh on the region's own layer, so the face camera renders it into
    /// the sheet's texture and it folds, clips and mirrors with the rest of the face: nothing in the fold or
    /// occlusion systems knows it exists, and cutting the feature is removing this component from the terrain
    /// prefabs. Runs in edit mode too, so the Studio's panes and fold preview show what the game will show; there
    /// it also follows collider and colour edits (through the editor's object-change and undo events). Drawn in
    /// the region's local space at its own z (the terrain prefabs sit at -0.05: above the art at -0.01, under
    /// creases at -0.1). The colour is per prefab, so Wall and Water read differently.
    /// </remarks>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TerrainRegion))]
    public sealed class TerrainFill : MonoBehaviour
    {
        /// <summary>Name of the hidden child carrying the mesh; found again by name if it outlived this component's state.</summary>
        public const string PartName = "Collision Fill";
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [Header("Fill")]
        [SerializeField, Tooltip("Colour of the fill while the Sheet shows collision. Solid so the collision reads at a glance; alpha is honoured.")]
        Color color = new(0.35f, 0.35f, 0.35f, 1f);

        [SerializeField, Tooltip("URP/Unlit transparent material. Its _BaseColor is replaced by Color at runtime.")]
        Material material;

        readonly PolygonMeshBuilder builder = new();
        readonly List<Vector2> outline = new();
        // The shape the mesh was last built from, compared exactly so an edit-mode drag rebuilds and nothing else does.
        readonly List<Vector2> builtOutline = new();
        Sheet sheet;
        MeshFilter filter;
        MeshRenderer fillRenderer;
        MaterialPropertyBlock block;
        bool built;
        Vector2 builtOffset;
        Vector2 builtSize;
        bool reportedNoMaterial;

        /// <summary>The hidden child's renderer, or null while none has been created. Test seam.</summary>
        internal MeshRenderer Part => fillRenderer;

        /// <summary>The fill colour. Test seam.</summary>
        internal Color Color => color;

        void OnEnable()
        {
#if UNITY_EDITOR
            Live.Add(this);
            HookEditorEdits();
#endif
            BindSheet();
            Refresh();
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            Live.Remove(this);
#endif
            UnbindSheet();
            if (fillRenderer != null)
                fillRenderer.enabled = false;
        }

        void OnDestroy()
        {
            // The part is DontSave, so it is never written out, but it must not outlive the region either.
            if (filter != null && filter.sharedMesh != null)
                DestroyNow(filter.sharedMesh);
            if (fillRenderer != null)
                DestroyNow(fillRenderer.gameObject);
            filter = null;
            fillRenderer = null;
        }

        void OnTransformParentChanged()
        {
            // The Studio parents a freshly placed region after instantiating it; a region moved between sheets in
            // the hierarchy changes sheet the same way.
            BindSheet();
            Refresh();
        }

#if UNITY_EDITOR
        // Editor edits. An ExecuteAlways Update is not called reliably in edit mode (a SerializedObject resize of the
        // collider never ticked it), so every enabled fill registers here and any published object change or undo
        // re-checks them all - the same hooks the Studio window repaints on. The check is an exact shape and colour
        // compare, so a sheet of regions costs nothing measurable per edit. In Play Mode the authored colliders are
        // static; only the flag changes, and that arrives by event.
        static readonly HashSet<TerrainFill> Live = new();
        static bool hookedEditorEdits;

        static void HookEditorEdits()
        {
            if (hookedEditorEdits)
                return;
            hookedEditorEdits = true; // Statics reset with the domain, so the next OnEnable after a reload re-hooks.
            UnityEditor.ObjectChangeEvents.changesPublished += OnChangesPublished;
            UnityEditor.Undo.undoRedoPerformed += RefreshAllLive;
        }

        static void OnChangesPublished(ref UnityEditor.ObjectChangeEventStream stream) => RefreshAllLive();

        static void RefreshAllLive()
        {
            // Refresh never registers or unregisters a fill, so enumerating the live set is safe.
            foreach (var fill in Live)
                if (fill != null)
                    fill.Refresh();
        }
#endif

        void BindSheet()
        {
            var found = GetComponentInParent<Sheet>(true);
            if (found == sheet)
                return;
            UnbindSheet();
            sheet = found;
            if (sheet != null)
                sheet.ShowCollisionChanged += Refresh;
        }

        void UnbindSheet()
        {
            if (sheet != null)
                sheet.ShowCollisionChanged -= Refresh;
            sheet = null;
        }

        /// <summary>
        /// Brings the fill in line with the sheet's flag, the collider's shape and the colour. Idempotent; the
        /// sheet's event, the editor hooks and the test seam all come here.
        /// </summary>
        internal void Refresh()
        {
            if (this == null || !isActiveAndEnabled || sheet == null || !sheet.ShowCollision)
            {
                if (fillRenderer != null)
                    fillRenderer.enabled = false;
                return;
            }

            if (material == null)
            {
                if (!reportedNoMaterial)
                {
                    reportedNoMaterial = true;
                    Debug.LogError($"TerrainFill '{name}' has no material; its collision fill is invisible.", this);
                }
                return;
            }

            var authored = AuthoredCollider();
            if (authored == null)
            {
                // TerrainRegion reports a region with no usable collider; there is nothing to fill.
                if (fillRenderer != null)
                    fillRenderer.enabled = false;
                return;
            }

            EnsurePart();
            if (ShapeChanged(authored))
                Build(authored);
            block.SetColor(BaseColor, color);
            fillRenderer.SetPropertyBlock(block);
            fillRenderer.sharedMaterial = material;
            fillRenderer.gameObject.layer = gameObject.layer;
            fillRenderer.enabled = true;
        }

        /// <summary>The region's one authored collider, by TerrainRegion's rule: a box, else a polygon; both or neither is nothing.</summary>
        Collider2D AuthoredCollider()
        {
            var box = GetComponent<BoxCollider2D>();
            var polygon = GetComponent<PolygonCollider2D>();
            if (box != null && polygon != null)
                return null;
            return box != null ? box : polygon;
        }

        void EnsurePart()
        {
            if (fillRenderer == null)
            {
                // A part left from before a domain reload or a Play Mode transition is adopted rather than doubled.
                var existing = transform.Find(PartName);
                if (existing != null && existing.TryGetComponent(out MeshFilter existingFilter) && existing.TryGetComponent(out MeshRenderer existingRenderer))
                {
                    filter = existingFilter;
                    fillRenderer = existingRenderer;
                }
                else
                {
                    if (existing != null)
                        DestroyNow(existing.gameObject);
                    var go = new GameObject(PartName) { layer = gameObject.layer, hideFlags = HideFlags.HideAndDontSave };
                    go.transform.SetParent(transform, false);
                    filter = go.AddComponent<MeshFilter>();
                    fillRenderer = go.AddComponent<MeshRenderer>();
                }
                fillRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                fillRenderer.receiveShadows = false;
                fillRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                fillRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                built = false;
            }
            if (filter.sharedMesh == null)
            {
                filter.sharedMesh = new Mesh { name = $"{name} collision fill", hideFlags = HideFlags.HideAndDontSave };
                filter.sharedMesh.MarkDynamic();
                built = false;
            }
            block ??= new MaterialPropertyBlock();
        }

        bool ShapeChanged(Collider2D authored)
        {
            if (!built)
                return true;
            switch (authored)
            {
                case BoxCollider2D box:
                    return builtOutline.Count != 0 || box.offset != builtOffset || box.size != builtSize;
                case PolygonCollider2D polygon:
                {
                    if (polygon.pathCount != 1)
                        return builtOutline.Count != 0;
                    polygon.GetPath(0, outline);
                    if (polygon.offset != builtOffset || outline.Count != builtOutline.Count)
                        return true;
                    for (int i = 0; i < outline.Count; i++)
                        if (outline[i] != builtOutline[i])
                            return true;
                    return false;
                }
                default:
                    return true;
            }
        }

        void Build(Collider2D authored)
        {
            builder.Clear();
            builtOutline.Clear();
            builtOffset = authored.offset;
            builtSize = Vector2.zero;
            switch (authored)
            {
                case BoxCollider2D box:
                {
                    builtSize = box.size;
                    var piece = ConvexPolygon.FromRect(new Rect(box.offset - box.size * 0.5f, box.size));
                    if (!piece.IsEmpty)
                        builder.AddPolygon(piece, _ => Vector2.zero);
                    break;
                }
                case PolygonCollider2D polygon when polygon.pathCount == 1:
                {
                    polygon.GetPath(0, builtOutline);
                    outline.Clear();
                    foreach (var point in builtOutline)
                        outline.Add(point + polygon.offset);
                    // An outline that is not one simple polygon has no footprint (and no live collider either -
                    // TerrainRegion reports it, the Studio draws it red); the fill is simply empty.
                    if (FaceFootprint.TryFromOutline(outline, out var footprint, out _))
                        foreach (var piece in footprint.Pieces)
                            builder.AddPolygon(piece, _ => Vector2.zero);
                    break;
                }
            }
            builder.Apply(filter.sharedMesh);
            built = true;
        }

        static void DestroyNow(Object o)
        {
            if (Application.isPlaying)
                Destroy(o);
            else
                DestroyImmediate(o);
        }
    }
}
