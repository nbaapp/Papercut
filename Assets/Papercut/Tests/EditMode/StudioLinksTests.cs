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
    /// PlateEffect components (a list slot since 2026-09-21), wiring several targets (including cross-face and
    /// undo), unwiring one, and clearing; and the two new prefabs (Gate (Off), Toggle Plate) as authored.
    /// </summary>
    public sealed class StudioLinksTests
    {
        const string HoldPlatePath = "Assets/Papercut/Prefabs/Objects/Hold Plate.prefab";
        const string LatchPlatePath = "Assets/Papercut/Prefabs/Objects/Latch Plate.prefab";
        const string TogglePlatePath = "Assets/Papercut/Prefabs/Objects/Toggle Plate.prefab";
        const string GatePath = "Assets/Papercut/Prefabs/Terrain/Gate.prefab";
        const string GateOffPath = "Assets/Papercut/Prefabs/Terrain/Gate (Off).prefab";
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

        static StudioLinks.Slot SlotOf(GameObject plate)
        {
            var slots = StudioLinks.GetSlots(plate);
            Assert.AreEqual(1, slots.Count, $"{plate.name} has exactly one wireable slot");
            return slots[0];
        }

        [Test]
        public void GetSlots_FindsTheTargetListOnEveryPlate_AndNothingOnOtherElements()
        {
            var hold = Place(HoldPlatePath, front, SheetFace.Front);
            var slot = SlotOf(hold);
            Assert.IsTrue(slot.IsList, "the targets slot is a list");
            Assert.AreEqual(0, slot.Targets.Count, "a freshly placed plate is unwired");
            StringAssert.Contains("Switch Presence Effect", slot.DisplayName);

            Assert.IsTrue(SlotOf(Place(LatchPlatePath, front, SheetFace.Front)).IsList, "Latch Plate");
            Assert.IsTrue(SlotOf(Place(TogglePlatePath, front, SheetFace.Front)).IsList, "Toggle Plate");
            Assert.AreEqual(0, StudioLinks.GetSlots(Place(WallPath, front, SheetFace.Front)).Count, "Wall has no effects");
            Assert.AreEqual(0, StudioLinks.GetSlots(Place(GatePath, front, SheetFace.Front)).Count, "Gate is a target, not a source");
            Assert.AreEqual(0, StudioLinks.GetSlots(Place(GateOffPath, front, SheetFace.Front)).Count, "Gate (Off) is a target, not a source");
            Assert.AreEqual(0, StudioLinks.GetSlots(Place(BlockPath, front, SheetFace.Front)).Count, "Block");
        }

        [Test]
        public void Wire_AppendsTargets_ARepeatIsOneEntry_AndUndoRevertsIt()
        {
            var plate = Place(HoldPlatePath, front, SheetFace.Front);
            var gateA = Place(GatePath, front, SheetFace.Front);
            var gateB = Place(GateOffPath, front, SheetFace.Front);

            Undo.IncrementCurrentGroup();
            StudioLinks.Wire(SlotOf(plate), gateA);
            StudioLinks.Wire(SlotOf(plate), gateB);
            StudioLinks.Wire(SlotOf(plate), gateA);
            var slot = SlotOf(plate);
            CollectionAssert.AreEqual(new[] { gateA, gateB }, slot.Targets, "both gates once each, in wiring order");
            Assert.IsTrue(slot.Contains(gateA));
            Assert.IsTrue(slot.Contains(gateB));

            Undo.PerformUndo();
            Undo.PerformUndo();
            Assert.AreEqual(0, SlotOf(plate).Targets.Count, "wiring must be a normal undoable edit");
        }

        [Test]
        public void Wire_AcrossFaces_Works()
        {
            // A Front plate affecting a Back gate: the data model is face-agnostic and the Studio allows it.
            var plate = Place(TogglePlatePath, front, SheetFace.Front);
            var gate = Place(GatePath, back, SheetFace.Back);

            StudioLinks.Wire(SlotOf(plate), gate);
            CollectionAssert.AreEqual(new[] { gate }, SlotOf(plate).Targets);
        }

        [Test]
        public void Unwire_RemovesOneTarget_AndKeepsTheOthers()
        {
            var plate = Place(HoldPlatePath, front, SheetFace.Front);
            var gateA = Place(GatePath, front, SheetFace.Front);
            var gateB = Place(GatePath, front, SheetFace.Front);
            StudioLinks.Wire(SlotOf(plate), gateA);
            StudioLinks.Wire(SlotOf(plate), gateB);

            StudioLinks.Unwire(SlotOf(plate), gateA);
            CollectionAssert.AreEqual(new[] { gateB }, SlotOf(plate).Targets, "the entry is gone, not nulled");

            StudioLinks.Unwire(SlotOf(plate), gateA);
            CollectionAssert.AreEqual(new[] { gateB }, SlotOf(plate).Targets, "unwiring something not wired is a no-op");
        }

        [Test]
        public void Clear_EmptiesTheList()
        {
            var plate = Place(HoldPlatePath, front, SheetFace.Front);
            StudioLinks.Wire(SlotOf(plate), Place(GatePath, front, SheetFace.Front));
            StudioLinks.Wire(SlotOf(plate), Place(GateOffPath, front, SheetFace.Front));

            StudioLinks.Clear(SlotOf(plate));
            Assert.AreEqual(0, SlotOf(plate).Targets.Count);
            Assert.AreEqual(0, RawTargetCount(plate), "the serialized list is empty, not a list of nulls");
        }

        [Test]
        public void Delete_UnwiresTheElementFromEveryPlate_OnBothFaces()
        {
            // A deleted gate must not linger as a null the Studio cannot show or clear (code review S1).
            var frontPlate = Place(HoldPlatePath, front, SheetFace.Front);
            var backPlate = Place(TogglePlatePath, back, SheetFace.Back);
            var gate = Place(GatePath, front, SheetFace.Front);
            var other = Place(GateOffPath, front, SheetFace.Front);
            StudioLinks.Wire(SlotOf(frontPlate), gate);
            StudioLinks.Wire(SlotOf(frontPlate), other);
            StudioLinks.Wire(SlotOf(backPlate), gate);

            StudioPlacement.Delete(gate);

            CollectionAssert.AreEqual(new[] { other }, SlotOf(frontPlate).Targets);
            Assert.AreEqual(1, RawTargetCount(frontPlate));
            Assert.AreEqual(0, SlotOf(backPlate).Targets.Count);
            Assert.AreEqual(0, RawTargetCount(backPlate));
        }

        [Test]
        public void Wire_DropsAStaleNullEntry()
        {
            // A target destroyed outside the Studio leaves a null; the next wiring compacts it away.
            var plate = Place(HoldPlatePath, front, SheetFace.Front);
            var gone = Place(GatePath, front, SheetFace.Front);
            StudioLinks.Wire(SlotOf(plate), gone);
            Object.DestroyImmediate(gone);
            Assert.AreEqual(1, RawTargetCount(plate), "precondition: a null lingers");

            var gate = Place(GatePath, front, SheetFace.Front);
            StudioLinks.Wire(SlotOf(plate), gate);
            CollectionAssert.AreEqual(new[] { gate }, SlotOf(plate).Targets);
            Assert.AreEqual(1, RawTargetCount(plate), "the null is gone");
        }

        static int RawTargetCount(GameObject plate)
            => new SerializedObject(plate.GetComponent<SwitchPresenceEffect>()).FindProperty("targets").arraySize;

        [Test]
        public void GateOffPrefab_IsAGateVariantThatRestsAbsent()
        {
            var gateOff = LoadPrefab(GateOffPath);
            Assert.AreEqual(PrefabAssetType.Variant, PrefabUtility.GetPrefabAssetType(gateOff));
            Assert.AreEqual(LoadPrefab(GatePath), PrefabUtility.GetCorrespondingObjectFromSource(gateOff), "a variant of Gate");
            var presence = gateOff.GetComponent<ObjectPresence>();
            Assert.IsNotNull(presence, "carries ObjectPresence");
            Assert.IsTrue(presence.StartsAbsent, "rests absent");
            Assert.IsTrue(gateOff.activeSelf, "authored active: the rest state is applied at Start, not in the asset");
            Assert.IsNotNull(gateOff.GetComponent<TerrainRegion>(), "still a gate");
            Assert.IsTrue(StudioPlacement.IsResizable(gateOff), "resizes like a Gate");
        }

        [Test]
        public void TogglePlatePrefab_IsAPlateInToggleMode_WithTheSharedDrawing()
        {
            var toggle = LoadPrefab(TogglePlatePath);
            var hold = LoadPrefab(HoldPlatePath);
            var mode = new SerializedObject(toggle.GetComponent<PressurePlate>()).FindProperty("mode");
            Assert.AreEqual((int)PlateMode.Toggle, mode.enumValueIndex);
            Assert.AreEqual(hold.GetComponent<SpriteRenderer>().sprite, toggle.GetComponent<SpriteRenderer>().sprite, "same drawing as the other plates");
            Assert.IsNotNull(toggle.GetComponent<SwitchPresenceEffect>());
            Assert.AreEqual(1, new SerializedObject(toggle.GetComponent<PressurePlate>()).FindProperty("effects").arraySize);
        }
    }
}
