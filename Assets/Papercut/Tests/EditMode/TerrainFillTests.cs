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
    /// The per-sheet collision view: <see cref="Sheet.ShowCollision"/> (set through the Studio's
    /// <see cref="StudioSheetOps.SetShowCollision"/>) and the <see cref="TerrainFill"/> that draws each region's
    /// collider while it is on. Edit-mode tests: the fill is ExecuteAlways, so it behaves here as in the Studio.
    /// </summary>
    public sealed class TerrainFillTests
    {
        const string MaterialPath = "Assets/Papercut/Materials/Terrain Fill.mat";
        const string TerrainPrefabFolder = "Assets/Papercut/Prefabs/Terrain";
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

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

        // ----- Helpers -----

        Sheet CreateSheet(out Transform front)
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

        static TerrainFill AddBoxRegion(Transform front, Vector2 size, bool withMaterial = true)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(front, false);
            go.AddComponent<BoxCollider2D>().size = size;
            var fill = go.AddComponent<TerrainFill>(); // RequireComponent brings the TerrainRegion.
            if (withMaterial)
                SetMaterial(fill);
            return fill;
        }

        static TerrainFill AddPolygonRegion(Transform front, Vector2[] outline)
        {
            var go = new GameObject("Wall (Polygon)");
            go.transform.SetParent(front, false);
            go.AddComponent<PolygonCollider2D>().SetPath(0, outline);
            var fill = go.AddComponent<TerrainFill>();
            SetMaterial(fill);
            return fill;
        }

        static void SetMaterial(TerrainFill fill)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Assert.IsNotNull(material, $"'{MaterialPath}' must exist");
            var serialized = new SerializedObject(fill);
            serialized.FindProperty("material").objectReferenceValue = material;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static Color AppliedColor(TerrainFill fill)
        {
            var block = new MaterialPropertyBlock();
            fill.Part.GetPropertyBlock(block);
            return block.GetColor(BaseColor);
        }

        /// <summary>A colour round-trips through the property block with float rounding; compare per channel.</summary>
        static void AssertColour(Color expected, Color actual)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-4f, "r");
            Assert.AreEqual(expected.g, actual.g, 1e-4f, "g");
            Assert.AreEqual(expected.b, actual.b, 1e-4f, "b");
            Assert.AreEqual(expected.a, actual.a, 1e-4f, "a");
        }

        // ----- The sheet flag -----

        [Test]
        public void ShowCollisionField_ResolvesOnSheet()
        {
            var sheet = CreateSheet(out _);
            Assert.IsNotNull(new SerializedObject(sheet).FindProperty(StudioSheetOps.ShowCollisionField),
                "the Studio writes the flag by this serialized name");
        }

        [Test]
        public void SetShowCollision_SetsTheFlag_AndIsUndoable()
        {
            var sheet = CreateSheet(out _);
            Assert.IsFalse(sheet.ShowCollision, "off by default: a shipped sheet shows its art, not its collision");

            StudioSheetOps.SetShowCollision(sheet, true);
            Assert.IsTrue(sheet.ShowCollision);

            Undo.PerformUndo();
            Assert.IsFalse(sheet.ShowCollision);
        }

        // ----- The fill -----

        [Test]
        public void Off_DrawsNothing()
        {
            var sheet = CreateSheet(out var front);
            var fill = AddBoxRegion(front, new Vector2(2f, 3f));

            fill.Refresh();

            Assert.IsNull(fill.Part, "no part is even created while the sheet is not showing collision");
        }

        [Test]
        public void On_FillsTheBox_HiddenAndUnsaved_OnTheRegionsLayer()
        {
            var sheet = CreateSheet(out var front);
            var fill = AddBoxRegion(front, new Vector2(2f, 3f));
            fill.gameObject.layer = 5;

            StudioSheetOps.SetShowCollision(sheet, true); // Sheet.OnValidate -> ShowCollisionChanged -> the fill refreshes itself.

            Assert.IsNotNull(fill.Part);
            Assert.IsTrue(fill.Part.enabled);
            var part = fill.Part.gameObject;
            Assert.AreEqual(TerrainFill.PartName, part.name);
            Assert.AreEqual(fill.transform, part.transform.parent);
            Assert.AreEqual(Vector3.zero, part.transform.localPosition, "the fill sits at the region's own z");
            Assert.IsTrue((part.hideFlags & HideFlags.DontSaveInEditor) != 0, "never written into the sheet variant");
            Assert.IsTrue((part.hideFlags & HideFlags.HideInHierarchy) != 0, "not clutter in the hierarchy");
            Assert.AreEqual(5, part.layer, "rendered by the same face camera as the region");
            var mesh = fill.Part.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(4, mesh.vertexCount);
            Assert.AreEqual(6, mesh.triangles.Length);
            Assert.AreEqual(new Vector3(2f, 3f, 0f), mesh.bounds.size);
            Assert.AreEqual(Vector3.zero, mesh.bounds.center);
            Assert.AreEqual(MaterialPath, AssetDatabase.GetAssetPath(fill.Part.sharedMaterial));
            AssertColour(fill.Color, AppliedColor(fill));

            StudioSheetOps.SetShowCollision(sheet, false);
            Assert.IsFalse(fill.Part.enabled, "off hides the fill again");
        }

        [Test]
        public void On_FillsAPolygonInConvexPieces_HonouringItsOffset()
        {
            var sheet = CreateSheet(out var front);
            // An L: concave, so at least two convex pieces.
            var fill = AddPolygonRegion(front, new[]
            {
                new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 1f),
                new Vector2(1f, 1f), new Vector2(1f, 3f), new Vector2(0f, 3f),
            });
            fill.GetComponent<PolygonCollider2D>().offset = new Vector2(10f, 0f);

            StudioSheetOps.SetShowCollision(sheet, true);

            var mesh = fill.Part.GetComponent<MeshFilter>().sharedMesh;
            Assert.GreaterOrEqual(mesh.vertexCount, 6);
            Assert.GreaterOrEqual(mesh.triangles.Length, 12, "two rects of the L are four triangles at least");
            Assert.AreEqual(new Vector3(4f, 3f, 0f), mesh.bounds.size);
            Assert.AreEqual(new Vector3(12f, 1.5f, 0f), mesh.bounds.center, "drawn where the collider is, offset included");
        }

        [Test]
        public void FollowsAColliderEdit_AndAColourEdit()
        {
            var sheet = CreateSheet(out var front);
            var fill = AddBoxRegion(front, new Vector2(2f, 3f));
            StudioSheetOps.SetShowCollision(sheet, true);
            var mesh = fill.Part.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(new Vector3(2f, 3f, 0f), mesh.bounds.size);

            var box = fill.GetComponent<BoxCollider2D>();
            box.size = new Vector2(5f, 1f);
            box.offset = new Vector2(1f, 1f);
            fill.Refresh(); // What the editor's object-change hook does after a Studio drag.
            Assert.AreEqual(new Vector3(5f, 1f, 0f), mesh.bounds.size);
            Assert.AreEqual(new Vector3(1f, 1f, 0f), mesh.bounds.center);

            var serialized = new SerializedObject(fill);
            serialized.FindProperty("color").colorValue = Color.magenta;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            fill.Refresh();
            AssertColour(Color.magenta, AppliedColor(fill));
        }

        [Test]
        public void AnOutlineThatIsNotOneSimplePolygon_FillsNothing_WithoutALogOfItsOwn()
        {
            var sheet = CreateSheet(out var front);
            // A bow tie: self-crossing. TerrainRegion reports it at Awake and the Studio draws it red; the fill stays quiet.
            var fill = AddPolygonRegion(front, new[]
            {
                new Vector2(0f, 0f), new Vector2(2f, 2f), new Vector2(2f, 0f), new Vector2(0f, 2f),
            });

            StudioSheetOps.SetShowCollision(sheet, true);

            Assert.IsNotNull(fill.Part);
            Assert.AreEqual(0, fill.Part.GetComponent<MeshFilter>().sharedMesh.vertexCount);
        }

        [Test]
        public void WithoutAMaterial_ReportsOnce_AndDrawsNothing()
        {
            var sheet = CreateSheet(out var front);
            var fill = AddBoxRegion(front, new Vector2(2f, 3f), withMaterial: false);

            LogAssert.Expect(LogType.Error, new Regex("has no material"));
            StudioSheetOps.SetShowCollision(sheet, true);
            fill.Refresh(); // A second refresh must not report again (an unexpected error would fail the test).

            Assert.IsNull(fill.Part);
        }

        [Test]
        public void RemovingTheComponent_RemovesItsPartAndMesh()
        {
            var sheet = CreateSheet(out var front);
            var fill = AddBoxRegion(front, new Vector2(2f, 3f));
            StudioSheetOps.SetShowCollision(sheet, true);
            var region = fill.transform;
            var mesh = fill.Part.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsNotNull(region.Find(TerrainFill.PartName));

            Object.DestroyImmediate(fill);

            Assert.IsNull(region.Find(TerrainFill.PartName), "cutting the feature leaves nothing behind");
            Assert.IsTrue(mesh == null, "the runtime mesh is destroyed, not leaked");
        }

        [Test]
        public void ReparentingUnderAnotherSheet_FollowsThatSheetsFlag()
        {
            var shown = CreateSheet(out var shownFront);
            var hidden = CreateSheet(out var hiddenFront);
            StudioSheetOps.SetShowCollision(shown, true);
            var fill = AddBoxRegion(hiddenFront, new Vector2(2f, 3f));
            Assert.IsNull(fill.Part);

            fill.transform.SetParent(shownFront, false); // OnTransformParentChanged rebinds the sheet.

            Assert.IsNotNull(fill.Part);
            Assert.IsTrue(fill.Part.enabled);

            StudioSheetOps.SetShowCollision(shown, false);
            Assert.IsFalse(fill.Part.enabled, "listening to the new sheet, not the old one");
        }

        // ----- The real prefabs -----

        [Test]
        public void EveryTerrainPrefabWithoutItsOwnDrawing_HasAFillWithAMaterial()
        {
            // Wall and Water (box and polygon) are invisible as shipped; each must carry the testing fill. A region
            // prefab with its own SpriteRenderer (the Gate) draws itself and needs none.
            var checkedAny = false;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { TerrainPrefabFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab.GetComponent<TerrainRegion>() == null || prefab.GetComponent<SpriteRenderer>() != null)
                    continue;
                checkedAny = true;
                var fill = prefab.GetComponent<TerrainFill>();
                Assert.IsNotNull(fill, $"'{path}' has no TerrainFill");
                Assert.IsNotNull(new SerializedObject(fill).FindProperty("material").objectReferenceValue, $"'{path}' fill has no material");
            }
            Assert.IsTrue(checkedAny, "the Wall/Water prefabs exist under " + TerrainPrefabFolder);
        }

        [Test]
        public void WallAndWaterFills_ReadDifferently()
        {
            var wall = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainPrefabFolder + "/Wall.prefab").GetComponent<TerrainFill>();
            var water = AssetDatabase.LoadAssetAtPath<GameObject>(TerrainPrefabFolder + "/Water.prefab").GetComponent<TerrainFill>();
            Assert.IsNotNull(wall);
            Assert.IsNotNull(water);
            Assert.AreNotEqual(wall.Color, water.Color, "a wall and water must be told apart at a glance");
        }

        // ----- Universal regions (above the sheet, 2026-09-28): the fill follows the sheet's displayed folds -----

        Sheet CreateSheetWithAbove(out Transform front, out Transform above, bool withFolds)
        {
            var sheet = CreateSheet(out front);
            above = new GameObject("Above").transform;
            above.SetParent(sheet.transform, false);
            above.localPosition = new Vector3(0f, 0f, 0.3f);
            var serialized = new SerializedObject(sheet);
            serialized.FindProperty("above").objectReferenceValue = above;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (withFolds)
                sheet.gameObject.AddComponent<SheetFolds>(); // No Awake in edit mode; its Layers is Flat by construction.
            return sheet;
        }

        static TerrainFill AddUniversalBoxRegion(Transform above, Vector2 position, Vector2 size)
        {
            var go = new GameObject("Universal Wall");
            go.transform.SetParent(above, false);
            go.transform.localPosition = position;
            go.AddComponent<BoxCollider2D>().size = size;
            var region = go.AddComponent<TerrainRegion>();
            var serialized = new SerializedObject(region);
            serialized.FindProperty("universal").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var fill = go.AddComponent<TerrainFill>();
            SetMaterial(fill);
            return fill;
        }

        [Test]
        public void Universal_WithoutFolds_DrawsTheAuthoredShape_OnItsOwnLayer()
        {
            var sheet = CreateSheetWithAbove(out _, out var above, withFolds: false);
            var fill = AddUniversalBoxRegion(above, new Vector2(0f, -3.5f), new Vector2(1f, 1f));

            StudioSheetOps.SetShowCollision(sheet, true);

            Assert.IsNotNull(fill.Part);
            Assert.IsTrue(fill.Part.enabled);
            Assert.AreEqual(0, fill.Part.gameObject.layer, "main-camera content, never a face layer");
            var mesh = fill.Part.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(new Vector3(1f, 1f, 0f), mesh.bounds.size);
        }

        [Test]
        public void Universal_IsClippedToTheSheetsFootprint_AsDisplayed()
        {
            var sheet = CreateSheetWithAbove(out _, out var above, withFolds: true);
            var fill = AddUniversalBoxRegion(above, new Vector2(0f, -3.5f), new Vector2(1f, 1f)); // y in [-4, -3]
            StudioSheetOps.SetShowCollision(sheet, true);

            // A south fold of depth 1 leaves the sheet at y >= -3.25: the fill keeps the top quarter, at the region's own depth.
            var folded = SheetLayers.Flat.Apply(new Fold(FoldAnchor.EdgeSouth, 1f), 0, out _);
            fill.RefreshWith(folded);
            var mesh = fill.Part.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(1f, mesh.bounds.size.x, 1e-4f);
            Assert.AreEqual(0.25f, mesh.bounds.size.y, 1e-4f);
            Assert.AreEqual(0f, mesh.bounds.size.z, 1e-6f, "x/y only: the fill stays at the region's z");
            Assert.AreEqual(0.375f, mesh.bounds.center.y, 1e-4f, "the surviving strip, region-local");

            fill.RefreshWith(null); // Back to the sheet's own (flat) folds: the whole shape again.
            Assert.AreEqual(1f, fill.Part.GetComponent<MeshFilter>().sharedMesh.bounds.size.y, 1e-4f);
        }

        [Test]
        public void Universal_StillDraws_AfterAFoldClipsItsCollider()
        {
            // The bug: a fold that clipped a box region's collider added the runtime clip polygon beside the box, the
            // fill took box + polygon for "no usable collider" and hid itself for good, while the collision went on.
            var sheet = CreateSheetWithAbove(out _, out var above, withFolds: true);
            var fill = AddUniversalBoxRegion(above, new Vector2(0f, -3.5f), new Vector2(1f, 1f)); // y in [-4, -3]
            var region = fill.GetComponent<TerrainRegion>();
            typeof(TerrainRegion).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(region, null); // No Awake in edit mode; the game resolves the authored collider here.
            StudioSheetOps.SetShowCollision(sheet, true);

            // What SheetOcclusion hands the region after a south fold of depth 1: the strip left at y >= -3.25.
            var folded = SheetLayers.Flat.Apply(new Fold(FoldAnchor.EdgeSouth, 1f), 0, out _);
            var coverage = folded.CoverageAbove(region.FaceLocalFootprint(above));
            Assert.IsFalse(coverage.IsWhole || coverage.IsNone, "precondition: the fold clips the region");
            region.OnFoldCoverageChanged(coverage, sheet.transform);
            Assert.AreEqual(2, fill.GetComponents<Collider2D>().Length, "precondition: the runtime clip polygon sits beside the box");

            fill.RefreshWith(folded);
            Assert.IsTrue(fill.Part.enabled, "the fill must survive its collider being clipped");
            Assert.AreEqual(0.25f, fill.Part.GetComponent<MeshFilter>().sharedMesh.bounds.size.y, 1e-4f, "clipped like the collider");

            fill.RefreshWith(null); // Unfolded: the whole authored box again, not the clip polygon.
            Assert.IsTrue(fill.Part.enabled);
            Assert.AreEqual(new Vector3(1f, 1f, 0f), fill.Part.GetComponent<MeshFilter>().sharedMesh.bounds.size);
        }

        [Test]
        public void FaceFill_IgnoresTheDisplayedFolds()
        {
            // Face content folds with the paper through the face camera; its fill is never clipped here.
            var sheet = CreateSheetWithAbove(out var front, out _, withFolds: true);
            var fill = AddBoxRegion(front, new Vector2(1f, 1f));
            fill.transform.localPosition = new Vector2(0f, -3.5f);
            StudioSheetOps.SetShowCollision(sheet, true);

            fill.RefreshWith(SheetLayers.Flat.Apply(new Fold(FoldAnchor.EdgeSouth, 1f), 0, out _));
            Assert.AreEqual(new Vector3(1f, 1f, 0f), fill.Part.GetComponent<MeshFilter>().sharedMesh.bounds.size);
        }
    }
}
