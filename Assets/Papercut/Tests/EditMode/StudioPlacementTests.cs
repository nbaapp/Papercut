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
    /// The Studio's placement operations against the real element prefabs: authored-z preservation,
    /// recursive layers, the explicit resizable set, guard rails, and hit-testing.
    /// </summary>
    public sealed class StudioPlacementTests
    {
        const string WallPath = "Assets/Papercut/Prefabs/Terrain/Wall.prefab";
        const string WaterPath = "Assets/Papercut/Prefabs/Terrain/Water.prefab";
        const string GatePath = "Assets/Papercut/Prefabs/Terrain/Gate.prefab";
        const string BlockPath = "Assets/Papercut/Prefabs/Objects/Block.prefab";
        const string HoldPlatePath = "Assets/Papercut/Prefabs/Objects/Hold Plate.prefab";
        const string LatchPlatePath = "Assets/Papercut/Prefabs/Objects/Latch Plate.prefab";

        readonly List<GameObject> sceneObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in sceneObjects)
                if (go != null)
                    Object.DestroyImmediate(go);
            sceneObjects.Clear();
        }

        GameObject Track(GameObject go)
        {
            sceneObjects.Add(go);
            return go;
        }

        static GameObject LoadPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"prefab missing: {path}");
            return prefab;
        }

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

        // ----- Snap -----

        [Test]
        public void Snap_RoundsToIncrement_AndZeroMeansFree()
        {
            Assert.AreEqual(1.5f, StudioPlacement.Snap(1.4f, 0.5f), 1e-5f);
            Assert.AreEqual(1.25f, StudioPlacement.Snap(1.3f, 0.25f), 1e-5f);
            Assert.AreEqual(1.3721f, StudioPlacement.Snap(1.3721f, 0f), 1e-5f, "snap 0 must not round");
        }

        // ----- Place -----

        [Test]
        public void Place_PreservesThePrefabRootsAuthoredZ()
        {
            Assert.IsTrue(FoldLayers.Valid);
            CreateTestSheet(out var front, out _);

            var wall = StudioPlacement.Place(LoadPrefab(WallPath), front, SheetFace.Front, new Vector2(2f, -1f), 0f);
            Assert.IsNotNull(wall);
            Assert.AreEqual(2f, wall.transform.localPosition.x, 1e-4f);
            Assert.AreEqual(-1f, wall.transform.localPosition.y, 1e-4f);
            Assert.AreEqual(-0.05f, wall.transform.localPosition.z, 1e-4f,
                "a placed Wall must keep its authored z (−0.05) — z 0 is coplanar with the Surface quad");

            var block = StudioPlacement.Place(LoadPrefab(BlockPath), front, SheetFace.Front, Vector2.zero, 0f);
            Assert.AreEqual(0f, block.transform.localPosition.z, 1e-4f, "a placed Block keeps its authored z 0");
        }

        [Test]
        public void Place_SetsTheFaceLayerOnEveryChild()
        {
            Assert.IsTrue(FoldLayers.Valid);
            CreateTestSheet(out var front, out var back);

            var block = StudioPlacement.Place(LoadPrefab(BlockPath), front, SheetFace.Front, Vector2.zero, 0f);
            foreach (var t in block.GetComponentsInChildren<Transform>(true))
                Assert.AreEqual(FoldLayers.Front, t.gameObject.layer, $"'{t.name}' must be on the Front layer");

            var wall = StudioPlacement.Place(LoadPrefab(WallPath), back, SheetFace.Back, Vector2.zero, 0f);
            Assert.AreEqual(FoldLayers.Back, wall.layer);
        }

        [Test]
        public void Place_ParentsUnderTheGivenFaceRoot_AndSnaps()
        {
            CreateTestSheet(out var front, out var back);
            var onBack = StudioPlacement.Place(LoadPrefab(WallPath), back, SheetFace.Back, new Vector2(1.3f, 0.8f), 0.5f);
            Assert.AreEqual(back, onBack.transform.parent);
            Assert.AreEqual(1.5f, onBack.transform.localPosition.x, 1e-4f);
            Assert.AreEqual(1f, onBack.transform.localPosition.y, 1e-4f);
        }

        // ----- Resizable set -----

        [Test]
        public void ResizableSet_IsTerrainAndPlates_NeverBlock()
        {
            Assert.IsTrue(StudioPlacement.IsResizable(LoadPrefab(WallPath)), "Wall");
            Assert.IsTrue(StudioPlacement.IsResizable(LoadPrefab(WaterPath)), "Water");
            Assert.IsTrue(StudioPlacement.IsResizable(LoadPrefab(GatePath)), "Gate");
            Assert.IsTrue(StudioPlacement.IsResizable(LoadPrefab(HoldPlatePath)), "Hold Plate (Aaron: plates resizable)");
            Assert.IsTrue(StudioPlacement.IsResizable(LoadPrefab(LatchPlatePath)), "Latch Plate");
            Assert.IsFalse(StudioPlacement.IsResizable(LoadPrefab(BlockPath)),
                "Block has a root BoxCollider2D but must never be resizable — it would desync its pusher shape");
        }

        [Test]
        public void Resize_MovesCollider_Transform_AndTiledSpriteTogether()
        {
            CreateTestSheet(out var front, out _);
            var wall = StudioPlacement.Place(LoadPrefab(WallPath), front, SheetFace.Front, Vector2.zero, 0f);

            StudioPlacement.Resize(wall, Rect.MinMaxRect(1f, 2f, 4f, 4f), 0f);

            Assert.AreEqual(2.5f, wall.transform.localPosition.x, 1e-4f);
            Assert.AreEqual(3f, wall.transform.localPosition.y, 1e-4f);
            Assert.AreEqual(-0.05f, wall.transform.localPosition.z, 1e-4f, "resize must preserve z");
            var box = wall.GetComponent<BoxCollider2D>();
            Assert.AreEqual(new Vector2(3f, 2f), box.size);
            Assert.AreEqual(Vector2.zero, box.offset);
            var sprite = wall.GetComponent<SpriteRenderer>();
            Assert.AreEqual(new Vector2(3f, 2f), sprite.size, "tiled sprite must track the collider size");
        }

        // ----- Guard rails -----

        [Test]
        public void CanEdit_RefusesRootsSurfaceAndArt_AllowsElements()
        {
            var sheet = CreateTestSheet(out var front, out _);

            var surface = new GameObject("Surface");
            surface.transform.SetParent(front, false);
            surface.AddComponent<MeshFilter>();

            var art = new GameObject("Map 1");
            art.transform.SetParent(front, false);
            art.transform.localPosition = new Vector3(0f, 0f, StudioSheetOps.ArtZ);
            art.AddComponent<SpriteRenderer>().sortingOrder = 0;

            var wall = StudioPlacement.Place(LoadPrefab(WallPath), front, SheetFace.Front, Vector2.zero, 0f);

            Assert.IsFalse(StudioPlacement.CanEdit(front.gameObject, sheet), "face root");
            Assert.IsFalse(StudioPlacement.CanEdit(sheet.gameObject, sheet), "sheet root");
            Assert.IsFalse(StudioPlacement.CanEdit(surface, sheet), "Surface quad");
            Assert.IsFalse(StudioPlacement.CanEdit(art, sheet), "background art belongs to the art slot");
            Assert.IsTrue(StudioPlacement.CanEdit(wall, sheet), "a placed element must be editable");
        }

        // ----- Hit-testing -----

        [Test]
        public void PickElement_FindsTheSmallestHitAndMissesEmptySpace()
        {
            var sheet = CreateTestSheet(out var front, out _);
            var big = StudioPlacement.Place(LoadPrefab(WallPath), front, SheetFace.Front, Vector2.zero, 0f);
            StudioPlacement.Resize(big, Rect.MinMaxRect(-3f, -3f, 3f, 3f), 0f);
            var small = StudioPlacement.Place(LoadPrefab(WallPath), front, SheetFace.Front, Vector2.zero, 0f);
            StudioPlacement.Resize(small, Rect.MinMaxRect(-1f, -1f, 1f, 1f), 0f);

            Assert.AreEqual(small, StudioPlacement.PickElement(front, sheet, new Vector2(0.5f, 0.5f)),
                "overlapping hit must pick the smaller element");
            Assert.AreEqual(big, StudioPlacement.PickElement(front, sheet, new Vector2(2.5f, 2.5f)));
            Assert.IsNull(StudioPlacement.PickElement(front, sheet, new Vector2(5f, 5f)), "empty space picks nothing");
        }

        [Test]
        public void PickElement_ReturnsTheElementRootForNestedColliders()
        {
            var sheet = CreateTestSheet(out var front, out _);
            var block = StudioPlacement.Place(LoadPrefab(BlockPath), front, SheetFace.Front, new Vector2(1f, 1f), 0f);
            var picked = StudioPlacement.PickElement(front, sheet, new Vector2(1f, 1f));
            Assert.AreEqual(block, picked, "clicking any collider of an element must select the element root");
        }
    }
}
