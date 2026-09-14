using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// Sheet-level Studio operations: enumerating and creating sheet prefab variants, adding them to the
    /// open Desk scene, normalizing face-content layers, and the per-face background-art slot.
    /// </summary>
    /// <remarks>
    /// All methods are dialog-free so tests can drive them; <see cref="SheetStudioWindow"/> wraps them in UI.
    /// Each Desk scene owns a <b>sheet set</b>: the subfolder of <see cref="SheetRoot"/> named after the scene
    /// (Aaron, 2026-09-09: the test Desk and the official Desk are separate scenes, so their sheets — both
    /// named by grid position — must live apart). <c>Test Desk.unity</c> → <c>Sheets/Test Desk/</c>.
    /// </remarks>
    public static class StudioSheetOps
    {
        public const string BaseSheetPrefabPath = "Assets/Papercut/Prefabs/Sheet.prefab";

        /// <summary>Folder holding one sheet-set subfolder per Desk scene.</summary>
        public const string SheetRoot = "Assets/Papercut/Sheets";

        /// <summary>
        /// Face-content convention (see the authored variants): the transparent Surface quad sits at z 0 on
        /// the Front (+0.01 on the Back), background art at −0.01, elements at −0.05, creases at −0.1.
        /// Art must sort by z at sortingOrder 0 or it vanishes behind the Surface.
        /// </summary>
        public const float ArtZ = -0.01f;
        const float ArtZEpsilon = 0.001f;
        const string ArtObjectName = "Art";
        const string SpriteUnlitMaterialGuid = "9dfc825aed78fcd4ba02077103263b40"; // URP Sprite-Unlit-Default, as used by all authored face content.

        // ----- Sheet assets -----

        /// <summary>One Desk scene's sheets: the set's display name (its scene name) and its variant paths.</summary>
        public readonly struct SheetSet
        {
            public readonly string Name;
            public readonly string Folder;
            public readonly List<string> Paths;

            public SheetSet(string name, string folder, List<string> paths)
            {
                Name = name;
                Folder = folder;
                Paths = paths;
            }
        }

        /// <summary>
        /// The sheet-set folder for a scene file: <see cref="SheetRoot"/>/&lt;scene name&gt;. Null for an
        /// unsaved scene (empty path), which has no name to derive a folder from.
        /// </summary>
        public static string SheetFolderForScene(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
                return null;
            return $"{SheetRoot}/{Path.GetFileNameWithoutExtension(scenePath)}";
        }

        /// <summary>The sheet-set folder of the scene <paramref name="desk"/> lives in; null if no Desk or the scene is unsaved.</summary>
        public static string SheetFolderFor(Desk desk)
            => desk == null ? null : SheetFolderForScene(desk.gameObject.scene.path);

        /// <summary>Paths of every sheet prefab variant directly in <paramref name="folder"/>, sorted by name.</summary>
        public static List<string> FindSheetAssets(string folder)
        {
            var paths = new List<string>();
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                return paths;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path).Replace('\\', '/') == folder) // FindAssets recurses; sets don't nest.
                    paths.Add(path);
            }
            paths.Sort();
            return paths;
        }

        /// <summary>Every sheet set under <see cref="SheetRoot"/> (one subfolder each, including empty ones), sorted by name.</summary>
        public static List<SheetSet> FindSheetSets()
        {
            var sets = new List<SheetSet>();
            if (!AssetDatabase.IsValidFolder(SheetRoot))
                return sets;
            var folders = AssetDatabase.GetSubFolders(SheetRoot);
            System.Array.Sort(folders, string.CompareOrdinal);
            foreach (var folder in folders)
                sets.Add(new SheetSet(Path.GetFileName(folder), folder, FindSheetAssets(folder)));
            return sets;
        }

        public static string SheetAssetPath(Vector2Int gridPosition, string folder)
            => $"{folder}/Sheet ({gridPosition.x},{gridPosition.y}).prefab";

        /// <summary>
        /// Makes sure a sheet-set folder exists, creating it (one level) under its parent when it doesn't.
        /// A new Desk scene has no set until its first sheet; that must not be a failure.
        /// </summary>
        public static bool EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
                return false;
            if (AssetDatabase.IsValidFolder(folder))
                return true;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) || !AssetDatabase.IsValidFolder(parent))
                return false;
            return !string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(folder)));
        }

        /// <summary>
        /// The Desk in any open scene, found without <c>Desk.TryGetSheet</c>'s registry (which is built once
        /// and never invalidated in edit mode). Null when no Desk scene is open.
        /// </summary>
        public static Desk FindOpenDesk() => Object.FindAnyObjectByType<Desk>();

        /// <summary>The sheet at a grid position under the open Desk, enumerating children directly.</summary>
        public static Sheet FindSheetOnDesk(Desk desk, Vector2Int gridPosition)
        {
            if (desk == null)
                return null;
            foreach (var sheet in desk.GetComponentsInChildren<Sheet>(true))
            {
                if (sheet.GridPosition == gridPosition)
                    return sheet;
            }
            return null;
        }

        public enum CreateSheetResult
        {
            Created,
            AddedExistingToDesk,
            CreatedWithoutDesk,
            RefusedPositionOccupied,
            Failed,
        }

        /// <summary>
        /// Creates the sheet variant for a grid position and, when a Desk scene is open, adds an instance to
        /// its SheetGrid (Aaron, 2026-08-31: "Create + add to Desk"). If the variant already exists but is
        /// not on the Desk, the existing variant is added instead of refusing — that recovers cleanly from an
        /// undone add, since the asset save itself is not undoable. <paramref name="folder"/> is the sheet set
        /// to create into (normally <see cref="SheetFolderFor"/> the Desk); it is created if missing.
        /// </summary>
        public static CreateSheetResult CreateSheet(Vector2Int gridPosition, Desk desk, string folder, out string message)
        {
            if (string.IsNullOrEmpty(folder))
            {
                message = "No sheet folder: the Desk scene must be saved so its sheet set has a name.";
                return CreateSheetResult.Failed;
            }
            if (FindSheetOnDesk(desk, gridPosition) != null)
            {
                message = $"The Desk already has a sheet at {gridPosition}.";
                return CreateSheetResult.RefusedPositionOccupied;
            }

            var path = SheetAssetPath(gridPosition, folder);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                if (desk == null)
                {
                    message = $"'{path}' already exists and no Desk scene is open; nothing to do.";
                    return CreateSheetResult.Failed;
                }
                if (!AddSheetToDesk(existing, desk, gridPosition, out message))
                    return CreateSheetResult.Failed;
                message = $"Added the existing variant '{path}' to the Desk at {gridPosition}.";
                return CreateSheetResult.AddedExistingToDesk;
            }

            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaseSheetPrefabPath);
            if (basePrefab == null)
            {
                message = $"Base Sheet prefab not found at '{BaseSheetPrefabPath}'.";
                return CreateSheetResult.Failed;
            }
            if (!EnsureFolder(folder))
            {
                message = $"Sheet folder '{folder}' does not exist and could not be created (its parent must exist).";
                return CreateSheetResult.Failed;
            }

            // The temp instance goes into a throwaway preview scene: instantiating into the active scene
            // would leave its dirty flag set for no actual change, giving Aaron a spurious save prompt.
            var previewScene = EditorSceneManager.NewPreviewScene();
            GameObject variant;
            bool saved;
            try
            {
                var temp = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab, previewScene);
                temp.name = Path.GetFileNameWithoutExtension(path);
                variant = PrefabUtility.SaveAsPrefabAsset(temp, path, out saved);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
            }
            if (!saved || variant == null)
            {
                message = $"Could not save the new sheet variant to '{path}'.";
                return CreateSheetResult.Failed;
            }

            if (desk == null)
            {
                message = $"Created '{path}'. No Desk scene is open, so it was not placed on the Desk.";
                return CreateSheetResult.CreatedWithoutDesk;
            }
            if (!AddSheetToDesk(variant, desk, gridPosition, out message))
                return CreateSheetResult.Failed;
            message = $"Created '{path}' and placed it on the Desk at {gridPosition}.";
            return CreateSheetResult.Created;
        }

        static bool AddSheetToDesk(GameObject variant, Desk desk, Vector2Int gridPosition, out string message)
        {
            if (desk.SheetGrid == null)
            {
                message = "The open Desk has no SheetGrid assigned.";
                return false;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(variant, desk.SheetGrid);
            var sheet = instance.GetComponent<Sheet>();
            if (sheet == null)
            {
                Object.DestroyImmediate(instance);
                message = $"'{variant.name}' has no Sheet component; not adding it to the Desk.";
                return false;
            }

            var serialized = new SerializedObject(sheet);
            serialized.FindProperty("gridPosition").vector2IntValue = gridPosition;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            instance.transform.localPosition = desk.GridToLocal(gridPosition);
            Undo.RegisterCreatedObjectUndo(instance, $"Add {variant.name} to Desk");
            EditorSceneManager.MarkSceneDirty(instance.scene);
            message = string.Empty;
            return true;
        }

        // ----- Sheet map: slots, move, remove, delete -----

        // Canonical integers only (no leading zeros, no "-0"): exactly the names SheetAssetPath produces, so
        // no two distinct file names can parse to one position and shadow each other on the map.
        static readonly Regex SheetNamePattern = new(@"^Sheet \((0|-?[1-9]\d*),(0|-?[1-9]\d*)\)$");

        /// <summary>
        /// Inverse of <see cref="SheetAssetPath"/>: the grid position a variant's file name records
        /// (<c>Sheet (x,y).prefab</c> with canonical integers; only the file name matters). False for any
        /// other name — such a file is listed under the map as "not on the map" rather than hidden.
        /// </summary>
        public static bool TryParseGridPosition(string assetPath, out Vector2Int gridPosition)
        {
            gridPosition = default;
            if (string.IsNullOrEmpty(assetPath))
                return false;
            var match = SheetNamePattern.Match(Path.GetFileNameWithoutExtension(assetPath));
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var x) || !int.TryParse(match.Groups[2].Value, out var y))
                return false;
            gridPosition = new Vector2Int(x, y);
            return true;
        }

        static string Fmt(Vector2Int p) => $"({p.x},{p.y})";

        /// <summary>
        /// One grid position of a sheet set: the set's variant named for it (by file name — the set's record
        /// of position) and the open Desk's sheet there (by <see cref="Sheet.GridPosition"/> — the Desk's
        /// record). Disagreement between the two is surfaced, never papered over.
        /// </summary>
        public sealed class SheetSlot
        {
            public Vector2Int GridPosition;

            /// <summary>The set's variant named for this position, or null.</summary>
            public string AssetPath;

            /// <summary>The Desk's sheet at this position (the first found when several claim it), or null.</summary>
            public Sheet Instance;

            /// <summary>The prefab asset <see cref="Instance"/> is an instance of, or null (no instance, or not a prefab).</summary>
            public string InstanceAssetPath;

            /// <summary>How many Desk sheets claim this position; more than one is authored ambiguity.</summary>
            public int InstanceCount;

            /// <summary>The Desk's sheet here is not an instance of the set's variant for this position.</summary>
            public bool Mismatch => Instance != null && InstanceAssetPath != AssetPath;

            public bool Duplicate => InstanceCount > 1;

            /// <summary>The one rule for the drag affordance, <see cref="MoveSheet"/> and <see cref="DeleteSheet"/>.</summary>
            public bool Movable => AssetPath != null && !Mismatch && !Duplicate;

            public bool IsEmpty => AssetPath == null && Instance == null;

            /// <summary>Anything the map should draw as a warning: a mismatch, a duplicate, or an instance with no variant in the set.</summary>
            public bool HasWarning => Mismatch || Duplicate || (Instance != null && AssetPath == null);
        }

        /// <summary>
        /// The set's slots: one per position that has a parseable variant in <paramref name="folder"/> or a
        /// Sheet under <paramref name="desk"/> (null = assets only), sorted by position. Variants whose name
        /// does not parse go to <paramref name="unmappedAssetPaths"/> (may be null).
        /// </summary>
        public static List<SheetSlot> BuildSlots(string folder, Desk desk, List<string> unmappedAssetPaths)
        {
            var byPosition = new Dictionary<Vector2Int, SheetSlot>();
            unmappedAssetPaths?.Clear();
            foreach (var path in FindSheetAssets(folder))
            {
                if (TryParseGridPosition(path, out var position))
                    SlotAt(byPosition, position).AssetPath = path;
                else
                    unmappedAssetPaths?.Add(path);
            }
            if (desk != null)
            {
                foreach (var sheet in desk.GetComponentsInChildren<Sheet>(true))
                {
                    var slot = SlotAt(byPosition, sheet.GridPosition);
                    slot.InstanceCount++;
                    if (slot.Instance != null)
                        continue;
                    slot.Instance = sheet;
                    var instancePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sheet.gameObject);
                    slot.InstanceAssetPath = string.IsNullOrEmpty(instancePath) ? null : instancePath;
                }
            }
            var slots = new List<SheetSlot>(byPosition.Values);
            slots.Sort((a, b) => a.GridPosition.y != b.GridPosition.y
                ? a.GridPosition.y.CompareTo(b.GridPosition.y)
                : a.GridPosition.x.CompareTo(b.GridPosition.x));
            return slots;
        }

        static SheetSlot SlotAt(Dictionary<Vector2Int, SheetSlot> byPosition, Vector2Int position)
        {
            if (!byPosition.TryGetValue(position, out var slot))
            {
                slot = new SheetSlot { GridPosition = position };
                byPosition.Add(position, slot);
            }
            return slot;
        }

        static string ImmovableReason(SheetSlot slot)
        {
            if (slot.Duplicate)
                return $"{slot.InstanceCount} sheets on the Desk claim {Fmt(slot.GridPosition)}; remove the extra ones first.";
            if (slot.Mismatch)
                return $"the Desk's sheet there is an instance of '{slot.InstanceAssetPath ?? "(not a prefab)"}', not of '{slot.AssetPath ?? "(no variant in this set)"}'.";
            if (slot.AssetPath == null)
                return "it has no variant in this set (only a Desk instance).";
            return string.Empty;
        }

        /// <summary>The player under the Desk (a child of the SheetGrid, per Bible §7), or null.</summary>
        public static PlayerMover FindPlayerOnDesk(Desk desk)
            => desk == null ? null : desk.GetComponentInChildren<PlayerMover>(true);

        /// <summary>
        /// True if the player stands inside the sheet's Base. Uses the transform, not
        /// <see cref="PlayerMover.Position"/> (the Rigidbody2D is not simulated in edit mode).
        /// </summary>
        public static bool IsPlayerOnSheet(PlayerMover player, Sheet sheet)
            => player != null && sheet != null && sheet.Bounds.Contains(player.transform.position);

        public enum MoveSheetResult
        {
            Moved,
            Swapped,
            NoChange,
            Refused,
            Failed,
        }

        /// <summary>
        /// Moves the sheet at <paramref name="from"/> to <paramref name="to"/>, swapping with the sheet
        /// already there if any (Aaron, 2026-09-09: "Swap"). The variant is renamed on disk (through a
        /// temporary name for a swap, with best-effort rollback) and the Desk instance's grid position,
        /// name and layout are updated. Not undoable — asset renames cannot be undone, and an undone half
        /// would desynchronise the file name from the instance. The player is never moved (Aaron: "Stay at
        /// the cell"); the message warns when it is left on empty desk.
        /// </summary>
        public static MoveSheetResult MoveSheet(Vector2Int from, Vector2Int to, Desk desk, string folder, out string message)
        {
            if (from == to)
            {
                message = $"Sheet {Fmt(from)} is already there.";
                return MoveSheetResult.NoChange;
            }
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                message = $"No sheet set folder '{folder}'.";
                return MoveSheetResult.Failed;
            }

            var slots = BuildSlots(folder, desk, null);
            var source = slots.Find(s => s.GridPosition == from);
            var target = slots.Find(s => s.GridPosition == to);
            if (source == null || source.IsEmpty)
            {
                message = $"There is no sheet at {Fmt(from)}.";
                return MoveSheetResult.Refused;
            }
            if (!source.Movable)
            {
                message = $"Sheet {Fmt(from)} cannot be moved: {ImmovableReason(source)}";
                return MoveSheetResult.Refused;
            }
            var swap = target != null && !target.IsEmpty;
            if (swap && !target.Movable)
            {
                message = $"Cannot swap with the sheet at {Fmt(to)}: {ImmovableReason(target)}";
                return MoveSheetResult.Refused;
            }

            var pathFrom = source.AssetPath;
            var pathTo = SheetAssetPath(to, folder);
            if (swap)
            {
                if (!SwapAssets(pathFrom, pathTo, folder, from, out message))
                    return MoveSheetResult.Failed;
            }
            else
            {
                var error = AssetDatabase.MoveAsset(pathFrom, pathTo);
                if (!string.IsNullOrEmpty(error))
                {
                    message = $"Could not rename '{pathFrom}' to '{pathTo}': {error}";
                    return MoveSheetResult.Failed;
                }
            }

            // The player never moves; warn if its sheet leaves and nothing arrives in its place. In a swap
            // that happens too when the other variant is off the Desk (no instance to swap in).
            var player = FindPlayerOnDesk(desk);
            Vector2Int? emptiedUnderPlayer = null;
            if (IsPlayerOnSheet(player, source.Instance) && (!swap || target.Instance == null))
                emptiedUnderPlayer = from;
            else if (swap && IsPlayerOnSheet(player, target.Instance) && source.Instance == null)
                emptiedUnderPlayer = to;

            PlaceInstance(source.Instance, to, pathTo);
            if (swap)
                PlaceInstance(target.Instance, from, pathFrom);
            if (desk != null)
            {
                desk.LayoutSheets(); // The same re-layout Desk.OnValidate does; the Studio never writes positions itself.
                EditorSceneManager.MarkSceneDirty(desk.gameObject.scene);
            }

            message = swap
                ? $"Swapped Sheet {Fmt(from)} and Sheet {Fmt(to)}."
                : $"Moved Sheet {Fmt(from)} to {Fmt(to)}.";
            if (emptiedUnderPlayer.HasValue)
                message += $" The player now stands on empty desk at {Fmt(emptiedUnderPlayer.Value)}; Play Mode cannot pick a starting Screen until it is moved.";
            return swap ? MoveSheetResult.Swapped : MoveSheetResult.Moved;
        }

        static bool SwapAssets(string pathA, string pathB, string folder, Vector2Int a, out string message)
        {
            var temp = AssetDatabase.GenerateUniqueAssetPath($"{folder}/Sheet ({a.x},{a.y}) swapping.prefab");
            var steps = new (string from, string to)[] { (pathA, temp), (pathB, pathA), (temp, pathB) };
            for (int i = 0; i < steps.Length; i++)
            {
                var error = AssetDatabase.MoveAsset(steps[i].from, steps[i].to);
                if (string.IsNullOrEmpty(error))
                    continue;
                message = $"Could not rename '{steps[i].from}' to '{steps[i].to}': {error}";
                for (int j = i - 1; j >= 0; j--)
                {
                    var rollbackError = AssetDatabase.MoveAsset(steps[j].to, steps[j].from);
                    if (!string.IsNullOrEmpty(rollbackError))
                        message += $" Rolling back '{steps[j].to}' to '{steps[j].from}' also failed: {rollbackError}";
                }
                return false;
            }
            message = string.Empty;
            return true;
        }

        static void PlaceInstance(Sheet sheet, Vector2Int gridPosition, string assetPath)
        {
            if (sheet == null)
                return;
            var serialized = new SerializedObject(sheet);
            serialized.FindProperty("gridPosition").vector2IntValue = gridPosition;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            sheet.gameObject.name = Path.GetFileNameWithoutExtension(assetPath);
            EditorUtility.SetDirty(sheet.gameObject);
        }

        /// <summary>
        /// Removes a sheet from the Desk, keeping its variant in the set (Create Sheet at that position puts
        /// it back). Undoable — the counterpart of the undoable add in <see cref="CreateSheet"/>.
        /// </summary>
        public static bool RemoveSheetFromDesk(Sheet sheet, out string message)
        {
            if (sheet == null)
            {
                message = "No sheet to remove.";
                return false;
            }
            var position = sheet.GridPosition;
            var scene = sheet.gameObject.scene;
            Undo.DestroyObjectImmediate(sheet.gameObject);
            if (scene.IsValid())
                EditorSceneManager.MarkSceneDirty(scene);
            message = $"Removed Sheet {Fmt(position)} from the Desk; its variant is still in the set — Create Sheet {Fmt(position)} puts it back.";
            return true;
        }

        public enum DeleteSheetResult
        {
            Deleted,
            DeletedAssetOnly,
            Refused,
            Failed,
        }

        /// <summary>
        /// Deletes the set's variant for a grid position and its Desk instance. Not undoable: the instance is
        /// destroyed without Undo and the Undo history is cleared afterwards, because any surviving entry
        /// (this sheet's Create, an earlier Remove) could bring back an instance of a file that no longer
        /// exists — a known-broken object. Only the variant file goes; art and element prefabs stay.
        /// </summary>
        public static DeleteSheetResult DeleteSheet(Vector2Int gridPosition, Desk desk, string folder, out string message)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                message = $"No sheet set folder '{folder}'.";
                return DeleteSheetResult.Failed;
            }
            var slot = BuildSlots(folder, desk, null).Find(s => s.GridPosition == gridPosition);
            if (slot == null || slot.AssetPath == null)
            {
                message = $"There is no variant for {Fmt(gridPosition)} in '{folder}'.";
                return DeleteSheetResult.Refused;
            }
            if (!slot.Movable)
            {
                message = $"Sheet {Fmt(gridPosition)} cannot be deleted: {ImmovableReason(slot)}";
                return DeleteSheetResult.Refused;
            }

            var path = slot.AssetPath;
            var hadInstance = slot.Instance != null;
            if (hadInstance)
            {
                var scene = slot.Instance.gameObject.scene;
                Object.DestroyImmediate(slot.Instance.gameObject);
                if (scene.IsValid())
                    EditorSceneManager.MarkSceneDirty(scene);
            }
            if (!AssetDatabase.DeleteAsset(path))
            {
                message = hadInstance
                    ? $"Removed Sheet {Fmt(gridPosition)} from the Desk, but '{path}' could not be deleted; Create Sheet {Fmt(gridPosition)} puts it back on the Desk."
                    : $"'{path}' could not be deleted.";
                return DeleteSheetResult.Failed;
            }
            Undo.ClearAll();
            message = hadInstance
                ? $"Deleted '{path}' and removed Sheet {Fmt(gridPosition)} from the Desk (Undo history cleared)."
                : $"Deleted '{path}' (Undo history cleared).";
            return hadInstance ? DeleteSheetResult.Deleted : DeleteSheetResult.DeletedAssetOnly;
        }

        // ----- Face layers -----

        /// <summary>
        /// Sets everything under both face roots to the matching SheetFront/SheetBack layer — the same thing
        /// <c>Sheet.Awake</c> forces at runtime — so the Studio's per-face cull masks work in the editor.
        /// Returns the number of objects changed (0 = already normalized, nothing dirtied).
        /// </summary>
        /// <remarks>
        /// Deliberately NOT undoable: the layers are an invariant, not an edit. If normalization sat on the
        /// undo stack, Ctrl+Z right after opening a sheet would revert content to layer Default and the panes
        /// would silently stop rendering it.
        /// </remarks>
        public static int NormalizeFaceLayers(Sheet sheet)
        {
            if (sheet == null || !FoldLayers.Valid)
                return 0;
            var changed = 0;
            changed += NormalizeLayers(sheet.Front, FoldLayers.Front);
            changed += NormalizeLayers(sheet.Back, FoldLayers.Back);
            if (changed > 0 && sheet.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(sheet.gameObject.scene);
            return changed;
        }

        static int NormalizeLayers(Transform root, int layer)
        {
            if (root == null)
                return 0;
            var changed = 0;
            if (root.gameObject.layer != layer)
            {
                root.gameObject.layer = layer;
                EditorUtility.SetDirty(root.gameObject);
                changed++;
            }
            for (int i = 0; i < root.childCount; i++)
                changed += NormalizeLayers(root.GetChild(i), layer);
            return changed;
        }

        // ----- Face art slot -----

        /// <summary>
        /// The face's background-art object, identified by convention rather than name (the hand-authored
        /// art is named 'Map 1'): a SpriteRenderer child directly under the face root at z −0.01 (epsilon —
        /// serialized floats) with sortingOrder 0. <paramref name="matchCount"/> reports how many children
        /// matched; more than one is authored ambiguity the window surfaces as a warning.
        /// </summary>
        public static GameObject FindFaceArt(Transform faceRoot, out int matchCount)
        {
            matchCount = 0;
            if (faceRoot == null)
                return null;
            GameObject first = null;
            for (int i = 0; i < faceRoot.childCount; i++)
            {
                var child = faceRoot.GetChild(i);
                if (!child.TryGetComponent(out SpriteRenderer renderer))
                    continue;
                if (renderer.sortingOrder != 0 || Mathf.Abs(child.localPosition.z - ArtZ) > ArtZEpsilon)
                    continue;
                matchCount++;
                if (first == null)
                    first = child.gameObject;
            }
            return first;
        }

        /// <summary>
        /// Map art is exported portrait (8.5 × 11 at 300 px/unit, as Photoshop lays the page) and lies on
        /// the landscape sheet turned 90° counter-clockwise — the convention Map 1 was hand-authored with
        /// (Aaron, 2026-09-09: the Studio-placed maps came in "rotated 90 degrees from what they should be").
        /// </summary>
        public const float PortraitArtRotationDegrees = 90f;

        /// <summary>The local rotation the art object needs for <paramref name="sprite"/>: portrait art is turned to lie landscape; landscape (or square) art is not.</summary>
        public static Quaternion ArtRotationFor(Sprite sprite)
            => sprite != null && sprite.rect.height > sprite.rect.width
                ? Quaternion.Euler(0f, 0f, PortraitArtRotationDegrees)
                : Quaternion.identity;

        /// <summary>
        /// Assigns a face's background art: adopts the existing art object when one matches the convention,
        /// else creates a child at the conventional z, sorting order, layer, and unlit sprite material. Either
        /// way the art is turned per <see cref="ArtRotationFor"/>, so a re-drop of a differently shaped image
        /// corrects the rotation too.
        /// </summary>
        public static void SetFaceArt(Transform faceRoot, SheetFace face, Sprite sprite)
        {
            if (faceRoot == null || sprite == null)
                return;

            var art = FindFaceArt(faceRoot, out _);
            if (art != null)
            {
                var renderer = art.GetComponent<SpriteRenderer>();
                Undo.RecordObjects(new Object[] { renderer, art.transform }, $"Set {face} art");
                renderer.sprite = sprite;
                art.transform.localRotation = ArtRotationFor(sprite);
                StudioEdits.Edited(art);
                return;
            }

            art = new GameObject(ArtObjectName);
            art.transform.SetParent(faceRoot, false);
            art.transform.localPosition = new Vector3(0f, 0f, ArtZ);
            art.transform.localRotation = ArtRotationFor(sprite);
            if (FoldLayers.Valid)
                art.layer = FoldLayers.LayerOf(face);
            var created = art.AddComponent<SpriteRenderer>();
            created.sprite = sprite;
            created.sortingOrder = 0;
            var materialPath = AssetDatabase.GUIDToAssetPath(SpriteUnlitMaterialGuid);
            var material = string.IsNullOrEmpty(materialPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material != null)
                created.sharedMaterial = material;
            else
                Debug.LogError("StudioSheetOps.SetFaceArt: URP Sprite-Unlit-Default material not found; the art uses the default sprite material.");
            Undo.RegisterCreatedObjectUndo(art, $"Set {face} art");
            StudioEdits.Edited(art);
        }

        /// <summary>Removes the face's background-art object (adopted or created).</summary>
        public static void ClearFaceArt(Transform faceRoot)
        {
            var art = FindFaceArt(faceRoot, out _);
            if (art == null)
                return;
            var scene = art.scene;
            Undo.DestroyObjectImmediate(art);
            StudioEdits.Edited(scene);
        }

        // ----- Collision view -----

        /// <summary>Serialized name of <see cref="Sheet.ShowCollision"/>; a test asserts it still resolves.</summary>
        public const string ShowCollisionField = "showCollision";

        /// <summary>
        /// Sets the sheet's per-sheet collision view (Aaron, 2026-09-14: solid fills over the collision so a sheet can
        /// be played and designed before its map art is drawn). Undoable, and dirties the stage so Ctrl+S / Auto Save
        /// writes it into the variant; the sheet's OnValidate tells every <see cref="TerrainFill"/> at once.
        /// </summary>
        public static void SetShowCollision(Sheet sheet, bool show)
        {
            if (sheet == null)
                return;
            var serialized = new SerializedObject(sheet);
            var property = serialized.FindProperty(ShowCollisionField);
            if (property == null)
            {
                Debug.LogError($"StudioSheetOps.SetShowCollision: Sheet has no '{ShowCollisionField}' field; the toggle does nothing.");
                return;
            }
            property.boolValue = show;
            serialized.ApplyModifiedProperties();
            StudioEdits.Edited(sheet.gameObject);
        }
    }
}
