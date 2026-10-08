using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// The Sheet Studio's Playtest button (Aaron, 2026-09-24: play the sheet being edited, starting where he
    /// chooses): the per-sheet spawn point, the pre-flight checks, saving the stage, and the hand-off to
    /// <see cref="PlaytestStart"/> before entering Play Mode.
    /// </summary>
    /// <remarks>
    /// The spawn is remembered per sheet asset in editor prefs (Aaron: in the Studio, not in the prefab), keyed
    /// by the sheet's asset path under a per-project prefix - <see cref="EditorPrefs"/> is shared by every
    /// project on the machine, so a second checkout must not see this one's spawns. Unset means the sheet's
    /// centre. The spawn is a Front point on the flat sheet: the player is always on the Base (Bible §4) and a
    /// sheet starts unfolded. The Desk scene is never touched: the saved Player object stays where it is, so the
    /// ordinary Play button still starts there.
    /// </remarks>
    public static class StudioPlaytest
    {
        const string PrefPrefix = "Papercut.SheetStudio.spawn.";
        /// <summary>A stale request is one older than this and not followed by a Play Mode entry (Unity aborted it).</summary>
        const double StaleRequestSeconds = 5.0;
        const string RequestStampKey = "Papercut.SheetStudio.playtestRequestedAt";

        /// <summary>The player's collision box as authored: size and offset from the player's position, world units.</summary>
        public readonly struct PlayerFootprint
        {
            public readonly Vector2 Size;
            public readonly Vector2 Offset;

            public PlayerFootprint(Vector2 size, Vector2 offset)
            {
                Size = size;
                Offset = offset;
            }

            /// <summary>The box with the player's position at <paramref name="centre"/>.</summary>
            public Rect At(Vector2 centre) => new(centre + Offset - Size * 0.5f, Size);
        }

        public enum PlaytestRefusal
        {
            None,
            EditorBusy,
            CompileErrors,
            NoDesk,
            NotOnDesk,
            Duplicate,
            Mismatch,
            NoRoom,
            SaveFailed,
        }

        // ----- Spawn point (editor prefs) -----

        static string projectKey;

        /// <summary>A short stable tag for this project checkout, so prefs never leak between copies of the project.</summary>
        static string ProjectKey
        {
            get
            {
                if (projectKey == null)
                {
                    // A hash, not the path: the path has characters and a length the prefs backend may not like.
                    var path = Application.dataPath.Replace('\\', '/');
                    unchecked
                    {
                        uint hash = 2166136261;
                        foreach (var c in path)
                            hash = (hash ^ c) * 16777619;
                        projectKey = hash.ToString("x8");
                    }
                }
                return projectKey;
            }
        }

        public static string SpawnKey(string sheetAssetPath) => PrefPrefix + ProjectKey + "." + sheetAssetPath;

        /// <summary>True once a spawn has been set for the sheet; false means the marker is at the default (the centre).</summary>
        public static bool HasSpawn(string sheetAssetPath)
            => !string.IsNullOrEmpty(sheetAssetPath) && TryParse(EditorPrefs.GetString(SpawnKey(sheetAssetPath), string.Empty), out _);

        /// <summary>The sheet's spawn point, sheet-local (Front space); the centre when none is set or the stored value is unreadable.</summary>
        public static Vector2 GetSpawn(string sheetAssetPath)
        {
            if (string.IsNullOrEmpty(sheetAssetPath))
                return Vector2.zero;
            return TryParse(EditorPrefs.GetString(SpawnKey(sheetAssetPath), string.Empty), out var point) ? point : Vector2.zero;
        }

        /// <summary>
        /// Remembers the spawn for the sheet, clamped so the player's box lies inside the sheet
        /// (<see cref="ClampToSheet"/>).
        /// </summary>
        public static void SetSpawn(string sheetAssetPath, Vector2 sheetLocal, in PlayerFootprint footprint)
        {
            if (string.IsNullOrEmpty(sheetAssetPath))
                return;
            var clamped = ClampToSheet(sheetLocal, footprint);
            EditorPrefs.SetString(SpawnKey(sheetAssetPath), Format(clamped));
        }

        public static void ClearSpawn(string sheetAssetPath)
        {
            if (!string.IsNullOrEmpty(sheetAssetPath))
                EditorPrefs.DeleteKey(SpawnKey(sheetAssetPath));
        }

        /// <summary>The sheet map renamed a variant: its spawn follows the file.</summary>
        public static void RekeySpawn(string oldAssetPath, string newAssetPath)
        {
            if (string.IsNullOrEmpty(oldAssetPath) || string.IsNullOrEmpty(newAssetPath) || oldAssetPath == newAssetPath)
                return;
            var stored = EditorPrefs.GetString(SpawnKey(oldAssetPath), null);
            EditorPrefs.DeleteKey(SpawnKey(oldAssetPath));
            if (stored != null)
                EditorPrefs.SetString(SpawnKey(newAssetPath), stored);
            else
                EditorPrefs.DeleteKey(SpawnKey(newAssetPath));
        }

        /// <summary>The sheet map swapped two variants' files: their spawns swap with them.</summary>
        public static void SwapSpawns(string assetPathA, string assetPathB)
        {
            if (string.IsNullOrEmpty(assetPathA) || string.IsNullOrEmpty(assetPathB) || assetPathA == assetPathB)
                return;
            var a = EditorPrefs.GetString(SpawnKey(assetPathA), null);
            var b = EditorPrefs.GetString(SpawnKey(assetPathB), null);
            if (b != null) EditorPrefs.SetString(SpawnKey(assetPathA), b); else EditorPrefs.DeleteKey(SpawnKey(assetPathA));
            if (a != null) EditorPrefs.SetString(SpawnKey(assetPathB), a); else EditorPrefs.DeleteKey(SpawnKey(assetPathB));
        }

        /// <summary>The nearest point to <paramref name="sheetLocal"/> at which the player's box lies wholly on the sheet.</summary>
        public static Vector2 ClampToSheet(Vector2 sheetLocal, in PlayerFootprint footprint)
        {
            var half = SheetGeometry.HalfSize;
            var box = footprint.At(sheetLocal);
            var min = -half - box.min + sheetLocal; // The position at which the box's min corner sits on the sheet's min corner.
            var max = half - box.max + sheetLocal;
            return new Vector2(
                min.x <= max.x ? Mathf.Clamp(sheetLocal.x, min.x, max.x) : 0f,
                min.y <= max.y ? Mathf.Clamp(sheetLocal.y, min.y, max.y) : 0f);
        }

        static string Format(Vector2 point)
            => point.x.ToString("R", CultureInfo.InvariantCulture) + ";" + point.y.ToString("R", CultureInfo.InvariantCulture);

        static bool TryParse(string stored, out Vector2 point)
        {
            point = default;
            if (string.IsNullOrEmpty(stored))
                return false;
            var parts = stored.Split(';');
            if (parts.Length != 2
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y))
                return false;
            point = new Vector2(x, y);
            return true;
        }

        // ----- Player footprint -----

        const string PlayerPrefabPath = "Assets/Papercut/Prefabs/Player.prefab";
        static bool warnedNoFootprint;

        /// <summary>
        /// The player the playtest will use: the open Desk's Player instance (it may carry overrides), else
        /// Player.prefab. Serialized box size and offset, scaled - never <c>Collider2D.bounds</c>, which is
        /// physics-populated and degenerate on an asset or before the first physics step.
        /// </summary>
        public static PlayerFootprint ReadPlayerFootprint(Desk desk)
        {
            var player = StudioSheetOps.FindPlayerOnDesk(desk);
            var box = player != null ? player.GetComponent<BoxCollider2D>() : null;
            if (box == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                box = prefab != null ? prefab.GetComponent<BoxCollider2D>() : null;
            }
            if (box == null || box.size.x <= 0f || box.size.y <= 0f)
            {
                if (!warnedNoFootprint) // Read on every Studio layout; one warning, not a stream.
                {
                    warnedNoFootprint = true;
                    Debug.LogWarning("Sheet Studio: no Player BoxCollider2D on the Desk or in Player.prefab; the spawn marker and the test-player ghost use a 0.5 x 0.5 footprint.");
                }
                return new PlayerFootprint(new Vector2(0.5f, 0.5f), Vector2.zero);
            }
            warnedNoFootprint = false;
            var scale = (Vector2)box.transform.lossyScale;
            return new PlayerFootprint(
                Vector2.Scale(box.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y))),
                Vector2.Scale(box.offset, scale));
        }

        // ----- Room -----

        /// <summary>
        /// Edit-mode counterpart of the game's arrival room test: the player's box at <paramref name="sheetLocal"/>
        /// must lie on the sheet and share no area with any active <see cref="IArrivalObstacle"/> on the Front - a
        /// <see cref="TerrainRegion"/> the player cannot pass with <paramref name="abilities"/> (null: none), or a
        /// block/paperweight - read from authored colliders, the way the fold preview sees them. A region that
        /// rests absent (a Gate (Off)) is active and counts: the game may see it as solid at its first frame too.
        /// </summary>
        public static bool SpawnHasRoom(Sheet sheet, Vector2 sheetLocal, in PlayerFootprint footprint, PlayerAbilities abilities)
        {
            if (sheet == null || sheet.Front == null)
                return false;
            var box = footprint.At(sheetLocal);
            var bounds = new Rect(-SheetGeometry.HalfSize, SheetGeometry.Size);
            if (box.xMin < bounds.xMin || box.yMin < bounds.yMin || box.xMax > bounds.xMax || box.yMax > bounds.yMax)
                return false;

            var pieces = new List<ConvexPolygon>();
            // Front content and, since 2026-09-28, the universal regions above the sheet (sheet space = Front space, flat).
            foreach (var root in new[] { sheet.Front, sheet.Above })
            {
                if (root == null)
                    continue;
                foreach (var obstacle in root.GetComponentsInChildren<IArrivalObstacle>(false))
                {
                    if (obstacle is not Component component)
                        continue;
                    if (obstacle is TerrainRegion region)
                    {
                        if (region.IsPassableBy(abilities))
                            continue;
                        StudioPlacement.TryGetRegionPieces(region, root, pieces);
                    }
                    else
                    {
                        StudioPlacement.AuthoredFootprintPieces(component.gameObject, root, pieces);
                    }
                    if (!TravelRules.HasRoom(box, pieces))
                        return false;
                }
            }
            return true;
        }

        // ----- Pre-flight and launch -----

        /// <summary>
        /// The open Desk's instance of the sheet asset, through the sheet map's own view of the set
        /// (<see cref="StudioSheetOps.BuildSlots"/>), so every ambiguity the map warns about refuses here:
        /// the asset is not in this Desk's set, it has no instance, several sheets claim its position, or the
        /// instance there is of another asset. A variant's own grid position is never authored - only the Desk
        /// instance carries it - so the instance is found by asset path and its position is what the request gets.
        /// </summary>
        public static PlaytestRefusal ResolveDeskInstance(string sheetAssetPath, Desk desk, string folder, out Sheet instance, out string reason)
        {
            instance = null;
            var name = System.IO.Path.GetFileNameWithoutExtension(sheetAssetPath ?? string.Empty);
            if (desk == null)
            {
                reason = "No Desk scene is open.";
                return PlaytestRefusal.NoDesk;
            }
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
            {
                reason = $"The open Desk has no sheet set folder ('{folder}'), so '{name}' cannot be on it.";
                return PlaytestRefusal.NotOnDesk;
            }
            var slot = StudioSheetOps.BuildSlots(folder, desk, null).Find(s => s.AssetPath == sheetAssetPath);
            if (slot == null)
            {
                reason = $"'{name}' is not in the open Desk's sheet set ('{folder}'). Open the Desk scene it belongs to.";
                return PlaytestRefusal.NotOnDesk;
            }
            if (slot.Instance == null)
            {
                reason = $"'{name}' is not on the open Desk. Add it from the sheet map first.";
                return PlaytestRefusal.NotOnDesk;
            }
            if (slot.Duplicate)
            {
                reason = $"{slot.InstanceCount} sheets on the Desk claim {Fmt(slot.GridPosition)}; remove the extras first.";
                return PlaytestRefusal.Duplicate;
            }
            if (slot.Mismatch)
            {
                reason = $"The Desk's sheet at {Fmt(slot.GridPosition)} is an instance of '{slot.InstanceAssetPath ?? "(not a prefab)"}', not of '{name}'. Fix it on the sheet map first.";
                return PlaytestRefusal.Mismatch;
            }
            instance = slot.Instance;
            reason = string.Empty;
            return PlaytestRefusal.None;
        }

        /// <summary>Everything that would stop a playtest, checked in the order the button reports it.</summary>
        public static PlaytestRefusal CanPlaytest(PrefabStage stage, Sheet sheet, Desk desk, out Sheet instance, out string reason)
        {
            instance = null;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                reason = "The editor is already playing or compiling.";
                return PlaytestRefusal.EditorBusy;
            }
            if (EditorUtility.scriptCompilationFailed)
            {
                reason = "Fix the compile errors first.";
                return PlaytestRefusal.CompileErrors;
            }
            if (stage == null || sheet == null)
            {
                reason = "No sheet is open in the Studio.";
                return PlaytestRefusal.NotOnDesk;
            }
            var refusal = ResolveDeskInstance(stage.assetPath, desk, StudioSheetOps.SheetFolderFor(desk), out instance, out reason);
            if (refusal != PlaytestRefusal.None)
                return refusal;

            var abilities = StudioSheetOps.FindPlayerOnDesk(desk)?.GetComponent<PlayerAbilities>();
            if (!SpawnHasRoom(sheet, GetSpawn(stage.assetPath), ReadPlayerFootprint(desk), abilities))
            {
                reason = "The spawn point is inside a wall, a block, or off the sheet. Move it (Spawn mode) first.";
                return PlaytestRefusal.NoRoom;
            }
            reason = string.Empty;
            return PlaytestRefusal.None;
        }

        /// <summary>
        /// Saves the stage if it has unsaved edits (what Ctrl+S does), writes the start request, and enters Play
        /// Mode. False with the reason when refused; nothing is written then, so the next ordinary Play is untouched.
        /// </summary>
        public static bool Playtest(PrefabStage stage, Sheet sheet, Desk desk, out string message)
        {
            if (CanPlaytest(stage, sheet, desk, out var instance, out message) != PlaytestRefusal.None)
                return false;

            if (stage.scene.isDirty)
            {
                GameObject saved = null;
                try
                {
                    saved = PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
                }
                catch (System.Exception e)
                {
                    message = $"Could not save the sheet: {e.Message}";
                    return false;
                }
                if (saved == null)
                {
                    message = $"Could not save the sheet to '{stage.assetPath}'.";
                    return false;
                }
                stage.ClearDirtiness();
            }

            var request = new PlaytestStart.Request(instance.GridPosition, GetSpawn(stage.assetPath));
            PlaytestStart.Set(request);
            SessionState.SetFloat(RequestStampKey, (float)EditorApplication.timeSinceStartup);
            EditorApplication.EnterPlaymode();
            message = $"Playtesting {request}.";
            return true;
        }

        /// <summary>
        /// Drops a request that Play Mode never picked up: written, then Unity declined to enter Play Mode for a
        /// reason the pre-flight did not catch, so no edit-mode re-entry ever fires. Runs on every editor update
        /// (not only while the Studio is open, so closing the window cannot leave one behind); a request younger
        /// than <see cref="StaleRequestSeconds"/> is still on its way.
        /// </summary>
        public static void ClearStaleRequest()
        {
            if (!PlaytestStart.IsPending || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            var age = EditorApplication.timeSinceStartup - SessionState.GetFloat(RequestStampKey, float.NegativeInfinity);
            if (age > StaleRequestSeconds)
            {
                PlaytestStart.Clear();
                Debug.LogWarning("Sheet Studio: a playtest start request was never picked up by Play Mode and has been dropped.");
            }
        }

        [InitializeOnLoadMethod]
        static void HookPlayModeChanges()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredEditMode)
                    return;
                // Consumed at Start in the normal course; this catches a session that never reached it.
                PlaytestStart.Clear();
                // After the domain reload: the editor is back, but let this state change finish before opening windows.
                EditorApplication.delayCall += ReopenStudioIfRequested;
            };
            EditorApplication.update += ClearStaleRequest; // Two SessionState reads a tick while nothing is pending.
        }

        // ----- Hide the Studio for the playtest, bring it back after -----

        const string ReopenKey = "Papercut.SheetStudio.reopenAfterPlaytest";
        const string ReopenStageKey = "Papercut.SheetStudio.reopenStagePath";

        /// <summary>
        /// The Playtest button closes the Studio (a floating window would cover the Game view; Aaron, 2026-09-28)
        /// and records that it did, so <see cref="ReopenStudioIfRequested"/> brings it back with the same sheet
        /// when Play Mode stops. Ordinary Play never sets this, so it never opens a window on its own.
        /// </summary>
        public static void RememberReopen(string stageAssetPath)
        {
            SessionState.SetBool(ReopenKey, true);
            SessionState.SetString(ReopenStageKey, stageAssetPath ?? string.Empty);
        }

        /// <summary>True while a Playtest-closed Studio is waiting to be brought back.</summary>
        public static bool ReopenRequested => SessionState.GetBool(ReopenKey, false);

        /// <summary>
        /// Reopens the Studio, and the sheet it was editing if Unity did not keep that stage open, when
        /// <see cref="RememberReopen"/> was called for the Play Mode session that just ended. A no-op otherwise.
        /// </summary>
        public static void ReopenStudioIfRequested()
        {
            if (!ReopenRequested || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            var stagePath = SessionState.GetString(ReopenStageKey, string.Empty);
            SessionState.EraseBool(ReopenKey);
            SessionState.EraseString(ReopenStageKey);

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!string.IsNullOrEmpty(stagePath) && (stage == null || stage.assetPath != stagePath))
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(stagePath) != null)
                    PrefabStageUtility.OpenPrefab(stagePath);
                else
                    Debug.LogWarning($"Sheet Studio: '{stagePath}' no longer exists; reopening the Studio without it.");
            }
            SheetStudioWindow.Open();
        }

        static string Fmt(Vector2Int p) => $"({p.x},{p.y})";
    }
}
