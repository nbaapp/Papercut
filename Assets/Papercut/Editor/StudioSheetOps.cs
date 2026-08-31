using System.Collections.Generic;
using System.IO;
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
    /// </remarks>
    public static class StudioSheetOps
    {
        public const string BaseSheetPrefabPath = "Assets/Papercut/Prefabs/Sheet.prefab";
        public const string SheetFolder = "Assets/Papercut/Sheets";

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

        /// <summary>Paths of every sheet prefab variant in the sheet folder, sorted by name.</summary>
        public static List<string> FindSheetAssets()
        {
            var paths = new List<string>();
            if (!AssetDatabase.IsValidFolder(SheetFolder))
                return paths;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { SheetFolder }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            paths.Sort();
            return paths;
        }

        public static string SheetAssetPath(Vector2Int gridPosition, string folder = SheetFolder)
            => $"{folder}/Sheet ({gridPosition.x},{gridPosition.y}).prefab";

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
        /// undone add, since the asset save itself is not undoable.
        /// </summary>
        public static CreateSheetResult CreateSheet(Vector2Int gridPosition, Desk desk, out string message, string folder = SheetFolder)
        {
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
            if (!AssetDatabase.IsValidFolder(folder))
            {
                message = $"Sheet folder '{folder}' does not exist.";
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
        /// Assigns a face's background art: adopts the existing art object when one matches the convention,
        /// else creates a child at the conventional z, sorting order, layer, and unlit sprite material.
        /// </summary>
        public static void SetFaceArt(Transform faceRoot, SheetFace face, Sprite sprite)
        {
            if (faceRoot == null || sprite == null)
                return;

            var art = FindFaceArt(faceRoot, out _);
            if (art != null)
            {
                var renderer = art.GetComponent<SpriteRenderer>();
                Undo.RecordObject(renderer, $"Set {face} art");
                renderer.sprite = sprite;
                return;
            }

            art = new GameObject(ArtObjectName);
            art.transform.SetParent(faceRoot, false);
            art.transform.localPosition = new Vector3(0f, 0f, ArtZ);
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
        }

        /// <summary>Removes the face's background-art object (adopted or created).</summary>
        public static void ClearFaceArt(Transform faceRoot)
        {
            var art = FindFaceArt(faceRoot, out _);
            if (art != null)
                Undo.DestroyObjectImmediate(art);
        }
    }
}
