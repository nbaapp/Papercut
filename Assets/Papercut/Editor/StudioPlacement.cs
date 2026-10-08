using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// The Sheet Studio's editing operations — place, move, resize, duplicate, delete — plus the collider
    /// geometry used for pane hit-testing. Every operation goes through <see cref="Undo"/> and ends with
    /// <see cref="StudioEdits.Edited(GameObject)"/>, so the Prefab Stage always knows it has something to save.
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
        /// A universal region (above the sheet, Aaron 2026-09-28): a prefab asset or placed element whose root
        /// <see cref="TerrainRegion"/> has its Universal flag on. Serialized read, so it holds in edit mode.
        /// </summary>
        public static bool IsUniversal(GameObject element)
        {
            if (element == null || !element.TryGetComponent(out TerrainRegion region))
                return false;
            return new SerializedObject(region).FindProperty("universal").boolValue;
        }

        /// <summary>
        /// The root a prefab is placed under: the sheet's Above root for a universal prefab, else
        /// <paramref name="faceRoot"/>. Null, with a reason, when a universal prefab has nowhere to go (the sheet
        /// has no Above root) or is being placed from a pane that does not edit Above content.
        /// </summary>
        public static Transform TargetRoot(GameObject prefab, Sheet sheet, Transform faceRoot, bool aboveEditable, out string reason)
        {
            reason = null;
            if (!IsUniversal(prefab))
                return faceRoot;
            if (!aboveEditable)
            {
                reason = "Universal regions sit above the sheet: place them in the Front pane.";
                return null;
            }
            if (sheet == null || sheet.Above == null)
            {
                reason = "This sheet has no Above root; re-create it from the Sheet prefab to place universal regions.";
                return null;
            }
            return sheet.Above;
        }

        /// <summary>The layer content under <paramref name="root"/> lives on: the face's layer, or Default under the Above root.</summary>
        public static int LayerFor(Transform root, Sheet sheet, SheetFace face)
            => sheet != null && root == sheet.Above ? Sheet.AboveLayer : FoldLayers.LayerOf(face);

        /// <summary>
        /// Instantiates <paramref name="prefab"/> under <paramref name="root"/> (a face root, or the sheet's Above root
        /// for a universal region) at the given sheet-local x/y. The prefab root's authored z is preserved (the element z
        /// convention: Wall/Water/Gate/plates/props roots at −0.05; the Block root stays at 0 because its runtime mesh
        /// takes its depth from the fold renderer, and its Drawing child carries −0.08, in front of the other elements,
        /// so a block or paperweight over a wall draws above it in the Studio as the game composites it — at z 0 a sprite
        /// sits on the opaque Surface quad, behind the face art at −0.01). The root's layer is applied recursively (Block
        /// has Visual and Drawing children): the face's layer, or Default under Above (<see cref="LayerFor"/>).
        /// </summary>
        public static GameObject Place(GameObject prefab, Transform root, SheetFace face, Vector2 sheetLocal, float snapIncrement)
        {
            if (prefab == null || root == null)
            {
                Debug.LogError("StudioPlacement.Place: prefab and root must both be assigned.");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            if (instance == null)
            {
                Debug.LogError($"StudioPlacement.Place: could not instantiate '{prefab.name}'.");
                return null;
            }

            var snapped = Snap(sheetLocal, snapIncrement);
            var t = instance.transform;
            t.localPosition = new Vector3(snapped.x, snapped.y, t.localPosition.z);
            SetLayerRecursively(instance.transform, LayerFor(root, root.GetComponentInParent<Sheet>(true), face));
            Undo.RegisterCreatedObjectUndo(instance, $"Place {prefab.name}");
            StudioEdits.Edited(instance);
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
            StudioEdits.Edited(element);
        }

        /// <summary>
        /// Moves an element to an authored position on a face — reparenting to the other face root when the
        /// fold-view drag crossed onto the other face's content (Aaron: follow-the-cursor). The cross-face
        /// re-layer is hierarchy-undo-recorded (a raw layer write inside an undo group would survive undo,
        /// leaving the element on the wrong face's layer, silently invisible in its pane).
        /// </summary>
        public static bool MoveMapped(GameObject element, Sheet sheet, SheetFace face, Vector2 authoredLocal, float snapIncrement, out string reason)
        {
            reason = null;
            if (element == null || sheet == null)
                return false;
            var targetRoot = face == SheetFace.Front ? sheet.Front : sheet.Back;
            if (targetRoot == null)
                return false;

            var t = element.transform;
            if (sheet.Above != null && t.parent == sheet.Above)
            {
                // Above content is in sheet space and is never mapped through a fold: the Base is the sheet itself.
                if (face != SheetFace.Front)
                {
                    reason = $"'{element.name}' sits above the sheet and cannot be moved to the Back.";
                    return false;
                }
                Move(element, authoredLocal, snapIncrement);
                return true;
            }
            if (t.parent == targetRoot)
            {
                Move(element, authoredLocal, snapIncrement);
                return true;
            }

            // An object living INSIDE a larger prefab instance (e.g. inherited from the base Sheet prefab)
            // cannot be restructured; an element that is itself its instance's root (every Studio-placed
            // prefab) can. Refuse cleanly, never let Unity log a restructure error mid-drag.
            if (PrefabUtility.IsPartOfPrefabInstance(element)
                && PrefabUtility.GetNearestPrefabInstanceRoot(element) != element)
            {
                reason = $"'{element.name}' is part of a larger prefab and cannot change face.";
                return false;
            }

            Undo.SetTransformParent(t, targetRoot, $"Move {element.name}");
            Undo.RegisterFullObjectHierarchyUndo(element, $"Move {element.name}");
            var snapped = Snap(authoredLocal, snapIncrement);
            t.localPosition = new Vector3(snapped.x, snapped.y, t.localPosition.z);
            SetLayerRecursively(t, FoldLayers.LayerOf(face));
            StudioEdits.Edited(element);
            return true;
        }

        /// <summary>
        /// The element's authored-space footprint rect: its root BoxCollider2D when it has one, else the bounding
        /// rect of its collider outlines, else — a prop — its sprite rect. Never silently empty.
        /// </summary>
        public static Rect AuthoredFootprint(GameObject element, Transform faceRoot)
            => element == null || faceRoot == null ? Rect.zero : Footprint(element, faceRoot.worldToLocalMatrix);

        /// <summary>
        /// <see cref="AuthoredFootprint"/> for a palette prefab as <see cref="Place"/> would land it: relative to
        /// the prefab root's position, with the root's own rotation and scale kept (the Tree root is at scale 3,
        /// so its drawing lands three times its sprite rect). For the placement ghosts, which have no face root.
        /// </summary>
        public static Rect PlacedFootprint(GameObject prefab)
            => prefab == null ? Rect.zero : Footprint(prefab, PlacedWorldToFace(prefab));

        static Rect Footprint(GameObject element, Matrix4x4 worldToFace)
        {
            var rect = FaceLocalRect(element, worldToFace);
            if (rect.width > 0f && rect.height > 0f)
                return rect;

            var outline = new List<Vector2>();
            var any = false;
            Vector2 min = default, max = default;
            foreach (var collider in element.GetComponentsInChildren<Collider2D>(true))
            {
                if (TryGetOutline(collider, worldToFace, outline))
                    Extend(outline, ref any, ref min, ref max);
            }
            if (!any && TryGetPropOutline(element, worldToFace, outline))
                Extend(outline, ref any, ref min, ref max);
            return any ? Rect.MinMaxRect(min.x, min.y, max.x, max.y) : Rect.zero;
        }

        static void Extend(List<Vector2> outline, ref bool any, ref Vector2 min, ref Vector2 max)
        {
            foreach (var p in outline)
            {
                if (!any)
                {
                    min = max = p;
                    any = true;
                }
                else
                {
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            }
        }

        // ----- Props (Aaron, 2026-09-10: static scenery is art only; its collision is laid over it as Wall regions) -----

        /// <summary>
        /// A prop: an element drawn by a Simple-mode sprite on its root with no authored collider anywhere under
        /// it (the Tree). It has no collision of its own — Aaron: "I would rather separate out the collision from
        /// those objects, and manually put it over them" — so the Studio's footprint for it is its sprite rect.
        /// The face's background art is also a bare sprite; it is never an element (<see cref="CanEdit"/>), so
        /// callers that enumerate a face's props go through <see cref="CollectProps"/>.
        /// </summary>
        public static bool IsProp(GameObject element)
        {
            if (element == null || !element.TryGetComponent(out SpriteRenderer sprite)
                || sprite.sprite == null || sprite.drawMode != SpriteDrawMode.Simple)
                return false;
            foreach (var collider in element.GetComponentsInChildren<Collider2D>(true))
            {
                if (IsAuthoredShape(collider))
                    return false;
            }
            return true;
        }

        /// <summary>The editable props directly under <paramref name="faceRoot"/> (the face art excluded).</summary>
        public static void CollectProps(Transform faceRoot, Sheet sheet, List<GameObject> props)
        {
            props.Clear();
            if (faceRoot == null)
                return;
            for (int i = 0; i < faceRoot.childCount; i++)
            {
                var child = faceRoot.GetChild(i).gameObject;
                if (IsProp(child) && CanEdit(child, sheet))
                    props.Add(child);
            }
        }

        /// <summary>
        /// The face-local outline (four corners) of a prop's sprite rect: the sprite's local bounds, flipped as
        /// the renderer flips them, through the same matrix a collider outline takes. False for anything that is
        /// not a prop.
        /// </summary>
        public static bool TryGetPropOutline(GameObject element, Transform faceRoot, List<Vector2> outline)
            => faceRoot != null && TryGetPropOutline(element, faceRoot.worldToLocalMatrix, outline);

        /// <summary><see cref="TryGetPropOutline"/> for a palette prefab, as <see cref="Place"/> would land it (see <see cref="PlacedFootprint"/>).</summary>
        public static bool TryGetPlacedPropOutline(GameObject prefab, List<Vector2> outline)
            => prefab != null && TryGetPropOutline(prefab, PlacedWorldToFace(prefab), outline);

        static bool TryGetPropOutline(GameObject element, Matrix4x4 worldToFace, List<Vector2> outline)
        {
            outline.Clear();
            if (!IsProp(element))
                return false;
            var renderer = element.GetComponent<SpriteRenderer>();
            var bounds = renderer.sprite.bounds;
            var centre = (Vector2)bounds.center;
            if (renderer.flipX) centre.x = -centre.x;
            if (renderer.flipY) centre.y = -centre.y;
            AppendRect(outline, worldToFace * element.transform.localToWorldMatrix, centre, bounds.size);
            return true;
        }

        // ----- Resizing -----

        /// <summary>
        /// The resizable set: a terrain region or pressure plate that is not drawn with a Simple-mode sprite —
        /// a bare collision box (Wall, Water: the map art draws them, Aaron 2026-09-03) or a Tiled/Sliced
        /// placeholder (Gate), the cases where <see cref="Resize"/> can keep the picture and the collider together.
        /// A hand-drawn Simple sprite is a fixed-size drawing (the plates, the props; Aaron, 2026-09-03 — this
        /// supersedes "plates are resizable", 2026-08-31). Never keyed on "has a BoxCollider2D" alone — Block has one
        /// on its root, and resizing a Block would desync its footprint from its PolygonCollider2D pusher shape.
        /// </summary>
        public static bool IsResizable(GameObject element)
            => element != null &&
               (element.GetComponent<TerrainRegion>() != null || element.GetComponent<PressurePlate>() != null) &&
               element.GetComponent<BoxCollider2D>() != null &&
               !(element.TryGetComponent(out SpriteRenderer sprite) && sprite.drawMode == SpriteDrawMode.Simple);

        /// <summary>
        /// Applies a face-local rect to a resizable element: the transform moves to the rect's centre
        /// (z preserved), the BoxCollider2D covers the rect with offset zero, and a same-object
        /// SpriteRenderer in a Tiled or Sliced draw mode (the Gate placeholder), if any, keeps its
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
            StudioEdits.Edited(element);
        }

        const float MinimumSize = 0.05f;

        // ----- Polygon terrain -----

        /// <summary>
        /// A polygon terrain region, prefab asset or placed instance: a <see cref="TerrainRegion"/> whose root
        /// has an authored <see cref="PolygonCollider2D"/> and no <see cref="BoxCollider2D"/>. A runtime clip
        /// polygon (<see cref="HideFlags.DontSave"/>) never counts.
        /// </summary>
        public static bool IsPolygonTerrain(GameObject element)
            => element != null
               && element.GetComponent<TerrainRegion>() != null
               && element.GetComponent<BoxCollider2D>() == null
               && AuthoredPolygon(element) != null;

        static PolygonCollider2D AuthoredPolygon(GameObject element)
        {
            foreach (var polygon in element.GetComponents<PolygonCollider2D>())
            {
                if ((polygon.hideFlags & HideFlags.DontSave) == 0)
                    return polygon;
            }
            return null;
        }

        /// <summary>
        /// Instantiates a polygon terrain prefab under <paramref name="faceRoot"/> with the given face-local
        /// outline: the root sits at the outline's bounds centre (prefab z preserved, face layer applied) and
        /// the collider's single path holds the vertices relative to it. Refuses, with an error, a prefab that
        /// is not polygon terrain or an outline that is not a simple polygon. One undo step.
        /// </summary>
        public static GameObject PlacePolygon(GameObject prefab, Transform faceRoot, SheetFace face, IReadOnlyList<Vector2> faceLocalPoints, float snapIncrement)
        {
            if (!IsPolygonTerrain(prefab))
            {
                Debug.LogError($"StudioPlacement.PlacePolygon: '{(prefab == null ? "null" : prefab.name)}' is not a polygon terrain prefab.");
                return null;
            }
            if (faceRoot == null)
            {
                Debug.LogError("StudioPlacement.PlacePolygon: the face root must be assigned.");
                return null;
            }

            var points = new List<Vector2>(faceLocalPoints.Count);
            foreach (var p in faceLocalPoints)
                points.Add(Snap(p, snapIncrement));
            if (!PolygonDecomposition.Validate(points, out var reason))
            {
                Debug.LogError($"StudioPlacement.PlacePolygon: {reason}.");
                return null;
            }

            var min = points[0];
            var max = points[0];
            foreach (var p in points)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            var instance = Place(prefab, faceRoot, face, (min + max) * 0.5f, 0f);
            if (instance == null)
                return null;
            WriteOutline(instance, faceRoot, points); // Covered by the creation's undo step.
            return instance;
        }

        /// <summary>The face-local vertices of a polygon terrain region's authored outline. False if it has none.</summary>
        public static bool GetOutline(GameObject element, Transform faceRoot, List<Vector2> outline)
        {
            outline.Clear();
            var polygon = element == null ? null : AuthoredPolygon(element);
            return polygon != null && faceRoot != null && FoldFootprint.FaceLocalOutline(polygon, faceRoot, outline);
        }

        /// <summary>Replaces the authored outline with face-local <paramref name="faceLocalPoints"/>, as one undo step.</summary>
        public static void SetOutline(GameObject element, Transform faceRoot, IReadOnlyList<Vector2> faceLocalPoints, string undoName)
        {
            var polygon = element == null ? null : AuthoredPolygon(element);
            if (polygon == null || faceRoot == null)
            {
                Debug.LogError($"StudioPlacement.SetOutline: '{element?.name}' is not a polygon terrain region.");
                return;
            }
            Undo.RecordObject(polygon, undoName);
            WriteOutline(element, faceRoot, faceLocalPoints);
            StudioEdits.Edited(element);
        }

        static void WriteOutline(GameObject element, Transform faceRoot, IReadOnlyList<Vector2> faceLocalPoints)
        {
            var polygon = AuthoredPolygon(element);
            var toLocal = element.transform.worldToLocalMatrix * faceRoot.localToWorldMatrix;
            var path = new Vector2[faceLocalPoints.Count];
            for (int i = 0; i < path.Length; i++)
                path[i] = (Vector2)toLocal.MultiplyPoint3x4(faceLocalPoints[i]) - polygon.offset;
            polygon.pathCount = 1;
            polygon.SetPath(0, path);
        }

        /// <summary>Moves one vertex to a face-local position (snapped).</summary>
        public static void SetVertex(GameObject element, Transform faceRoot, int index, Vector2 faceLocal, float snapIncrement)
        {
            var outline = new List<Vector2>();
            if (!GetOutline(element, faceRoot, outline) || index < 0 || index >= outline.Count)
            {
                Debug.LogError($"StudioPlacement.SetVertex: '{element?.name}' has no vertex {index}.");
                return;
            }
            outline[index] = Snap(faceLocal, snapIncrement);
            SetOutline(element, faceRoot, outline, $"Move vertex of {element.name}");
        }

        /// <summary>Inserts a vertex after <paramref name="afterIndex"/> (on the edge it starts), at a face-local position (snapped).</summary>
        public static void InsertVertex(GameObject element, Transform faceRoot, int afterIndex, Vector2 faceLocal, float snapIncrement)
        {
            var outline = new List<Vector2>();
            if (!GetOutline(element, faceRoot, outline) || afterIndex < 0 || afterIndex >= outline.Count)
            {
                Debug.LogError($"StudioPlacement.InsertVertex: '{element?.name}' has no edge {afterIndex}.");
                return;
            }
            outline.Insert(afterIndex + 1, Snap(faceLocal, snapIncrement));
            SetOutline(element, faceRoot, outline, $"Insert vertex of {element.name}");
        }

        /// <summary>Removes a vertex; refuses if fewer than three would remain or the result would not be a simple polygon.</summary>
        public static bool TryRemoveVertex(GameObject element, Transform faceRoot, int index, out string reason)
        {
            var outline = new List<Vector2>();
            if (!GetOutline(element, faceRoot, outline) || index < 0 || index >= outline.Count)
            {
                reason = $"'{element?.name}' has no vertex {index}";
                return false;
            }
            if (outline.Count <= 3)
            {
                reason = "a region needs at least three vertices";
                return false;
            }
            outline.RemoveAt(index);
            if (!PolygonDecomposition.Validate(outline, out reason))
                return false;
            SetOutline(element, faceRoot, outline, $"Remove vertex of {element.name}");
            return true;
        }

        /// <summary>True if the region's authored outline is a simple polygon (what Play Mode will accept).</summary>
        public static bool IsValidOutline(GameObject element, Transform faceRoot, out string reason)
        {
            var outline = new List<Vector2>();
            if (!GetOutline(element, faceRoot, outline))
            {
                reason = "the region has no outline";
                return false;
            }
            return PolygonDecomposition.Validate(outline, out reason);
        }

        /// <summary>
        /// The element's authored footprint as convex pieces, face-local: every authored collider under it
        /// through <see cref="FoldFootprint.Of"/> (a box is one piece, a polygon its decomposition, an invalid
        /// polygon nothing); a prop's sprite rect is its one piece. The exact shape for the fold preview's highlight.
        /// </summary>
        public static void AuthoredFootprintPieces(GameObject element, Transform faceRoot, List<ConvexPolygon> pieces)
        {
            pieces.Clear();
            if (element == null || faceRoot == null)
                return;
            foreach (var collider in element.GetComponentsInChildren<Collider2D>(true))
            {
                if (!IsAuthoredShape(collider))
                    continue;
                pieces.AddRange(FoldFootprint.Of(collider, faceRoot, out _).Pieces);
            }
            var outline = new List<Vector2>();
            if (TryGetPropOutline(element, faceRoot, outline))
                pieces.Add(new ConvexPolygon(outline));
        }

        /// <summary>
        /// True for a collider that is an element's authored footprint: never a runtime clip polygon
        /// (<see cref="HideFlags.DontSave"/>), and never a <see cref="PolygonCollider2D"/> beside a
        /// <see cref="BoxCollider2D"/> on the same object (the Block's pusher shape; the box is the footprint).
        /// </summary>
        static bool IsAuthoredShape(Collider2D collider)
        {
            if ((collider.hideFlags & HideFlags.DontSave) != 0)
                return false;
            return collider is not PolygonCollider2D || collider.GetComponent<BoxCollider2D>() == null;
        }

        /// <summary>A terrain region's own authored collider as convex pieces, for the Terrain-view fill. False if it has none (or an invalid outline).</summary>
        public static bool TryGetRegionPieces(TerrainRegion region, Transform faceRoot, List<ConvexPolygon> pieces)
        {
            pieces.Clear();
            if (region == null || faceRoot == null)
                return false;
            Collider2D authored = region.GetComponent<BoxCollider2D>();
            if (authored == null)
                authored = AuthoredPolygon(region.gameObject);
            if (authored == null)
                return false;
            pieces.AddRange(FoldFootprint.Of(authored, faceRoot, out _).Pieces);
            return pieces.Count > 0;
        }

        // ----- Magnet snap (Aaron, 2026-09-10: vertices snap to other regions' vertices and the sheet edge) -----

        /// <summary>
        /// Every vertex (box corner or polygon vertex) of every terrain region under <paramref name="faceRoot"/>
        /// except those of <paramref name="exclude"/> (the element being edited), face-local. Regions only
        /// (Aaron: "other regions' vertices and the sheet edge"); plates, blocks and props are not magnets.
        /// </summary>
        public static void MagnetTargets(Transform faceRoot, GameObject exclude, List<Vector2> targets)
        {
            targets.Clear();
            AddMagnetTargets(faceRoot, exclude, targets);
        }

        /// <summary><see cref="MagnetTargets(Transform, GameObject, List{Vector2})"/> over several roots sharing one space (a face root and the Above root: universal and face regions magnet to each other).</summary>
        public static void MagnetTargets(IReadOnlyList<Transform> roots, GameObject exclude, List<Vector2> targets)
        {
            targets.Clear();
            foreach (var root in roots)
                AddMagnetTargets(root, exclude, targets);
        }

        static void AddMagnetTargets(Transform faceRoot, GameObject exclude, List<Vector2> targets)
        {
            if (faceRoot == null)
                return;
            var outline = new List<Vector2>();
            foreach (var region in faceRoot.GetComponentsInChildren<TerrainRegion>(true))
            {
                if (exclude != null && region.transform.IsChildOf(exclude.transform))
                    continue;
                foreach (var collider in region.GetComponents<Collider2D>())
                {
                    if (IsAuthoredShape(collider) && TryGetFaceLocalOutline(collider, faceRoot, outline))
                        targets.AddRange(outline);
                }
            }
        }

        /// <summary>
        /// <paramref name="point"/> pulled onto the nearest target within <paramref name="radius"/> (sheet units);
        /// failing that, each coordinate within the radius of a sheet edge is put on that edge. Otherwise the
        /// point itself.
        /// </summary>
        public static Vector2 Magnet(Vector2 point, IReadOnlyList<Vector2> targets, float radius)
        {
            var best = point;
            var bestDistance = radius;
            var found = false;
            foreach (var target in targets)
            {
                var distance = Vector2.Distance(point, target);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = target;
                    found = true;
                }
            }
            if (found)
                return best;

            var half = SheetGeometry.HalfSize;
            var result = point;
            if (Mathf.Abs(point.x - half.x) <= radius) result.x = half.x;
            else if (Mathf.Abs(point.x + half.x) <= radius) result.x = -half.x;
            if (Mathf.Abs(point.y - half.y) <= radius) result.y = half.y;
            else if (Mathf.Abs(point.y + half.y) <= radius) result.y = -half.y;
            return result;
        }

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
            StudioEdits.Edited(copy);
            return copy;
        }

        /// <summary>
        /// Destroys an element, first unwiring it from every plate on its sheet so no effect keeps a dangling
        /// target the Studio could not show or clear (code review S1). One undo group with the destroy.
        /// </summary>
        public static void Delete(GameObject element)
        {
            if (element == null)
                return;
            var scene = element.scene;
            UnwireEverywhere(element);
            Undo.DestroyObjectImmediate(element);
            StudioEdits.Edited(scene);
        }

        static void UnwireEverywhere(GameObject target)
        {
            var sheet = target.GetComponentInParent<Sheet>();
            if (sheet == null)
                return;
            foreach (var root in new[] { sheet.Front, sheet.Back })
            {
                if (root == null)
                    continue;
                for (int i = 0; i < root.childCount; i++)
                {
                    var element = root.GetChild(i).gameObject;
                    if (element == target)
                        continue;
                    foreach (var slot in StudioLinks.GetSlots(element))
                    {
                        if (slot.Contains(target))
                            StudioLinks.Unwire(slot, target);
                    }
                }
            }
        }

        // ----- Guard rails -----

        /// <summary>
        /// True if the Studio may move/resize/delete this object: a descendant of the sheet's Front, Back or
        /// Above root, but never a root itself, the Surface quad, or the face's background-art object (the
        /// art slot owns that).
        /// </summary>
        public static bool CanEdit(GameObject candidate, Sheet sheet)
        {
            if (candidate == null || sheet == null || sheet.Front == null || sheet.Back == null)
                return false;

            var t = candidate.transform;
            if (t == sheet.Front || t == sheet.Back || t == sheet.Above)
                return false;
            var underAbove = sheet.Above != null && t.IsChildOf(sheet.Above);
            if (!t.IsChildOf(sheet.Front) && !t.IsChildOf(sheet.Back) && !underAbove)
                return false;
            if (candidate.GetComponent<MeshFilter>() != null && candidate.name == "Surface")
                return false;
            if (underAbove)
                return true;

            var faceRoot = t.IsChildOf(sheet.Front) ? sheet.Front : sheet.Back;
            if (StudioSheetOps.FindFaceArt(faceRoot, out _) == candidate)
                return false;
            return true;
        }

        // ----- Collider geometry (shared by pane overlays, hit-testing, and resize handles) -----

        /// <summary>
        /// The face-local outline of a collider, from its serialized shape data. Circle colliders become a
        /// 16-gon; capsules are approximated by their box. Returns false for shapes with no useful outline, and
        /// for a runtime clip polygon (<see cref="HideFlags.DontSave"/>) — never authored, never shown.
        /// </summary>
        public static bool TryGetFaceLocalOutline(Collider2D collider, Transform faceRoot, List<Vector2> outline)
        {
            if (faceRoot == null)
            {
                outline.Clear();
                return false;
            }
            return TryGetOutline(collider, faceRoot.worldToLocalMatrix, outline);
        }

        /// <summary>
        /// <see cref="TryGetFaceLocalOutline"/> for a shape of a palette prefab, as <see cref="Place"/> would land
        /// it: relative to the prefab root's position, with the root's own rotation and scale kept (see
        /// <see cref="PlacedFootprint"/>). For the placement ghosts.
        /// </summary>
        public static bool TryGetPlacedOutline(Collider2D collider, GameObject prefab, List<Vector2> outline)
        {
            if (prefab == null)
            {
                outline.Clear();
                return false;
            }
            return TryGetOutline(collider, PlacedWorldToFace(prefab), outline);
        }

        /// <summary>
        /// World → "face" for a prefab asset with no face root: strip the root's position and keep everything
        /// else, so a shape lands where <see cref="Place"/> (which only sets the root's x/y) will put it.
        /// </summary>
        static Matrix4x4 PlacedWorldToFace(GameObject prefab) => Matrix4x4.Translate(-prefab.transform.position);

        static bool TryGetOutline(Collider2D collider, Matrix4x4 worldToFace, List<Vector2> outline)
        {
            outline.Clear();
            if (collider == null || (collider.hideFlags & HideFlags.DontSave) != 0)
                return false;

            var toFace = worldToFace * collider.transform.localToWorldMatrix;

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
        /// tests serialized shapes, then the props by their sprite rects; among hits, the smallest outline wins
        /// so overlapping/nested things stay pickable (a Wall laid over a Tree's trunk is picked through the
        /// tree's drawing). Returns the outermost editable ancestor below the face root (the element root).
        /// </summary>
        public static GameObject PickElement(Transform faceRoot, Sheet sheet, Vector2 faceLocalPoint)
        {
            GameObject best = null;
            var bestArea = float.MaxValue;
            PickElement(faceRoot, sheet, faceLocalPoint, ref best, ref bestArea);
            return best;
        }

        /// <summary>
        /// <see cref="PickElement(Transform, Sheet, Vector2)"/> across several roots sharing one space (the Front
        /// pane's face root and the Above root): the smallest hit outline over all of them wins; on an equal area
        /// the later root wins (roots are searched last to first and the first hit keeps a tie), so listing Above
        /// last picks a universal region lying exactly over a face region of the same shape - it is on top.
        /// </summary>
        public static GameObject PickElement(IReadOnlyList<Transform> roots, Sheet sheet, Vector2 sheetLocalPoint)
        {
            GameObject best = null;
            var bestArea = float.MaxValue;
            for (int i = roots.Count - 1; i >= 0; i--)
                PickElement(roots[i], sheet, sheetLocalPoint, ref best, ref bestArea);
            return best;
        }

        /// <summary>The editable element at a point under one root, with the area of the outline it was hit through (for choosing between roots that do not share a space).</summary>
        public static bool TryPickElement(Transform root, Sheet sheet, Vector2 localPoint, out GameObject element, out float area)
        {
            element = null;
            area = float.MaxValue;
            PickElement(root, sheet, localPoint, ref element, ref area);
            return element != null;
        }

        static void PickElement(Transform faceRoot, Sheet sheet, Vector2 faceLocalPoint, ref GameObject best, ref float bestArea)
        {
            if (faceRoot == null)
                return;

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

                Consider(element, outline, ref best, ref bestArea);
            }

            var props = new List<GameObject>();
            CollectProps(faceRoot, sheet, props);
            foreach (var prop in props)
            {
                if (TryGetPropOutline(prop, faceRoot, outline) && PointInPolygon(faceLocalPoint, outline))
                    Consider(prop, outline, ref best, ref bestArea);
            }
        }

        static void Consider(GameObject element, List<Vector2> outline, ref GameObject best, ref float bestArea)
        {
            var area = Mathf.Abs(SignedArea(outline));
            if (area < bestArea)
            {
                bestArea = area;
                best = element;
            }
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
            => element == null || faceRoot == null ? Rect.zero : FaceLocalRect(element, faceRoot.worldToLocalMatrix);

        static Rect FaceLocalRect(GameObject element, Matrix4x4 worldToFace)
        {
            var box = element.GetComponent<BoxCollider2D>();
            var outline = new List<Vector2>();
            if (box == null || !TryGetOutline(box, worldToFace, outline))
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
