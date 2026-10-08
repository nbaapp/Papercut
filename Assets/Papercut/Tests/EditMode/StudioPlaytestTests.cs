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
    /// The Studio's playtest pre-flight: the per-sheet spawn pref (round trip, clamp, following the sheet map's
    /// renames), the edit-mode room test against every arrival obstacle the game would refuse on (walls, gated
    /// terrain without the ability, blocks - plan review B2), and finding the Desk instance of the edited asset
    /// by path rather than by the variant's never-authored grid position (plan review B1).
    /// </summary>
    public sealed class StudioPlaytestTests
    {
        const string TempFolder = "Assets/Temp_StudioPlaytestTests";
        const string PathA = TempFolder + "/Sheet (7,7).prefab";
        const string PathB = TempFolder + "/Sheet (8,8).prefab";

        static readonly StudioPlaytest.PlayerFootprint Footprint = new(new Vector2(0.44f, 0.7f), new Vector2(-0.03f, -0.04f));

        readonly List<GameObject> sceneObjects = new();

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "Temp_StudioPlaytestTests");
            StudioPlaytest.ClearSpawn(PathA);
            StudioPlaytest.ClearSpawn(PathB);
        }

        [TearDown]
        public void TearDown()
        {
            StudioPlaytest.ClearSpawn(PathA);
            StudioPlaytest.ClearSpawn(PathB);
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

        Sheet CreateTestSheet(out Transform front)
        {
            var go = Track(new GameObject("TestSheet"));
            front = new GameObject("Front").transform;
            front.SetParent(go.transform, false);
            var back = new GameObject("Back").transform;
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

        PlayerAbilities CreateAbilities(Ability abilities)
        {
            var go = Track(new GameObject("Abilities"));
            var component = go.AddComponent<PlayerAbilities>();
            var serialized = new SerializedObject(component);
            serialized.FindProperty("abilities").intValue = (int)abilities;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return component;
        }

        static GameObject Prefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            return prefab;
        }

        // ----- Spawn pref -----

        [Test]
        public void Spawn_UnsetIsTheCentre_AndRoundTrips()
        {
            Assert.IsFalse(StudioPlaytest.HasSpawn(PathA));
            Assert.AreEqual(Vector2.zero, StudioPlaytest.GetSpawn(PathA));

            StudioPlaytest.SetSpawn(PathA, new Vector2(-2.375f, 1.5f), Footprint);

            Assert.IsTrue(StudioPlaytest.HasSpawn(PathA));
            var stored = StudioPlaytest.GetSpawn(PathA);
            Assert.AreEqual(-2.375f, stored.x, 1e-6f);
            Assert.AreEqual(1.5f, stored.y, 1e-6f);
            Assert.IsFalse(StudioPlaytest.HasSpawn(PathB), "prefs are per sheet asset");

            StudioPlaytest.ClearSpawn(PathA);
            Assert.IsFalse(StudioPlaytest.HasSpawn(PathA));
        }

        [Test]
        public void Spawn_IsClampedSoThePlayerBoxLiesOnTheSheet()
        {
            StudioPlaytest.SetSpawn(PathA, new Vector2(10f, -10f), Footprint);
            var stored = StudioPlaytest.GetSpawn(PathA);
            // Box max x = p.x + offset.x + size.x/2 must be <= 5.5; box min y = p.y + offset.y - size.y/2 >= -4.25.
            Assert.AreEqual(5.5f - (-0.03f) - 0.22f, stored.x, 1e-5f);
            Assert.AreEqual(-4.25f - (-0.04f) + 0.35f, stored.y, 1e-5f);
            var box = Footprint.At(stored);
            Assert.LessOrEqual(box.xMax, 5.5f + 1e-5f);
            Assert.GreaterOrEqual(box.yMin, -4.25f - 1e-5f);
        }

        [Test]
        public void Spawn_FollowsARename_AndASwap()
        {
            StudioPlaytest.SetSpawn(PathA, new Vector2(1f, 1f), Footprint);
            StudioPlaytest.RekeySpawn(PathA, PathB);
            Assert.IsFalse(StudioPlaytest.HasSpawn(PathA));
            Assert.AreEqual(new Vector2(1f, 1f), StudioPlaytest.GetSpawn(PathB));

            StudioPlaytest.SetSpawn(PathA, new Vector2(-1f, -1f), Footprint);
            StudioPlaytest.SwapSpawns(PathA, PathB);
            Assert.AreEqual(new Vector2(1f, 1f), StudioPlaytest.GetSpawn(PathA));
            Assert.AreEqual(new Vector2(-1f, -1f), StudioPlaytest.GetSpawn(PathB));

            // A swap with an unset side moves the value and leaves the other side unset.
            StudioPlaytest.ClearSpawn(PathB);
            StudioPlaytest.SwapSpawns(PathA, PathB);
            Assert.IsFalse(StudioPlaytest.HasSpawn(PathA));
            Assert.AreEqual(new Vector2(1f, 1f), StudioPlaytest.GetSpawn(PathB));
        }

        // ----- Room -----

        [Test]
        public void SpawnHasRoom_RefusesWallsBlocksAndOffSheet_AllowsOpenGround()
        {
            var sheet = CreateTestSheet(out var front);
            StudioPlacement.Place(Prefab("Assets/Papercut/Prefabs/Terrain/Wall.prefab"), front, SheetFace.Front, new Vector2(2f, 0f), 0f);
            StudioPlacement.Place(Prefab("Assets/Papercut/Prefabs/Objects/Block.prefab"), front, SheetFace.Front, new Vector2(-3f, 2f), 0f);
            var none = CreateAbilities(Ability.None);

            Assert.IsFalse(StudioPlaytest.SpawnHasRoom(sheet, new Vector2(2f, 0f), Footprint, none), "in a wall");
            Assert.IsFalse(StudioPlaytest.SpawnHasRoom(sheet, new Vector2(-3f, 2f), Footprint, none), "on a block (an arrival obstacle, like the game's room test)");
            Assert.IsFalse(StudioPlaytest.SpawnHasRoom(sheet, new Vector2(5.5f, 0f), Footprint, none), "half off the sheet");
            Assert.IsTrue(StudioPlaytest.SpawnHasRoom(sheet, new Vector2(0f, -2f), Footprint, none), "open ground");
            Assert.IsTrue(StudioPlaytest.SpawnHasRoom(sheet, new Vector2(0f, -2f), Footprint, null), "no player abilities at all still stands on open ground");
        }

        [Test]
        public void SpawnHasRoom_GatedTerrain_NeedsTheAbility()
        {
            var sheet = CreateTestSheet(out var front);
            StudioPlacement.Place(Prefab("Assets/Papercut/Prefabs/Terrain/Water.prefab"), front, SheetFace.Front, new Vector2(-2f, -2f), 0f);

            Assert.IsFalse(StudioPlaytest.SpawnHasRoom(sheet, new Vector2(-2f, -2f), Footprint, CreateAbilities(Ability.None)), "water without Swim");
            Assert.IsFalse(StudioPlaytest.SpawnHasRoom(sheet, new Vector2(-2f, -2f), Footprint, null), "water with no abilities component");
            Assert.IsTrue(StudioPlaytest.SpawnHasRoom(sheet, new Vector2(-2f, -2f), Footprint, CreateAbilities(Ability.Swim)), "water with Swim");
        }

        // ----- The Desk instance of the edited asset -----

        [Test]
        public void ResolveDeskInstance_FindsTheInstanceByAssetPath_WithTheDesksGridPosition()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(new Vector2Int(2, 1), desk, TempFolder, out var message), message);
            var path = StudioSheetOps.SheetAssetPath(new Vector2Int(2, 1), TempFolder);

            // The variant itself never carries a grid position (only the Desk instance does), so a resolve by the
            // stage sheet's own GridPosition would have found (0,0) - the bug the button exists to avoid.
            var refusal = StudioPlaytest.ResolveDeskInstance(path, desk, TempFolder, out var instance, out var reason);

            Assert.AreEqual(StudioPlaytest.PlaytestRefusal.None, refusal, reason);
            Assert.AreEqual(new Vector2Int(2, 1), instance.GridPosition);
            Assert.AreEqual(path, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance.gameObject));
        }

        [Test]
        public void ResolveDeskInstance_RefusesMissingDuplicateAndMismatchedInstances()
        {
            var desk = CreateTestDesk();
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.Created, StudioSheetOps.CreateSheet(new Vector2Int(2, 1), desk, TempFolder, out _));
            Assert.AreEqual(StudioSheetOps.CreateSheetResult.CreatedWithoutDesk, StudioSheetOps.CreateSheet(new Vector2Int(3, 3), null, TempFolder, out _));
            var pathA = StudioSheetOps.SheetAssetPath(new Vector2Int(2, 1), TempFolder);
            var pathB = StudioSheetOps.SheetAssetPath(new Vector2Int(3, 3), TempFolder);

            Assert.AreEqual(StudioPlaytest.PlaytestRefusal.NotOnDesk, StudioPlaytest.ResolveDeskInstance(pathB, desk, TempFolder, out _, out var reason), "variant with no instance");
            StringAssert.Contains("not on the open Desk", reason);
            Assert.AreEqual(StudioPlaytest.PlaytestRefusal.NotOnDesk, StudioPlaytest.ResolveDeskInstance("Assets/Elsewhere/Sheet (2,1).prefab", desk, TempFolder, out _, out reason), "asset outside the Desk's set");
            StringAssert.Contains("sheet set", reason);
            Assert.AreEqual(StudioPlaytest.PlaytestRefusal.NoDesk, StudioPlaytest.ResolveDeskInstance(pathA, null, TempFolder, out _, out _));

            // A second sheet claiming (2,1).
            var extra = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(pathB), desk.SheetGrid);
            var extraSerialized = new SerializedObject(extra.GetComponent<Sheet>());
            extraSerialized.FindProperty("gridPosition").vector2IntValue = new Vector2Int(2, 1);
            extraSerialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreEqual(StudioPlaytest.PlaytestRefusal.Duplicate, StudioPlaytest.ResolveDeskInstance(pathA, desk, TempFolder, out _, out reason));
            StringAssert.Contains("2 sheets", reason);

            // Only the other asset's instance left at (2,1): a mismatch, the map's own warning.
            foreach (var sheet in desk.GetComponentsInChildren<Sheet>(true))
                if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(sheet.gameObject) == pathA)
                    Object.DestroyImmediate(sheet.gameObject);
            Assert.AreEqual(StudioPlaytest.PlaytestRefusal.Mismatch, StudioPlaytest.ResolveDeskInstance(pathA, desk, TempFolder, out _, out reason));
            StringAssert.Contains(pathB, reason);
        }

        // ----- Universal and filter walls (2026-09-28) -----

        [Test]
        public void SpawnHasRoom_UniversalWallRefuses_FilterWallsDoNot()
        {
            var sheet = CreateTestSheet(out var front);
            var above = new GameObject("Above").transform;
            above.SetParent(sheet.transform, false);
            var serialized = new SerializedObject(sheet);
            serialized.FindProperty("above").objectReferenceValue = above;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var none = CreateAbilities(Ability.None);

            StudioPlacement.Place(Prefab("Assets/Papercut/Prefabs/Terrain/Universal Wall.prefab"), above, SheetFace.Front, new Vector2(2f, 0f), 0f);
            StudioPlacement.Place(Prefab("Assets/Papercut/Prefabs/Terrain/Filter Wall.prefab"), front, SheetFace.Front, new Vector2(-2f, 0f), 0f);
            StudioPlacement.Place(Prefab("Assets/Papercut/Prefabs/Terrain/Universal Filter Wall.prefab"), above, SheetFace.Front, new Vector2(0f, 2.5f), 0f);

            Assert.IsFalse(StudioPlaytest.SpawnHasRoom(sheet, new Vector2(2f, 0f), Footprint, none), "in a universal wall");
            Assert.IsFalse(StudioPlaytest.SpawnHasRoom(sheet, new Vector2(2f, 0f), Footprint, null), "in a universal wall, no player either");
            Assert.IsTrue(StudioPlaytest.SpawnHasRoom(sheet, new Vector2(-2f, 0f), Footprint, none), "a filter wall never blocks the player");
            Assert.IsTrue(StudioPlaytest.SpawnHasRoom(sheet, new Vector2(-2f, 0f), Footprint, null), "nor with no player");
            Assert.IsTrue(StudioPlaytest.SpawnHasRoom(sheet, new Vector2(0f, 2.5f), Footprint, none), "a universal filter wall neither");
        }
    }
}
