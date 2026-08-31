using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// The Sheet Studio: Aaron's level editor. Shows the open sheet's Front and Back side by side (Back in
    /// its authored Back-space, optionally mirrored for display), an element palette, per-face background-art
    /// slots, and creates new sheet variants onto the Desk. Editing happens in the ordinary Prefab Stage, so
    /// Unity's Undo, Inspector, and prefab saving (Ctrl+S / Auto Save) are the real machinery throughout.
    /// </summary>
    public sealed class SheetStudioWindow : EditorWindow
    {
        const string PrefPrefix = "Papercut.SheetStudio.";
        const float ToolbarHeight = 24f;
        const float ArtRowHeight = 24f;
        const float PaletteHeight = 84f;
        const float PaneGap = 4f;
        const float PaneHeaderHeight = 18f;

        StudioPane frontPane;
        StudioPane backPane;
        StudioPalette palette;
        readonly StudioLinkState links = new();
        string attachedStagePath;
        Vector2Int newSheetPosition;

        static float SnapIncrement
        {
            get => EditorPrefs.GetFloat(PrefPrefix + "snapIncrement", 0f);
            set => EditorPrefs.SetFloat(PrefPrefix + "snapIncrement", Mathf.Max(0f, value));
        }

        static bool ShowColliderOverlays
        {
            get => EditorPrefs.GetBool(PrefPrefix + "showColliderOverlays", true);
            set => EditorPrefs.SetBool(PrefPrefix + "showColliderOverlays", value);
        }

        static bool ShowLabels
        {
            get => EditorPrefs.GetBool(PrefPrefix + "showLabels", true);
            set => EditorPrefs.SetBool(PrefPrefix + "showLabels", value);
        }

        static bool MirrorBackPane
        {
            get => EditorPrefs.GetBool(PrefPrefix + "mirrorBackPane", false);
            set => EditorPrefs.SetBool(PrefPrefix + "mirrorBackPane", value);
        }

        [MenuItem("Papercut/Sheet Studio")]
        public static void Open()
        {
            var window = GetWindow<SheetStudioWindow>("Sheet Studio");
            window.minSize = new Vector2(720f, 420f);
        }

        void OnEnable()
        {
            frontPane = new StudioPane(SheetFace.Front);
            backPane = new StudioPane(SheetFace.Back);
            palette = new StudioPalette();
            palette.Refresh();
            wantsMouseMove = true;
            Undo.undoRedoPerformed += OnEditsChanged;
            EditorApplication.hierarchyChanged += OnEditsChanged;
            ObjectChangeEvents.changesPublished += OnObjectsChanged;
            PrefabStage.prefabStageOpened += OnStageChanged;
            PrefabStage.prefabStageClosing += OnStageChanged;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnEditsChanged;
            EditorApplication.hierarchyChanged -= OnEditsChanged;
            ObjectChangeEvents.changesPublished -= OnObjectsChanged;
            PrefabStage.prefabStageOpened -= OnStageChanged;
            PrefabStage.prefabStageClosing -= OnStageChanged;
            frontPane?.Dispose();
            backPane?.Dispose();
        }

        void OnStageChanged(PrefabStage stage) => OnEditsChanged();

        // Property-only edits (e.g. a collider resized in the Inspector) fire neither hierarchyChanged nor a
        // repaint of an unfocused window, so overlays would go stale without the ObjectChangeEvents hook.
        void OnObjectsChanged(ref ObjectChangeEventStream stream) => OnEditsChanged();

        void OnEditsChanged()
        {
            frontPane?.InvalidateOverlayCache();
            backPane?.InvalidateOverlayCache();
            Repaint();
        }

        void OnGUI()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            var sheet = stage != null && stage.prefabContentsRoot != null
                ? stage.prefabContentsRoot.GetComponent<Sheet>()
                : null;

            if (stage == null || sheet == null)
            {
                attachedStagePath = null;
                DrawSheetPicker(stage);
                return;
            }

            if (!FoldLayers.Valid)
            {
                EditorGUILayout.HelpBox(
                    $"Layers '{FoldLayers.FrontLayerName}' and '{FoldLayers.BackLayerName}' are missing from Project Settings > Tags and Layers. " +
                    "Add them, then trigger a script recompile or domain reload — the layer lookup is cached until then.",
                    MessageType.Error);
                return;
            }

            if (sheet.Front == null || sheet.Back == null)
            {
                EditorGUILayout.HelpBox($"Sheet '{sheet.name}' is missing its Front/Back root assignment; fix the prefab before editing.", MessageType.Error);
                return;
            }

            if (attachedStagePath != stage.assetPath)
                Attach(stage, sheet);

            HandleKeyboard(sheet);
            DrawToolbar();
            DrawArtRow(sheet);
            DrawPanes(stage, sheet);
            palette.Draw(new Rect(0f, position.height - PaletteHeight, position.width, PaletteHeight));
            if (links.Active && palette.Armed != null)
                links.Exit(); // Arming a prefab means Aaron wants to place, not wire.
        }

        void Attach(PrefabStage stage, Sheet sheet)
        {
            attachedStagePath = stage.assetPath;
            links.Exit(); // The old stage's source object is gone with its stage.
            var changed = StudioSheetOps.NormalizeFaceLayers(sheet);
            if (changed > 0)
                Debug.Log($"Sheet Studio: moved {changed} face-content object(s) of '{sheet.name}' onto the SheetFront/SheetBack layers (one-time; runtime forces the same layers at Awake).");
            frontPane.FrameSheet();
            backPane.FrameSheet();
            palette.Refresh();
        }

        // ----- No-sheet view -----

        void DrawSheetPicker(PrefabStage stage)
        {
            if (stage != null)
                EditorGUILayout.HelpBox("The open Prefab Stage is not a Sheet. Open one of the sheets below to edit it.", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Open a sheet in Prefab Mode to edit it.", MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sheets", EditorStyles.boldLabel);
            foreach (var path in StudioSheetOps.FindSheetAssets())
            {
                if (GUILayout.Button(System.IO.Path.GetFileNameWithoutExtension(path)))
                    PrefabStageUtility.OpenPrefab(path);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("New Sheet", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                newSheetPosition = EditorGUILayout.Vector2IntField(GUIContent.none, newSheetPosition, GUILayout.Width(140f));
                if (GUILayout.Button($"Create Sheet ({newSheetPosition.x},{newSheetPosition.y})"))
                    CreateSheet(newSheetPosition);
            }
            if (StudioSheetOps.FindOpenDesk() == null)
                EditorGUILayout.LabelField("No Desk scene is open: a created sheet will not be placed on the Desk.", EditorStyles.miniLabel);
        }

        void CreateSheet(Vector2Int gridPosition)
        {
            var result = StudioSheetOps.CreateSheet(gridPosition, StudioSheetOps.FindOpenDesk(), out var message);
            switch (result)
            {
                case StudioSheetOps.CreateSheetResult.Created:
                case StudioSheetOps.CreateSheetResult.CreatedWithoutDesk:
                case StudioSheetOps.CreateSheetResult.AddedExistingToDesk:
                    Debug.Log($"Sheet Studio: {message}");
                    PrefabStageUtility.OpenPrefab(StudioSheetOps.SheetAssetPath(gridPosition));
                    break;
                default:
                    EditorUtility.DisplayDialog("Sheet Studio", message, "OK");
                    break;
            }
        }

        // ----- Editing view -----

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(ToolbarHeight)))
            {
                GUILayout.Label("Snap", GUILayout.Width(34f));
                SnapIncrement = EditorGUILayout.FloatField(SnapIncrement, GUILayout.Width(44f));
                ShowColliderOverlays = GUILayout.Toggle(ShowColliderOverlays, "Overlays", EditorStyles.toolbarButton, GUILayout.Width(64f));
                ShowLabels = GUILayout.Toggle(ShowLabels, "Labels", EditorStyles.toolbarButton, GUILayout.Width(54f));
                MirrorBackPane = GUILayout.Toggle(MirrorBackPane, "Mirror Back", EditorStyles.toolbarButton, GUILayout.Width(50f));
                var linkMode = GUILayout.Toggle(links.Active, "Link", EditorStyles.toolbarButton, GUILayout.Width(40f));
                if (linkMode != links.Active)
                {
                    links.Exit();
                    links.Active = linkMode;
                    if (linkMode)
                        palette.Disarm(); // Link mode and placement are exclusive: pane clicks can only mean one thing.
                }
                if (GUILayout.Button("Frame (F)", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                {
                    frontPane.FrameSheet();
                    backPane.FrameSheet();
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label(links.Active
                        ? "Link mode: click a plate, then click what it affects (either pane) — right-click a plate to clear its links"
                        : "Snap 0 = free placement (the world is not tile-based)",
                    EditorStyles.miniLabel);
            }
        }

        void DrawArtRow(Sheet sheet)
        {
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(ArtRowHeight)))
            {
                DrawArtSlot(sheet.Front, SheetFace.Front, "Front art");
                DrawArtSlot(sheet.Back, SheetFace.Back, "Back art");
            }
        }

        void DrawArtSlot(Transform faceRoot, SheetFace face, string label)
        {
            var art = StudioSheetOps.FindFaceArt(faceRoot, out var matches);
            var current = art != null ? art.GetComponent<SpriteRenderer>().sprite : null;
            using (new EditorGUILayout.HorizontalScope(GUILayout.Width(position.width * 0.5f - PaneGap)))
            {
                GUILayout.Label(label, GUILayout.Width(60f));
                var assigned = (Sprite)EditorGUILayout.ObjectField(current, typeof(Sprite), false);
                if (assigned != current)
                {
                    if (assigned == null)
                        StudioSheetOps.ClearFaceArt(faceRoot);
                    else
                        StudioSheetOps.SetFaceArt(faceRoot, face, assigned);
                }
                if (matches > 1)
                    GUILayout.Label($"{matches} art-like children — showing the first", EditorStyles.miniLabel);
            }
        }

        void DrawPanes(PrefabStage stage, Sheet sheet)
        {
            var top = ToolbarHeight + ArtRowHeight + PaneHeaderHeight + 4f;
            var height = position.height - top - PaletteHeight - PaneGap;
            var width = (position.width - PaneGap) * 0.5f;
            if (height <= 20f || width <= 20f)
                return;

            var frontRect = new Rect(0f, top, width, height);
            var backRect = new Rect(width + PaneGap, top, width, height);
            GUI.Label(new Rect(frontRect.x, top - PaneHeaderHeight, width, PaneHeaderHeight), "Front", EditorStyles.boldLabel);
            GUI.Label(new Rect(backRect.x, top - PaneHeaderHeight, width, PaneHeaderHeight),
                MirrorBackPane ? "Back (mirrored display — aligned with Front)" : "Back (authored Back-space)", EditorStyles.boldLabel);

            frontPane.Draw(frontRect, ContextFor(stage, sheet, SheetFace.Front));
            backPane.Draw(backRect, ContextFor(stage, sheet, SheetFace.Back));
        }

        StudioPane.Context ContextFor(PrefabStage stage, Sheet sheet, SheetFace face) => new()
        {
            Stage = stage,
            Sheet = sheet,
            FaceRoot = face == SheetFace.Front ? sheet.Front : sheet.Back,
            Palette = palette,
            Links = links,
            SnapIncrement = SnapIncrement,
            ShowOverlays = ShowColliderOverlays,
            ShowLabels = ShowLabels,
            MirrorX = face == SheetFace.Back && MirrorBackPane,
        };

        // ----- Keyboard -----

        void HandleKeyboard(Sheet sheet)
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown)
                return;

            // While a text field (e.g. the Snap value) is being edited, keystrokes belong to it —
            // Backspace must delete a character, never the selected element.
            if (EditorGUIUtility.editingTextField)
                return;

            if (e.keyCode == KeyCode.Escape)
            {
                // First Escape disarms the link source, the second leaves link mode; outside link mode it disarms the palette.
                if (links.Active)
                {
                    if (links.Source != null)
                        links.Source = null;
                    else
                        links.Exit();
                }
                else
                {
                    palette.Disarm();
                }
                e.Use();
                return;
            }

            // A focused control (e.g. an art-slot ObjectField) owns the remaining keys — Delete must clear
            // the field, not the selected element. Clicking a pane clears keyboard focus (StudioPane).
            if (GUIUtility.keyboardControl != 0)
                return;

            if (e.keyCode == KeyCode.F)
            {
                frontPane.FrameSheet();
                backPane.FrameSheet();
                e.Use();
                return;
            }

            var selected = SelectedEditableElement(sheet);
            if (selected == null)
                return;

            switch (e.keyCode)
            {
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    StudioPlacement.Delete(selected);
                    Selection.activeGameObject = null;
                    e.Use();
                    break;
                case KeyCode.D when e.control || e.command:
                    Selection.activeGameObject = StudioPlacement.Duplicate(selected, SnapIncrement);
                    e.Use();
                    break;
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                {
                    var step = SnapIncrement > 0f ? SnapIncrement : StudioPlacement.FreeNudge;
                    var delta = e.keyCode switch
                    {
                        KeyCode.UpArrow => new Vector2(0f, step),
                        KeyCode.DownArrow => new Vector2(0f, -step),
                        KeyCode.LeftArrow => new Vector2(-step, 0f),
                        _ => new Vector2(step, 0f),
                    };
                    var target = (Vector2)selected.transform.localPosition + delta;
                    StudioPlacement.Move(selected, target, 0f); // The step already respects snap; don't re-round.
                    e.Use();
                    break;
                }
            }
        }

        GameObject SelectedEditableElement(Sheet sheet)
        {
            var active = Selection.activeGameObject;
            if (active == null)
                return null;
            foreach (var root in new[] { sheet.Front, sheet.Back })
            {
                var element = StudioPlacement.ElementRootOf(active.transform, root);
                if (element != null && StudioPlacement.CanEdit(element, sheet))
                    return element;
            }
            return null;
        }
    }
}
