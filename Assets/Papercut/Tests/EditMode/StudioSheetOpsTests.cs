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
    /// Sheet-level Studio operations: variant creation, the sheet map's slots / move / remove / delete,
    /// the face-art slot (including adoption of the existing 'Map 1'-style art), and layer normalization.
    /// Temp assets live under a throwaway folder deleted in teardown; scene objects are destroyed in teardown.
    /// </summary>
    /// <remarks>
    /// The DeleteSheet tests run the real operation, which ends with <c>Undo.ClearAll()</c> — so a test run
    /// in the open editor clears its Undo history. Inherent to the operation as specified (plan review N2).
    /// </remarks>
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

        // ----- Sheet sets -----

        [TestCase("Assets/Scenes/Desk.unity", "Assets/Papercut/Sheets/Desk")]
        [TestCase("Assets/Scenes/Test Desk.unity", "Assets/Papercut/Sheets/Test Desk")]
        [TestCase("", null)]
        public void SheetFolderForScene_IsTheRootSubfolderNamedAfterTheScene(string scenePath, string expected)
        {
            Assert.AreEqual(expected, StudioSheetOps.SheetFolderForScene(scenePath));
        }

        [Test]
        public void EverySheetSetBelongsToADeskSceneInTheBuild()
        {
            // The convention the Studio relies on, checked against the real project: a set folder is named
            // after a Desk scene (an orphan set means a scene was renamed without its sheets). A scene may
            // have no set yet — the official Desk starts empty and gets its folder with its first sheet.
            var sets = StudioSheetOps.FindSheetSets();
            var expected = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                expected.Add(StudioSheetOps.SheetFolderForScene(scene.path));
            foreach (var set in sets)
            {
                CollectionAssert.Contains(expected, set.Folder, $"sheet set '{set.Name}' matches no Desk scene in Build Settings");
                // The map moves sheets by renaming them, so every real variant must follow the name convention.
                foreach (var path in set.Paths)
                    Assert.IsTrue(StudioSheetOps.TryParseGridPosition(path, out _), $"'{path}' is not named 'Sheet (x,y)'");
            }

            // Not a fixed count: Aaron rearranges and deletes the test Desk's sheets with the map.
            Assert.IsTrue(sets.Exists(s => s.Name == "Test Desk"), "the test Desk has a sheet set");
            var testSet = sets.Find(s => s.Name == "Test Desk");
            Assert.IsNotEmpty(testSet.Paths, "the test Desk's hand-authored sheets live in its set");
        }

        [Test]
        public void FindSheetAssets_ListsOnlyTheFoldersOwnSheets()
        {
            // FindAssets recurses; a set must not pick up another set nested (by mistake) below it.
            AssetDatabase.CreateFolder(TempFolder, "Nested");
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk,
                StudioSheetOps.CreateSheet(new Vector2Int(1, 1), null, TempFolder, out _));
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk,
                StudioSheetOps.CreateSheet(new Vector2Int(2, 2), null, TempFolder + "/Nested", out _));

            var paths = StudioSheetOps.FindSheetAssets(TempFolder);
            Assert.AreEqual(1, paths.Count);
            Assert.AreEqual(StudioSheetOps.SheetAssetPath(new Vector2Int(1, 1), TempFolder), paths[0]);
        }

        // ----- CreateSheet -----

        [Test]
        public void CreateSheet_CreatesAMissingSetFolder()
        {
            // A freshly saved Desk scene has no set folder until its first sheet.
            var folder = TempFolder + "/New Desk";
            Assert.IsFalse(AssetDatabase.IsValidFolder(folder));
            var result = StudioSheetOps.CreateSheet(new Vector2Int(0, 0), null, folder, out var message);
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk, result, message);
            Assert.IsTrue(AssetDatabase.IsValidFolder(folder));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(StudioSheetOps.SheetAssetPath(new Vector2Int(0, 0), folder)));
        }

        [Test]
        public void CreateSheet_FailsWithoutAFolder()
        {
            var result = StudioSheetOps.CreateSheet(new Vector2Int(0, 0), null, null, out var message);
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Failed, result);
            StringAssert.Contains("saved", message);
        }

        [Test]
        public void CreateSheet_WithoutDesk_CreatesAVariantOfTheBaseSheet()
        {
            var result = StudioSheetOps.CreateSheet(new Vector2Int(9, 9), null, TempFolder, out var message);
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
            var result = StudioSheetOps.CreateSheet(new Vector2Int(8, 8), desk, TempFolder, out var message);
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
                StudioSheetOps.CreateSheet(new Vector2Int(7, 7), desk, TempFolder, out _));
            var result = StudioSheetOps.CreateSheet(new Vector2Int(7, 7), desk, TempFolder, out var message);
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.RefusedPositionOccupied, result, message);
        }

        [Test]
        public void CreateSheet_AddsAnExistingOffDeskVariantInsteadOfRefusing()
        {
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk,
                StudioSheetOps.CreateSheet(new Vector2Int(6, 6), null, TempFolder, out _));

            var desk = CreateTestDesk();
            var result = StudioSheetOps.CreateSheet(new Vector2Int(6, 6), desk, TempFolder, out var message);
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.AddedExistingToDesk, result, message);
            Assert.IsNotNull(StudioSheetOps.FindSheetOnDesk(desk, new Vector2Int(6, 6)));
        }

        // ----- Sheet map: names, slots -----

        static string TempPath(int x, int y) => StudioSheetOps.SheetAssetPath(new Vector2Int(x, y), TempFolder);

        static Vector2Int P(int x, int y) => new(x, y);

        /// <summary>An instance of the temp set's variant at <paramref name="variant"/>, placed on the Desk at <paramref name="at"/> (a mismatch or duplicate when they differ).</summary>
        Sheet AddInstance(Desk desk, Vector2Int variant, Vector2Int at)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TempPath(variant.x, variant.y));
            Assert.IsNotNull(prefab, $"variant {variant} must exist");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, desk.SheetGrid);
            var sheet = instance.GetComponent<Sheet>();
            var serialized = new SerializedObject(sheet);
            serialized.FindProperty("gridPosition").vector2IntValue = at;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return sheet;
        }

        /// <summary>A non-prefab Sheet on the Desk: an instance with no variant in any set.</summary>
        Sheet AddLooseSheet(Desk desk, Vector2Int at)
        {
            var go = new GameObject($"Loose {at}");
            go.transform.SetParent(desk.SheetGrid, false);
            // Sheet.OnValidate reports the missing roots on AddComponent and again when the grid position is applied.
            LogAssert.Expect(LogType.Error, new Regex("no Front root"));
            LogAssert.Expect(LogType.Error, new Regex("no Back root"));
            var sheet = go.AddComponent<Sheet>();
            LogAssert.Expect(LogType.Error, new Regex("no Front root"));
            LogAssert.Expect(LogType.Error, new Regex("no Back root"));
            var serialized = new SerializedObject(sheet);
            serialized.FindProperty("gridPosition").vector2IntValue = at;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return sheet;
        }

        PlayerMover AddPlayer(Desk desk, Vector2 worldPosition)
        {
            var go = new GameObject("Player");
            go.transform.SetParent(desk.SheetGrid, false);
            go.transform.position = worldPosition;
            return go.AddComponent<PlayerMover>();
        }

        [TestCase("Assets/X/Sheet (0,0).prefab", 0, 0)]
        [TestCase("Assets/X/Sheet (-1,2).prefab", -1, 2)]
        [TestCase("Assets/X/Nested/Sheet (10,-3).prefab", 10, -3)]
        public void TryParseGridPosition_ReadsTheFileName(string path, int x, int y)
        {
            Assert.IsTrue(StudioSheetOps.TryParseGridPosition(path, out var position));
            Assert.AreEqual(P(x, y), position);
        }

        [TestCase("Assets/X/Sheet Copy.prefab")]
        [TestCase("Assets/X/Sheet (1, 2).prefab")]
        [TestCase("Assets/X/Sheet (a,b).prefab")]
        [TestCase("Assets/X/Sheet (0,0) swapping.prefab")]
        [TestCase("Assets/X/Sheet.prefab")]
        [TestCase("Assets/X/Sheet (01,0).prefab")] // Non-canonical numerals would alias a position and shadow the real file.
        [TestCase("Assets/X/Sheet (-0,0).prefab")]
        [TestCase("Assets/X/Sheet (1,00).prefab")]
        [TestCase("")]
        public void TryParseGridPosition_RejectsOtherNames(string path)
        {
            Assert.IsFalse(StudioSheetOps.TryParseGridPosition(path, out _));
        }

        [Test]
        public void TryParseGridPosition_RoundTripsSheetAssetPath()
        {
            foreach (var p in new[] { P(0, 0), P(-4, 7), P(12, -12) })
            {
                Assert.IsTrue(StudioSheetOps.TryParseGridPosition(StudioSheetOps.SheetAssetPath(p, TempFolder), out var parsed));
                Assert.AreEqual(p, parsed);
            }
        }

        [Test]
        public void BuildSlots_MergesVariantsAndDeskInstancesByPosition()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _)); // matching
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk, StudioSheetOps.CreateSheet(P(1, 0), null, TempFolder, out _)); // off Desk
            AddLooseSheet(desk, P(2, 0)); // instance, no variant
            AddInstance(desk, P(0, 0), P(3, 0)); // mismatch: the (0,0) variant sitting at (3,0)
            AddInstance(desk, P(0, 0), P(0, 0)); // duplicate at (0,0)
            Assert.IsTrue(AssetDatabase.CopyAsset(TempPath(1, 0), TempFolder + "/Sheet Copy.prefab"));

            var unmapped = new List<string>();
            var slots = StudioSheetOps.BuildSlots(TempFolder, desk, unmapped);

            CollectionAssert.AreEqual(new[] { P(0, 0), P(1, 0), P(2, 0), P(3, 0) }, slots.ConvertAll(s => s.GridPosition), "one slot per position, sorted");
            var at00 = slots[0];
            Assert.AreEqual(TempPath(0, 0), at00.AssetPath);
            Assert.IsNotNull(at00.Instance);
            Assert.AreEqual(2, at00.InstanceCount);
            Assert.IsTrue(at00.Duplicate);
            Assert.IsFalse(at00.Mismatch);
            Assert.IsFalse(at00.Movable, "a duplicate position is not movable");

            var at10 = slots[1];
            Assert.AreEqual(TempPath(1, 0), at10.AssetPath);
            Assert.IsNull(at10.Instance);
            Assert.IsTrue(at10.Movable, "an off-Desk variant can still be renamed");
            Assert.IsFalse(at10.HasWarning);

            var at20 = slots[2];
            Assert.IsNull(at20.AssetPath);
            Assert.IsNotNull(at20.Instance);
            Assert.IsNull(at20.InstanceAssetPath);
            Assert.IsFalse(at20.Mismatch, "no variant on either side is not a mismatch");
            Assert.IsFalse(at20.Movable);
            Assert.IsTrue(at20.HasWarning);

            var at30 = slots[3];
            Assert.IsNull(at30.AssetPath);
            Assert.AreEqual(TempPath(0, 0), at30.InstanceAssetPath);
            Assert.IsTrue(at30.Mismatch);
            Assert.IsFalse(at30.Movable);

            CollectionAssert.AreEqual(new[] { TempFolder + "/Sheet Copy.prefab" }, unmapped);
        }

        // ----- Sheet map: move -----

        [Test]
        public void MoveSheet_ToAnEmptyCell_RenamesTheVariantAndUpdatesTheInstance()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _));
            var guid = AssetDatabase.AssetPathToGUID(TempPath(0, 0));
            var instance = StudioSheetOps.FindSheetOnDesk(desk, P(0, 0));

            var result = StudioSheetOps.MoveSheet(P(0, 0), P(2, 1), desk, TempFolder, out var message);

            Assert.AreEqual(StudioSheetOps.MoveSheetResult.Moved, result, message);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(TempPath(0, 0)), "old file gone");
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(TempPath(2, 1)), "same asset under the new name");
            Assert.IsNull(StudioSheetOps.FindSheetOnDesk(desk, P(0, 0)));
            Assert.AreSame(instance, StudioSheetOps.FindSheetOnDesk(desk, P(2, 1)), "the same instance, at the new position");
            Assert.AreEqual("Sheet (2,1)", instance.name);
            Assert.AreEqual(desk.GridToLocal(P(2, 1)), instance.transform.localPosition, "laid out at the new cell");
            Assert.AreEqual(TempPath(2, 1), PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance.gameObject));
            StringAssert.DoesNotContain("empty desk", message, "no player: no warning");
        }

        [Test]
        public void MoveSheet_OntoAnOccupiedCell_SwapsBothVariantsAndInstances()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _));
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(1, 0), desk, TempFolder, out _));
            var guidA = AssetDatabase.AssetPathToGUID(TempPath(0, 0));
            var guidB = AssetDatabase.AssetPathToGUID(TempPath(1, 0));
            var a = StudioSheetOps.FindSheetOnDesk(desk, P(0, 0));
            var b = StudioSheetOps.FindSheetOnDesk(desk, P(1, 0));

            var result = StudioSheetOps.MoveSheet(P(0, 0), P(1, 0), desk, TempFolder, out var message);

            Assert.AreEqual(StudioSheetOps.MoveSheetResult.Swapped, result, message);
            Assert.AreEqual(guidA, AssetDatabase.AssetPathToGUID(TempPath(1, 0)), "A's file now carries B's name");
            Assert.AreEqual(guidB, AssetDatabase.AssetPathToGUID(TempPath(0, 0)), "B's file now carries A's name");
            Assert.AreEqual(P(1, 0), a.GridPosition);
            Assert.AreEqual(P(0, 0), b.GridPosition);
            Assert.AreEqual("Sheet (1,0)", a.name);
            Assert.AreEqual("Sheet (0,0)", b.name);
            Assert.AreEqual(desk.GridToLocal(P(1, 0)), a.transform.localPosition);
            Assert.AreEqual(desk.GridToLocal(P(0, 0)), b.transform.localPosition);
            Assert.AreEqual(TempPath(1, 0), PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(a.gameObject));
            var paths = StudioSheetOps.FindSheetAssets(TempFolder);
            Assert.AreEqual(2, paths.Count, "no temporary 'swapping' file survives");
            foreach (var path in paths)
                StringAssert.DoesNotContain("swapping", path);
        }

        [Test]
        public void MoveSheet_SwapWithAnOffDeskVariant_LeavesItOffDesk()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _));
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk, StudioSheetOps.CreateSheet(P(1, 0), null, TempFolder, out _));
            var guidA = AssetDatabase.AssetPathToGUID(TempPath(0, 0));
            var guidB = AssetDatabase.AssetPathToGUID(TempPath(1, 0));
            var a = StudioSheetOps.FindSheetOnDesk(desk, P(0, 0));

            var result = StudioSheetOps.MoveSheet(P(0, 0), P(1, 0), desk, TempFolder, out var message);

            Assert.AreEqual(StudioSheetOps.MoveSheetResult.Swapped, result, message);
            Assert.AreEqual(guidA, AssetDatabase.AssetPathToGUID(TempPath(1, 0)));
            Assert.AreEqual(guidB, AssetDatabase.AssetPathToGUID(TempPath(0, 0)));
            Assert.AreEqual(P(1, 0), a.GridPosition);
            Assert.IsNull(StudioSheetOps.FindSheetOnDesk(desk, P(0, 0)), "the off-Desk variant stays off the Desk under its new name");
        }

        [Test]
        public void MoveSheet_WithoutADesk_RenamesOnly()
        {
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk, StudioSheetOps.CreateSheet(P(0, 0), null, TempFolder, out _));
            var result = StudioSheetOps.MoveSheet(P(0, 0), P(0, 5), null, TempFolder, out var message);
            Assert.AreEqual(StudioSheetOps.MoveSheetResult.Moved, result, message);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(TempPath(0, 0)));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(TempPath(0, 5)));
        }

        [Test]
        public void MoveSheet_RefusesWhatItCannotKeepConsistent()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _));
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(1, 0), desk, TempFolder, out _));
            AddInstance(desk, P(0, 0), P(3, 0)); // mismatch at (3,0)
            AddLooseSheet(desk, P(4, 0)); // no variant at (4,0)
            AddInstance(desk, P(1, 0), P(1, 0)); // duplicate at (1,0)

            Assert.AreEqual(StudioSheetOps.MoveSheetResult.NoChange, StudioSheetOps.MoveSheet(P(0, 0), P(0, 0), desk, TempFolder, out _));
            Assert.AreEqual(StudioSheetOps.MoveSheetResult.Refused, StudioSheetOps.MoveSheet(P(9, 9), P(8, 8), desk, TempFolder, out var none));
            StringAssert.Contains("no sheet", none);
            Assert.AreEqual(StudioSheetOps.MoveSheetResult.Refused, StudioSheetOps.MoveSheet(P(3, 0), P(5, 0), desk, TempFolder, out var mismatchSource));
            StringAssert.Contains("instance of", mismatchSource);
            Assert.AreEqual(StudioSheetOps.MoveSheetResult.Refused, StudioSheetOps.MoveSheet(P(0, 0), P(3, 0), desk, TempFolder, out var mismatchTarget));
            StringAssert.Contains("swap", mismatchTarget);
            Assert.AreEqual(StudioSheetOps.MoveSheetResult.Refused, StudioSheetOps.MoveSheet(P(4, 0), P(5, 0), desk, TempFolder, out var loose));
            StringAssert.Contains("no variant", loose);
            Assert.AreEqual(StudioSheetOps.MoveSheetResult.Refused, StudioSheetOps.MoveSheet(P(1, 0), P(5, 0), desk, TempFolder, out var duplicate));
            StringAssert.Contains("claim", duplicate);

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(TempPath(0, 0)), "a refusal renames nothing");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(TempPath(1, 0)));
            Assert.AreEqual(2, StudioSheetOps.FindSheetAssets(TempFolder).Count);
        }

        [Test]
        public void MoveSheet_LeavesThePlayerAtItsCell_AndWarnsWhenTheCellEmpties()
        {
            // Aaron, 2026-09-09: "Stay at the cell" — the player is never moved.
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _));
            var sheet = StudioSheetOps.FindSheetOnDesk(desk, P(0, 0));
            var player = AddPlayer(desk, sheet.Centre + Vector2.one);
            var before = player.transform.position;

            Assert.AreEqual(StudioSheetOps.MoveSheetResult.Moved, StudioSheetOps.MoveSheet(P(0, 0), P(2, 0), desk, TempFolder, out var moved), moved);
            Assert.AreEqual(before, player.transform.position, "the player did not ride the sheet");
            StringAssert.Contains("empty desk", moved);

            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _));
            Assert.AreEqual(StudioSheetOps.MoveSheetResult.Swapped, StudioSheetOps.MoveSheet(P(0, 0), P(2, 0), desk, TempFolder, out var swapped), swapped);
            Assert.AreEqual(before, player.transform.position);
            StringAssert.DoesNotContain("empty desk", swapped, "a swap leaves a sheet under the player");
        }

        [Test]
        public void MoveSheet_WarnsWhenASwapWithAnOffDeskVariantLeavesThePlayerOnEmptyDesk()
        {
            // A swap only puts a sheet back under the player when the other variant has a Desk instance.
            var desk = CreateTestDesk();

            // Source on the Desk with the player, target variant off the Desk: the source leaves, nothing arrives.
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _));
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk, StudioSheetOps.CreateSheet(P(1, 0), null, TempFolder, out _));
            var player = AddPlayer(desk, StudioSheetOps.FindSheetOnDesk(desk, P(0, 0)).Centre + Vector2.one);
            var before = player.transform.position;
            Assert.AreEqual(StudioSheetOps.MoveSheetResult.Swapped, StudioSheetOps.MoveSheet(P(0, 0), P(1, 0), desk, TempFolder, out var sourceLeft), sourceLeft);
            StringAssert.Contains("empty desk at (0,0)", sourceLeft);
            Assert.AreEqual(before, player.transform.position);

            // Source variant off the Desk, target on the Desk with the player: the target moves away to `from`.
            Object.DestroyImmediate(player.gameObject);
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk, StudioSheetOps.CreateSheet(P(2, 0), null, TempFolder, out _));
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(3, 0), desk, TempFolder, out _));
            AddPlayer(desk, StudioSheetOps.FindSheetOnDesk(desk, P(3, 0)).Centre + Vector2.one);
            Assert.AreEqual(StudioSheetOps.MoveSheetResult.Swapped, StudioSheetOps.MoveSheet(P(2, 0), P(3, 0), desk, TempFolder, out var targetLeft), targetLeft);
            StringAssert.Contains("empty desk at (3,0)", targetLeft);
            Assert.IsNull(StudioSheetOps.FindSheetOnDesk(desk, P(3, 0)));
            Assert.IsNotNull(StudioSheetOps.FindSheetOnDesk(desk, P(2, 0)));
        }

        [Test]
        public void MoveSheet_ToItsOwnCell_SaysSo()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _));
            Assert.AreEqual(StudioSheetOps.MoveSheetResult.NoChange, StudioSheetOps.MoveSheet(P(0, 0), P(0, 0), desk, TempFolder, out var message));
            StringAssert.Contains("already there", message, "the Move button must not look dead");
        }

        // ----- Sheet map: remove, delete -----

        [Test]
        public void DeleteSheet_RefusesADuplicatePosition()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _));
            AddInstance(desk, P(0, 0), P(0, 0));

            Assert.AreEqual(StudioSheetOps.DeleteSheetResult.Refused, StudioSheetOps.DeleteSheet(P(0, 0), desk, TempFolder, out var message));
            StringAssert.Contains("claim", message);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(TempPath(0, 0)), "a refusal deletes nothing");
            Assert.AreEqual(2, desk.GetComponentsInChildren<Sheet>(true).Length);
        }

        [Test]
        public void RemoveSheetFromDesk_KeepsTheVariant_AndCreateSheetPutsItBack()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _));

            Assert.IsTrue(StudioSheetOps.RemoveSheetFromDesk(StudioSheetOps.FindSheetOnDesk(desk, P(0, 0)), out var message), message);
            Assert.IsNull(StudioSheetOps.FindSheetOnDesk(desk, P(0, 0)));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(TempPath(0, 0)), "the variant stays in the set");
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.AddedExistingToDesk, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _));
        }

        [Test]
        public void RemoveSheetFromDesk_IsUndoable()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _));
            Undo.IncrementCurrentGroup();

            Assert.IsTrue(StudioSheetOps.RemoveSheetFromDesk(StudioSheetOps.FindSheetOnDesk(desk, P(0, 0)), out _));
            Undo.PerformUndo();

            Assert.IsNotNull(StudioSheetOps.FindSheetOnDesk(desk, P(0, 0)), "undo brings the instance back");
        }

        [Test]
        public void DeleteSheet_RemovesTheVariantAndTheInstance()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _));

            var result = StudioSheetOps.DeleteSheet(P(0, 0), desk, TempFolder, out var message);

            Assert.AreEqual(StudioSheetOps.DeleteSheetResult.Deleted, result, message);
            Assert.IsNull(StudioSheetOps.FindSheetOnDesk(desk, P(0, 0)));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(TempPath(0, 0)));
        }

        [Test]
        public void DeleteSheet_WithoutAnInstance_DeletesTheVariantOnly()
        {
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk, StudioSheetOps.CreateSheet(P(0, 0), null, TempFolder, out _));
            var result = StudioSheetOps.DeleteSheet(P(0, 0), null, TempFolder, out var message);
            Assert.AreEqual(StudioSheetOps.DeleteSheetResult.DeletedAssetOnly, result, message);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(TempPath(0, 0)));
        }

        [Test]
        public void DeleteSheet_RefusesAMismatchAndAMissingVariant()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk, StudioSheetOps.CreateSheet(P(0, 0), null, TempFolder, out _));
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk, StudioSheetOps.CreateSheet(P(3, 0), null, TempFolder, out _));
            AddInstance(desk, P(0, 0), P(3, 0)); // (3,0)'s Desk sheet is the (0,0) variant

            Assert.AreEqual(StudioSheetOps.DeleteSheetResult.Refused, StudioSheetOps.DeleteSheet(P(3, 0), desk, TempFolder, out var mismatch));
            StringAssert.Contains("instance of", mismatch);
            Assert.AreEqual(StudioSheetOps.DeleteSheetResult.Refused, StudioSheetOps.DeleteSheet(P(9, 9), desk, TempFolder, out var missing));
            StringAssert.Contains("no variant", missing);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(TempPath(3, 0)), "a refusal deletes nothing");
            Assert.IsNotNull(StudioSheetOps.FindSheetOnDesk(desk, P(3, 0)));
        }

        [Test]
        public void DeleteSheet_ClearsUndoSoRedoCannotResurrectAnInstanceOfTheDeletedFile()
        {
            var desk = CreateTestDesk();
            Undo.IncrementCurrentGroup();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(P(0, 0), desk, TempFolder, out _)); // undoable add
            Undo.IncrementCurrentGroup();

            Assert.AreEqual(StudioSheetOps.DeleteSheetResult.Deleted, StudioSheetOps.DeleteSheet(P(0, 0), desk, TempFolder, out var message), message);
            Undo.PerformUndo();
            Undo.PerformRedo();

            Assert.IsNull(StudioSheetOps.FindSheetOnDesk(desk, P(0, 0)), "no undo entry may bring back a sheet whose file is gone");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(TempPath(0, 0)));
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
        public void SetFaceArt_TurnsPortraitArtToLieLandscape_AndLeavesLandscapeArtAlone()
        {
            // Maps are exported portrait (2550 × 3300) and lie on the landscape sheet turned 90° CCW, as
            // Map 1 was hand-authored; a landscape or square image needs no turn.
            CreateTestSheet(out var front, out _);
            var portrait = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 2, 4), Vector2.one * 0.5f);
            var landscape = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 2), Vector2.one * 0.5f);
            var square = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.one * 0.5f);

            StudioSheetOps.SetFaceArt(front, SheetFace.Front, portrait);
            var art = StudioSheetOps.FindFaceArt(front, out _).transform;
            Assert.AreEqual(StudioSheetOps.PortraitArtRotationDegrees, art.localEulerAngles.z, 1e-3f, "created portrait art is turned");
            Assert.AreEqual(StudioSheetOps.ArtZ, art.localPosition.z, 1e-4f, "turning must not move the art off the convention z");

            StudioSheetOps.SetFaceArt(front, SheetFace.Front, landscape);
            Assert.AreEqual(0f, art.localEulerAngles.z, 1e-3f, "re-dropping landscape art on the adopted object straightens it");

            StudioSheetOps.SetFaceArt(front, SheetFace.Front, portrait);
            Assert.AreEqual(StudioSheetOps.PortraitArtRotationDegrees, art.localEulerAngles.z, 1e-3f, "re-dropping portrait art turns it again");

            StudioSheetOps.SetFaceArt(front, SheetFace.Front, square);
            Assert.AreEqual(0f, art.localEulerAngles.z, 1e-3f, "square art is not turned");
        }

        [Test]
        public void ArtRotationFor_MatchesTheHandAuthoredMap1()
        {
            // Map 1's art object in the test Desk's Sheet (0,0) is the precedent: same turn, same direction.
            var root = PrefabUtility.LoadPrefabContents("Assets/Papercut/Sheets/Test Desk/Sheet (0,0).prefab");
            try
            {
                var sheet = root.GetComponent<Sheet>();
                var art = StudioSheetOps.FindFaceArt(sheet.Front, out _);
                Assert.IsNotNull(art);
                var sprite = art.GetComponent<SpriteRenderer>().sprite;
                Assert.IsNotNull(sprite, "Map 1 art must have its sprite");
                Assert.Greater(sprite.rect.height, sprite.rect.width, "Map 1 is exported portrait");
                Assert.AreEqual(0f, Quaternion.Angle(art.transform.localRotation, StudioSheetOps.ArtRotationFor(sprite)), 1e-3f,
                    "the slot's turn for a portrait sprite must equal Map 1's hand-authored rotation");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
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
            Assert.IsTrue(AssetDatabase.CopyAsset("Assets/Papercut/Sheets/Test Desk/Sheet (0,0).prefab", copyPath));
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
