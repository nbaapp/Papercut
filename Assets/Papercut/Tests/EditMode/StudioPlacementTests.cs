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
        const string TreePath = "Assets/Papercut/Prefabs/Props/Tree.prefab";

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
        public void ResizableSet_IsTiledTerrainOnly()
        {
            Assert.IsTrue(StudioPlacement.IsResizable(LoadPrefab(WallPath)), "Wall (tiled placeholder)");
            Assert.IsTrue(StudioPlacement.IsResizable(LoadPrefab(WaterPath)), "Water (tiled placeholder)");
            Assert.IsTrue(StudioPlacement.IsResizable(LoadPrefab(GatePath)), "Gate (tiled placeholder)");
            Assert.IsFalse(StudioPlacement.IsResizable(LoadPrefab(HoldPlatePath)),
                "Hold Plate is a fixed hand-drawn button (Aaron, 2026-09-03: resizable only when the drawing tiles)");
            Assert.IsFalse(StudioPlacement.IsResizable(LoadPrefab(LatchPlatePath)), "Latch Plate, same");
            Assert.IsFalse(StudioPlacement.IsResizable(LoadPrefab(TreePath)), "Tree is a fixed drawing");
            Assert.IsFalse(StudioPlacement.IsResizable(LoadPrefab(BlockPath)),
                "Block has a root BoxCollider2D but must never be resizable — it would desync its pusher shape");
        }

        [Test]
        public void Tree_IsAProp_PickedAndFootprintedByItsDrawing()
        {
            var sheet = CreateTestSheet(out var front, out _);
            var prefab = LoadPrefab(TreePath);
            Assert.IsTrue(StudioPlacement.IsProp(prefab), "the Tree is art only (Aaron, 2026-09-10)");
            var tree = StudioPlacement.Place(prefab, front, SheetFace.Front, new Vector2(2f, 1f), 0f);

            Assert.IsTrue(StudioPlacement.CanEdit(tree, sheet), "the Tree root is the editable element");
            Assert.AreEqual(0, tree.GetComponentsInChildren<Collider2D>(true).Length, "no collision of its own");
            Assert.IsTrue(StudioPlacement.IsProp(tree));
            Assert.IsFalse(StudioPlacement.IsResizable(tree), "a hand-drawn sprite is a fixed-size drawing");
            Assert.AreEqual(FoldLayers.Front, tree.layer, "placement sets the face layer");

            // The footprint is the sprite rect at the root's scale, around the placed position.
            var expected = SpriteRectOf(tree, new Vector2(2f, 1f));
            var footprint = StudioPlacement.AuthoredFootprint(tree, front);
            AssertRect(expected, footprint);
            var pieces = new List<ConvexPolygon>();
            StudioPlacement.AuthoredFootprintPieces(tree, front, pieces);
            Assert.AreEqual(1, pieces.Count, "one piece: the sprite rect");
            Assert.AreEqual(expected.width * expected.height, pieces[0].Area, 1e-4f);
            var outline = new List<Vector2>();
            Assert.IsTrue(StudioPlacement.TryGetPropOutline(tree, front, outline));
            Assert.AreEqual(4, outline.Count);

            // Clicking the drawing picks the Tree; clicking beside it picks nothing.
            Assert.AreEqual(tree, StudioPlacement.PickElement(front, sheet, footprint.center), "inside the drawing");
            Assert.IsNull(StudioPlacement.PickElement(front, sheet, new Vector2(footprint.xMax + 0.5f, footprint.center.y)), "beside the Tree");

            // Collision is laid over a prop by hand: a Wall over the trunk is picked through the drawing
            // (the smallest hit wins), and the canopy above it is still the Tree.
            var wallAt = new Vector2(footprint.center.x, footprint.yMin + 0.2f);
            var wall = StudioPlacement.Place(LoadPrefab(WallPath), front, SheetFace.Front, wallAt, 0f);
            StudioPlacement.Resize(wall, new Rect(wallAt - new Vector2(0.25f, 0.25f), new Vector2(0.5f, 0.5f)), 0f);
            Assert.AreEqual(wall, StudioPlacement.PickElement(front, sheet, wallAt), "the Wall over the trunk");
            Assert.AreEqual(tree, StudioPlacement.PickElement(front, sheet, new Vector2(footprint.center.x, footprint.yMax - 0.1f)), "the canopy");

            var props = new List<GameObject>();
            StudioPlacement.CollectProps(front, sheet, props);
            CollectionAssert.AreEquivalent(new[] { tree }, props, "the Tree, never the Wall");
        }

        [Test]
        public void IsProp_IsABareSimpleSprite_NeverAColliderElement_NorTheFaceArt()
        {
            foreach (var path in new[] { WallPath, WaterPath, GatePath, BlockPath, HoldPlatePath, LatchPlatePath })
                Assert.IsFalse(StudioPlacement.IsProp(LoadPrefab(path)), path);
            Assert.IsTrue(StudioPlacement.IsProp(LoadPrefab(TreePath)));

            // The face art is a bare sprite too; CollectProps leaves it to the art slot.
            var sheet = CreateTestSheet(out var front, out _);
            var art = new GameObject("Art", typeof(SpriteRenderer));
            art.transform.SetParent(front, false);
            art.transform.localPosition = new Vector3(0f, 0f, StudioSheetOps.ArtZ);
            art.GetComponent<SpriteRenderer>().sprite = LoadPrefab(TreePath).GetComponent<SpriteRenderer>().sprite;
            Assert.AreEqual(art, StudioSheetOps.FindFaceArt(front, out _), "the test art is the face art");
            var props = new List<GameObject>();
            StudioPlacement.CollectProps(front, sheet, props);
            CollectionAssert.IsEmpty(props);
            Assert.IsNull(StudioPlacement.PickElement(front, sheet, Vector2.zero), "the art is never picked");
        }

        [Test]
        public void PlacedFootprint_KeepsThePrefabRootsScale_AndMatchesThePlacedElement()
        {
            CreateTestSheet(out var front, out _);
            foreach (var path in new[] { TreePath, WallPath, BlockPath, HoldPlatePath })
            {
                var prefab = LoadPrefab(path);
                var instance = StudioPlacement.Place(prefab, front, SheetFace.Front, Vector2.zero, 0f);
                AssertRect(StudioPlacement.AuthoredFootprint(instance, front), StudioPlacement.PlacedFootprint(prefab), path);
            }

            // The Tree root is scaled; its ghost must be the scaled drawing, not the unit sprite.
            var tree = LoadPrefab(TreePath);
            Assert.AreNotEqual(Vector3.one, tree.transform.localScale, "the Tree root is scaled, so this exercises the scale");
            AssertRect(SpriteRectOf(tree, Vector2.zero), StudioPlacement.PlacedFootprint(tree), "scaled sprite rect");
            var outline = new List<Vector2>();
            Assert.IsTrue(StudioPlacement.TryGetPlacedPropOutline(tree, outline));
            Assert.AreEqual(4, outline.Count);

            var wall = LoadPrefab(WallPath);
            Assert.IsTrue(StudioPlacement.TryGetPlacedOutline(wall.GetComponent<BoxCollider2D>(), wall, outline));
            Assert.AreEqual(4, outline.Count);
        }

        /// <summary>The sprite rect of a Simple-sprite element at <paramref name="at"/>, at the root's own scale.</summary>
        static Rect SpriteRectOf(GameObject element, Vector2 at)
        {
            var bounds = element.GetComponent<SpriteRenderer>().sprite.bounds;
            var scale = (Vector2)element.transform.localScale;
            return new Rect(at + Vector2.Scale(bounds.min, scale), Vector2.Scale(bounds.size, scale));
        }

        static void AssertRect(Rect expected, Rect actual, string what = "")
        {
            Assert.AreEqual(expected.xMin, actual.xMin, 1e-4f, $"{what} xMin");
            Assert.AreEqual(expected.yMin, actual.yMin, 1e-4f, $"{what} yMin");
            Assert.AreEqual(expected.xMax, actual.xMax, 1e-4f, $"{what} xMax");
            Assert.AreEqual(expected.yMax, actual.yMax, 1e-4f, $"{what} yMax");
            Assert.Greater(actual.width, 0f, $"{what} width");
        }

        [Test]
        public void StaticTerrain_IsABareCollisionBox_AndStillResizable()
        {
            // The map art draws walls and water (Aaron, 2026-09-03): no renderer of any kind on the prefab.
            foreach (var path in new[] { WallPath, WaterPath })
            {
                var prefab = LoadPrefab(path);
                Assert.IsNull(prefab.GetComponentInChildren<Renderer>(true), $"{path} must not draw anything");
                Assert.IsNotNull(prefab.GetComponent<TerrainRegion>(), path);
                Assert.IsNotNull(prefab.GetComponent<BoxCollider2D>(), path);
                Assert.IsTrue(StudioPlacement.IsResizable(prefab), $"{path}: a bare box is resizable");
            }
        }

        [Test]
        public void Resize_MovesCollider_Transform_AndTiledSpriteTogether()
        {
            CreateTestSheet(out var front, out _);
            var wall = StudioPlacement.Place(LoadPrefab(GatePath), front, SheetFace.Front, Vector2.zero, 0f); // Gate: the one terrain still drawn with a Tiled placeholder

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

        // ----- MoveMapped (place-through-fold drags) -----

        [Test]
        public void MoveMapped_SameFace_MovesInPlace()
        {
            var sheet = CreateTestSheet(out var front, out _);
            var wall = StudioPlacement.Place(LoadPrefab(WallPath), front, SheetFace.Front, new Vector2(1f, 1f), 0f);
            Assert.IsTrue(StudioPlacement.MoveMapped(wall, sheet, SheetFace.Front, new Vector2(2f, -1f), 0f, out _));
            Assert.AreEqual(front, wall.transform.parent);
            Assert.AreEqual(2f, wall.transform.localPosition.x, 1e-4f);
            Assert.AreEqual(-0.05f, wall.transform.localPosition.z, 1e-4f, "z preserved");
        }

        [Test]
        public void MoveMapped_CrossFace_ReparentsRelayers_AndOneUndoRestoresEverything()
        {
            Assert.IsTrue(FoldLayers.Valid);
            var sheet = CreateTestSheet(out var front, out var back);
            var block = StudioPlacement.Place(LoadPrefab(BlockPath), front, SheetFace.Front, new Vector2(1f, 1f), 0f);

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Assert.IsTrue(StudioPlacement.MoveMapped(block, sheet, SheetFace.Back, new Vector2(-3f, 1f), 0f, out var reason), reason);
            Undo.CollapseUndoOperations(group);

            Assert.AreEqual(back, block.transform.parent, "reparented under the Back root");
            Assert.AreEqual(-3f, block.transform.localPosition.x, 1e-4f);
            Assert.AreEqual(0f, block.transform.localPosition.z, 1e-4f, "Block's authored z preserved");
            foreach (var t in block.GetComponentsInChildren<Transform>(true))
                Assert.AreEqual(FoldLayers.Back, t.gameObject.layer, $"'{t.name}' re-layered to the Back");

            Undo.PerformUndo();
            Assert.AreEqual(front, block.transform.parent, "undo restores the parent");
            Assert.AreEqual(1f, block.transform.localPosition.x, 1e-4f, "undo restores the position");
            foreach (var t in block.GetComponentsInChildren<Transform>(true))
                Assert.AreEqual(FoldLayers.Front, t.gameObject.layer,
                    $"'{t.name}' layer must be undo-restored (round-1 B2's regression case)");
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

        // ----- Universal regions (above the sheet, Aaron 2026-09-28) -----

        const string UniversalWallPath = "Assets/Papercut/Prefabs/Terrain/Universal Wall.prefab";
        const string FilterWallPath = "Assets/Papercut/Prefabs/Terrain/Filter Wall.prefab";
        const string UniversalFilterWallPath = "Assets/Papercut/Prefabs/Terrain/Universal Filter Wall.prefab";

        static Transform AddAbove(Sheet sheet)
        {
            var above = new GameObject("Above").transform;
            above.SetParent(sheet.transform, false);
            above.localPosition = new Vector3(0f, 0f, 0.3f);
            var serialized = new SerializedObject(sheet);
            serialized.FindProperty("above").objectReferenceValue = above;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return above;
        }

        [Test]
        public void IsUniversal_ReadsThePrefabsFlag()
        {
            foreach (var path in new[] { UniversalWallPath, UniversalFilterWallPath, "Assets/Papercut/Prefabs/Terrain/Universal Wall (Polygon).prefab" })
                Assert.IsTrue(StudioPlacement.IsUniversal(LoadPrefab(path)), path);
            foreach (var path in new[] { WallPath, WaterPath, GatePath, FilterWallPath, BlockPath, TreePath })
                Assert.IsFalse(StudioPlacement.IsUniversal(LoadPrefab(path)), path);
        }

        [Test]
        public void TargetRoot_SendsAUniversalPrefabAbove_FromTheFrontPaneOnly()
        {
            var sheet = CreateTestSheet(out var front, out _);
            Assert.AreEqual(front, StudioPlacement.TargetRoot(LoadPrefab(WallPath), sheet, front, aboveEditable: true, out _), "a face prefab goes to the face root");
            Assert.IsNull(StudioPlacement.TargetRoot(LoadPrefab(UniversalWallPath), sheet, front, aboveEditable: true, out var noRoot), "no Above root yet");
            StringAssert.Contains("Above root", noRoot);

            var above = AddAbove(sheet);
            Assert.AreEqual(above, StudioPlacement.TargetRoot(LoadPrefab(UniversalWallPath), sheet, front, aboveEditable: true, out _));
            Assert.IsNull(StudioPlacement.TargetRoot(LoadPrefab(UniversalWallPath), sheet, front, aboveEditable: false, out var backPane), "the Back pane does not edit Above content");
            StringAssert.Contains("Front pane", backPane);
        }

        [Test]
        public void Place_UnderAbove_UsesTheDefaultLayer_AndIsEditable()
        {
            var sheet = CreateTestSheet(out var front, out _);
            var above = AddAbove(sheet);

            var placed = StudioPlacement.Place(LoadPrefab(UniversalWallPath), above, SheetFace.Front, new Vector2(1f, 2f), 0f);
            Assert.AreEqual(above, placed.transform.parent);
            Assert.AreEqual(Sheet.AboveLayer, placed.layer);
            foreach (var child in placed.GetComponentsInChildren<Transform>(true))
                Assert.AreEqual(Sheet.AboveLayer, child.gameObject.layer, child.name);
            Assert.AreEqual(-0.05f, placed.transform.localPosition.z, 1e-5f, "authored z kept");
            Assert.IsTrue(StudioPlacement.CanEdit(placed, sheet));
            Assert.IsFalse(StudioPlacement.CanEdit(above.gameObject, sheet), "the root itself is never an element");

            var wall = StudioPlacement.Place(LoadPrefab(WallPath), front, SheetFace.Front, Vector2.zero, 0f);
            Assert.AreEqual(FoldLayers.Front, wall.layer, "face placement unchanged");
        }

        [Test]
        public void MoveMapped_KeepsAUniversalElementAbove_AndRefusesTheBack()
        {
            var sheet = CreateTestSheet(out _, out _);
            var above = AddAbove(sheet);
            var placed = StudioPlacement.Place(LoadPrefab(UniversalWallPath), above, SheetFace.Front, new Vector2(1f, 2f), 0f);

            Assert.IsTrue(StudioPlacement.MoveMapped(placed, sheet, SheetFace.Front, new Vector2(-1f, 0.5f), 0f, out _));
            Assert.AreEqual(above, placed.transform.parent);
            Assert.AreEqual(new Vector2(-1f, 0.5f), (Vector2)placed.transform.localPosition);

            Assert.IsFalse(StudioPlacement.MoveMapped(placed, sheet, SheetFace.Back, Vector2.zero, 0f, out var reason));
            StringAssert.Contains("above the sheet", reason);
            Assert.AreEqual(above, placed.transform.parent, "never reparented");
        }

        [Test]
        public void PickElement_OverRoots_PrefersTheSmallerHit_AndAboveOnATie()
        {
            var sheet = CreateTestSheet(out var front, out _);
            var above = AddAbove(sheet);
            var wall = StudioPlacement.Place(LoadPrefab(WallPath), front, SheetFace.Front, Vector2.zero, 0f);
            var universal = StudioPlacement.Place(LoadPrefab(UniversalWallPath), above, SheetFace.Front, Vector2.zero, 0f);
            var roots = new[] { front, above };

            Assert.AreEqual(universal, StudioPlacement.PickElement(roots, sheet, Vector2.zero), "same 2x2 box on both roots: the one on top wins");

            StudioPlacement.Resize(wall, Rect.MinMaxRect(-0.5f, -0.5f, 0.5f, 0.5f), 0f);
            Assert.AreEqual(wall, StudioPlacement.PickElement(roots, sheet, Vector2.zero), "the smaller outline wins across roots");
            Assert.AreEqual(universal, StudioPlacement.PickElement(roots, sheet, new Vector2(0.8f, 0.8f)), "outside the small wall, the universal one");
            Assert.IsNull(StudioPlacement.PickElement(roots, sheet, new Vector2(4f, 4f)));
        }

        [Test]
        public void MagnetTargets_OverRoots_IncludeUniversalVertices()
        {
            var sheet = CreateTestSheet(out var front, out _);
            var above = AddAbove(sheet);
            StudioPlacement.Place(LoadPrefab(UniversalWallPath), above, SheetFace.Front, new Vector2(2f, 2f), 0f);
            var targets = new List<Vector2>();

            StudioPlacement.MagnetTargets(front, null, targets);
            Assert.AreEqual(0, targets.Count, "the face root alone has no regions");
            StudioPlacement.MagnetTargets(new[] { front, above }, null, targets);
            Assert.AreEqual(4, targets.Count, "the universal box's corners");
            CollectionAssert.Contains(targets, new Vector2(1f, 1f));
        }
    }
}
