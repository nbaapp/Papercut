using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Papercut.EditorTools
{
    /// <summary>
    /// One face pane of the Sheet Studio: renders that face's content with a hidden editor camera into a
    /// RenderTexture, draws overlays (sheet outline, collider outlines, labels, selection, resize handles),
    /// and turns mouse input into <see cref="StudioPlacement"/> operations. All pixel math goes through
    /// <see cref="PaneView"/>.
    /// </summary>
    /// <remarks>
    /// Rendering uses a URP <c>SingleCameraRequest</c> per repaint (<c>Camera.Render()</c> is unsupported
    /// under a scriptable pipeline) with <c>Camera.scene</c> aimed at the open Prefab Stage's preview scene,
    /// and a cull mask of exactly one face layer. Validated by spike before this class was built on it.
    /// </remarks>
    public sealed class StudioPane
    {
        /// <summary>Everything a pane needs from the window for one draw.</summary>
        public struct Context
        {
            public PrefabStage Stage;
            public Sheet Sheet;
            public Transform FaceRoot;
            public StudioPalette Palette;
            public StudioLinkState Links;
            public float SnapIncrement;
            public bool ShowOverlays;
            public bool ShowLabels;
            public bool MirrorX;
        }

        const float CameraDistance = 5f;
        const float MinZoom = 4f;
        const float MaxZoom = 400f;
        const float ZoomStep = 0.05f;
        const float HandleHitPixels = 7f;
        const float HandleDrawPixels = 4f;
        static readonly Color BackgroundColor = new(0.22f, 0.2f, 0.18f, 1f);
        static readonly Color SheetOutlineColor = new(1f, 1f, 1f, 0.9f);
        static readonly Color WallColor = new(0.25f, 0.25f, 0.25f, 1f);
        static readonly Color GatedColor = new(0.25f, 0.45f, 0.9f, 1f);
        static readonly Color PlateColor = new(0.95f, 0.6f, 0.15f, 1f);
        static readonly Color BlockColor = new(0.6f, 0.4f, 0.2f, 1f);
        static readonly Color OtherColor = new(0.8f, 0.8f, 0.8f, 1f);
        static readonly Color SelectionColor = new(1f, 0.9f, 0.2f, 1f);
        static readonly Color GhostColor = new(0.3f, 1f, 0.4f, 0.7f);
        static readonly Color LinkColor = new(0.85f, 0.4f, 1f, 1f);

        enum DragKind { None, Pan, Move, Resize }

        readonly SheetFace face;

        PaneView view;
        bool framed;
        DragKind drag;
        Vector2 moveGrabOffset;
        Vector2Int resizeHandle; // Which edges the grabbed handle moves: components in {-1, 0, 1}.
        Rect resizeStartRect;
        int dragUndoGroup;

        GameObject cameraObject;
        Camera paneCamera;
        RenderTexture texture;
        bool textureFailed;

        readonly List<Vector2> scratchOutline = new();
        readonly Dictionary<TerrainRegion, bool> gatedCache = new();
        readonly Dictionary<GameObject, List<StudioLinks.Slot>> linkCache = new();

        public StudioPane(SheetFace face) => this.face = face;

        /// <summary>Drops cached per-element overlay data; the window calls this when objects change.</summary>
        public void InvalidateOverlayCache()
        {
            gatedCache.Clear();
            linkCache.Clear();
        }

        List<StudioLinks.Slot> SlotsOf(GameObject element)
        {
            if (!linkCache.TryGetValue(element, out var slots))
                linkCache[element] = slots = StudioLinks.GetSlots(element);
            return slots;
        }

        public SheetFace Face => face;

        /// <summary>The pane's live view, for probes and tests.</summary>
        public PaneView View => view;

        /// <summary>The pane's last rendered texture; null until the first repaint.</summary>
        public RenderTexture Texture => texture;

        public void FrameSheet() => framed = false;

        public void Dispose()
        {
            if (cameraObject != null)
                Object.DestroyImmediate(cameraObject);
            cameraObject = null;
            paneCamera = null;
            if (texture != null)
            {
                texture.Release();
                Object.DestroyImmediate(texture);
            }
            texture = null;
            textureFailed = false;
        }

        public void Draw(Rect rect, in Context ctx)
        {
            if (!framed || view.PaneRect.size != rect.size)
                view = framed ? view.WithRect(rect) : PaneView.FitSheet(rect, ctx.MirrorX);
            else
                view = view.WithRect(rect);
            view = view.WithMirror(ctx.MirrorX);
            framed = true;

            HandleInput(rect, ctx);

            if (Event.current.type != EventType.Repaint)
                return;

            if (!Render(rect, ctx))
            {
                EditorGUI.HelpBox(rect, "Pane could not render: RenderTexture creation or the URP render request failed.", MessageType.Error);
                return;
            }

            // The engine keeps GL texture conventions on RenderTextures (v=1 is world-up), which matches
            // GUI's top-at-top display; the mirror is applied in the blit so the camera stays plain.
            var coords = new Rect(ctx.MirrorX ? 1f : 0f, 0f, ctx.MirrorX ? -1f : 1f, 1f);
            GUI.DrawTextureWithTexCoords(rect, texture, coords);

            DrawOverlays(ctx);
            DrawGhost(ctx);
        }

        // ----- Rendering -----

        bool Render(Rect rect, in Context ctx)
        {
            var width = Mathf.Max(1, (int)rect.width);
            var height = Mathf.Max(1, (int)rect.height);
            if (!EnsureResources(width, height))
                return false;
            if (ctx.Stage == null || !FoldLayers.Valid)
                return false;

            paneCamera.transform.position = new Vector3(view.Centre.x, view.Centre.y, -CameraDistance);
            paneCamera.orthographicSize = rect.height / (2f * view.Zoom);
            paneCamera.aspect = rect.width / rect.height;
            paneCamera.cullingMask = 1 << FoldLayers.LayerOf(face);
            paneCamera.scene = ctx.Stage.scene;

            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = texture };
            if (!RenderPipeline.SupportsRenderRequest(paneCamera, request))
                return false;
            RenderPipeline.SubmitRenderRequest(paneCamera, request);
            return true;
        }

        bool EnsureResources(int width, int height)
        {
            if (paneCamera == null)
            {
                Dispose();
                cameraObject = new GameObject($"Sheet Studio {face} Camera") { hideFlags = HideFlags.HideAndDontSave };
                paneCamera = cameraObject.AddComponent<Camera>();
                paneCamera.enabled = false;
                paneCamera.orthographic = true;
                paneCamera.nearClipPlane = 0.1f;
                paneCamera.farClipPlane = CameraDistance * 2f;
                paneCamera.clearFlags = CameraClearFlags.SolidColor;
                paneCamera.backgroundColor = BackgroundColor;
            }

            if (texture != null && (texture.width != width || texture.height != height))
            {
                texture.Release();
                Object.DestroyImmediate(texture);
                texture = null;
                textureFailed = false;
            }
            if (texture == null && !textureFailed)
            {
                // URP's Render Graph requires a depth buffer on camera output textures.
                texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    name = $"Sheet Studio {face} Pane",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                textureFailed = !texture.Create();
                if (textureFailed)
                {
                    Object.DestroyImmediate(texture);
                    texture = null;
                }
            }
            return texture != null;
        }

        // ----- Overlays -----

        void DrawOverlays(in Context ctx)
        {
            DrawPolyline(SheetOutlineColor, 2f,
                new Vector2(-SheetGeometry.HalfSize.x, -SheetGeometry.HalfSize.y),
                new Vector2(SheetGeometry.HalfSize.x, -SheetGeometry.HalfSize.y),
                new Vector2(SheetGeometry.HalfSize.x, SheetGeometry.HalfSize.y),
                new Vector2(-SheetGeometry.HalfSize.x, SheetGeometry.HalfSize.y));

            if (ctx.FaceRoot == null)
                return;

            if (ctx.ShowOverlays)
            {
                foreach (var collider in ctx.FaceRoot.GetComponentsInChildren<Collider2D>(true))
                {
                    if (!StudioPlacement.TryGetFaceLocalOutline(collider, ctx.FaceRoot, scratchOutline))
                        continue;
                    var element = StudioPlacement.ElementRootOf(collider.transform, ctx.FaceRoot);
                    DrawPolyline(ColorFor(element), 2f, scratchOutline.ToArray());

                    if (ctx.ShowLabels && element != null && collider.transform == element.transform)
                    {
                        var top = scratchOutline[0];
                        foreach (var p in scratchOutline)
                            if (p.y > top.y)
                                top = p;
                        var gui = view.SheetLocalToPane(top);
                        GUI.Label(new Rect(gui.x - 60f, gui.y - 18f, 120f, 16f), element.name, CentredMiniLabel);
                    }
                }
            }

            DrawLinks(ctx);

            var selected = SelectedElementIn(ctx);
            if (selected == null)
                return;
            foreach (var collider in selected.GetComponentsInChildren<Collider2D>(true))
            {
                if (StudioPlacement.TryGetFaceLocalOutline(collider, ctx.FaceRoot, scratchOutline))
                    DrawPolyline(SelectionColor, 3f, scratchOutline.ToArray());
            }
            if (StudioPlacement.IsResizable(selected))
            {
                var rect = StudioPlacement.FaceLocalRect(selected, ctx.FaceRoot);
                foreach (var handle in HandlePoints(rect))
                {
                    var gui = view.SheetLocalToPane(handle);
                    var r = new Rect(gui.x - HandleDrawPixels, gui.y - HandleDrawPixels, HandleDrawPixels * 2f, HandleDrawPixels * 2f);
                    EditorGUI.DrawRect(r, SelectionColor);
                }
            }
        }

        /// <summary>
        /// Plate wiring: a line from each plate to each effect target on this face, a marker + label when
        /// either end lives on the other face, and a strong highlight on link mode's armed source.
        /// </summary>
        void DrawLinks(in Context ctx)
        {
            var linkMode = ctx.Links is { Active: true };
            if (!ctx.ShowOverlays && !linkMode)
                return;
            if (ctx.Sheet == null || ctx.FaceRoot == null)
                return;

            for (int i = 0; i < ctx.FaceRoot.childCount; i++)
            {
                var element = ctx.FaceRoot.GetChild(i).gameObject;
                foreach (var slot in SlotsOf(element))
                {
                    if (slot.Target == null)
                        continue;
                    var from = (Vector2)element.transform.localPosition;
                    var targetRoot = StudioPlacement.ElementRootOf(slot.Target.transform, ctx.FaceRoot);
                    if (targetRoot != null)
                        DrawWire(from, targetRoot.transform.localPosition);
                    else
                        DrawLinkMarker(from, $"→ {slot.Target.name} (other face)");
                }
            }

            var otherRoot = ctx.FaceRoot == ctx.Sheet.Front ? ctx.Sheet.Back : ctx.Sheet.Front;
            if (otherRoot != null)
            {
                for (int i = 0; i < otherRoot.childCount; i++)
                {
                    var element = otherRoot.GetChild(i).gameObject;
                    foreach (var slot in SlotsOf(element))
                    {
                        if (slot.Target == null)
                            continue;
                        var targetRoot = StudioPlacement.ElementRootOf(slot.Target.transform, ctx.FaceRoot);
                        if (targetRoot != null)
                            DrawLinkMarker(targetRoot.transform.localPosition, $"← {element.name} (other face)");
                    }
                }
            }

            var source = linkMode ? ctx.Links.Source : null;
            if (source != null && source.transform.parent == ctx.FaceRoot)
            {
                foreach (var collider in source.GetComponentsInChildren<Collider2D>(true))
                {
                    if (StudioPlacement.TryGetFaceLocalOutline(collider, ctx.FaceRoot, scratchOutline))
                        DrawPolyline(LinkColor, 4f, scratchOutline.ToArray());
                }
            }
        }

        void DrawWire(Vector2 fromSheetLocal, Vector2 toSheetLocal)
        {
            var a = view.SheetLocalToPane(fromSheetLocal);
            var b = view.SheetLocalToPane(toSheetLocal);
            Handles.color = LinkColor;
            Handles.DrawAAPolyLine(2.5f, a, b);
            EditorGUI.DrawRect(new Rect(b.x - 3f, b.y - 3f, 6f, 6f), LinkColor);
        }

        void DrawLinkMarker(Vector2 atSheetLocal, string label)
        {
            var gui = view.SheetLocalToPane(atSheetLocal);
            EditorGUI.DrawRect(new Rect(gui.x - 3f, gui.y - 3f, 6f, 6f), LinkColor);
            GUI.Label(new Rect(gui.x + 6f, gui.y - 8f, 220f, 16f), label, LeftMiniLabel);
        }

        void DrawGhost(in Context ctx)
        {
            var armed = ctx.Palette?.Armed;
            if (armed == null || ctx.FaceRoot == null)
                return;
            var mouse = Event.current.mousePosition;
            if (!view.PaneRect.Contains(mouse))
                return;

            var at = StudioPlacement.Snap(view.PaneToSheetLocal(mouse), ctx.SnapIncrement);
            var prefabRoot = armed.transform;
            var any = false;
            foreach (var collider in armed.GetComponentsInChildren<Collider2D>(true))
            {
                if (!StudioPlacement.TryGetFaceLocalOutline(collider, prefabRoot, scratchOutline))
                    continue;
                for (int i = 0; i < scratchOutline.Count; i++)
                    scratchOutline[i] += at;
                DrawPolyline(GhostColor, 2f, scratchOutline.ToArray());
                any = true;
            }
            if (!any)
            {
                var gui = view.SheetLocalToPane(at);
                EditorGUI.DrawRect(new Rect(gui.x - 3f, gui.y - 3f, 6f, 6f), GhostColor);
            }
        }

        void DrawPolyline(Color color, float width, params Vector2[] sheetLocalPoints)
        {
            if (sheetLocalPoints.Length < 2)
                return;
            var points = new Vector3[sheetLocalPoints.Length + 1];
            for (int i = 0; i < sheetLocalPoints.Length; i++)
                points[i] = view.SheetLocalToPane(sheetLocalPoints[i]);
            points[^1] = points[0];
            Handles.color = color;
            Handles.DrawAAPolyLine(width, points);
        }

        static GUIStyle centredMiniLabel;
        static GUIStyle CentredMiniLabel => centredMiniLabel ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
        };

        static GUIStyle leftMiniLabel;
        static GUIStyle LeftMiniLabel => leftMiniLabel ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = Color.white },
        };

        Color ColorFor(GameObject element)
        {
            if (element == null)
                return OtherColor;
            if (element.TryGetComponent(out TerrainRegion region))
                return HasRequiredAbility(region) ? GatedColor : WallColor;
            if (element.GetComponent<PressurePlate>() != null)
                return PlateColor;
            if (element.GetComponent<PushableBlock>() != null)
                return BlockColor;
            return OtherColor;
        }

        bool HasRequiredAbility(TerrainRegion region)
        {
            // Serialized field read: a region with a required ability is gated (water-blue), a plain one a
            // wall. Cached per region — a SerializedObject per region per repaint is real garbage on a
            // 40-region sheet — and dropped by InvalidateOverlayCache when anything changes.
            if (gatedCache.TryGetValue(region, out var gated))
                return gated;
            var serialized = new SerializedObject(region);
            gated = serialized.FindProperty("requiredAbility").intValue != 0;
            gatedCache[region] = gated;
            return gated;
        }

        // ----- Input -----

        void HandleInput(Rect rect, in Context ctx)
        {
            var e = Event.current;
            if (!rect.Contains(e.mousePosition) && drag == DragKind.None)
                return;

            switch (e.type)
            {
                case EventType.ScrollWheel:
                {
                    var newZoom = Mathf.Clamp(view.Zoom * (1f - e.delta.y * ZoomStep), MinZoom, MaxZoom);
                    view = view.ZoomedAbout(newZoom, e.mousePosition);
                    e.Use();
                    break;
                }
                case EventType.MouseDown when e.button == 2:
                    drag = DragKind.Pan;
                    e.Use();
                    break;
                case EventType.MouseDown when e.button == 1:
                    if (ctx.Links is { Active: true })
                        OnLinkRightClick(e, ctx);
                    else if (ctx.Palette?.Armed != null)
                    {
                        ctx.Palette.Disarm();
                        e.Use();
                    }
                    break;
                case EventType.MouseDown when e.button == 0:
                    OnLeftMouseDown(e, ctx);
                    break;
                case EventType.MouseDrag:
                    OnMouseDrag(e, ctx);
                    break;
                case EventType.MouseUp:
                    if (drag != DragKind.None)
                    {
                        if (drag is DragKind.Move or DragKind.Resize)
                            Undo.CollapseUndoOperations(dragUndoGroup);
                        drag = DragKind.None;
                        e.Use();
                    }
                    break;
                case EventType.MouseMove:
                    if (ctx.Palette?.Armed != null)
                        EditorWindow.focusedWindow?.Repaint();
                    break;
            }
        }

        void OnLeftMouseDown(Event e, in Context ctx)
        {
            if (ctx.FaceRoot == null || ctx.Sheet == null)
                return;
            // Clicking a pane hands keyboard ownership to the Studio's element keys (the window skips them
            // while any control, e.g. an art-slot ObjectField, holds keyboard focus).
            GUIUtility.keyboardControl = 0;
            var sheetLocal = view.PaneToSheetLocal(e.mousePosition);

            if (ctx.Links is { Active: true })
            {
                OnLinkLeftClick(e, ctx, sheetLocal);
                return;
            }

            if (ctx.Palette?.Armed != null)
            {
                var placed = StudioPlacement.Place(ctx.Palette.Armed, ctx.FaceRoot, face, sheetLocal, ctx.SnapIncrement);
                if (placed != null)
                    Selection.activeGameObject = placed;
                e.Use();
                return;
            }

            var selected = SelectedElementIn(ctx);
            if (selected != null && StudioPlacement.IsResizable(selected))
            {
                var rect = StudioPlacement.FaceLocalRect(selected, ctx.FaceRoot);
                foreach (var handle in HandleIds())
                {
                    var gui = view.SheetLocalToPane(HandlePoint(rect, handle));
                    if (Vector2.Distance(gui, e.mousePosition) <= HandleHitPixels)
                    {
                        drag = DragKind.Resize;
                        resizeHandle = handle;
                        resizeStartRect = rect;
                        dragUndoGroup = Undo.GetCurrentGroup();
                        e.Use();
                        return;
                    }
                }
            }

            var picked = StudioPlacement.PickElement(ctx.FaceRoot, ctx.Sheet, sheetLocal);
            Selection.activeGameObject = picked;
            if (picked != null)
            {
                drag = DragKind.Move;
                moveGrabOffset = (Vector2)picked.transform.localPosition - sheetLocal;
                dragUndoGroup = Undo.GetCurrentGroup();
            }
            e.Use();
        }

        void OnMouseDrag(Event e, in Context ctx)
        {
            switch (drag)
            {
                case DragKind.Pan:
                    view = view.PannedBy(e.delta);
                    e.Use();
                    break;
                case DragKind.Move:
                {
                    var selected = SelectedElementIn(ctx);
                    if (selected != null)
                        StudioPlacement.Move(selected, view.PaneToSheetLocal(e.mousePosition) + moveGrabOffset, ctx.SnapIncrement);
                    e.Use();
                    break;
                }
                case DragKind.Resize:
                {
                    var selected = SelectedElementIn(ctx);
                    if (selected != null)
                    {
                        var at = view.PaneToSheetLocal(e.mousePosition);
                        var rect = resizeStartRect;
                        var min = rect.min;
                        var max = rect.max;
                        if (resizeHandle.x < 0) min.x = Mathf.Min(at.x, max.x);
                        if (resizeHandle.x > 0) max.x = Mathf.Max(at.x, min.x);
                        if (resizeHandle.y < 0) min.y = Mathf.Min(at.y, max.y);
                        if (resizeHandle.y > 0) max.y = Mathf.Max(at.y, min.y);
                        StudioPlacement.Resize(selected, Rect.MinMaxRect(min.x, min.y, max.x, max.y), ctx.SnapIncrement);
                    }
                    e.Use();
                    break;
                }
            }
        }

        // ----- Link mode -----

        /// <summary>
        /// Link mode click: a plate (anything with effect target slots) becomes the armed source; any other
        /// element wires the source's slot to it (a popup picks the slot when there are several). The source
        /// is shared window state, so the target may be clicked in the other pane (cross-face wiring).
        /// </summary>
        void OnLinkLeftClick(Event e, in Context ctx, Vector2 sheetLocal)
        {
            var links = ctx.Links;
            var picked = StudioPlacement.PickElement(ctx.FaceRoot, ctx.Sheet, sheetLocal);
            e.Use();
            if (picked == null)
                return;

            if (StudioLinks.GetSlots(picked).Count > 0)
            {
                links.Source = picked;
                Selection.activeGameObject = picked;
                return;
            }
            if (links.Source == null)
                return;

            var slots = StudioLinks.GetSlots(links.Source); // Fresh read — the pane cache may be mid-gesture stale.
            links.Source = null;
            if (slots.Count == 1)
            {
                StudioLinks.Wire(slots[0], picked);
                return;
            }
            var menu = new GenericMenu();
            foreach (var slot in slots)
            {
                var captured = slot;
                var current = slot.Target != null ? $" (currently {slot.Target.name})" : " (empty)";
                menu.AddItem(new GUIContent(slot.DisplayName + current), false,
                    () => StudioLinks.Wire(captured, picked));
            }
            menu.ShowAsContext();
        }

        /// <summary>Link mode right-click: a menu clearing a plate's wired slots; empty space disarms the source.</summary>
        void OnLinkRightClick(Event e, in Context ctx)
        {
            var links = ctx.Links;
            var picked = StudioPlacement.PickElement(ctx.FaceRoot, ctx.Sheet, view.PaneToSheetLocal(e.mousePosition));
            e.Use();
            if (picked == null)
            {
                links.Source = null;
                return;
            }

            var slots = StudioLinks.GetSlots(picked);
            var menu = new GenericMenu();
            var any = false;
            foreach (var slot in slots)
            {
                if (slot.Target == null)
                    continue;
                var captured = slot;
                menu.AddItem(new GUIContent($"Clear {slot.DisplayName} (currently {slot.Target.name})"), false,
                    () => StudioLinks.Wire(captured, null));
                any = true;
            }
            if (any)
                menu.ShowAsContext();
        }

        /// <summary>The selected element if it lives under this pane's face root; otherwise null.</summary>
        public GameObject SelectedElementIn(in Context ctx)
        {
            var active = Selection.activeGameObject;
            if (active == null || ctx.FaceRoot == null || ctx.Sheet == null)
                return null;
            var element = StudioPlacement.ElementRootOf(active.transform, ctx.FaceRoot);
            return element != null && StudioPlacement.CanEdit(element, ctx.Sheet) ? element : null;
        }

        static IEnumerable<Vector2Int> HandleIds()
        {
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    if (x != 0 || y != 0)
                        yield return new Vector2Int(x, y);
        }

        static Vector2 HandlePoint(Rect rect, Vector2Int handle)
            => rect.center + new Vector2(handle.x * rect.width * 0.5f, handle.y * rect.height * 0.5f);

        static IEnumerable<Vector2> HandlePoints(Rect rect)
        {
            foreach (var id in HandleIds())
                yield return HandlePoint(rect, id);
        }
    }
}
