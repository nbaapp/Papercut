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
        public const string CreaseColorField = "creaseColor";
        public const string RememberedCreaseColorField = "rememberedCreaseColor";
        public const string CreaseWidthField = "creaseWidth";
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

        public readonly float MinDepth;
        public readonly Color PreviewTint;
        public readonly Color InvalidTint;
        public readonly Color CreaseColor;
        public readonly Color RememberedCreaseColor;
        public readonly float CreaseWidth;
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

        /// <summary>Non-null when a sheet component was missing and defaults filled in — shown, never silent.</summary>
        public readonly string MissingNote;

        /// <summary>Editor constant: what shows where folds emptied the sheet. Not the game's Desk surface.</summary>
        public Color PaneBackground => new(0.22f, 0.2f, 0.18f, 1f);

        StudioFoldSettings(float minDepth, Color previewTint, Color invalidTint, Color creaseColor, Color rememberedCreaseColor,
            float creaseWidth, float seamWidth, Color seamColor, int pixelsPerUnit, Material faceMaterial,
            float cornerGrabRadius, float edgeGrabMargin, float unfoldGrabDistance, float creaseGrabDistance, float depthSnap,
            bool rememberCreases, string missingNote)
        {
            RememberCreases = rememberCreases;
            MissingNote = missingNote;
            MinDepth = minDepth;
            PreviewTint = previewTint;
            InvalidTint = invalidTint;
            CreaseColor = creaseColor;
            RememberedCreaseColor = rememberedCreaseColor;
            CreaseWidth = creaseWidth;
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
            var creaseColor = new Color(0.25f, 0.2f, 0.15f, 0.9f);
            var rememberedColor = new Color(0.25f, 0.2f, 0.15f, 0.35f);
            var creaseWidth = 0.06f;
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
            string missingNote = null;

            if (sheet != null && sheet.TryGetComponent(out SheetFolds folds))
            {
                var so = new SerializedObject(folds);
                minDepth = so.FindProperty(MinDepthField)?.floatValue ?? minDepth;
                rememberCreases = so.FindProperty(RememberCreasesField)?.boolValue ?? rememberCreases;
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
                creaseColor = so.FindProperty(CreaseColorField)?.colorValue ?? creaseColor;
                rememberedColor = so.FindProperty(RememberedCreaseColorField)?.colorValue ?? rememberedColor;
                creaseWidth = so.FindProperty(CreaseWidthField)?.floatValue ?? creaseWidth;
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

            return new StudioFoldSettings(minDepth, previewTint, invalidTint, creaseColor, rememberedColor,
                creaseWidth, seamWidth, seamColor, pixelsPerUnit, faceMaterial,
                cornerGrab, edgeGrab, unfoldGrab, creaseGrab, depthSnap, rememberCreases, missingNote);
        }

        /// <summary>
        /// The test-player footprint size, from Player.prefab's BoxCollider2D **serialized** size (never
        /// <c>Collider2D.bounds</c>, which is physics-populated and degenerate on an uninstantiated asset —
        /// a zero-size ghost would silently disable the covers-player rule).
        /// </summary>
        public static Vector2 ReadPlayerFootprintSize()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Papercut/Prefabs/Player.prefab");
            var box = player != null ? player.GetComponent<BoxCollider2D>() : null;
            if (box == null || box.size.x <= 0f || box.size.y <= 0f)
            {
                Debug.LogWarning("Sheet Studio: Player.prefab or its BoxCollider2D not found; the test-player ghost uses a 0.5 x 0.5 footprint.");
                return new Vector2(0.5f, 0.5f);
            }
            return box.size;
        }
    }

    /// <summary>
    /// The fold-preview pane: shows the sheet folded exactly as the game's composite would, with the game's
    /// drag gesture — Seam click unfolds, crease grab folds further, edge/corner grab starts a new fold; live
    /// preview, red refuse, release commits, right-click/Escape cancels — plus the draggable test-player ghost.
    /// </summary>
    public sealed class StudioFoldPane
    {
        public struct Context
        {
            public PrefabStage Stage;
            public StudioFoldModel Model;
            public StudioFoldScene Scene;
            public StudioFoldSettings Settings;
        }

        const float MinZoom = 4f;
        const float MaxZoom = 400f;
        const float ZoomStep = 0.05f;
        static readonly Color GhostFill = new(0.3f, 0.6f, 1f, 0.25f);
        static readonly Color GhostOutline = new(0.3f, 0.6f, 1f, 1f);
        static readonly Color SheetOutlineColor = new(1f, 1f, 1f, 0.5f);

        enum DragKind { None, Fold, Ghost }

        PaneView view;
        bool framed;
        DragKind drag;
        FoldAnchor dragAnchor;
        float dragGrabDepth;
        Fold dragFold;
        Vector2 ghostGrabOffset;

        /// <summary>
        /// Where the pane reports what it has to say (refusals, relocations). The window points this at its
        /// single message slot, shared with the fold list, so the newest message always wins (no stale
        /// refusal can shadow a later one). A new press clears it.
        /// </summary>
        public System.Action<string> Report = _ => { };

        public bool IsDragging => drag != DragKind.None;

        public PaneView View => view;

        public void FrameSheet() => framed = false;

        /// <summary>Cancels an active fold or ghost drag (Escape). The window routes Escape here first.</summary>
        public void CancelDrag(StudioFoldModel model)
        {
            if (drag == DragKind.Fold)
                model?.SetPreview(null);
            drag = DragKind.None;
        }

        public void Draw(Rect rect, in Context ctx)
        {
            if (!framed || view.PaneRect.size != rect.size)
                view = framed ? view.WithRect(rect) : PaneView.FitSheet(rect, false);
            else
                view = view.WithRect(rect);
            framed = true;

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
            var model = ctx.Model;
            var settings = ctx.Settings;
            var local = view.PaneToSheetLocal(e.mousePosition);
            Report(string.Empty);

            // The ghost grabs first, but only when the press isn't claiming a seam or crease.
            if (model.Ghost.HasValue && model.Ghost.Value.Contains(local) && !NearSeamOrCrease(model, local, settings))
            {
                drag = DragKind.Ghost;
                ghostGrabOffset = model.Ghost.Value.center - local;
                e.Use();
                return;
            }

            // The game's press order: Seam (unfold), then crease (fold further), then edge/corner (new fold).
            if (model.TryUnfoldAt(local, settings.UnfoldGrabDistance, out var rejection))
            {
                if (model.GhostWasRelocated)
                    Report("Ghost moved to stay on the Sheet.");
                e.Use();
                return;
            }
            if (rejection == FoldRejection.PlayerOnFlap)
            {
                Report("Not unfolded: the test player stands on that flap.");
                e.Use();
                return;
            }

            if (model.TryGrabCreaseAt(local, settings.CreaseGrabDistance, out var grabbed))
            {
                BeginFoldDrag(ctx, grabbed.Anchor, grabbed.Depth, local);
                e.Use();
                return;
            }
            if (TryResolveAnchor(local, settings, out var anchor))
            {
                BeginFoldDrag(ctx, anchor, 0f, local);
                e.Use();
                return;
            }
            if (rejection == FoldRejection.CoveredByLaterFold)
                Report("That fold is covered by a later fold; unfold that first.");
            e.Use();
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
                    if (rejection != FoldRejection.CoversPlayer && rejection != FoldRejection.TooShallow
                        && rejection != FoldRejection.NothingToFold)
                        Report($"Fold not made: {rejection}.");
                }
                else if (ctx.Model.GhostWasRelocated)
                {
                    Report("Ghost moved to stay on the Sheet.");
                }
                e.Use();
            }
            else if (drag == DragKind.Ghost)
            {
                e.Use();
            }
            drag = DragKind.None;
        }

        /// <summary>The fold under the cursor: depth from the drag point and grabbed line, snapped, clamped to MaxDepth.</summary>
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
