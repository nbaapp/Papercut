using System.Text.RegularExpressions;
using NUnit.Framework;
using Papercut.EditorTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Papercut.Tests
{
    /// <summary>
    /// The Studio's plate-wiring ops against the real element prefabs: generic slot discovery on
    /// PlateEffect components, wiring (including cross-face and undo), and clearing.
    /// </summary>
    public sealed class StudioLinksTests
    {
        const string HoldPlatePath = "Assets/Papercut/Prefabs/Objects/Hold Plate.prefab";
        const string LatchPlatePath = "Assets/Papercut/Prefabs/Objects/Latch Plate.prefab";
        const string GatePath = "Assets/Papercut/Prefabs/Terrain/Gate.prefab";
        const string WallPath = "Assets/Papercut/Prefabs/Terrain/Wall.prefab";
        const string BlockPath = "Assets/Papercut/Prefabs/Objects/Block.prefab";

        GameObject sheetObject;
        Sheet sheet;
        Transform front;
        Transform back;

        [SetUp]
        public void SetUp()
        {
            sheetObject = new GameObject("TestSheet");
            front = new GameObject("Front").transform;
            front.SetParent(sheetObject.transform, false);
            back = new GameObject("Back").transform;
            back.SetParent(sheetObject.transform, false);

            LogAssert.Expect(LogType.Error, new Regex("no Front root"));
            LogAssert.Expect(LogType.Error, new Regex("no Back root"));
            sheet = sheetObject.AddComponent<Sheet>();
            var serialized = new SerializedObject(sheet);
            serialized.FindProperty("front").objectReferenceValue = front;
            serialized.FindProperty("back").objectReferenceValue = back;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            if (sheetObject != null)
                Object.DestroyImmediate(sheetObject);
        }

        static GameObject LoadPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"prefab missing: {path}");
            return prefab;
        }

        GameObject Place(string path, Transform faceRoot, SheetFace face)
            => StudioPlacement.Place(LoadPrefab(path), faceRoot, face, Vector2.zero, 0f);

        [Test]
        public void GetSlots_FindsTheRemoveObjectTargetOnBothPlates_AndNothingOnOtherElements()
        {
            var hold = Place(HoldPlatePath, front, SheetFace.Front);
            var slots = StudioLinks.GetSlots(hold);
            Assert.AreEqual(1, slots.Count, "Hold Plate has exactly one wireable slot");
            Assert.IsNull(slots[0].Target, "a freshly placed plate is unwired");
            StringAssert.Contains("Remove Object Effect", slots[0].DisplayName);

            Assert.AreEqual(1, StudioLinks.GetSlots(Place(LatchPlatePath, front, SheetFace.Front)).Count, "Latch Plate");
            Assert.AreEqual(0, StudioLinks.GetSlots(Place(WallPath, front, SheetFace.Front)).Count, "Wall has no effects");
            Assert.AreEqual(0, StudioLinks.GetSlots(Place(GatePath, front, SheetFace.Front)).Count, "Gate is a target, not a source");
            Assert.AreEqual(0, StudioLinks.GetSlots(Place(BlockPath, front, SheetFace.Front)).Count, "Block");
        }

        [Test]
        public void Wire_SetsTheTarget_AndUndoRevertsIt()
        {
            var plate = Place(HoldPlatePath, front, SheetFace.Front);
            var gate = Place(GatePath, front, SheetFace.Front);

            Undo.IncrementCurrentGroup();
            StudioLinks.Wire(StudioLinks.GetSlots(plate)[0], gate);
            Assert.AreEqual(gate, StudioLinks.GetSlots(plate)[0].Target, "wire must set the effect's target");

            Undo.PerformUndo();
            Assert.IsNull(StudioLinks.GetSlots(plate)[0].Target, "wiring must be a normal undoable edit");
        }

        [Test]
        public void Wire_AcrossFaces_Works()
        {
            // A Front plate affecting a Back gate: the data model is face-agnostic and the Studio allows it.
            var plate = Place(HoldPlatePath, front, SheetFace.Front);
            var gate = Place(GatePath, back, SheetFace.Back);

            StudioLinks.Wire(StudioLinks.GetSlots(plate)[0], gate);
            Assert.AreEqual(gate, StudioLinks.GetSlots(plate)[0].Target);
        }

        [Test]
        public void Wire_Null_ClearsTheTarget()
        {
            var plate = Place(HoldPlatePath, front, SheetFace.Front);
            var gate = Place(GatePath, front, SheetFace.Front);
            StudioLinks.Wire(StudioLinks.GetSlots(plate)[0], gate);

            StudioLinks.Wire(StudioLinks.GetSlots(plate)[0], null);
            Assert.IsNull(StudioLinks.GetSlots(plate)[0].Target);
        }
    }
}
