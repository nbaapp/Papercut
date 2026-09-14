using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// The Sheet Studio: Aaron's level editor. Shows the open sheet's Front and Back side by side (Back in
    /// its authored Back-space, optionally mirrored for display), an element palette, per-face background-art
    /// slots, and creates new sheet variants into the open Desk scene's sheet set (see
    /// <see cref="StudioSheetOps.SheetFolderFor"/>). Editing happens in the ordinary Prefab Stage, so
    /// Unity's Undo, Inspector, and prefab saving (Ctrl+S / Auto Save) are the real machinery throughout — with
    /// one rule of our own so Auto Save never misses a gesture: see <see cref="StudioEdits"/>.
    /// </summary>
    public sealed class SheetStudioWindow : EditorWindow
    {
        const string PrefPrefix = "Papercut.SheetStudio.";
        const float ToolbarHeight = 24f;
        const float ArtRowHeight = 24f;
        const float PaletteHeight = 84f;
        const float PaneGap = 4f;
        const float PaneHeaderHeight = 18f;

        const float FoldListWidth = 240f;
        const float FaceRowFraction = 0.55f;
        static readonly GUIContent CollisionToggleContent = new("Collision",
            "This sheet draws its terrain collision as solid fills, in the game and here. Saved with the sheet (Ctrl+S / Auto Save) - for testing before the map art is drawn.");

        StudioPane frontPane;
        StudioPane backPane;
        StudioPalette palette;
        readonly StudioLinkState links = new();
        readonly StudioPolygonDraft draft = new();
        string studioMessage = string.Empty; // Face-pane status (draft progress, refusals); newest wins.
        StudioFoldModel foldModel;
        StudioFoldScene foldScene;
        StudioFoldPane foldPane;
        StudioFoldSettings? settingsCache;
        string foldMessage = string.Empty;
        string attachedStagePath;
        Vector2Int newSheetPosition;
        bool xrayHeld;

        // Picker (no-sheet view): the open Desk's set as a map. Slots are rebuilt at the top of every
        // OnGUI call; an operation ends its call with ExitGUI so nothing draws from stale slots.
        StudioSheetMap sheetMap;
        List<StudioSheetOps.SheetSlot> slots = new();
        readonly List<string> unmappedSheetPaths = new();
        string pickerMessage = string.Empty;
        MessageType pickerMessageType = MessageType.Info; // Warning for a refusal or failure, so it never reads as a success.
        Vector2Int moveToPosition;
        Desk pickerDesk;
        string pickerFolder;
        const float MaxMapHeight = 320f;

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

        /// <summary>Static terrain is bare collision drawn by the map art (Aaron, 2026-09-03); this view fills its boxes so it can be authored.</summary>
        static bool ShowTerrain
        {
            get => EditorPrefs.GetBool(PrefPrefix + "showTerrain", true);
            set => EditorPrefs.SetBool(PrefPrefix + "showTerrain", value);
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
            frontPane.Report = message => studioMessage = message;
            backPane.Report = message => studioMessage = message;
            palette = new StudioPalette();
            palette.Refresh();
            foldModel = new StudioFoldModel();
            foldModel.SetGhostSize(StudioFoldSettings.ReadPlayerFootprintSize());
            foldScene = new StudioFoldScene();
            foldPane = new StudioFoldPane();
            foldPane.Report = message => foldMessage = message; // One shared message slot: newest always wins.
            foldModel.Changed += OnFoldModelChanged;
            sheetMap = new StudioSheetMap();
            sheetMap.OpenRequested += OnMapOpenRequested;
            sheetMap.EmptyCellClicked += OnMapEmptyCellClicked;
            sheetMap.MoveRequested += OnMapMoveRequested;
            sheetMap.SelectionChanged += OnMapSelectionChanged;
            sheetMap.Changed += Repaint;
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
            if (foldModel != null)
                foldModel.Changed -= OnFoldModelChanged;
            EndDrags();
            if (sheetMap != null)
            {
                sheetMap.OpenRequested -= OnMapOpenRequested;
                sheetMap.EmptyCellClicked -= OnMapEmptyCellClicked;
                sheetMap.MoveRequested -= OnMapMoveRequested;
                sheetMap.SelectionChanged -= OnMapSelectionChanged;
                sheetMap.Changed -= Repaint;
            }
            foldScene?.Dispose();
            frontPane?.Dispose();
            backPane?.Dispose();
        }

        void OnFoldModelChanged()
        {
            foldScene?.MarkDirty();
            Repaint();
        }

        void OnLostFocus()
        {
            // A held Shift can't report its release here once focus is gone.
            xrayHeld = false;
            EndDrags(); // A MouseUp may never arrive now; a drag left open would hold hotControl and stall Auto Save.
            Repaint();
        }

        void OnStageChanged(PrefabStage stage)
        {
            EndDrags(); // The dragged object belongs to the stage that is going away.
            // The fold rig's cameras point at the (old) stage scene; rebuild lazily against the new one.
            foldScene?.Dispose();
            OnEditsChanged();
        }

        /// <summary>
        /// Ends every pane's drag: the face panes keep what was dragged so far (they commit live), the fold pane
        /// cancels as Escape would. Either way the pane's hotControl hold ends, so Prefab Mode's Auto Save resumes.
        /// </summary>
        void EndDrags()
        {
            frontPane?.EndDrag();
            backPane?.EndDrag();
            foldPane?.CancelDrag(foldModel);
        }

        // Property-only edits (e.g. a collider resized in the Inspector) fire neither hierarchyChanged nor a
        // repaint of an unfocused window, so overlays would go stale without the ObjectChangeEvents hook.
        void OnObjectsChanged(ref ObjectChangeEventStream stream) => OnEditsChanged();

        void OnEditsChanged()
        {
            frontPane?.InvalidateOverlayCache();
            backPane?.InvalidateOverlayCache();
            settingsCache = null; // Authored values (tints, minDepth, drag tuning) may have changed.
            foldScene?.MarkDirty();
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
                xrayHeld = false; // The release can be missed if the stage closes mid-hold.
                var e = Event.current;
                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && sheetMap.IsDragging)
                {
                    sheetMap.CancelDrag(); // HandleKeyboard runs only in the editing view; the map's cancel lives here.
                    e.Use();
                }
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
            // A draft follows the armed polygon kind (Wall ↔ Water mid-draw keeps the outline); disarming, arming
            // anything else, or link mode ends it — and says so, never silently (code review S3).
            if (draft.IsActive && (palette.Armed != draft.Prefab || links.Active))
            {
                if (!links.Active && StudioPlacement.IsPolygonTerrain(palette.Armed))
                    draft.Retarget(palette.Armed);
                else
                {
                    draft.Clear();
                    // Link mode shows its own hint; a message set now would surface stale after leaving it (code review S5).
                    studioMessage = links.Active ? string.Empty : "Polygon cancelled.";
                }
            }
            // The armed-placement ghost tracks the cursor in whichever pane it hovers; MouseMove events
            // don't repaint on their own, so hovering while armed must ask (one home for all three panes).
            if (Event.current.type == EventType.MouseMove && palette.Armed != null)
                Repaint();
            DrawToolbar(sheet);
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
            draft.Clear(); // A half-drawn outline belongs to the old stage's face.
            studioMessage = string.Empty;
            foldModel.Clear(); // Preview folds are transient editor state, per stage.
            foldModel.SetGhostEnabled(false);
            xrayHeld = false;
            foldMessage = string.Empty;
            settingsCache = null;
            foldScene.MarkDirty();
            var changed = StudioSheetOps.NormalizeFaceLayers(sheet);
            if (changed > 0)
                Debug.Log($"Sheet Studio: moved {changed} face-content object(s) of '{sheet.name}' onto the SheetFront/SheetBack layers (one-time; runtime forces the same layers at Awake).");
            frontPane.FrameSheet();
            backPane.FrameSheet();
            foldPane.FrameSheet();
            palette.Refresh();
        }

        // ----- No-sheet view -----

        void DrawSheetPicker(PrefabStage stage)
        {
            if (stage != null)
                EditorGUILayout.HelpBox("The open Prefab Stage is not a Sheet. Open one of the sheets below to edit it.", MessageType.Info);
            else
                EditorGUILayout.HelpBox("Open a sheet in Prefab Mode to edit it.", MessageType.Info);

            // The open Desk's own set comes first, as the sheet map (select, open, move, remove, delete);
            // every other set is listed under it read-only, so a sheet from the test Desk can still be
            // opened while the official Desk is loaded (and vice versa).
            var desk = StudioSheetOps.FindOpenDesk();
            var deskFolder = StudioSheetOps.SheetFolderFor(desk);
            pickerDesk = desk;
            pickerFolder = deskFolder;
            var sets = StudioSheetOps.FindSheetSets();
            if (deskFolder != null)
            {
                var index = sets.FindIndex(s => s.Folder == deskFolder);
                var ownName = index >= 0 ? sets[index].Name : Path.GetFileName(deskFolder);
                if (index >= 0)
                    sets.RemoveAt(index);
                DrawSheetMap(ownName, desk, deskFolder);
            }
            foreach (var set in sets)
                DrawSheetSet(set, deskFolder == null ? $"Sheets — {set.Name}" : $"Other set — {set.Name}");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("New Sheet", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(deskFolder == null))
            using (new EditorGUILayout.HorizontalScope())
            {
                newSheetPosition = EditorGUILayout.Vector2IntField(GUIContent.none, newSheetPosition, GUILayout.Width(140f));
                if (GUILayout.Button($"Create Sheet ({newSheetPosition.x},{newSheetPosition.y})"))
                    CreateSheet(newSheetPosition, desk, deskFolder);
            }
            if (desk == null)
                EditorGUILayout.LabelField("Open a Desk scene to create a sheet: the new sheet joins that scene's set and is placed on its Desk.", EditorStyles.miniLabel);
            else if (deskFolder == null)
                EditorGUILayout.LabelField("Save the Desk scene first: its sheet set is named after the scene.", EditorStyles.miniLabel);
        }

        void DrawSheetSet(StudioSheetOps.SheetSet set, string heading)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(heading, EditorStyles.boldLabel);
            if (set.Paths.Count == 0)
                EditorGUILayout.LabelField("(no sheets yet)", EditorStyles.miniLabel);
            foreach (var path in set.Paths)
            {
                if (GUILayout.Button(Path.GetFileNameWithoutExtension(path)))
                    PrefabStageUtility.OpenPrefab(path);
            }
        }

        // ----- Sheet map (open Desk's set) -----

        void DrawSheetMap(string setName, Desk desk, string folder)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Sheets — {setName} (open Desk)", EditorStyles.boldLabel);

            slots = StudioSheetOps.BuildSlots(folder, desk, unmappedSheetPaths);
            if (sheetMap.Selected.HasValue && SlotAt(sheetMap.Selected.Value) == null)
                sheetMap.Selected = null; // Deleted, or the scene changed under the picker.

            var occupied = new List<Vector2Int>(slots.Count);
            foreach (var slot in slots)
                if (!slot.IsEmpty)
                    occupied.Add(slot.GridPosition);
            var width = position.width - 8f; // EditorGUILayout's side margins.
            var height = Mathf.Min(StudioSheetMap.PreferredHeight(width, occupied), MaxMapHeight);
            var rect = GUILayoutUtility.GetRect(10f, float.MaxValue, height, height);
            sheetMap.Draw(rect, slots);

            DrawMapActions(desk, folder);
            if (!string.IsNullOrEmpty(pickerMessage))
                EditorGUILayout.HelpBox(pickerMessage, pickerMessageType);
            if (unmappedSheetPaths.Count > 0)
            {
                EditorGUILayout.LabelField("Not on the map — name is not 'Sheet (x,y)':", EditorStyles.miniLabel);
                foreach (var path in unmappedSheetPaths)
                {
                    if (GUILayout.Button(Path.GetFileNameWithoutExtension(path)))
                        PrefabStageUtility.OpenPrefab(path);
                }
            }
            EditorGUILayout.LabelField(
                "Click: select · double-click: open · drag onto a cell: move there (swap if occupied) · click an empty cell: "
                + "pick it for New Sheet. Blue: on the Desk · grey: variant not on the Desk · orange: needs attention. "
                + "Moves and deletes rename or delete files and cannot be undone; Remove from Desk can.",
                EditorStyles.wordWrappedMiniLabel);
        }

        StudioSheetOps.SheetSlot SlotAt(Vector2Int gridPosition) => slots.Find(s => s.GridPosition == gridPosition);

        void DrawMapActions(Desk desk, string folder)
        {
            var selected = sheetMap.Selected.HasValue ? SlotAt(sheetMap.Selected.Value) : null;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(selected != null ? $"Sheet ({selected.GridPosition.x},{selected.GridPosition.y})" : "No sheet selected", GUILayout.Width(110f));
                using (new EditorGUI.DisabledScope(selected == null || selected.AssetPath == null))
                {
                    if (GUILayout.Button("Open"))
                        OpenSheet(selected.AssetPath);
                }
                using (new EditorGUI.DisabledScope(selected == null || selected.Instance == null))
                {
                    if (GUILayout.Button("Remove from Desk"))
                        RemoveFromDesk(selected, desk);
                }
                // The way back for a grey (off-Desk) variant: the same path Create Sheet takes for an existing file.
                using (new EditorGUI.DisabledScope(selected == null || selected.AssetPath == null || selected.Instance != null))
                {
                    if (GUILayout.Button("Add to Desk"))
                        AddToDesk(selected, desk, folder);
                }
                using (new EditorGUI.DisabledScope(selected == null || !selected.Movable))
                {
                    if (GUILayout.Button("Delete…"))
                        DeleteSheet(selected, desk, folder);
                }
                GUILayout.FlexibleSpace();
                // Far moves (Aaron, 2026-09-09, post-review): a drag reaches one ring; this reaches anywhere.
                using (new EditorGUI.DisabledScope(selected == null || !selected.Movable))
                {
                    GUILayout.Label("Move to", GUILayout.Width(50f));
                    moveToPosition = EditorGUILayout.Vector2IntField(GUIContent.none, moveToPosition, GUILayout.Width(120f));
                    if (GUILayout.Button("Move", GUILayout.Width(50f)))
                        MoveSheet(selected.GridPosition, moveToPosition, desk, folder);
                }
            }
        }

        void OnMapOpenRequested(Vector2Int cell)
        {
            var slot = SlotAt(cell);
            if (slot?.AssetPath != null)
                OpenSheet(slot.AssetPath);
        }

        void OnMapEmptyCellClicked(Vector2Int cell) => newSheetPosition = cell;

        void OnMapMoveRequested(Vector2Int from, Vector2Int to) => MoveSheet(from, to, pickerDesk, pickerFolder);

        void OnMapSelectionChanged()
        {
            if (sheetMap.Selected.HasValue)
                moveToPosition = sheetMap.Selected.Value;
        }

        void OpenSheet(string path)
        {
            PrefabStageUtility.OpenPrefab(path);
            GUIUtility.ExitGUI(); // The stage changed under this OnGUI call; the rest of the picker is stale.
        }

        void MoveSheet(Vector2Int from, Vector2Int to, Desk desk, string folder)
        {
            var result = StudioSheetOps.MoveSheet(from, to, desk, folder, out var message);
            pickerMessage = message;
            pickerMessageType = MessageType.Info;
            switch (result)
            {
                case StudioSheetOps.MoveSheetResult.Moved:
                case StudioSheetOps.MoveSheetResult.Swapped:
                    Debug.Log($"Sheet Studio: {message} (not undoable)");
                    sheetMap.Selected = to;
                    break;
                case StudioSheetOps.MoveSheetResult.Refused:
                    pickerMessageType = MessageType.Warning;
                    break;
                case StudioSheetOps.MoveSheetResult.Failed:
                    pickerMessageType = MessageType.Warning;
                    Debug.LogError($"Sheet Studio: {message}");
                    break;
            }
            Repaint();
            GUIUtility.ExitGUI();
        }

        void AddToDesk(StudioSheetOps.SheetSlot slot, Desk desk, string folder)
        {
            var result = StudioSheetOps.CreateSheet(slot.GridPosition, desk, folder, out var message);
            pickerMessage = message;
            pickerMessageType = result == StudioSheetOps.CreateSheetResult.AddedExistingToDesk ? MessageType.Info : MessageType.Warning;
            if (result == StudioSheetOps.CreateSheetResult.AddedExistingToDesk)
                Debug.Log($"Sheet Studio: {message}");
            else
                Debug.LogError($"Sheet Studio: {message}");
            Repaint();
            GUIUtility.ExitGUI();
        }

        void RemoveFromDesk(StudioSheetOps.SheetSlot slot, Desk desk)
        {
            var p = slot.GridPosition;
            if (StudioSheetOps.IsPlayerOnSheet(StudioSheetOps.FindPlayerOnDesk(desk), slot.Instance)
                && !EditorUtility.DisplayDialog("Sheet Studio",
                    $"The player stands on Sheet ({p.x},{p.y}). Removing it leaves the player on empty desk, and Play Mode "
                    + "will fail to pick a starting Screen until the player is moved.\n\nRemove anyway?",
                    "Remove", "Cancel"))
            {
                GUIUtility.ExitGUI();
                return;
            }
            StudioSheetOps.RemoveSheetFromDesk(slot.Instance, out var message);
            pickerMessage = message;
            pickerMessageType = MessageType.Info;
            Debug.Log($"Sheet Studio: {message}");
            Repaint();
            GUIUtility.ExitGUI();
        }

        void DeleteSheet(StudioSheetOps.SheetSlot slot, Desk desk, string folder)
        {
            var p = slot.GridPosition;
            var text = $"Delete Sheet ({p.x},{p.y})?\n\nThis deletes '{slot.AssetPath}' from the project"
                + (slot.Instance != null ? " and removes it from the Desk" : "")
                + ". Only the variant file is deleted — its art sprites and the element prefabs it uses stay in the project."
                + "\n\nThis cannot be undone, and the editor's Undo history is cleared so an older undo cannot bring back a sheet whose file is gone.";
            if (StudioSheetOps.IsPlayerOnSheet(StudioSheetOps.FindPlayerOnDesk(desk), slot.Instance))
                text += "\n\nThe player stands on this sheet and will be left on empty desk; Play Mode will fail to pick a starting Screen until the player is moved.";
            if (!EditorUtility.DisplayDialog("Sheet Studio", text, "Delete", "Cancel"))
            {
                GUIUtility.ExitGUI();
                return;
            }
            var result = StudioSheetOps.DeleteSheet(p, desk, folder, out var message);
            pickerMessage = message;
            if (result is StudioSheetOps.DeleteSheetResult.Deleted or StudioSheetOps.DeleteSheetResult.DeletedAssetOnly)
            {
                pickerMessageType = MessageType.Info;
                Debug.Log($"Sheet Studio: {message}");
                sheetMap.Selected = null;
            }
            else
            {
                pickerMessageType = MessageType.Warning;
                Debug.LogError($"Sheet Studio: {message}");
            }
            Repaint();
            GUIUtility.ExitGUI();
        }

        void CreateSheet(Vector2Int gridPosition, Desk desk, string folder)
        {
            var result = StudioSheetOps.CreateSheet(gridPosition, desk, folder, out var message);
            switch (result)
            {
                case StudioSheetOps.CreateSheetResult.Created:
                case StudioSheetOps.CreateSheetResult.CreatedWithoutDesk:
                case StudioSheetOps.CreateSheetResult.AddedExistingToDesk:
                    Debug.Log($"Sheet Studio: {message}");
                    PrefabStageUtility.OpenPrefab(StudioSheetOps.SheetAssetPath(gridPosition, folder));
                    break;
                default:
                    EditorUtility.DisplayDialog("Sheet Studio", message, "OK");
                    break;
            }
        }

        // ----- Editing view -----

        void DrawToolbar(Sheet sheet)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(ToolbarHeight)))
            {
                GUILayout.Label("Snap", GUILayout.Width(34f));
                SnapIncrement = EditorGUILayout.FloatField(SnapIncrement, GUILayout.Width(44f));
                ShowColliderOverlays = GUILayout.Toggle(ShowColliderOverlays, "Overlays", EditorStyles.toolbarButton, GUILayout.Width(64f));
                ShowLabels = GUILayout.Toggle(ShowLabels, "Labels", EditorStyles.toolbarButton, GUILayout.Width(54f));
                ShowTerrain = GUILayout.Toggle(ShowTerrain, "Terrain", EditorStyles.toolbarButton, GUILayout.Width(58f));
                // Unlike the view toggles above (editor prefs), this one is authored: it is the open sheet's own
                // Show Collision flag, saved with the variant and honoured by the game.
                var showCollision = sheet.ShowCollision;
                var wantCollision = GUILayout.Toggle(showCollision, CollisionToggleContent, EditorStyles.toolbarButton, GUILayout.Width(64f));
                if (wantCollision != showCollision)
                    StudioSheetOps.SetShowCollision(sheet, wantCollision);
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
                    foldPane.FrameSheet();
                }
                GUILayout.FlexibleSpace();
                string hint;
                if (links.Active)
                    hint = "Link mode: click a plate, then click what it affects (face panes only) — right-click a plate to clear its links";
                else if (StudioPlacement.IsPolygonTerrain(palette.Armed))
                    hint = string.IsNullOrEmpty(studioMessage) ? StudioPane.DrawHint : $"{studioMessage}  ·  {StudioPane.DrawHint}";
                else if (!string.IsNullOrEmpty(studioMessage))
                    hint = studioMessage;
                else
                    hint = "Snap 0 = free placement · hold Shift: see through the Sheet · polygon region: drag a vertex, drag an edge midpoint to add one, right-click a vertex to remove it";
                GUILayout.Label(hint, EditorStyles.miniLabel);
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
            var totalHeight = position.height - top - PaletteHeight - PaneGap;
            var width = (position.width - PaneGap) * 0.5f;
            if (totalHeight <= 60f || width <= 20f)
                return;

            // Second-row layout (Aaron, 2026-08-31): faces on top, the fold preview in a wide row below.
            var faceHeight = totalHeight * FaceRowFraction - PaneHeaderHeight;
            var foldTop = top + faceHeight + PaneGap + PaneHeaderHeight;
            var foldHeight = position.height - foldTop - PaletteHeight - PaneGap;

            var frontRect = new Rect(0f, top, width, faceHeight);
            var backRect = new Rect(width + PaneGap, top, width, faceHeight);
            GUI.Label(new Rect(frontRect.x, top - PaneHeaderHeight, width, PaneHeaderHeight), "Front", EditorStyles.boldLabel);
            GUI.Label(new Rect(backRect.x, top - PaneHeaderHeight, width, PaneHeaderHeight),
                MirrorBackPane ? "Back (mirrored display — aligned with Front)" : "Back (authored Back-space)", EditorStyles.boldLabel);

            frontPane.Draw(frontRect, ContextFor(stage, sheet, SheetFace.Front));
            backPane.Draw(backRect, ContextFor(stage, sheet, SheetFace.Back));

            if (foldHeight <= 40f)
                return;
            var foldRect = new Rect(0f, foldTop, position.width - FoldListWidth - PaneGap, foldHeight);
            var listRect = new Rect(foldRect.xMax + PaneGap, foldTop, FoldListWidth, foldHeight);
            GUI.Label(new Rect(0f, foldTop - PaneHeaderHeight, foldRect.width, PaneHeaderHeight),
                "Fold preview (editor-only — folds are never saved)", EditorStyles.boldLabel);

            settingsCache ??= StudioFoldSettings.Read(sheet);
            foldModel.RememberCreasesEnabled = settingsCache.Value.RememberCreases;
            foldPane.Draw(foldRect, new StudioFoldPane.Context
            {
                Stage = stage,
                Model = foldModel,
                Scene = foldScene,
                Settings = settingsCache.Value,
                Sheet = sheet,
                Palette = palette,
                SnapIncrement = SnapIncrement,
                LinksActive = links.Active,
            });
            DrawFoldList(listRect);
        }

        void DrawFoldList(Rect rect)
        {
            GUILayout.BeginArea(rect);
            EditorGUILayout.LabelField("Folds", EditorStyles.boldLabel);
            var minDepth = settingsCache?.MinDepth ?? 0.25f;
            for (int i = 0; i < foldModel.Folds.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var fold = foldModel.Folds[i];
                    var pinned = foldModel.IsPinned(i);
                    GUILayout.Label($"{i + 1}. {fold.Anchor}{(pinned ? " (pinned)" : "")}", GUILayout.Width(130f));
                    var newDepth = EditorGUILayout.DelayedFloatField(fold.Depth, GUILayout.Width(50f));
                    if (!Mathf.Approximately(newDepth, fold.Depth))
                    {
                        // A success clears any stale refusal (and reports a ghost relocation); a refusal explains itself.
                        foldMessage = foldModel.TrySetDepth(i, newDepth, minDepth, out var depthReason)
                            ? (foldModel.GhostWasRelocated ? "Ghost moved to stay on the Sheet." : string.Empty)
                            : depthReason;
                    }
                    if (GUILayout.Button("✕", GUILayout.Width(22f)))
                    {
                        foldMessage = foldModel.TryRemoveAt(i, out var removeReason)
                            ? (foldModel.GhostWasRelocated ? "Ghost moved to stay on the Sheet." : string.Empty)
                            : removeReason;
                        if (removeReason == null)
                            break; // The list changed under this loop; redraw next frame.
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Unfold all"))
                {
                    foldModel.Clear();
                    foldMessage = string.Empty;
                }
                var ghostOn = GUILayout.Toggle(foldModel.Ghost.HasValue, "Test player", GUI.skin.button);
                if (ghostOn != foldModel.Ghost.HasValue)
                    foldModel.SetGhostEnabled(ghostOn);
            }
            if (!string.IsNullOrEmpty(foldMessage))
                EditorGUILayout.HelpBox(foldMessage, MessageType.Info);
            if (!string.IsNullOrEmpty(settingsCache?.MissingNote))
                EditorGUILayout.HelpBox(settingsCache.Value.MissingNote, MessageType.Warning);
            GUILayout.FlexibleSpace();
            var note = foldModel.Ghost.HasValue
                ? "Player rule live via the test ghost."
                : "Player rule OFF (no test ghost).";
            EditorGUILayout.LabelField(
                note + " Preview ignores the multiple-fold/stacking toggles. Emptied regions show an editor "
                + "background, not the Desk. Objects don't move or clip in preview — that's Play Mode's job. "
                + "Fold gestures win clicks: elements near edges, corners, creases or Seams are selected via "
                + "the face panes. Link mode wires in the face panes only.",
                EditorStyles.wordWrappedMiniLabel);
            GUILayout.EndArea();
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
            ShowTerrain = ShowTerrain,
            MirrorX = face == SheetFace.Back && MirrorBackPane,
            XRayHeld = xrayHeld,
            Draft = draft,
        };

        // ----- Keyboard -----

        void HandleKeyboard(Sheet sheet)
        {
            var e = Event.current;

            // X-ray follows the Shift modifier (Aaron, 2026-09-03: Tab fought the Inspector's focus
            // cycling). Read from the event's modifier flag on every key/mouse event rather than
            // Shift's own KeyDown/KeyUp — pure-modifier key events are not delivered reliably, and a
            // modifier never needs consuming. Release clears UNCONDITIONALLY; the hold is ignored while
            // a text field is edited so typing capitals in the Snap value doesn't flash the overlay
            // (same guard the Tab version had, plan round-1 N1).
            if (e.isKey || e.isMouse)
            {
                var shiftHeld = e.shift && !EditorGUIUtility.editingTextField;
                if (shiftHeld != xrayHeld)
                {
                    xrayHeld = shiftHeld;
                    Repaint();
                }
            }
            if (e.type != EventType.KeyDown)
                return;

            // While a text field (e.g. the Snap value) is being edited, keystrokes belong to it —
            // Backspace must delete a character, never the selected element.
            if (EditorGUIUtility.editingTextField)
                return;

            if (e.keyCode == KeyCode.Escape)
            {
                // An active fold/ghost drag claims Escape before anything else (plan round-2 B1) —
                // the cancel half of the game's input scheme must reach the fold pane.
                if (foldPane != null && foldPane.IsDragging)
                {
                    foldPane.CancelDrag(foldModel);
                    e.Use();
                    return;
                }
                // First Escape disarms the link source, the second leaves link mode; outside link mode a
                // half-drawn polygon is cancelled first (still armed), then the palette disarms.
                if (links.Active)
                {
                    if (links.Source != null)
                        links.Source = null;
                    else
                        links.Exit();
                }
                else if (draft.IsActive)
                {
                    draft.Clear();
                    studioMessage = "Polygon cancelled.";
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
                foldPane.FrameSheet();
                e.Use();
                return;
            }

            // Polygon drawing owns Enter and Backspace while polygon terrain is armed (plan review N1):
            // Backspace is the draft's key even with an empty draft, so the reflex Backspace right after a
            // finish never deletes the region just drawn. Delete still deletes the selected element.
            if (StudioPlacement.IsPolygonTerrain(palette.Armed))
            {
                switch (e.keyCode)
                {
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        if (draft.IsActive)
                            (draft.Face == SheetFace.Front ? frontPane : backPane).FinishDraft(ContextFor(PrefabStageUtility.GetCurrentPrefabStage(), sheet, draft.Face));
                        e.Use();
                        return;
                    case KeyCode.Backspace:
                        if (draft.RemoveLast())
                            studioMessage = draft.IsActive ? $"{draft.Points.Count} point(s)." : string.Empty;
                        e.Use();
                        return;
                }
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
