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
    /// Polygon terrain in the Sheet Studio (2026-09-10): the two prefabs, drawing (PlacePolygon / the draft's
    /// finish), vertex operations, footprint pieces, the magnet, picking inside a concave outline, and the
    /// fold-preview mapping of pieces.
    /// </summary>
    public sealed class StudioPolygonTests
    {
        const string WallPolygonPath = "Assets/Papercut/Prefabs/Terrain/Wall (Polygon).prefab";
        const string WaterPolygonPath = "Assets/Papercut/Prefabs/Terrain/Water (Polygon).prefab";
        const string WallPath = "Assets/Papercut/Prefabs/Terrain/Wall.prefab";
        const string WaterPath = "Assets/Papercut/Prefabs/Terrain/Water.prefab";
        const string GatePath = "Assets/Papercut/Prefabs/Terrain/Gate.prefab";
        const string TreePath = "Assets/Papercut/Prefabs/Props/Tree.prefab";
        const string BlockPath = "Assets/Papercut/Prefabs/Objects/Block.prefab";
        const string HoldPlatePath = "Assets/Papercut/Prefabs/Objects/Hold Plate.prefab";

        /// <summary>An L at (1,1): 3×3 minus the top-right 2×2. Area 5.</summary>
        static readonly Vector2[] L = { new(1f, 1f), new(4f, 1f), new(4f, 2f), new(2f, 2f), new(2f, 4f), new(1f, 4f) };

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

        static void AssertOutline(IReadOnlyList<Vector2> expected, List<Vector2> actual)
        {
            Assert.AreEqual(expected.Count, actual.Count, "vertex count");
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].x, actual[i].x, 1e-4f, $"vertex {i} x");
                Assert.AreEqual(expected[i].y, actual[i].y, 1e-4f, $"vertex {i} y");
            }
        }

        // ----- Prefabs -----

        [Test]
        public void PolygonPrefabs_AreBarePolygonTerrain_AndNothingElseIs()
        {
            foreach (var (path, ability) in new[] { (WallPolygonPath, Ability.None), (WaterPolygonPath, Ability.Swim) })
            {
                var prefab = LoadPrefab(path);
                Assert.IsTrue(StudioPlacement.IsPolygonTerrain(prefab), path);
                Assert.IsNull(prefab.GetComponentInChildren<Renderer>(true), $"{path}: the map art draws it");
                var polygon = prefab.GetComponent<PolygonCollider2D>();
                Assert.AreEqual(1, polygon.pathCount, $"{path}: one outline");
                Assert.IsFalse(polygon.isTrigger, path);
                Assert.AreEqual(-0.05f, prefab.transform.localPosition.z, 1e-4f, $"{path}: element z convention");
                var region = prefab.GetComponent<TerrainRegion>();
                Assert.AreEqual((int)ability, new SerializedObject(region).FindProperty("requiredAbility").intValue, path);
                Assert.IsFalse(StudioPlacement.IsResizable(prefab), $"{path}: vertex editing, not box resizing");
            }
            foreach (var path in new[] { WallPath, WaterPath, GatePath, TreePath, BlockPath, HoldPlatePath })
                Assert.IsFalse(StudioPlacement.IsPolygonTerrain(LoadPrefab(path)), path);
        }

        // ----- Drawing -----

        [Test]
        public void PlacePolygon_SitsAtTheBoundsCentre_KeepsZ_SetsTheLayer_AndHoldsTheOutline()
        {
            Assert.IsTrue(FoldLayers.Valid);
            CreateTestSheet(out _, out var back);
            var region = StudioPlacement.PlacePolygon(LoadPrefab(WaterPolygonPath), back, SheetFace.Back, L, 0f);
            Assert.IsNotNull(region);
            Assert.AreEqual(back, region.transform.parent);
            Assert.AreEqual(2.5f, region.transform.localPosition.x, 1e-4f);
            Assert.AreEqual(2.5f, region.transform.localPosition.y, 1e-4f);
            Assert.AreEqual(-0.05f, region.transform.localPosition.z, 1e-4f);
            Assert.AreEqual(FoldLayers.Back, region.layer);

            var outline = new List<Vector2>();
            Assert.IsTrue(StudioPlacement.GetOutline(region, back, outline));
            AssertOutline(L, outline);
            Assert.IsTrue(StudioPlacement.IsValidOutline(region, back, out _));
        }

        [Test]
        public void PlacePolygon_Snaps_AndRefusesANonSimpleOutline()
        {
            CreateTestSheet(out var front, out _);
            var rough = new[] { new Vector2(0.1f, 0.1f), new Vector2(2.9f, 0.2f), new Vector2(3.1f, 2.9f) };
            var region = StudioPlacement.PlacePolygon(LoadPrefab(WallPolygonPath), front, SheetFace.Front, rough, 0.5f);
            var outline = new List<Vector2>();
            StudioPlacement.GetOutline(region, front, outline);
            AssertOutline(new[] { new Vector2(0f, 0f), new Vector2(3f, 0f), new Vector2(3f, 3f) }, outline);

            LogAssert.Expect(LogType.Error, new Regex("crosses itself"));
            var bowTie = new[] { new Vector2(0f, 0f), new Vector2(2f, 2f), new Vector2(2f, 0f), new Vector2(0f, 2f) };
            Assert.IsNull(StudioPlacement.PlacePolygon(LoadPrefab(WallPolygonPath), front, SheetFace.Front, bowTie, 0f));

            LogAssert.Expect(LogType.Error, new Regex("not a polygon terrain prefab"));
            Assert.IsNull(StudioPlacement.PlacePolygon(LoadPrefab(WallPath), front, SheetFace.Front, L, 0f));
        }

        [Test]
        public void Draft_FinishPlacesTheRegionOnItsFace_AndClears()
        {
            var sheet = CreateTestSheet(out var front, out _);
            var draft = new StudioPolygonDraft();
            draft.Start(LoadPrefab(WallPolygonPath), SheetFace.Front);
            foreach (var p in L)
                Assert.IsTrue(draft.TryAdd(p, out _));

            Assert.IsTrue(draft.TryFinish(sheet, out var placed, out var reason), reason);
            Assert.IsNotNull(placed);
            Assert.AreEqual(front, placed.transform.parent);
            Assert.IsFalse(draft.IsActive);
            var outline = new List<Vector2>();
            StudioPlacement.GetOutline(placed, front, outline);
            AssertOutline(L, outline);
        }

        // ----- Vertex operations -----

        [Test]
        public void SetInsertRemoveVertex_EditTheOutline_AndRemoveRefusesBelowThreeOrWhenInvalid()
        {
            CreateTestSheet(out var front, out _);
            var region = StudioPlacement.PlacePolygon(LoadPrefab(WallPolygonPath), front, SheetFace.Front, L, 0f);
            var outline = new List<Vector2>();

            StudioPlacement.SetVertex(region, front, 2, new Vector2(4.2f, 2.4f), 0.5f);
            StudioPlacement.GetOutline(region, front, outline);
            Assert.AreEqual(new Vector2(4f, 2.5f), outline[2], "snapped");
            Assert.AreEqual(6, outline.Count);

            StudioPlacement.InsertVertex(region, front, 0, new Vector2(2.5f, 0.5f), 0f);
            StudioPlacement.GetOutline(region, front, outline);
            Assert.AreEqual(7, outline.Count);
            Assert.AreEqual(new Vector2(2.5f, 0.5f), outline[1], "inserted after vertex 0, on edge 0");

            Assert.IsTrue(StudioPlacement.TryRemoveVertex(region, front, 1, out var reason), reason);
            StudioPlacement.GetOutline(region, front, outline);
            Assert.AreEqual(6, outline.Count);

            // A spiral: removing the inner hook's corner (3,1) would join (3,3) to (0,1) straight through the arm at x = 1.
            var spiral = StudioPlacement.PlacePolygon(LoadPrefab(WallPolygonPath), front, SheetFace.Front, new[]
            {
                new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 4f), new Vector2(1f, 4f), new Vector2(1f, 1.5f),
                new Vector2(2f, 1.5f), new Vector2(2f, 3f), new Vector2(3f, 3f), new Vector2(3f, 1f), new Vector2(0f, 1f),
            }, 0f);
            Assert.IsNotNull(spiral, "the spiral is a simple polygon");
            Assert.IsFalse(StudioPlacement.TryRemoveVertex(spiral, front, 8, out reason), "(3,1) gone: (3,3)→(0,1) crosses the arm (1,4)→(1,1.5)");
            StringAssert.Contains("crosses", reason);
            StudioPlacement.GetOutline(spiral, front, outline);
            Assert.AreEqual(10, outline.Count, "a refused removal changes nothing");
            Assert.IsTrue(StudioPlacement.TryRemoveVertex(spiral, front, 6, out reason), reason); // (2,3): (2,1.5)→(3,3) is fine

            var triangle = StudioPlacement.PlacePolygon(LoadPrefab(WallPolygonPath), front, SheetFace.Front,
                new[] { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 2f) }, 0f);
            Assert.IsFalse(StudioPlacement.TryRemoveVertex(triangle, front, 0, out reason));
            StringAssert.Contains("three", reason);
        }

        [Test]
        public void SetOutline_IsUndoable_AsOneStep()
        {
            CreateTestSheet(out var front, out _);
            var region = StudioPlacement.PlacePolygon(LoadPrefab(WallPolygonPath), front, SheetFace.Front, L, 0f);
            Undo.IncrementCurrentGroup();
            var moved = new List<Vector2>(L) { [0] = new Vector2(0f, 0f) };
            StudioPlacement.SetOutline(region, front, moved, "test");
            var outline = new List<Vector2>();
            StudioPlacement.GetOutline(region, front, outline);
            Assert.AreEqual(Vector2.zero, outline[0]);

            Undo.PerformUndo();
            StudioPlacement.GetOutline(region, front, outline);
            AssertOutline(L, outline);
        }

        [Test]
        public void RevertAllDownToGroup_UndoesAnInsertAndEveryMoveOfOnePress()
        {
            // The pane's revert-on-release: everything recorded since the press — an edge insert and the drags that
            // followed — comes back in one step, and the region itself (created before the group) survives.
            CreateTestSheet(out var front, out _);
            var region = StudioPlacement.PlacePolygon(LoadPrefab(WallPolygonPath), front, SheetFace.Front, L, 0f);
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();

            StudioPlacement.InsertVertex(region, front, 0, new Vector2(2.5f, 1f), 0f);
            StudioPlacement.SetVertex(region, front, 1, new Vector2(2.5f, 3f), 0f);
            StudioPlacement.SetVertex(region, front, 1, new Vector2(0.5f, 3.5f), 0f); // Crosses the arm: what a release would revert.
            Assert.IsFalse(StudioPlacement.IsValidOutline(region, front, out _), "the drag left the outline self-crossing");

            Undo.RevertAllDownToGroup(group);

            Assert.IsNotNull(region, "the region was created before the group and survives");
            var outline = new List<Vector2>();
            StudioPlacement.GetOutline(region, front, outline);
            AssertOutline(L, outline);
        }

        // ----- Footprint pieces, picking, magnet, fold mapping -----

        [Test]
        public void AuthoredFootprintPieces_PolygonIsItsDecomposition_PropIsItsSpriteRect()
        {
            CreateTestSheet(out var front, out _);
            var region = StudioPlacement.PlacePolygon(LoadPrefab(WallPolygonPath), front, SheetFace.Front, L, 0f);
            var pieces = new List<ConvexPolygon>();
            StudioPlacement.AuthoredFootprintPieces(region, front, pieces);
            Assert.AreEqual(2, pieces.Count);
            var area = 0f;
            foreach (var piece in pieces)
                area += piece.Area;
            Assert.AreEqual(5f, area, 1e-4f);

            var tree = StudioPlacement.Place(LoadPrefab(TreePath), front, SheetFace.Front, new Vector2(2f, 1f), 0f);
            StudioPlacement.AuthoredFootprintPieces(tree, front, pieces);
            Assert.AreEqual(1, pieces.Count, "a prop's one piece: its sprite rect");
            var footprint = StudioPlacement.AuthoredFootprint(tree, front);
            Assert.AreEqual(footprint.width * footprint.height, pieces[0].Area, 1e-4f);

            var block = StudioPlacement.Place(LoadPrefab(BlockPath), front, SheetFace.Front, new Vector2(-2f, -2f), 0f);
            StudioPlacement.AuthoredFootprintPieces(block, front, pieces);
            Assert.AreEqual(1, pieces.Count, "the Block's pusher PolygonCollider2D beside its box is not a footprint (code review S2)");

            Assert.IsTrue(StudioPlacement.TryGetRegionPieces(region.GetComponent<TerrainRegion>(), front, pieces));
            Assert.AreEqual(2, pieces.Count);
        }

        [Test]
        public void PickElement_UsesTheConcaveOutline()
        {
            var sheet = CreateTestSheet(out var front, out _);
            var region = StudioPlacement.PlacePolygon(LoadPrefab(WallPolygonPath), front, SheetFace.Front, L, 0f);
            Assert.AreEqual(region, StudioPlacement.PickElement(front, sheet, new Vector2(1.5f, 3f)), "inside the tall arm");
            Assert.AreEqual(region, StudioPlacement.PickElement(front, sheet, new Vector2(3.5f, 1.5f)), "inside the foot");
            Assert.IsNull(StudioPlacement.PickElement(front, sheet, new Vector2(3f, 3f)), "inside the notch — the bounding rect, not the region");
        }

        [Test]
        public void Magnet_PullsToTheNearestTargetInRadius_ElseTheSheetEdge_ElseNothing()
        {
            var targets = new List<Vector2> { new(1f, 1f), new(1.05f, 1f) };
            Assert.AreEqual(new Vector2(1.05f, 1f), StudioPlacement.Magnet(new Vector2(1.06f, 1.02f), targets, 0.1f), "nearest wins");
            Assert.AreEqual(new Vector2(2f, 2f), StudioPlacement.Magnet(new Vector2(2f, 2f), targets, 0.1f), "outside the radius: unchanged");

            var half = SheetGeometry.HalfSize;
            var nearEdge = new Vector2(half.x - 0.05f, 0.3f);
            Assert.AreEqual(new Vector2(half.x, 0.3f), StudioPlacement.Magnet(nearEdge, new List<Vector2>(), 0.1f), "x clamps to the east edge, y untouched");
            var nearCorner = new Vector2(-half.x + 0.05f, -half.y - 0.05f);
            Assert.AreEqual(new Vector2(-half.x, -half.y), StudioPlacement.Magnet(nearCorner, new List<Vector2>(), 0.1f), "both axes clamp");

            CreateTestSheet(out var front, out _);
            var region = StudioPlacement.PlacePolygon(LoadPrefab(WallPolygonPath), front, SheetFace.Front, L, 0f);
            var wall = StudioPlacement.Place(LoadPrefab(WallPath), front, SheetFace.Front, new Vector2(-2f, -2f), 0f); // 2×2 box: corners at ±1 around (-2,-2)
            StudioPlacement.Place(LoadPrefab(BlockPath), front, SheetFace.Front, new Vector2(-4f, 2f), 0f); // Not a region: never a magnet (code review S2).
            var collected = new List<Vector2>();
            StudioPlacement.MagnetTargets(front, region, collected);
            Assert.AreEqual(4, collected.Count, "the excluded region's own vertices are not targets; the box's four corners are; the Block offers none");
            StudioPlacement.MagnetTargets(front, wall, collected);
            Assert.AreEqual(6, collected.Count, "the L's six vertices");
        }

        [Test]
        public void FoldMapping_PiecesOverload_AgreesWithTheRectOverload()
        {
            var layers = SheetLayers.Flat.Apply(new Fold(FoldAnchor.EdgeSouth, 2f), 0, out _);
            var rect = Rect.MinMaxRect(0f, -4f, 1f, -1f);
            var fromRect = new List<StudioFoldMapping.DeskPiece>();
            var fromPieces = new List<StudioFoldMapping.DeskPiece>();
            StudioFoldMapping.AuthoredBoxToDeskPieces(layers, SheetFace.Front, rect, fromRect);
            StudioFoldMapping.AuthoredPiecesToDeskPieces(layers, SheetFace.Front, new[] { ConvexPolygon.FromRect(rect) }, fromPieces);
            Assert.AreEqual(fromRect.Count, fromPieces.Count);
            for (int i = 0; i < fromRect.Count; i++)
            {
                Assert.AreEqual(fromRect[i].LayerIndex, fromPieces[i].LayerIndex);
                Assert.AreEqual(fromRect[i].FaceUp, fromPieces[i].FaceUp);
                Assert.AreEqual(fromRect[i].Piece.Area, fromPieces[i].Piece.Area, 1e-4f);
            }
        }
    }
}
