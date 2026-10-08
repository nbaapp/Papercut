using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// Authored values the fold preview mirrors from the game, read fresh from the stage's components (and the
    /// open Desk scene's FoldDragInput, when one is loaded) so the preview tracks Aaron's tuning. Field names
    /// are public consts so a test can assert each one still resolves on the real component — a rename must
    /// fail a test, not silently degrade to defaults.
    /// </summary>
    public readonly struct StudioFoldSettings
    {
        public const string MinDepthField = "minDepth";
        public const string PreviewTintField = "previewTint";
        public const string InvalidTintField = "invalidTint";
        public const string SeamWidthField = "seamWidth";
        public const string SeamColorField = "seamColor";
        public const string PixelsPerUnitField = "pixelsPerUnit";
        public const string FaceMaterialField = "faceMaterial";
        public const string CornerGrabRadiusField = "cornerGrabRadius";
        public const string EdgeGrabMarginField = "edgeGrabMargin";
        public const string UnfoldGrabDistanceField = "unfoldGrabDistance";
        public const string CreaseGrabDistanceField = "creaseGrabDistance";
        public const string DepthSnapField = "depthSnap";
        public const string RememberCreasesField = "rememberCreases";
        public const string AllowStackingField = "allowStacking";

        public readonly float MinDepth;
        public readonly Color PreviewTint;
        public readonly Color InvalidTint;
        public readonly float SeamWidth;
        public readonly Color SeamColor;
        public readonly int PixelsPerUnit;
        public readonly Material FaceMaterial;
        public readonly float CornerGrabRadius;
        public readonly float EdgeGrabMargin;
        public readonly float UnfoldGrabDistance;
        public readonly float CreaseGrabDistance;
        public readonly float DepthSnap;
        public readonly bool RememberCreases;

        /// <summary>The sheet's stacking toggle: off, the preview holds a drag short of the other folds' Flaps as the game does.</summary>
        public readonly bool AllowStacking;

        /// <summary>Non-null when a sheet component was missing and defaults filled in — shown, never silent.</summary>
        public readonly string MissingNote;

        /// <summary>Editor constant: what shows where folds emptied the sheet. Not the game's Desk surface.</summary>
        public Color PaneBackground => new(0.22f, 0.2f, 0.18f, 1f);

        StudioFoldSettings(float minDepth, Color previewTint, Color invalidTint,
            float seamWidth, Color seamColor, int pixelsPerUnit, Material faceMaterial,
            float cornerGrabRadius, float edgeGrabMargin, float unfoldGrabDistance, float creaseGrabDistance, float depthSnap,
            bool rememberCreases, bool allowStacking, string missingNote)
        {
            RememberCreases = rememberCreases;
            AllowStacking = allowStacking;
            MissingNote = missingNote;
            MinDepth = minDepth;
            PreviewTint = previewTint;
            InvalidTint = invalidTint;
            SeamWidth = seamWidth;
            SeamColor = seamColor;
            PixelsPerUnit = pixelsPerUnit;
            FaceMaterial = faceMaterial;
            CornerGrabRadius = cornerGrabRadius;
            EdgeGrabMargin = edgeGrabMargin;
            UnfoldGrabDistance = unfoldGrabDistance;
            CreaseGrabDistance = creaseGrabDistance;
            DepthSnap = depthSnap;
        }

        /// <summary>Reads the sheet's authored values; runtime defaults fill in whatever is missing.</summary>
        public static StudioFoldSettings Read(Sheet sheet)
        {
            // Defaults mirror the runtime components' field initializers.
            var minDepth = 0.25f;
            var previewTint = new Color(1f, 1f, 1f, 0.85f);
            var invalidTint = new Color(1f, 0.35f, 0.35f, 0.85f);
            var seamWidth = 0.08f;
            var seamColor = new Color(0.15f, 0.12f, 0.1f, 0.9f);
            var pixelsPerUnit = 128;
            Material faceMaterial = null;
            // These defaults duplicate the runtime components' field initializers (SheetFolds,
            // RenderTextureFoldRenderer, FoldDragInput) because the editor cannot instantiate them; if a
            // runtime default changes, change it here too. Renames are caught by the FindProperty tests.
            var cornerGrab = 0.6f;
            var edgeGrab = 0.35f;
            var unfoldGrab = 0.3f;
            var creaseGrab = 0.3f;
            var depthSnap = 0f;
            var rememberCreases = true;
            var allowStacking = true;
            string missingNote = null;

            if (sheet != null && sheet.TryGetComponent(out SheetFolds folds))
            {
                var so = new SerializedObject(folds);
                minDepth = so.FindProperty(MinDepthField)?.floatValue ?? minDepth;
                rememberCreases = so.FindProperty(RememberCreasesField)?.boolValue ?? rememberCreases;
                allowStacking = so.FindProperty(AllowStackingField)?.boolValue ?? allowStacking;
            }
            else
            {
                missingNote = "Sheet has no SheetFolds — preview uses default fold rules.";
            }
            if (sheet != null && sheet.TryGetComponent(out RenderTextureFoldRenderer renderer))
            {
                var so = new SerializedObject(renderer);
                previewTint = so.FindProperty(PreviewTintField)?.colorValue ?? previewTint;
                invalidTint = so.FindProperty(InvalidTintField)?.colorValue ?? invalidTint;
                seamWidth = so.FindProperty(SeamWidthField)?.floatValue ?? seamWidth;
                seamColor = so.FindProperty(SeamColorField)?.colorValue ?? seamColor;
                pixelsPerUnit = so.FindProperty(PixelsPerUnitField)?.intValue ?? pixelsPerUnit;
                faceMaterial = so.FindProperty(FaceMaterialField)?.objectReferenceValue as Material;
            }
            else
            {
                missingNote = (missingNote == null ? "" : missingNote + " ")
                    + "Sheet has no RenderTextureFoldRenderer — preview uses default tints and materials.";
            }

            // The drag feel lives on the Desk scene's FoldDragInput; track Aaron's tuning when it's loaded.
            var dragInput = Object.FindAnyObjectByType<FoldDragInput>();
            if (dragInput != null)
            {
                var so = new SerializedObject(dragInput);
                cornerGrab = so.FindProperty(CornerGrabRadiusField)?.floatValue ?? cornerGrab;
                edgeGrab = so.FindProperty(EdgeGrabMarginField)?.floatValue ?? edgeGrab;
                unfoldGrab = so.FindProperty(UnfoldGrabDistanceField)?.floatValue ?? unfoldGrab;
                creaseGrab = so.FindProperty(CreaseGrabDistanceField)?.floatValue ?? creaseGrab;
                depthSnap = so.FindProperty(DepthSnapField)?.floatValue ?? depthSnap;
            }

            return new StudioFoldSettings(minDepth, previewTint, invalidTint,
                seamWidth, seamColor, pixelsPerUnit, faceMaterial,
                cornerGrab, edgeGrab, unfoldGrab, creaseGrab, depthSnap, rememberCreases, allowStacking, missingNote);
        }

        /// <summary>
        /// The test-player footprint size, from Player.prefab's BoxCollider2D **serialized** size (never
        /// <c>Collider2D.bounds</c>, which is physics-populated and degenerate on an uninstantiated asset —
        /// a zero-size ghost would silently disable the covers-player rule).
        /// </summary>
        public static Vector2 ReadPlayerFootprintSize()
            => StudioPlaytest.ReadPlayerFootprint(StudioSheetOps.FindOpenDesk()).Size; // One reader: the open Desk's Player first, then Player.prefab.
    }

    /// <summary>
    /// The fold-preview pane: shows the sheet folded exactly as the game's composite would, with the game's
    /// drag gesture — Seam click unfolds, crease grab folds further, edge/corner grab starts a new fold; live
    /// preview, red refuse, release commits, right-click/Escape cancels; the drag holds short of the sheet's
    /// paperweights as in the game (the model's MaxDepth) — plus the draggable test-player ghost.
    /// </summary>
    public sealed class StudioFoldPane
    {
        public struct Context
        {
            public PrefabStage Stage;
            public StudioFoldModel Model;
            public StudioFoldScene Scene;
            public StudioFoldSettings Settings;
            public Sheet Sheet;
            /// <summary>The sheet's Above root (universal regions, Aaron 2026-09-28): drawn over the composite clipped to the sheet's footprint, picked and dragged in sheet space.</summary>
            public Transform AboveRoot;
            /// <summary>Fill the universal regions as the face panes' Terrain view does.</summary>
            public bool ShowTerrain;
            public StudioPalette Palette;
            public float SnapIncrement;
            /// <summary>Link mode is face-pane-only (Aaron, 2026-08-31): the element branch goes inert.</summary>
            public bool LinksActive;
        }

        /// <summary>What a press did — the priority chain (Aaron: fold gestures before elements) made testable.</summary>
        internal enum PressOutcome
        {
            Nothing,
            Placed,
            PlaceRefusedNoSheet,
            /// <summary>Polygon terrain is drawn in a face pane, never dropped as its prefab's default shape.</summary>
            PlaceRefusedPolygonTerrain,
            /// <summary>A universal region cannot be placed: the sheet has no Above root.</summary>
            PlaceRefusedNoAboveRoot,
            GhostDragStarted,
            Unfolded,
            UnfoldRefused,
            FoldDragStarted,
            ElementDragStarted,
            ElementInertLinkMode,
        }

        const float MinZoom = 4f;
        const float MaxZoom = 400f;
        const float ZoomStep = 0.05f;
        static readonly Color GhostFill = new(0.3f, 0.6f, 1f, 0.25f);
        static readonly Color SelectionColorFold = new(1f, 0.9f, 0.2f, 1f);
        static readonly Color PlaceGhostColor = new(0.3f, 1f, 0.4f, 0.7f);
        static readonly Color GhostOutline = new(0.3f, 0.6f, 1f, 1f);
        static readonly Color SheetOutlineColor = new(1f, 1f, 1f, 0.5f);

        enum DragKind { None, Fold, Ghost, Element }

        PaneView view;
        bool framed;
        DragKind drag;
        FoldAnchor dragAnchor;
        float dragGrabDepth;
        Fold dragFold;
        Vector2 ghostGrabOffset;
        GameObject dragElement;
        Vector2 elementGrabOffsetDesk; // Desk-space (plan round-1 N4): authored-space offsets mirror across flaps.
        int elementUndoGroup;
        /// <summary>The dragged element sits above the sheet: it moves in sheet space, never mapped through a fold.</summary>
        bool dragAbove;
        int controlId; // This pane's IMGUI control, allocated per draw; held as hotControl for an element drag (see StudioPane.BeginEditDrag).
        readonly List<StudioFoldMapping.DeskPiece> scratchPieces = new();
        readonly List<ConvexPolygon> scratchAuthored = new();

        /// <summary>Shown when polygon terrain is armed over the fold pane: it is drawn point by point in a face pane.</summary>
        public const string PolygonTerrainHint = "Draw polygon terrain in a face pane.";

        /// <summary>
        /// Where the pane reports what it has to say (refusals, relocations). The window points this at its
        /// single message slot, shared with the fold list, so the newest message always wins (no stale
        /// refusal can shadow a later one). A new press clears it.
        /// </summary>
        public System.Action<string> Report = _ => { };

        public bool IsDragging => drag != DragKind.None;

        /// <summary>Test seam: the desk-space grab offset of the active element drag (M1 regression coverage).</summary>
        internal Vector2 ElementGrabOffsetDesk => elementGrabOffsetDesk;

        public PaneView View => view;

        public void FrameSheet() => framed = false;

        /// <summary>
        /// Cancels an active drag (Escape/right-click/middle-press; the window routes Escape here first).
        /// A fold drag drops its preview; an element drag reverts the whole gesture (plan round-2 N1 —
        /// Escape means "as if I never dragged" everywhere in this pane); a ghost drag just ends (moves
        /// commit live).
        /// </summary>
        public void CancelDrag(StudioFoldModel model)
        {
            if (drag == DragKind.Fold)
                model?.SetPreview(null);
            else if (drag == DragKind.Element)
                Undo.RevertAllDownToGroup(elementUndoGroup);
            drag = DragKind.None;
            dragElement = null;
            ReleaseHotControl();
        }

        void ReleaseHotControl()
        {
            if (GUIUtility.hotControl == controlId)
                GUIUtility.hotControl = 0;
        }

        public void Draw(Rect rect, in Context ctx)
        {
            if (!framed || view.PaneRect.size != rect.size)
                view = framed ? view.WithRect(rect) : PaneView.FitSheet(rect, false);
            else
                view = view.WithRect(rect);
            framed = true;

            controlId = GUIUtility.GetControlID(FocusType.Passive, rect);
            HandleInput(rect, ctx);

            if (Event.current.type != EventType.Repaint)
                return;

            if (!ctx.Scene.Render(ctx.Stage, ctx.Model, ctx.Settings, view))
            {
                EditorGUI.HelpBox(rect, "Fold preview could not render: texture/preview-scene creation or the URP render request failed.", MessageType.Error);
                return;
            }
            GUI.DrawTextureWithTexCoords(rect, ctx.Scene.Texture, new Rect(0f, 0f, 1f, 1f));

            DrawOverlays(ctx);
            if (ctx.Scene.TooManyLayers)
            {
                EditorGUI.HelpBox(new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, 32f),
                    "More layers than the preview can order; upper flaps may draw in the wrong order.", MessageType.Warning);
            }
        }

        // ----- Overlays -----

        void DrawOverlays(in Context ctx)
        {
            DrawLoop(SheetOutlineColor, 2f,
                new Vector2(-SheetGeometry.HalfSize.x, -SheetGeometry.HalfSize.y),
                new Vector2(SheetGeometry.HalfSize.x, -SheetGeometry.HalfSize.y),
                new Vector2(SheetGeometry.HalfSize.x, SheetGeometry.HalfSize.y),
                new Vector2(-SheetGeometry.HalfSize.x, SheetGeometry.HalfSize.y));

            var model = ctx.Model;
            var settings = ctx.Settings;
            var seamPixels = Mathf.Clamp(settings.SeamWidth * view.Zoom, 1.5f, 8f);

            // Crease lines (live and remembered) are deliberately not drawn: Aaron (2026-08-31) doesn't want
            // them for level editing. Grabbing near a crease still folds further — the hit test is unchanged.
            // Seams stay: they mark where a click unfolds, for unpinned folds only (a pinned fold's segments
            // are stale — recorded against the stack at its apply time — and would be dead click targets).
            for (int i = 0; i < model.Folds.Count; i++)
            {
                if (model.IsPinned(i))
                    continue;
                foreach (var (a, b) in model.Effects[i].SeamSegments)
                    DrawSegment(settings.SeamColor, seamPixels, a, b);
            }

            // The dragged fold's seam, like the game's drag-time seam border.
            if (drag == DragKind.Fold && model.DisplayEffects.Count > model.Folds.Count)
            {
                var previewEffect = model.DisplayEffects[model.Folds.Count];
                foreach (var (a, b) in previewEffect.SeamSegments)
                    DrawSegment(settings.SeamColor, seamPixels, a, b);
            }

            if (model.Ghost.HasValue)
            {
                var ghost = model.Ghost.Value;
                var min = view.SheetLocalToPane(new Vector2(ghost.xMin, ghost.yMax));
                var max = view.SheetLocalToPane(new Vector2(ghost.xMax, ghost.yMin));
                var guiRect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                EditorGUI.DrawRect(guiRect, GhostFill);
                DrawLoop(GhostOutline, 2f,
                    new Vector2(ghost.xMin, ghost.yMin), new Vector2(ghost.xMax, ghost.yMin),
                    new Vector2(ghost.xMax, ghost.yMax), new Vector2(ghost.xMin, ghost.yMax));
            }

            DrawUniversalRegions(ctx);
            DrawSelectionThroughFold(ctx);
            DrawPlacementGhost(ctx);
        }

        /// <summary>
        /// The universal regions as the game will show them on this stack: authored pieces clipped to the sheet's
        /// footprint as displayed (<see cref="SheetLayers.CoverageAbove"/>), unmoved by any fold.
        /// </summary>
        void DrawUniversalRegions(in Context ctx)
        {
            // Drawn with the Terrain overlay, and whenever the sheet shows collision (the game would draw them; the
            // face fills in the composite already follow that flag through the face cameras).
            var showCollision = ctx.Sheet != null && ctx.Sheet.ShowCollision;
            if ((!ctx.ShowTerrain && !showCollision) || ctx.AboveRoot == null)
                return;
            foreach (var region in ctx.AboveRoot.GetComponentsInChildren<TerrainRegion>(true))
            {
                if (!StudioPlacement.TryGetRegionPieces(region, ctx.AboveRoot, scratchAuthored))
                    continue;
                var clipped = ctx.Model.DisplayLayers.CoverageAbove(FaceFootprint.FromPieces(scratchAuthored));
                foreach (var part in clipped.VisibleParts)
                    FillPolygon(StudioPane.UniversalFill, part);
            }
        }

        void FillPolygon(Color color, ConvexPolygon polygon)
        {
            if (polygon.Count < 3)
                return;
            var points = new Vector3[polygon.Count];
            for (int i = 0; i < points.Length; i++)
                points[i] = view.SheetLocalToPane(polygon.Vertices[i]);
            Handles.color = color;
            Handles.DrawAAConvexPolygon(points);
        }

        /// <summary>
        /// The selected element's footprint mapped through the fold: face-up pieces in the selection colour,
        /// face-down pieces dimmed (Aaron, 2026-08-31: mark hidden pieces so the whole extent reads).
        /// </summary>
        void DrawSelectionThroughFold(in Context ctx)
        {
            if (ctx.Sheet == null)
                return;
            var (element, face, root) = SelectedElement(ctx);
            if (element == null)
                return;

            // The element's true shape (a polygon region's pieces, a prop's sprite rect), not its bounding rect.
            StudioPlacement.AuthoredFootprintPieces(element, root, scratchAuthored);
            if (scratchAuthored.Count == 0)
                return;
            if (root == ctx.AboveRoot)
            {
                // Above the sheet: nothing of it is ever face-down; it is only clipped to the footprint as displayed.
                var clipped = ctx.Model.DisplayLayers.CoverageAbove(FaceFootprint.FromPieces(scratchAuthored));
                foreach (var part in clipped.VisibleParts)
                    DrawPolygon(SelectionColorFold, 3f, part);
                return;
            }
            // DisplayLayers, not Layers: during a fold drag the composite under the highlight includes the
            // previewed fold, and the highlight must agree with the pixels beneath it (code review S5).
            StudioFoldMapping.AuthoredPiecesToDeskPieces(ctx.Model.DisplayLayers, face, scratchAuthored, scratchPieces);
            var dimmed = new Color(SelectionColorFold.r, SelectionColorFold.g, SelectionColorFold.b, 0.35f);
            foreach (var piece in scratchPieces)
                DrawPolygon(piece.FaceUp ? SelectionColorFold : dimmed, piece.FaceUp ? 3f : 2f, piece.Piece);
        }

        /// <summary>
        /// The armed prefab's footprint under the cursor, mapped through the fold, with the target face named;
        /// red over empty desk.
        /// </summary>
        void DrawPlacementGhost(in Context ctx)
        {
            var armed = ctx.Palette?.Armed;
            if (armed == null || ctx.Sheet == null || ctx.LinksActive)
                return;
            var mouse = Event.current.mousePosition;
            if (!view.PaneRect.Contains(mouse))
                return;

            var cursor = view.PaneToSheetLocal(mouse);
            if (StudioPlacement.IsPolygonTerrain(armed))
            {
                // Polygon terrain is drawn point by point in a face pane; the prefab's default square is never what gets placed.
                var marker = view.SheetLocalToPane(cursor);
                EditorGUI.DrawRect(new Rect(marker.x - 3f, marker.y - 3f, 6f, 6f), PlaceGhostColor);
                GUI.Label(new Rect(marker.x + 8f, marker.y - 8f, 220f, 16f), PolygonTerrainHint, EditorStyles.miniLabel);
                return;
            }
            var footprint = StudioPlacement.PlacedFootprint(armed);
            if (StudioPlacement.IsUniversal(armed))
            {
                // Above the sheet: dropped in sheet space at the cursor, shown clipped to the footprint as displayed.
                var snappedCursor = StudioPlacement.Snap(cursor, ctx.SnapIncrement);
                var aboveRect = new Rect(footprint.position + snappedCursor, footprint.size);
                var clipped = ctx.Model.DisplayLayers.CoverageAbove(FaceFootprint.FromRect(aboveRect));
                var labelPoint = view.SheetLocalToPane(cursor);
                if (ctx.AboveRoot == null)
                {
                    DrawLoop(ctx.Settings.InvalidTint, 2f,
                        new Vector2(aboveRect.xMin, aboveRect.yMin), new Vector2(aboveRect.xMax, aboveRect.yMin),
                        new Vector2(aboveRect.xMax, aboveRect.yMax), new Vector2(aboveRect.xMin, aboveRect.yMax));
                    GUI.Label(new Rect(labelPoint.x + 6f, labelPoint.y - 8f, 200f, 16f), "No Above root on this sheet", EditorStyles.miniLabel);
                    return;
                }
                if (clipped.IsNone)
                {
                    DrawLoop(ctx.Settings.InvalidTint, 2f,
                        new Vector2(aboveRect.xMin, aboveRect.yMin), new Vector2(aboveRect.xMax, aboveRect.yMin),
                        new Vector2(aboveRect.xMax, aboveRect.yMax), new Vector2(aboveRect.xMin, aboveRect.yMax));
                    GUI.Label(new Rect(labelPoint.x + 6f, labelPoint.y - 8f, 160f, 16f), "No Sheet here", EditorStyles.miniLabel);
                    return;
                }
                foreach (var part in clipped.VisibleParts)
                    DrawPolygon(PlaceGhostColor, 2f, part);
                GUI.Label(new Rect(labelPoint.x + 8f, labelPoint.y - 8f, 120f, 16f), "→ Above", EditorStyles.miniLabel);
                return;
            }
            if (!StudioFoldMapping.TryMapToAuthored(ctx.Model.DisplayLayers, cursor, out var face, out var authored))
            {
                // Over empty desk: the footprint in the sheet's authored invalid tint (plan §5), never placeable.
                var refused = new Rect(footprint.position + cursor, footprint.size);
                DrawLoop(ctx.Settings.InvalidTint, 2f,
                    new Vector2(refused.xMin, refused.yMin), new Vector2(refused.xMax, refused.yMin),
                    new Vector2(refused.xMax, refused.yMax), new Vector2(refused.xMin, refused.yMax));
                var gui = view.SheetLocalToPane(cursor);
                GUI.Label(new Rect(gui.x + 6f, gui.y - 8f, 160f, 16f), "No Sheet here", EditorStyles.miniLabel);
                return;
            }
            authored = StudioPlacement.Snap(authored, ctx.SnapIncrement);
            var target = new Rect(footprint.position + authored, footprint.size);
            StudioFoldMapping.AuthoredBoxToDeskPieces(ctx.Model.DisplayLayers, face, target, scratchPieces);
            foreach (var piece in scratchPieces)
                DrawPolygon(PlaceGhostColor, 2f, piece.Piece);
            var labelAt = view.SheetLocalToPane(cursor);
            GUI.Label(new Rect(labelAt.x + 8f, labelAt.y - 8f, 120f, 16f), $"→ {face}", EditorStyles.miniLabel);
        }

        (GameObject element, SheetFace face, Transform root) SelectedElement(in Context ctx)
        {
            var active = Selection.activeGameObject;
            if (active == null)
                return (null, default, null);
            foreach (var (root, face) in new[] { (ctx.Sheet.Front, SheetFace.Front), (ctx.Sheet.Back, SheetFace.Back), (ctx.Sheet.Above, SheetFace.Front) })
            {
                var element = StudioPlacement.ElementRootOf(active.transform, root);
                if (element != null && StudioPlacement.CanEdit(element, ctx.Sheet))
                    return (element, face, root);
            }
            return (null, default, null);
        }

        void DrawPolygon(Color color, float width, ConvexPolygon polygon)
        {
            var vertices = polygon.Vertices;
            if (vertices.Count < 2)
                return;
            var points = new Vector3[vertices.Count + 1];
            for (int i = 0; i < vertices.Count; i++)
                points[i] = view.SheetLocalToPane(vertices[i]);
            points[^1] = points[0];
            Handles.color = color;
            Handles.DrawAAPolyLine(width, points);
        }

        void DrawSegment(Color color, float pixels, Vector2 a, Vector2 b)
        {
            Handles.color = color;
            Handles.DrawAAPolyLine(pixels, (Vector3)view.SheetLocalToPane(a), (Vector3)view.SheetLocalToPane(b));
        }

        void DrawLoop(Color color, float pixels, params Vector2[] sheetLocal)
        {
            var points = new Vector3[sheetLocal.Length + 1];
            for (int i = 0; i < sheetLocal.Length; i++)
                points[i] = view.SheetLocalToPane(sheetLocal[i]);
            points[^1] = points[0];
            Handles.color = color;
            Handles.DrawAAPolyLine(pixels, points);
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
                    view = view.ZoomedAbout(Mathf.Clamp(view.Zoom * (1f - e.delta.y * ZoomStep), MinZoom, MaxZoom), e.mousePosition);
                    e.Use();
                    break;
                case EventType.MouseDown when e.button == 2:
                    // A pan press ends any drag properly — never leave a preview stranded in the model.
                    CancelDrag(ctx.Model);
                    Report(string.Empty);
                    e.Use();
                    break;
                case EventType.MouseDown when e.button == 1:
                    if (drag != DragKind.None)
                    {
                        CancelDrag(ctx.Model);
                        Report(string.Empty);
                        e.Use();
                    }
                    break;
                case EventType.MouseDown when e.button == 0:
                    OnPress(e, ctx);
                    break;
                case EventType.MouseDrag when e.button == 2:
                    view = view.PannedBy(e.delta);
                    e.Use();
                    break;
                case EventType.MouseDrag when e.button == 0:
                    OnDrag(e, ctx);
                    break;
                case EventType.MouseUp when e.button == 0:
                    OnRelease(e, ctx);
                    break;
            }
        }

        void OnPress(Event e, in Context ctx)
        {
            GUIUtility.keyboardControl = 0;
            Report(string.Empty);
            BeginPress(ctx, view.PaneToSheetLocal(e.mousePosition));
            e.Use();
        }

        /// <summary>
        /// The press-priority chain (Aaron, 2026-08-31: fold gestures before elements):
        /// armed palette place → ghost → Seam unfold → crease grab → edge/corner fold → element select/move.
        /// Internal and Event-free so tests can drive it headlessly.
        /// </summary>
        internal PressOutcome BeginPress(in Context ctx, Vector2 local)
        {
            var model = ctx.Model;
            var settings = ctx.Settings;

            // 1. Armed palette: place through the fold — Aaron's original ask verbatim.
            var armed = ctx.Palette?.Armed;
            if (armed != null && ctx.Sheet != null)
            {
                if (StudioPlacement.IsPolygonTerrain(armed))
                {
                    Report(PolygonTerrainHint);
                    return PressOutcome.PlaceRefusedPolygonTerrain;
                }
                if (StudioPlacement.IsUniversal(armed))
                {
                    // Above the sheet: dropped in sheet space where the cursor is, if the sheet lies there.
                    var aboveRoot = StudioPlacement.TargetRoot(armed, ctx.Sheet, ctx.Sheet.Front, aboveEditable: true, out var refusal);
                    if (aboveRoot == null)
                    {
                        Report(refusal);
                        return PressOutcome.PlaceRefusedNoAboveRoot;
                    }
                    if (StudioFoldMapping.TopLayerAt(model.Layers, local) < 0)
                    {
                        Report("No Sheet under the cursor.");
                        return PressOutcome.PlaceRefusedNoSheet;
                    }
                    var placedAbove = StudioPlacement.Place(armed, aboveRoot, SheetFace.Front, local, ctx.SnapIncrement);
                    if (placedAbove == null)
                        return PressOutcome.Nothing;
                    Selection.activeGameObject = placedAbove;
                    Report("Placed above the sheet.");
                    return PressOutcome.Placed;
                }
                if (!StudioFoldMapping.TryMapToAuthored(model.Layers, local, out var targetFace, out var authored))
                {
                    Report("No Sheet under the cursor.");
                    return PressOutcome.PlaceRefusedNoSheet;
                }
                var targetRoot = targetFace == SheetFace.Front ? ctx.Sheet.Front : ctx.Sheet.Back;
                var placed = StudioPlacement.Place(armed, targetRoot, targetFace, authored, ctx.SnapIncrement);
                if (placed == null)
                    return PressOutcome.Nothing; // Place already logged the failure.
                Selection.activeGameObject = placed;
                Report($"Placed on the {targetFace} face.");
                return PressOutcome.Placed;
            }

            // 2. The ghost, when the press isn't claiming a seam or crease.
            if (model.Ghost.HasValue && model.Ghost.Value.Contains(local) && !NearSeamOrCrease(model, local, settings))
            {
                drag = DragKind.Ghost;
                ghostGrabOffset = model.Ghost.Value.center - local;
                return PressOutcome.GhostDragStarted;
            }

            // 3–5. The game's press order: Seam (unfold), then crease (fold further), then edge/corner (new fold).
            if (model.TryUnfoldAt(local, settings.UnfoldGrabDistance, out var rejection))
            {
                if (model.GhostWasRelocated)
                    Report("Ghost moved to stay on the Sheet.");
                return PressOutcome.Unfolded;
            }
            if (rejection == FoldRejection.PlayerOnFlap)
            {
                Report("Not unfolded: the test player stands on that flap.");
                return PressOutcome.UnfoldRefused;
            }
            if (model.TryGrabCreaseAt(local, settings.CreaseGrabDistance, out var grabbed))
            {
                BeginFoldDrag(ctx, grabbed.Anchor, grabbed.Depth, local);
                return PressOutcome.FoldDragStarted;
            }
            if (TryResolveAnchor(local, settings, out var anchor))
            {
                BeginFoldDrag(ctx, anchor, 0f, local);
                return PressOutcome.FoldDragStarted;
            }
            if (rejection == FoldRejection.CoveredByLaterFold)
            {
                Report("That fold is covered by a later fold; unfold that first.");
                return PressOutcome.UnfoldRefused;
            }

            // 6. Element select/move — inert while Link mode is on (Aaron: wiring stays in the face panes).
            if (ctx.LinksActive)
                return PressOutcome.ElementInertLinkMode;
            return BeginElementPress(ctx, local);
        }

        PressOutcome BeginElementPress(in Context ctx, Vector2 local)
        {
            if (ctx.Sheet == null)
                return PressOutcome.Nothing;
            var pressLayer = StudioFoldMapping.TopLayerAt(ctx.Model.Layers, local);
            if (pressLayer < 0)
            {
                Selection.activeGameObject = null; // Bare desk clears the selection, like empty sheet does.
                return PressOutcome.Nothing;
            }
            StudioFoldMapping.MapThroughLayer(ctx.Model.Layers, pressLayer, local, out var face, out var authored);

            // Above content is picked at the unmapped desk point (it never moves with a fold); the face hit through the
            // layer under the cursor. The smaller outline wins, a tie to Above - it is on top (plan review N3).
            var root = face == SheetFace.Front ? ctx.Sheet.Front : ctx.Sheet.Back;
            StudioPlacement.TryPickElement(root, ctx.Sheet, authored, out var picked, out var faceArea);
            if (ctx.AboveRoot != null && StudioPlacement.TryPickElement(ctx.AboveRoot, ctx.Sheet, local, out var above, out var aboveArea)
                && (picked == null || aboveArea <= faceArea))
            {
                Selection.activeGameObject = above;
                elementGrabOffsetDesk = (Vector2)above.transform.localPosition - local;
                drag = DragKind.Element;
                dragElement = above;
                dragAbove = true;
                elementUndoGroup = Undo.GetCurrentGroup();
                GUIUtility.hotControl = controlId;
                return PressOutcome.ElementDragStarted;
            }
            Selection.activeGameObject = picked;
            if (picked == null)
                return PressOutcome.Nothing;
            dragAbove = false;

            // Desk-space grab offset (round-1 N4), computed through the SAME layer the press mapped through
            // (code review M1): the element's centre may lie on a different piece with a different isometry
            // (a crease-straddler), and mixing isometries teleports the element on the first drag pixel.
            // Extrapolating the press layer's isometry keeps press offset and every MapThroughLayer coherent.
            // The element's face IS `face`: PickElement searched exactly that face's root.
            Vector2 authoredCentre = picked.transform.localPosition;
            var flatCentre = face == SheetFace.Front ? authoredCentre : SheetGeometry.BackToFront(authoredCentre);
            var elementDesk = ctx.Model.Layers.Layers[pressLayer].ToDesk.Apply(flatCentre);
            elementGrabOffsetDesk = elementDesk - local;

            drag = DragKind.Element;
            dragElement = picked;
            elementUndoGroup = Undo.GetCurrentGroup();
            // Held until release so Auto Save writes the drag once, at the end (StudioEdits); 0 when driven headlessly.
            GUIUtility.hotControl = controlId;
            return PressOutcome.ElementDragStarted;
        }

        void BeginFoldDrag(in Context ctx, FoldAnchor anchor, float grabDepth, Vector2 local)
        {
            drag = DragKind.Fold;
            dragAnchor = anchor;
            dragGrabDepth = grabDepth;
            dragFold = ComputeDragFold(ctx, local);
            ctx.Model.SetPreview(dragFold);
        }

        void OnDrag(Event e, in Context ctx)
        {
            switch (drag)
            {
                case DragKind.Fold:
                    dragFold = ComputeDragFold(ctx, view.PaneToSheetLocal(e.mousePosition));
                    ctx.Model.SetPreview(dragFold);
                    e.Use();
                    break;
                case DragKind.Ghost:
                    ctx.Model.MoveGhost(view.PaneToSheetLocal(e.mousePosition) + ghostGrabOffset);
                    e.Use();
                    break;
                case DragKind.Element:
                {
                    // Follow-the-cursor (Aaron): the cursor picks the topmost layer (and alone decides
                    // empty desk — the element is never dropped onto bare desk); cursor+offset maps through
                    // that layer's isometry, extrapolated, so motion is continuous near boundaries.
                    var cursor = view.PaneToSheetLocal(e.mousePosition);
                    if (dragAbove)
                    {
                        if (dragElement != null)
                            StudioPlacement.Move(dragElement, cursor + elementGrabOffsetDesk, ctx.SnapIncrement);
                        e.Use();
                        break;
                    }
                    var layerIndex = StudioFoldMapping.TopLayerAt(ctx.Model.Layers, cursor);
                    if (layerIndex >= 0 && dragElement != null)
                    {
                        StudioFoldMapping.MapThroughLayer(ctx.Model.Layers, layerIndex,
                            cursor + elementGrabOffsetDesk, out var face, out var authored);
                        if (!StudioPlacement.MoveMapped(dragElement, ctx.Sheet, face, authored, ctx.SnapIncrement, out var reason)
                            && reason != null)
                            Report(reason);
                    }
                    e.Use();
                    break;
                }
            }
        }

        void OnRelease(Event e, in Context ctx)
        {
            if (drag == DragKind.Fold)
            {
                // The game commits the fold under the cursor at release, not the last drag sample.
                dragFold = ComputeDragFold(ctx, view.PaneToSheetLocal(e.mousePosition));
                ctx.Model.SetPreview(null);
                if (!ctx.Model.TryCommit(dragFold, ctx.Settings.MinDepth, out var rejection))
                {
                    // The game's filter: refusals already shown red, and clicks that never became drags, stay quiet.
                    if (rejection != FoldRejection.CoversPlayer && rejection != FoldRejection.CoversObstacle
                        && rejection != FoldRejection.TooShallow && rejection != FoldRejection.NothingToFold)
                        Report($"Fold not made: {rejection}.");
                }
                else if (ctx.Model.GhostWasRelocated)
                {
                    Report("Ghost moved to stay on the Sheet.");
                }
                e.Use();
            }
            else if (drag == DragKind.Element)
            {
                Undo.CollapseUndoOperations(elementUndoGroup); // One undo step per drag, however many face crossings.
                dragElement = null;
                ReleaseHotControl();
                e.Use();
            }
            else if (drag == DragKind.Ghost)
            {
                e.Use();
            }
            drag = DragKind.None;
        }

        /// <summary>The fold under the cursor: depth from the drag point and grabbed line, snapped, clamped to MaxDepth (overhang and paperweights).</summary>
        Fold ComputeDragFold(in Context ctx, Vector2 local)
        {
            var depth = FoldGeometry.DepthForDragPoint(dragAnchor, local, dragGrabDepth);
            if (ctx.Settings.DepthSnap > 0f)
                depth = Mathf.Round(depth / ctx.Settings.DepthSnap) * ctx.Settings.DepthSnap;
            return new Fold(dragAnchor, Mathf.Min(depth, ctx.Model.MaxDepth(dragAnchor)));
        }

        static bool NearSeamOrCrease(StudioFoldModel model, Vector2 local, in StudioFoldSettings settings)
        {
            for (int i = 0; i < model.Effects.Count; i++)
            {
                if (model.IsPinned(i))
                    continue;
                if (model.Effects[i].DistanceToSeam(local) <= settings.UnfoldGrabDistance
                    || model.Effects[i].DistanceToCrease(local) <= settings.CreaseGrabDistance)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The game's anchor resolution: nearest corner within the radius wins, else nearest edge in the
        /// margin. Deliberately a line-for-line mirror of <c>FoldDragInput.TryResolveAnchor</c> (which cannot
        /// be called: it is private on a Desk-scene component) — if that method changes, change this one too.
        /// </summary>
        static bool TryResolveAnchor(Vector2 local, in StudioFoldSettings settings, out FoldAnchor anchor)
        {
            var half = SheetGeometry.HalfSize;
            anchor = default;
            var best = float.PositiveInfinity;

            foreach (var corner in new[] { FoldAnchor.CornerNorthEast, FoldAnchor.CornerNorthWest, FoldAnchor.CornerSouthEast, FoldAnchor.CornerSouthWest })
            {
                var d = Vector2.Distance(local, Vector2.Scale(corner.CornerSigns(), half));
                if (d <= settings.CornerGrabRadius && d < best)
                {
                    best = d;
                    anchor = corner;
                }
            }
            if (best < float.PositiveInfinity)
                return true;

            foreach (var edge in new[] { FoldAnchor.EdgeNorth, FoldAnchor.EdgeEast, FoldAnchor.EdgeSouth, FoldAnchor.EdgeWest })
            {
                var outward = edge.EdgeDirection().ToVector();
                var halfExtent = Mathf.Abs(Vector2.Dot(half, outward));
                var halfAlong = Mathf.Abs(Vector2.Dot(half, new Vector2(outward.y, outward.x)));
                var d = Mathf.Abs(Vector2.Dot(local, outward) - halfExtent);
                var along = Mathf.Abs(Vector2.Dot(local, new Vector2(outward.y, outward.x)));
                if (d <= settings.EdgeGrabMargin && along <= halfAlong + settings.EdgeGrabMargin && d < best)
                {
                    best = d;
                    anchor = edge;
                }
            }
            return best < float.PositiveInfinity;
        }
    }
}
