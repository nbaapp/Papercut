using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Papercut.EditorTools
{
    /// <summary>
    /// The one rule that makes Studio edits reach the sheet's prefab file: after every edit, the scene the edited
    /// object lives in (the Prefab Stage's) must be dirty, or Prefab Mode's Auto Save never writes it and closing
    /// the stage silently discards it.
    /// </summary>
    /// <remarks>
    /// Undo alone does not guarantee that. Auto Save runs on every editor tick unless an IMGUI control holds the
    /// mouse (<see cref="GUIUtility.hotControl"/>) or a text field is being edited, and a successful save clears
    /// the stage's dirtiness. A second <see cref="Undo.RecordObject(Object, string)"/> of the same object in the
    /// same undo group merges into the first record and does <b>not</b> dirty the scene again — so a gesture whose
    /// first frame was auto-saved (a pane drag, a held arrow key) left the stage showing an edit it believed was
    /// saved (Aaron, 2026-09-14: edits visible in the Studio that vanished in Play Mode and on reopening the
    /// sheet; reproduced in-editor). The panes therefore hold <c>hotControl</c> for the length of a drag, so a
    /// drag is one save at release, and every edit calls <see cref="Edited(GameObject)"/> so nothing that slips
    /// past the undo system's bookkeeping is ever lost.
    /// </remarks>
    public static class StudioEdits
    {
        /// <summary>
        /// Call after an edit of <paramref name="edited"/> (a stage object). Flushes the pending undo record —
        /// which is what normally dirties the scene — and marks the scene dirty itself if that did not.
        /// A no-op for an object outside any scene (a prefab asset).
        /// </summary>
        public static void Edited(GameObject edited)
        {
            if (edited != null)
                Edited(edited.scene);
        }

        /// <summary>
        /// <see cref="Edited(GameObject)"/> for an edit whose object no longer exists (a delete): pass the scene it
        /// was in, read before the destroy.
        /// </summary>
        public static void Edited(Scene scene)
        {
            if (!scene.IsValid())
                return;
            Undo.FlushUndoRecordObjects();
            if (!scene.isDirty)
                EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
