using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Papercut.EditorTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Papercut.Tests
{
    /// <summary>
    /// Sheet-level Studio operations: variant creation, the face-art slot (including adoption of the
    /// existing 'Map 1'-style art), and layer normalization. Temp assets live under a throwaway folder
    /// deleted in teardown; scene objects are destroyed in teardown.
    /// </summary>
    public sealed class StudioSheetOpsTests
    {
        const string TempFolder = "Assets/Temp_StudioTests";

        readonly List<GameObject> sceneObjects = new();

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "Temp_StudioTests");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in sceneObjects)
                if (go != null)
                    Object.DestroyImmediate(go);
            sceneObjects.Clear();
            AssetDatabase.DeleteAsset(TempFolder);
        }

        GameObject Track(GameObject go)
        {
            sceneObjects.Add(go);
            return go;
        }

        // ----- Helpers -----

        Sheet CreateTestSheet(out Transform front, out Transform back)
        {
            var go = Track(new GameObject("TestSheet"));
            front = new GameObject("Front").transform;
            front.SetParent(go.transform, false);
            back = new GameObject("Back").transform;
            back.SetParent(go.transform, false);

            LogAssert.Expect(LogType.Error, new Regex("no Front root"));
            LogAssert.Expect(LogType.Error, new Regex("no Back root"));
            var sheet = go.AddComponent<Sheet>();
            var serialized = new SerializedObject(sheet);
            serialized.FindProperty("front").objectReferenceValue = front;
            serialized.FindProperty("back").objectReferenceValue = back;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return sheet;
        }

        Desk CreateTestDesk()
        {
            var go = Track(new GameObject("TestDesk"));
            var grid = new GameObject("SheetGrid").transform;
            grid.SetParent(go.transform, false);

            LogAssert.Expect(LogType.Error, new Regex("no SheetGrid assigned"));
            var desk = go.AddComponent<Desk>();
            var serialized = new SerializedObject(desk);
            serialized.FindProperty("sheetGrid").objectReferenceValue = grid;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return desk;
        }

        // ----- CreateSheet -----

        [Test]
        public void CreateSheet_WithoutDesk_CreatesAVariantOfTheBaseSheet()
        {
            var result = StudioSheetOps.CreateSheet(new Vector2Int(9, 9), null, out var message, TempFolder);
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk, result, message);

            var path = StudioSheetOps.SheetAssetPath(new Vector2Int(9, 9), TempFolder);
            var variant = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(variant, "variant asset should exist");
            Assert.AreEqual(PrefabAssetType.Variant, PrefabUtility.GetPrefabAssetType(variant), "should be a prefab variant");
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StudioSheetOps.BaseSheetPrefabPath);
            Assert.AreEqual(basePrefab, PrefabUtility.GetCorrespondingObjectFromSource(variant), "base must be the Sheet prefab");
            Assert.IsNotNull(variant.GetComponent<Sheet>());
            Assert.IsNotNull(variant.GetComponent<SheetFolds>());
            Assert.IsNotNull(variant.GetComponent<IFoldRenderer>());
        }

        [Test]
        public void CreateSheet_WithDesk_PlacesTheSheetOnTheGrid()
        {
            var desk = CreateTestDesk();
            var result = StudioSheetOps.CreateSheet(new Vector2Int(8, 8), desk, out var message, TempFolder);
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, result, message);

            var placed = StudioSheetOps.FindSheetOnDesk(desk, new Vector2Int(8, 8));
            Assert.IsNotNull(placed, "sheet should be on the desk");
            Assert.AreEqual(desk.SheetGrid, placed.transform.parent, "sheet must be a direct child of the SheetGrid");
        }

        [Test]
        public void CreateSheet_RefusesAnOccupiedGridPosition()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created,
                StudioSheetOps.CreateSheet(new Vector2Int(7, 7), desk, out _, TempFolder));
            var result = StudioSheetOps.CreateSheet(new Vector2Int(7, 7), desk, out var message, TempFolder);
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.RefusedPositionOccupied, result, message);
        }

        [Test]
        public void CreateSheet_AddsAnExistingOffDeskVariantInsteadOfRefusing()
        {
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk,
                StudioSheetOps.CreateSheet(new Vector2Int(6, 6), null, out _, TempFolder));

            var desk = CreateTestDesk();
            var result = StudioSheetOps.CreateSheet(new Vector2Int(6, 6), desk, out var message, TempFolder);
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.AddedExistingToDesk, result, message);
            Assert.IsNotNull(StudioSheetOps.FindSheetOnDesk(desk, new Vector2Int(6, 6)));
        }

        // ----- Face art -----

        [Test]
        public void SetFaceArt_CreatesTheRendererAtTheConventionalPlace()
        {
            CreateTestSheet(out var front, out _);
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);

            StudioSheetOps.SetFaceArt(front, SheetFace.Front, sprite);

            var art = StudioSheetOps.FindFaceArt(front, out var matches);
            Assert.IsNotNull(art, "art object should exist");
            Assert.AreEqual(1, matches);
            Assert.AreEqual(StudioSheetOps.ArtZ, art.transform.localPosition.z, 1e-4f);
            var renderer = art.GetComponent<SpriteRenderer>();
            Assert.AreEqual(0, renderer.sortingOrder);
            Assert.AreEqual(sprite, renderer.sprite);
            StringAssert.Contains("Sprite-Unlit", renderer.sharedMaterial.name);
        }

        [Test]
        public void SetFaceArt_AdoptsExistingArtInsteadOfCreatingASecondRenderer()
        {
            // The hand-authored sheets name their art 'Map 1'; the slot must adopt by convention, not name.
            CreateTestSheet(out var front, out _);
            var existing = new GameObject("Map 1");
            existing.transform.SetParent(front, false);
            existing.transform.localPosition = new Vector3(0f, 0f, StudioSheetOps.ArtZ);
            var existingRenderer = existing.AddComponent<SpriteRenderer>();
            existingRenderer.sortingOrder = 0;

            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            StudioSheetOps.SetFaceArt(front, SheetFace.Front, sprite);

            Assert.AreEqual(1, front.childCount, "must not create a second art renderer");
            Assert.AreEqual(sprite, existingRenderer.sprite, "the existing art object should have been adopted");
        }

        [Test]
        public void SetFaceArt_AdoptsTheRealVariantsExistingArt()
        {
            // Against a copy of the real hand-authored variant, not synthetic data: its art's z is a
            // serialized float (the reason FindFaceArt compares with an epsilon).
            var copyPath = TempFolder + "/Sheet Copy.prefab";
            Assert.IsTrue(AssetDatabase.CopyAsset("Assets/Papercut/Sheets/Sheet (0,0).prefab", copyPath));
            var root = PrefabUtility.LoadPrefabContents(copyPath);
            try
            {
                var sheet = root.GetComponent<Sheet>();
                Assert.IsNotNull(sheet);
                var existingArt = StudioSheetOps.FindFaceArt(sheet.Front, out var matches);
                Assert.IsNotNull(existingArt, "the real variant's authored art must match the convention");
                Assert.AreEqual(1, matches);

                var childCount = sheet.Front.childCount;
                var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
                StudioSheetOps.SetFaceArt(sheet.Front, SheetFace.Front, sprite);

                Assert.AreEqual(childCount, sheet.Front.childCount, "must adopt the real art, not create a second renderer");
                Assert.AreEqual(sprite, existingArt.GetComponent<SpriteRenderer>().sprite);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void ClearFaceArt_RemovesTheAdoptedObject()
        {
            CreateTestSheet(out var front, out _);
            var sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            StudioSheetOps.SetFaceArt(front, SheetFace.Front, sprite);

            StudioSheetOps.ClearFaceArt(front);
            Assert.IsNull(StudioSheetOps.FindFaceArt(front, out _));
        }

        [Test]
        public void FindFaceArt_IgnoresElementsAndNonConventionalChildren()
        {
            CreateTestSheet(out var front, out _);
            var element = new GameObject("Wallish");
            element.transform.SetParent(front, false);
            element.transform.localPosition = new Vector3(0f, 0f, -0.05f);
            element.AddComponent<SpriteRenderer>();

            Assert.IsNull(StudioSheetOps.FindFaceArt(front, out var matches));
            Assert.AreEqual(0, matches);
        }

        // ----- Layer normalization -----

        [Test]
        public void NormalizeFaceLayers_SetsEveryDescendantAndIsThenANoOp()
        {
            Assert.IsTrue(FoldLayers.Valid, "project must define the SheetFront/SheetBack layers");
            var sheet = CreateTestSheet(out var front, out var back);
            var frontChild = new GameObject("FrontChild");
            frontChild.transform.SetParent(front, false);
            var grandchild = new GameObject("Visual");
            grandchild.transform.SetParent(frontChild.transform, false);
            var backChild = new GameObject("BackChild");
            backChild.transform.SetParent(back, false);

            var changed = StudioSheetOps.NormalizeFaceLayers(sheet);
            Assert.Greater(changed, 0);
            Assert.AreEqual(FoldLayers.Front, front.gameObject.layer);
            Assert.AreEqual(FoldLayers.Front, frontChild.layer);
            Assert.AreEqual(FoldLayers.Front, grandchild.layer, "layers must be recursive — Block's Visual child");
            Assert.AreEqual(FoldLayers.Back, backChild.layer);

            Assert.AreEqual(0, StudioSheetOps.NormalizeFaceLayers(sheet), "second run must not change (or dirty) anything");
        }
    }
}
