using System.Collections.Generic;
using NUnit.Framework;
using Papercut.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Papercut.Tests
{
    /// <summary>
    /// Studio edits must leave their scene dirty so Prefab Mode's Auto Save writes them (the 2026-09-14 lost-edit
    /// bug: a second undo record of an object in the same undo group merges into the first and does not dirty the
    /// scene, so a gesture auto-saved on its first frame ended clean but unsaved). Driven in a preview scene, which
    /// starts clean like a freshly opened Prefab Stage, and against a real Prefab Stage for the merged-record case.
    /// </summary>
    public sealed class StudioEditsTests
    {
        const string TempFolder = "Assets/Temp_StudioEditsTests";
        const string TempPrefabPath = TempFolder + "/Edits Probe.prefab";

        readonly List<GameObject> sceneObjects = new();
        Scene previewScene;
        bool previewOpen;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in sceneObjects)
                if (go != null)
                    Object.DestroyImmediate(go);
            sceneObjects.Clear();
            if (previewOpen)
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
                previewOpen = false;
            }
            if (PrefabStageUtility.GetCurrentPrefabStage() is { } stage && stage.assetPath == TempPrefabPath)
            {
                stage.ClearDirtiness(); // Never leave a dirty stage behind: with Auto Save off it would prompt.
                StageUtility.GoToMainStage();
            }
            if (AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.DeleteAsset(TempFolder);
        }

        GameObject InPreviewScene(string name)
        {
            if (!previewOpen)
            {
                previewScene = EditorSceneManager.NewPreviewScene();
                previewOpen = true;
            }
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, previewScene);
            sceneObjects.Add(go);
            return go;
        }

        [Test]
        public void Edited_MarksACleanSceneDirty()
        {
            var go = InPreviewScene("Element");
            Assume.That(previewScene.isDirty, Is.False, "a new preview scene starts clean");

            StudioEdits.Edited(go);

            Assert.IsTrue(previewScene.isDirty);
        }

        [Test]
        public void Edited_IsANoOpForAnObjectOutsideAnyScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Papercut/Prefabs/Terrain/Wall.prefab");
            Assume.That(prefab, Is.Not.Null);
            Assert.DoesNotThrow(() => StudioEdits.Edited(prefab));
        }

        [Test]
        public void Move_LeavesTheSceneDirty()
        {
            var go = InPreviewScene("Element");
            StudioPlacement.Move(go, new Vector2(1f, 2f), 0f);
            Assert.IsTrue(previewScene.isDirty);
        }

        [Test]
        public void Delete_LeavesTheSceneDirty()
        {
            var go = InPreviewScene("Element");
            StudioPlacement.Delete(go);
            Assert.IsTrue(previewScene.isDirty);
        }

        /// <summary>
        /// The bug itself: a move recorded into the same undo group as an earlier, already-saved move (Auto Save
        /// stood in for by <see cref="PrefabStage.ClearDirtiness"/>) must still leave the stage with unsaved changes.
        /// </summary>
        [Test]
        public void Move_AfterAnAutoSaveInTheSameUndoGroup_StillDirtiesTheStage()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "Temp_StudioEditsTests");
            var source = new GameObject("Edits Probe");
            var child = new GameObject("Element").transform;
            child.SetParent(source.transform, false);
            try
            {
                PrefabUtility.SaveAsPrefabAsset(source, TempPrefabPath, out var saved);
                Assume.That(saved, Is.True, "temp prefab saved");
            }
            finally
            {
                Object.DestroyImmediate(source);
            }

            var stage = PrefabStageUtility.OpenPrefab(TempPrefabPath);
            Assume.That(stage, Is.Not.Null, "temp prefab opened in Prefab Mode");
            Assume.That(stage.scene.isDirty, Is.False, "a freshly opened stage is clean");
            var element = stage.prefabContentsRoot.transform.GetChild(0).gameObject;

            StudioPlacement.Move(element, new Vector2(0.1f, 0f), 0f);
            Assume.That(stage.scene.isDirty, Is.True, "the first move dirties the stage");
            stage.ClearDirtiness(); // What a successful Auto Save does.
            Assume.That(stage.scene.isDirty, Is.False);

            StudioPlacement.Move(element, new Vector2(0.2f, 0f), 0f); // Same undo group: no event has passed.

            Assert.IsTrue(stage.scene.isDirty, "the second move of the gesture must be saved too");
            Assert.AreEqual(0.2f, element.transform.localPosition.x, 1e-5f);
        }
    }
}
