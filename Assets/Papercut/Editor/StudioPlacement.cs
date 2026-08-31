using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// The Sheet Studio's editing operations — place, move, resize, duplicate, delete — plus the collider
    /// geometry used for pane hit-testing. Every operation goes through <see cref="Undo"/>.
    /// </summary>
    /// <remarks>
    /// Hit-testing computes purely from serialized collider shape data transformed by
    /// <c>localToWorldMatrix</c> — never <c>Physics2D</c> queries or <c>Collider2D.bounds</c>, because in a
    /// Prefab Stage preview scene the physics world has not registered the colliders and both are stale.
    /// </remarks>
    public static class StudioPlacement
    {
        /// <summary>Arrow-key nudge distance when the snap increment is 0 (free placement).</summary>
        public const float FreeNudge = 0.1f;

        // ----- Placement -----

        /// <summary>Rounds to the nearest snap increment; an increment of 0 means free placement.</summary>
        public static float Snap(float value, float increment)
            => increment > 0f ? Mathf.Round(value / increment) * increment : value;

        public static Vector2 Snap(Vector2 value, float increment)
            => new(Snap(value.x, increment), Snap(value.y, increment));

        /// <summary>
        /// Instantiates <paramref name="prefab"/> under <paramref name="faceRoot"/> at the given sheet-local
        /// x/y. The prefab root's authored z is preserved (the element z convention lives on prefab roots:
        /// Wall/Water/Gate/plates at −0.05, Block at 0 — z 0 would be coplanar with the transparent Surface
        /// quad). The face's layer is applied recursively (Block has a Visual child).
        /// </summary>
        public static GameObject Place(GameObject prefab, Transform faceRoot, SheetFace face, Vector2 sheetLocal, float snapIncrement)
        {
            if (prefab == null || faceRoot == null)
            {
                Debug.LogError("StudioPlacement.Place: prefab and face root must both be assigned.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, faceRoot);
            if (instance == null)
            {
                Debug.LogError($"StudioPlacement.Place: could not instantiate '{prefab.name}'.");
                return null;
            }

            var snapped = Snap(sheetLocal, snapIncrement);
            var t = instance.transform;
            t.localPosition = new Vector3(snapped.x, snapped.y, t.localPosition.z);
            SetLayerRecursively(instance.transform, FoldLayers.LayerOf(face));
            Undo.RegisterCreatedObjectUndo(instance, $"Place {prefab.name}");
            return instance;
        }

        /// <summary>Moves an element to a new sheet-local x/y, preserving its z.</summary>
        public static void Move(GameObject element, Vector2 sheetLocal, float snapIncrement)
        {
            if (element == null)
                return;
            var t = element.transform;
            Undo.RecordObject(t, $"Move {element.name}");
            var snapped = Snap(sheetLocal, snapIncrement);
            t.localPosition = new Vector3(snapped.x, snapped.y, t.localPosition.z);
        }

        // ----- Resizing -----

        /// <summary>
        /// The explicit resizable set: terrain regions (Wall, Water, Gate) and pressure plates (Aaron,
        /// 2026-08-31). Never keyed on "has a BoxCollider2D" — Block and both plates have one on their root,
        /// and resizing a Block would desync its footprint from its PolygonCollider2D pusher shape.
        /// </summary>
        public static bool IsResizable(GameObject element)
            => element != null &&
               (element.GetComponent<TerrainRegion>() != null || element.GetComponent<PressurePlate>() != null) &&
               element.GetComponent<BoxCollider2D>() != null;

        /// <summary>
        /// Applies a face-local rect to a resizable element: the transform moves to the rect's centre
        /// (z preserved), the BoxCollider2D covers the rect with offset zero, and a same-object
        /// SpriteRenderer in a non-Simple draw mode (the Wall/Water/Hold Plate tiled pattern) keeps its
        /// size equal to the collider's.
        /// </summary>
        public static void Resize(GameObject element, Rect faceLocalRect, float snapIncrement)
        {
            if (!IsResizable(element))
            {
                Debug.LogError($"StudioPlacement.Resize: '{element?.name}' is not in the resizable set.");
                return;
            }

            var min = Snap(faceLocalRect.min, snapIncrement);
            var max = Snap(faceLocalRect.max, snapIncrement);
            var size = new Vector2(Mathf.Max(max.x - min.x, MinimumSize), Mathf.Max(max.y - min.y, MinimumSize));
            var centre = (min + max) * 0.5f;

            var t = element.transform;
            var box = element.GetComponent<BoxCollider2D>();
            var sprite = element.GetComponent<SpriteRenderer>();
            var syncSprite = sprite != null && sprite.drawMode != SpriteDrawMode.Simple;

            Undo.RecordObject(t, $"Resize {element.name}");
            Undo.RecordObject(box, $"Resize {element.name}");
            if (syncSprite)
                Undo.RecordObject(sprite, $"Resize {element.name}");

            t.localPosition = new Vector3(centre.x, centre.y, t.localPosition.z);
            box.offset = Vector2.zero;
            box.size = size;
            if (syncSprite)
                sprite.size = size;
        }

        const float MinimumSize = 0.05f;

        // ----- Duplicate / delete -----

        /// <summary>
        /// Duplicates an element through the editor pasteboard (preserving its prefab connection and every
        /// override), offset slightly so the copy is visible. Returns the copy, or null.
        /// </summary>
        public static GameObject Duplicate(GameObject element, float snapIncrement)
        {
            if (element == null)
                return null;

            // The pasteboard duplicate is the only editor path that copies a prefab instance with its
            // connection and every override intact (plain Instantiate severs the connection); Unity's own
            // hierarchy Ctrl+D goes through the same internal call.
            var previous = Selection.objects;
            Selection.activeGameObject = element;
            Unsupported.DuplicateGameObjectsUsingPasteboard();
            var copy = Selection.activeGameObject;
            if (copy == null || copy == element)
            {
                Selection.objects = previous;
                return null;
            }

            var offset = snapIncrement > 0f ? snapIncrement : 0.5f;
            var t = copy.transform;
            Undo.RecordObject(t, $"Duplicate {element.name}"); // Without this, redo recreates the copy exactly on top of the original.
            t.localPosition += new Vector3(offset, -offset, 0f);
            return copy;
        }

        public static void Delete(GameObject element)
        {
            if (element != null)
                Undo.DestroyObjectImmediate(element);
        }

        // ----- Guard rails -----

        /// <summary>
        /// True if the Studio may move/resize/delete this object: a descendant of the sheet's Front or Back
        /// root, but never a face root itself, the Surface quad, or the face's background-art object (the
        /// art slot owns that).
        /// </summary>
        public static bool CanEdit(GameObject candidate, Sheet sheet)
        {
            if (candidate == null || sheet == null || sheet.Front == null || sheet.Back == null)
                return false;

            var t = candidate.transform;
            if (t == sheet.Front || t == sheet.Back)
                return false;
            if (!t.IsChildOf(sheet.Front) && !t.IsChildOf(sheet.Back))
                return false;
            if (candidate.GetComponent<MeshFilter>() != null && candidate.name == "Surface")
                return false;

            var faceRoot = t.IsChildOf(sheet.Front) ? sheet.Front : sheet.Back;
            if (StudioSheetOps.FindFaceArt(faceRoot, out _) == candidate)
                return false;
            return true;
        }

        // ----- Collider geometry (shared by pane overlays, hit-testing, and resize handles) -----

        /// <summary>
        /// The face-local outline of a collider, from its serialized shape data. Circle colliders become a
        /// 16-gon; capsules are approximated by their box. Returns false for shapes with no useful outline.
        /// </summary>
        public static bool TryGetFaceLocalOutline(Collider2D collider, Transform faceRoot, List<Vector2> outline)
        {
            outline.Clear();
            if (collider == null || faceRoot == null)
                return false;

            var toFace = faceRoot.worldToLocalMatrix * collider.transform.localToWorldMatrix;

            switch (collider)
            {
                case BoxCollider2D box:
                    AppendRect(outline, toFace, box.offset, box.size);
                    return true;
                case CapsuleCollider2D capsule:
                    AppendRect(outline, toFace, capsule.offset, capsule.size);
                    return true;
                case CircleCollider2D circle:
                {
                    const int segments = 16;
                    for (int i = 0; i < segments; i++)
                    {
                        var angle = i * Mathf.PI * 2f / segments;
                        var local = circle.offset + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * circle.radius;
                        outline.Add(toFace.MultiplyPoint3x4(local));
                    }
                    return true;
                }
                case PolygonCollider2D polygon when polygon.pathCount > 0:
                {
                    foreach (var point in polygon.GetPath(0))
                        outline.Add(toFace.MultiplyPoint3x4(point + polygon.offset));
                    return outline.Count >= 3;
                }
                default:
                    return false;
            }
        }

        static void AppendRect(List<Vector2> outline, Matrix4x4 toFace, Vector2 offset, Vector2 size)
        {
            var half = size * 0.5f;
            outline.Add(toFace.MultiplyPoint3x4(offset + new Vector2(-half.x, -half.y)));
            outline.Add(toFace.MultiplyPoint3x4(offset + new Vector2(half.x, -half.y)));
            outline.Add(toFace.MultiplyPoint3x4(offset + new Vector2(half.x, half.y)));
            outline.Add(toFace.MultiplyPoint3x4(offset + new Vector2(-half.x, half.y)));
        }

        /// <summary>
        /// The editable element at a face-local point, or null. Iterates colliders under the face root and
        /// tests serialized shapes; among hits, the smallest outline wins so overlapping/nested things stay
        /// pickable. Returns the outermost editable ancestor below the face root (the element root).
        /// </summary>
        public static GameObject PickElement(Transform faceRoot, Sheet sheet, Vector2 faceLocalPoint)
        {
            if (faceRoot == null)
                return null;

            GameObject best = null;
            var bestArea = float.MaxValue;
            var outline = new List<Vector2>();
            foreach (var collider in faceRoot.GetComponentsInChildren<Collider2D>(true))
            {
                if (!TryGetFaceLocalOutline(collider, faceRoot, outline))
                    continue;
                if (!PointInPolygon(faceLocalPoint, outline))
                    continue;

                var element = ElementRootOf(collider.transform, faceRoot);
                if (element == null || !CanEdit(element, sheet))
                    continue;

                var area = Mathf.Abs(SignedArea(outline));
                if (area < bestArea)
                {
                    bestArea = area;
                    best = element;
                }
            }
            return best;
        }

        /// <summary>The direct child of the face root this transform belongs to — the placed element's root.</summary>
        public static GameObject ElementRootOf(Transform transform, Transform faceRoot)
        {
            if (transform == null || faceRoot == null || !transform.IsChildOf(faceRoot) || transform == faceRoot)
                return null;
            while (transform.parent != faceRoot)
                transform = transform.parent;
            return transform.gameObject;
        }

        /// <summary>The face-local axis-aligned rect around an element's BoxCollider2D, for resize handles.</summary>
        public static Rect FaceLocalRect(GameObject element, Transform faceRoot)
        {
            var box = element.GetComponent<BoxCollider2D>();
            var outline = new List<Vector2>();
            if (box == null || !TryGetFaceLocalOutline(box, faceRoot, outline))
                return Rect.zero;

            Vector2 min = outline[0], max = outline[0];
            foreach (var p in outline)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        public static bool PointInPolygon(Vector2 point, List<Vector2> polygon)
        {
            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var a = polygon[i];
                var b = polygon[j];
                if (a.y > point.y != b.y > point.y &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        static float SignedArea(List<Vector2> polygon)
        {
            var area = 0f;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
                area += (polygon[j].x + polygon[i].x) * (polygon[j].y - polygon[i].y);
            return area * 0.5f;
        }

        /// <summary>Mirrors <c>Sheet.Awake</c>'s recursive layer assignment for a single placed element.</summary>
        public static void SetLayerRecursively(Transform root, int layer)
        {
            if (root == null || layer < 0)
                return;
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }
    }
}
