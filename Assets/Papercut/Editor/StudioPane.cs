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
            /// <summary>Fill every terrain region's box in its kind colour: static terrain has no drawing of its own (the map art is it), so this is how it is seen while authoring.</summary>
            public bool ShowTerrain;
            public bool MirrorX;
            /// <summary>While held (Shift), the other face's content ghosts through the Sheet, mirrored as it lies beneath.</summary>
            public bool XRayHeld;
            /// <summary>The polygon terrain outline being drawn, shared by both face panes (window state).</summary>
            public StudioPolygonDraft Draft;
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
        /// <summary>Terrain-view fills: translucent so the map art under a box stays readable.</summary>
        static readonly Color WallFill = new(0.25f, 0.25f, 0.25f, 0.35f);
        static readonly Color GatedFill = new(0.25f, 0.45f, 0.9f, 0.35f);
        static readonly Color PlateColor = new(0.95f, 0.6f, 0.15f, 1f);
        static readonly Color BlockColor = new(0.6f, 0.4f, 0.2f, 1f);
        static readonly Color OtherColor = new(0.8f, 0.8f, 0.8f, 1f);
        /// <summary>A prop (art only, no collision of its own): its sprite rect, so it can be picked and moved.</summary>
        static readonly Color PropColor = new(0.55f, 0.75f, 0.4f, 1f);
        static readonly Color SelectionColor = new(1f, 0.9f, 0.2f, 1f);
        static readonly Color GhostColor = new(0.3f, 1f, 0.4f, 0.7f);
        static readonly Color LinkColor = new(0.85f, 0.4f, 1f, 1f);
        /// <summary>An outline that is not a simple polygon (self-crossing): Play Mode would refuse it.</summary>
        static readonly Color InvalidColor = new(1f, 0.3f, 0.25f, 1f);
        static readonly Color MidpointColor = new(1f, 0.9f, 0.2f, 0.6f);

        /// <summary>Toolbar hint while polygon terrain is armed.</summary>
        public const string DrawHint = "Draw: click to add points · click the first point or Enter to finish · Backspace removes the last point · Esc/right-click cancels";

        enum DragKind { None, Pan, Move, Resize, Vertex }

        readonly SheetFace face;

        PaneView view;
        bool framed;
        DragKind drag;
        Vector2 moveGrabOffset;
        Vector2Int resizeHandle; // Which edges the grabbed handle moves: components in {-1, 0, 1}.
        Rect resizeStartRect;
        int dragVertex;
        int dragUndoGroup;
        int controlId; // This pane's IMGUI control, allocated per draw; held as hotControl for the length of an editing drag.

        /// <summary>One-line status for the window's toolbar (draft progress, refusals). Newest wins.</summary>
        public System.Action<string> Report = _ => { };

        readonly List<ConvexPolygon> scratchPieces = new();
        readonly List<Vector2> scratchTargets = new();

        const float XRayAlpha = 0.45f;

        GameObject cameraObject;
        Camera paneCamera;
        RenderTexture texture;
        bool textureFailed;
        GameObject xrayCameraObject;
        Camera xrayCamera;
        RenderTexture xrayTexture;
        bool xrayFailed;

        readonly List<Vector2> scratchOutline = new();
        /// <summary>Elements labelled this repaint, so an element with several colliders gets one label.</summary>
        readonly HashSet<GameObject> labelled = new();
        readonly List<GameObject> scratchProps = new();
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

        /// <summary>
        /// Ends an editing drag without reverting it — for the moments a MouseUp may never come (the window losing
        /// focus, the stage closing, disposal). Face-pane drags commit live, so nothing is lost; the undo group is
        /// collapsed as a release would, and the held hotControl is let go so Auto Save can run again (an interrupted
        /// vertex drag keeps its last position even if self-crossing; the pane draws that red).
        /// </summary>
        public void EndDrag()
        {
            if (drag == DragKind.None)
                return;
            if (drag is DragKind.Move or DragKind.Resize or DragKind.Vertex)
                Undo.CollapseUndoOperations(dragUndoGroup);
            drag = DragKind.None;
            ReleaseHotControl();
        }

        public void Dispose()
        {
            EndDrag();
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
            if (xrayCameraObject != null)
                Object.DestroyImmediate(xrayCameraObject);
            xrayCameraObject = null;
            xrayCamera = null;
            if (xrayTexture != null)
            {
                xrayTexture.Release();
                Object.DestroyImmediate(xrayTexture);
            }
            xrayTexture = null;
            xrayFailed = false;
        }

        public void Draw(Rect rect, in Context ctx)
        {
            if (!framed || view.PaneRect.size != rect.size)
                view = framed ? view.WithRect(rect) : PaneView.FitSheet(rect, ctx.MirrorX);
            else
                view = view.WithRect(rect);
            view = view.WithMirror(ctx.MirrorX);
            framed = true;

            controlId = GUIUtility.GetControlID(FocusType.Passive, rect);
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

            // X-ray: over the composite, under the outlines. What lies beneath Sheet point (x, y) is the
            // other face's point (−x, y) (SheetGeometry.BackToFront), so the overlay camera sits at the
            // mirrored centre and the blit flips x relative to this pane's own coords.
            if (ctx.XRayHeld && RenderXRay(rect, ctx))
            {
                var previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, XRayAlpha);
                var xrayCoords = new Rect(ctx.MirrorX ? 0f : 1f, 0f, ctx.MirrorX ? 1f : -1f, 1f);
                GUI.DrawTextureWithTexCoords(rect, xrayTexture, xrayCoords);
                GUI.color = previous;
            }
            else if (ctx.XRayHeld && xrayFailed)
            {
                GUI.Label(new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, 16f),
                    "X-ray could not render.", EditorStyles.miniLabel);
            }

            DrawOverlays(ctx);
            DrawGhost(ctx);
            DrawDraft(ctx);
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

        /// <summary>Renders the other face for the x-ray overlay: mirrored camera centre, clear background.</summary>
        bool RenderXRay(Rect rect, in Context ctx)
        {
            if (xrayFailed || ctx.Stage == null || !FoldLayers.Valid)
                return false;

            var width = Mathf.Max(1, (int)rect.width);
            var height = Mathf.Max(1, (int)rect.height);
            if (xrayTexture != null && (xrayTexture.width != width || xrayTexture.height != height))
            {
                xrayTexture.Release();
                Object.DestroyImmediate(xrayTexture);
                xrayTexture = null;
            }
            if (xrayTexture == null)
            {
                xrayTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    name = $"Sheet Studio {face} X-Ray",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                if (!xrayTexture.Create())
                {
                    Object.DestroyImmediate(xrayTexture);
                    xrayTexture = null;
                    xrayFailed = true;
                    return false;
                }
            }
            if (xrayCamera == null)
            {
                xrayCameraObject = new GameObject($"Sheet Studio {face} X-Ray Camera") { hideFlags = HideFlags.HideAndDontSave };
                xrayCamera = xrayCameraObject.AddComponent<Camera>();
                xrayCamera.enabled = false;
                xrayCamera.orthographic = true;
                xrayCamera.nearClipPlane = 0.1f;
                xrayCamera.farClipPlane = CameraDistance * 2f;
                xrayCamera.clearFlags = CameraClearFlags.SolidColor;
                xrayCamera.backgroundColor = Color.clear; // Opaque would wash the whole pane with tint.
            }

            var otherFace = face == SheetFace.Front ? SheetFace.Back : SheetFace.Front;
            xrayCamera.transform.position = new Vector3(-view.Centre.x, view.Centre.y, -CameraDistance);
            xrayCamera.orthographicSize = rect.height / (2f * view.Zoom);
            xrayCamera.aspect = rect.width / rect.height;
            xrayCamera.cullingMask = 1 << FoldLayers.LayerOf(otherFace);
            xrayCamera.scene = ctx.Stage.scene;

            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = xrayTexture };
            if (!RenderPipeline.SupportsRenderRequest(xrayCamera, request))
            {
                xrayFailed = true;
                return false;
            }
            RenderPipeline.SubmitRenderRequest(xrayCamera, request);
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

            if (ctx.ShowTerrain)
            {
                foreach (var region in ctx.FaceRoot.GetComponentsInChildren<TerrainRegion>(true))
                {
                    // A box is one piece; a polygon region fills piece by piece (the fill primitive is convex-only).
                    if (!StudioPlacement.TryGetRegionPieces(region, ctx.FaceRoot, scratchPieces))
                    {
                        // A polygon region whose outline cannot be decomposed is shown red rather than vanishing (code review S4).
                        if (StudioPlacement.IsPolygonTerrain(region.gameObject) && StudioPlacement.GetOutline(region.gameObject, ctx.FaceRoot, scratchOutline))
                            DrawPolyline(InvalidColor, 2f, scratchOutline.ToArray());
                        continue;
                    }
                    var fill = HasRequiredAbility(region) ? GatedFill : WallFill;
                    foreach (var piece in scratchPieces)
                        DrawFill(fill, Points(piece));
                }
            }

            if (ctx.ShowOverlays)
            {
                labelled.Clear();
                foreach (var collider in ctx.FaceRoot.GetComponentsInChildren<Collider2D>(true))
                {
                    if (!StudioPlacement.TryGetFaceLocalOutline(collider, ctx.FaceRoot, scratchOutline))
                        continue;
                    var element = StudioPlacement.ElementRootOf(collider.transform, ctx.FaceRoot);
                    // A polygon region that would refuse itself at Play is red whether or not it is selected (code review S1).
                    var invalid = element != null && StudioPlacement.IsPolygonTerrain(element) && !StudioPlacement.IsValidOutline(element, ctx.FaceRoot, out _);
                    DrawPolyline(invalid ? InvalidColor : ColorFor(element), 2f, scratchOutline.ToArray());

                    // One label per element, above its whole footprint (an element's colliders may sit on children).
                    if (ctx.ShowLabels && element != null && labelled.Add(element))
                        DrawLabel(element, ctx);
                }

                // Props have no collider: their sprite rect is the outline (Aaron, 2026-09-10).
                StudioPlacement.CollectProps(ctx.FaceRoot, ctx.Sheet, scratchProps);
                foreach (var prop in scratchProps)
                {
                    if (!StudioPlacement.TryGetPropOutline(prop, ctx.FaceRoot, scratchOutline))
                        continue;
                    DrawPolyline(PropColor, 2f, scratchOutline.ToArray());
                    if (ctx.ShowLabels && labelled.Add(prop))
                        DrawLabel(prop, ctx);
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
            if (StudioPlacement.TryGetPropOutline(selected, ctx.FaceRoot, scratchOutline))
                DrawPolyline(SelectionColor, 3f, scratchOutline.ToArray());
            if (StudioPlacement.IsPolygonTerrain(selected))
            {
                DrawVertexHandles(selected, ctx);
                return;
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
            if (StudioPlacement.IsPolygonTerrain(armed))
            {
                // Drawn point by point: the next vertex, with the magnet applied, is the ghost.
                var next = view.SheetLocalToPane(SnapWithMagnet(view.PaneToSheetLocal(mouse), ctx, null));
                EditorGUI.DrawRect(new Rect(next.x - 3f, next.y - 3f, 6f, 6f), GhostColor);
                return;
            }
            // As Place will land it: relative to the prefab root's position, the root's own scale kept.
            var any = false;
            foreach (var collider in armed.GetComponentsInChildren<Collider2D>(true))
            {
                if (!StudioPlacement.TryGetPlacedOutline(collider, armed, scratchOutline))
                    continue;
                DrawGhostOutline(at);
                any = true;
            }
            if (StudioPlacement.TryGetPlacedPropOutline(armed, scratchOutline))
            {
                DrawGhostOutline(at);
                any = true;
            }
            if (!any)
            {
                var gui = view.SheetLocalToPane(at);
                EditorGUI.DrawRect(new Rect(gui.x - 3f, gui.y - 3f, 6f, 6f), GhostColor);
            }
        }

        /// <summary>Draws <see cref="scratchOutline"/>, offset to the cursor, in the ghost colour.</summary>
        void DrawGhostOutline(Vector2 at)
        {
            for (int i = 0; i < scratchOutline.Count; i++)
                scratchOutline[i] += at;
            DrawPolyline(GhostColor, 2f, scratchOutline.ToArray());
        }

        /// <summary>
        /// The outline being drawn on this face: its vertices, the open polyline, a thin closing edge (red once
        /// three or more points no longer make a simple polygon), the first vertex enlarged as the close target,
        /// and a rubber band from the last vertex to the cursor.
        /// </summary>
        void DrawDraft(in Context ctx)
        {
            var draft = ctx.Draft;
            if (draft == null || !draft.IsActive || draft.Face != face)
                return;

            var points = draft.Points;
            var valid = points.Count < 3 || draft.CanFinish(out _);
            var color = valid ? GhostColor : InvalidColor;
            var gui = new Vector3[points.Count];
            for (int i = 0; i < points.Count; i++)
                gui[i] = view.SheetLocalToPane(points[i]);
            Handles.color = color;
            if (points.Count >= 2)
                Handles.DrawAAPolyLine(2f, gui);
            if (points.Count >= 3)
                Handles.DrawAAPolyLine(1f, gui[gui.Length - 1], gui[0]);
            for (int i = 0; i < gui.Length; i++)
            {
                var size = i == 0 ? HandleDrawPixels + 2f : HandleDrawPixels;
                EditorGUI.DrawRect(new Rect(gui[i].x - size, gui[i].y - size, size * 2f, size * 2f), color);
            }

            var mouse = Event.current.mousePosition;
            if (ctx.Palette?.Armed == draft.Prefab && view.PaneRect.Contains(mouse))
            {
                var next = view.SheetLocalToPane(SnapWithMagnet(view.PaneToSheetLocal(mouse), ctx, null));
                Handles.color = new Color(color.r, color.g, color.b, 0.5f);
                Handles.DrawAAPolyLine(1.5f, gui[gui.Length - 1], new Vector3(next.x, next.y, 0f));
            }
        }

        /// <summary>Vertex handles (squares) and edge-midpoint handles (smaller) of the selected polygon region; the outline in red while it would refuse itself.</summary>
        void DrawVertexHandles(GameObject selected, in Context ctx)
        {
            if (!StudioPlacement.GetOutline(selected, ctx.FaceRoot, scratchOutline))
                return;
            if (!StudioPlacement.IsValidOutline(selected, ctx.FaceRoot, out _))
                DrawPolyline(InvalidColor, 3f, scratchOutline.ToArray());
            for (int i = 0; i < scratchOutline.Count; i++)
            {
                var mid = view.SheetLocalToPane((scratchOutline[i] + scratchOutline[(i + 1) % scratchOutline.Count]) * 0.5f);
                EditorGUI.DrawRect(new Rect(mid.x - MidpointHandlePixels, mid.y - MidpointHandlePixels, MidpointHandlePixels * 2f, MidpointHandlePixels * 2f), MidpointColor);
            }
            foreach (var vertex in scratchOutline)
            {
                var gui = view.SheetLocalToPane(vertex);
                EditorGUI.DrawRect(new Rect(gui.x - HandleDrawPixels, gui.y - HandleDrawPixels, HandleDrawPixels * 2f, HandleDrawPixels * 2f), SelectionColor);
            }
        }

        const float MidpointHandlePixels = 3f;

        static Vector2[] Points(ConvexPolygon polygon)
        {
            var points = new Vector2[polygon.Count];
            for (int i = 0; i < points.Length; i++)
                points[i] = polygon.Vertices[i];
            return points;
        }

        /// <summary>Grid snap, then the magnet to other elements' vertices and the sheet edge within the handle radius.</summary>
        Vector2 SnapWithMagnet(Vector2 sheetLocal, in Context ctx, GameObject exclude)
        {
            var snapped = StudioPlacement.Snap(sheetLocal, ctx.SnapIncrement);
            StudioPlacement.MagnetTargets(ctx.FaceRoot, exclude, scratchTargets);
            return StudioPlacement.Magnet(snapped, scratchTargets, HandleHitPixels / view.Zoom);
        }

        void DrawFill(Color color, params Vector2[] sheetLocalPoints)
        {
            if (sheetLocalPoints.Length < 3)
                return;
            var points = new Vector3[sheetLocalPoints.Length];
            for (int i = 0; i < sheetLocalPoints.Length; i++)
                points[i] = view.SheetLocalToPane(sheetLocalPoints[i]);
            Handles.color = color;
            Handles.DrawAAConvexPolygon(points);
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

        void DrawLabel(GameObject element, in Context ctx)
        {
            var footprint = StudioPlacement.AuthoredFootprint(element, ctx.FaceRoot);
            var gui = view.SheetLocalToPane(new Vector2(footprint.center.x, footprint.yMax));
            GUI.Label(new Rect(gui.x - 60f, gui.y - 18f, 120f, 16f), element.name, CentredMiniLabel);
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
            var region = element.GetComponentInChildren<TerrainRegion>(true); // An element's regions may be children of its root.
            if (region != null)
                return HasRequiredAbility(region) ? GatedColor : WallColor;
            if (element.GetComponent<PressurePlate>() != null)
                return PlateColor;
            if (element.GetComponent<PushableBlock>() != null)
                return BlockColor;
            if (StudioPlacement.IsProp(element))
                return PropColor;
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
                    OnRightMouseDown(e, ctx);
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
                        if (drag == DragKind.Vertex)
                            EndVertexDrag(ctx);
                        if (drag is DragKind.Move or DragKind.Resize or DragKind.Vertex)
                            Undo.CollapseUndoOperations(dragUndoGroup);
                        drag = DragKind.None;
                        ReleaseHotControl();
                        e.Use();
                    }
                    break;
                // Armed-hover repaint lives in SheetStudioWindow (one home for all three panes).
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
                if (StudioPlacement.IsPolygonTerrain(ctx.Palette.Armed))
                {
                    OnDrawClick(e, ctx, sheetLocal);
                    return;
                }
                var placed = StudioPlacement.Place(ctx.Palette.Armed, ctx.FaceRoot, face, sheetLocal, ctx.SnapIncrement);
                if (placed != null)
                    Selection.activeGameObject = placed;
                e.Use();
                return;
            }

            var selected = SelectedElementIn(ctx);
            if (selected != null && StudioPlacement.IsPolygonTerrain(selected)
                && StudioPlacement.GetOutline(selected, ctx.FaceRoot, scratchOutline))
            {
                var vertex = HitVertex(scratchOutline, e.mousePosition);
                if (vertex >= 0)
                {
                    BeginVertexDrag(vertex);
                    e.Use();
                    return;
                }
                var edge = HitMidpoint(scratchOutline, e.mousePosition);
                if (edge >= 0)
                {
                    // Insert on the edge and keep dragging the new vertex; a revert undoes the insert too.
                    BeginEditDrag(DragKind.Vertex);
                    var mid = (scratchOutline[edge] + scratchOutline[(edge + 1) % scratchOutline.Count]) * 0.5f;
                    StudioPlacement.InsertVertex(selected, ctx.FaceRoot, edge, mid, 0f);
                    dragVertex = edge + 1;
                    e.Use();
                    return;
                }
            }
            if (selected != null && StudioPlacement.IsResizable(selected))
            {
                var rect = StudioPlacement.FaceLocalRect(selected, ctx.FaceRoot);
                foreach (var handle in HandleIds())
                {
                    var gui = view.SheetLocalToPane(HandlePoint(rect, handle));
                    if (Vector2.Distance(gui, e.mousePosition) <= HandleHitPixels)
                    {
                        BeginEditDrag(DragKind.Resize);
                        resizeHandle = handle;
                        resizeStartRect = rect;
                        e.Use();
                        return;
                    }
                }
            }

            var picked = StudioPlacement.PickElement(ctx.FaceRoot, ctx.Sheet, sheetLocal);
            Selection.activeGameObject = picked;
            if (picked != null)
            {
                BeginEditDrag(DragKind.Move);
                moveGrabOffset = (Vector2)picked.transform.localPosition - sheetLocal;
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
                case DragKind.Vertex:
                {
                    var selected = SelectedElementIn(ctx);
                    if (selected != null)
                        StudioPlacement.SetVertex(selected, ctx.FaceRoot, dragVertex, SnapWithMagnet(view.PaneToSheetLocal(e.mousePosition), ctx, selected), 0f);
                    e.Use();
                    break;
                }
            }
        }

        // ----- Polygon terrain: drawing and vertex editing -----

        /// <summary>
        /// A click while polygon terrain is armed: starts or extends the draft on this face, or — on the first
        /// vertex once three or more points exist — finishes it. A draft on the other face is never discarded
        /// by a click here; it has to be finished or cancelled first.
        /// </summary>
        void OnDrawClick(Event e, in Context ctx, Vector2 sheetLocal)
        {
            e.Use();
            var draft = ctx.Draft;
            var armed = ctx.Palette?.Armed;
            if (draft == null || armed == null)
                return;
            if (draft.IsActive && draft.Face != face)
            {
                Report($"Finish or cancel the polygon on the {draft.Face} face first.");
                return;
            }
            if (!draft.IsActive)
                draft.Start(armed, face);
            else if (draft.Prefab != armed)
                draft.Retarget(armed); // The other polygon kind mid-draw keeps the outline (code review S3).

            if (draft.Points.Count >= 3 && Vector2.Distance(view.SheetLocalToPane(draft.Points[0]), e.mousePosition) <= HandleHitPixels)
            {
                FinishDraft(ctx);
                return;
            }
            // A pixel near-miss on an existing vertex is a close attempt, not a sliver vertex (code review S2).
            foreach (var existing in draft.Points)
            {
                if (Vector2.Distance(view.SheetLocalToPane(existing), e.mousePosition) <= HandleHitPixels)
                {
                    Report(draft.Points.Count < 3 ? "A region needs at least three points before it can close." : "That point is already a vertex.");
                    return;
                }
            }

            var point = SnapWithMagnet(sheetLocal, ctx, null);
            if (!draft.TryAdd(point, out var reason))
            {
                Report(reason);
                return;
            }
            Report(draft.Points.Count < 3
                ? $"{draft.Points.Count} point(s) — keep clicking."
                : draft.CanFinish(out var why) ? $"{draft.Points.Count} points — click the first point or press Enter to finish." : $"{draft.Points.Count} points — {why}.");
        }

        /// <summary>Places the drafted region (selected afterwards) and stays armed for the next one.</summary>
        public void FinishDraft(in Context ctx)
        {
            var draft = ctx.Draft;
            if (draft == null || !draft.IsActive)
                return;
            if (draft.TryFinish(ctx.Sheet, out var placed, out var reason))
            {
                Selection.activeGameObject = placed;
                Report($"Placed {placed.name}. Still armed — draw the next one, or Esc to stop.");
            }
            else
                Report($"Cannot finish: {reason}.");
        }

        void OnRightMouseDown(Event e, in Context ctx)
        {
            var draft = ctx.Draft;
            if (draft != null && draft.IsActive)
            {
                draft.Clear();
                Report("Polygon cancelled.");
                e.Use();
                return;
            }
            if (ctx.Links is { Active: true })
            {
                OnLinkRightClick(e, ctx);
                return;
            }
            if (ctx.Palette?.Armed != null)
            {
                ctx.Palette.Disarm();
                e.Use();
                return;
            }

            // Right-click on a vertex handle of the selected polygon region removes that vertex.
            var selected = SelectedElementIn(ctx);
            if (selected == null || !StudioPlacement.IsPolygonTerrain(selected)
                || !StudioPlacement.GetOutline(selected, ctx.FaceRoot, scratchOutline))
                return;
            var vertex = HitVertex(scratchOutline, e.mousePosition);
            if (vertex < 0)
                return;
            Report(StudioPlacement.TryRemoveVertex(selected, ctx.FaceRoot, vertex, out var reason) ? string.Empty : $"Cannot remove that vertex: {reason}.");
            e.Use();
        }

        void BeginVertexDrag(int vertex)
        {
            BeginEditDrag(DragKind.Vertex);
            dragVertex = vertex;
        }

        /// <summary>
        /// Starts an editing drag: one undo group for the gesture, and this pane's control held as
        /// <see cref="GUIUtility.hotControl"/> until release. Prefab Mode's Auto Save waits while a control is hot,
        /// so the drag is written once at release — an auto-save mid-gesture would clear the stage's dirtiness and
        /// the rest of the gesture, merged into the same undo record, would never dirty it again (see
        /// <see cref="StudioEdits"/>).
        /// </summary>
        void BeginEditDrag(DragKind kind)
        {
            drag = kind;
            dragUndoGroup = Undo.GetCurrentGroup();
            GUIUtility.hotControl = controlId;
        }

        void ReleaseHotControl()
        {
            if (GUIUtility.hotControl == controlId)
                GUIUtility.hotControl = 0;
        }

        /// <summary>
        /// Revert-on-release (Aaron, 2026-09-10): a drag that ends self-crossing is undone as a whole — every
        /// vertex write (and an insert) since the press is reverted, so no no-op undo entry is left behind.
        /// </summary>
        void EndVertexDrag(in Context ctx)
        {
            var selected = SelectedElementIn(ctx);
            if (selected == null || StudioPlacement.IsValidOutline(selected, ctx.FaceRoot, out var reason))
                return;
            Undo.RevertAllDownToGroup(dragUndoGroup);
            Report($"Reverted: {reason}.");
        }

        int HitVertex(List<Vector2> outline, Vector2 mouse)
        {
            for (int i = 0; i < outline.Count; i++)
            {
                if (Vector2.Distance(view.SheetLocalToPane(outline[i]), mouse) <= HandleHitPixels)
                    return i;
            }
            return -1;
        }

        int HitMidpoint(List<Vector2> outline, Vector2 mouse)
        {
            for (int i = 0; i < outline.Count; i++)
            {
                var mid = (outline[i] + outline[(i + 1) % outline.Count]) * 0.5f;
                if (Vector2.Distance(view.SheetLocalToPane(mid), mouse) <= HandleHitPixels)
                    return i;
            }
            return -1;
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
