using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Papercut.Tests
{
    /// <summary>
    /// <see cref="Unlockable"/> authoring checks and its collection rule, on a minimal Sheet hierarchy. Awake is
    /// invoked directly (edit mode never calls it on a plain MonoBehaviour), so what is tested is exactly the
    /// component's own set-up: face resolution, the flat rect, and the refusals.
    /// </summary>
    public sealed class UnlockableTests
    {
        readonly List<GameObject> objects = new();
        Sheet sheet;
        Transform front;
        Transform back;

        [SetUp]
        public void SetUp()
        {
            var sheetObject = Track(new GameObject("Sheet"));
            // Sheet.OnValidate runs on AddComponent and reports the roots that are not assigned yet.
            LogAssert.Expect(LogType.Error, new Regex("no Front root"));
            LogAssert.Expect(LogType.Error, new Regex("no Back root"));
            sheet = sheetObject.AddComponent<Sheet>();
            front = Track(new GameObject("Front")).transform;
            front.SetParent(sheetObject.transform, false);
            back = Track(new GameObject("Back")).transform;
            back.SetParent(sheetObject.transform, false);
            var serialized = new SerializedObject(sheet);
            serialized.FindProperty("front").objectReferenceValue = front;
            serialized.FindProperty("back").objectReferenceValue = back;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in objects)
                if (go != null)
                    Object.DestroyImmediate(go);
            objects.Clear();
        }

        GameObject Track(GameObject go)
        {
            objects.Add(go);
            return go;
        }

        /// <summary>A pickup under <paramref name="parent"/> with a centred box of the given size, set up as Awake does at load.</summary>
        Unlockable Create(Transform parent, Vector2 localPosition, Vector2 boxSize, Ability grants = Ability.Push, bool trigger = true)
        {
            var go = Track(new GameObject("Pickup"));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = trigger;
            box.size = boxSize;
            var unlockable = go.AddComponent<Unlockable>();
            var serialized = new SerializedObject(unlockable);
            serialized.FindProperty("grants").intValue = (int)grants;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            typeof(Unlockable).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(unlockable, null);
            return unlockable;
        }

        [Test]
        public void Front_IsReachedOnTheFrontInsideItsBox_NotOnTheBack_NotOutside()
        {
            var pickup = Create(front, new Vector2(2f, 1f), new Vector2(0.8f, 0.5f));
            Assert.IsTrue(pickup.enabled);
            Assert.AreEqual(Ability.Push, pickup.Grants);
            Assert.IsTrue(pickup.IsReachedBy(new SheetPoint(new Vector2(2f, 1f), SheetFace.Front)), "centre");
            Assert.IsTrue(pickup.IsReachedBy(new SheetPoint(new Vector2(2.3f, 1.2f), SheetFace.Front)), "inside the box");
            Assert.IsFalse(pickup.IsReachedBy(new SheetPoint(new Vector2(2f, 1f), SheetFace.Back)), "same place, other face");
            Assert.IsFalse(pickup.IsReachedBy(new SheetPoint(new Vector2(2.5f, 1f), SheetFace.Front)), "beside it");
            Assert.IsFalse(pickup.IsReachedBy(new SheetPoint(new Vector2(2f, 1.3f), SheetFace.Front)), "above it");
        }

        [Test]
        public void Back_IsReachedAtTheMirroredFlatPlace_OnTheBack()
        {
            // Back-space (−1, −3) lies beneath Front (1, −3): the flat rect is centred at (1, −3) on the Back.
            var pickup = Create(back, new Vector2(-1f, -3f), new Vector2(0.8f, 0.5f));
            Assert.IsTrue(pickup.enabled);
            Assert.IsTrue(pickup.IsReachedBy(new SheetPoint(new Vector2(1f, -3f), SheetFace.Back)), "the mirrored place, Back side");
            Assert.IsFalse(pickup.IsReachedBy(new SheetPoint(new Vector2(-1f, -3f), SheetFace.Back)), "the authored coordinates are Back-space, not flat");
            Assert.IsFalse(pickup.IsReachedBy(new SheetPoint(new Vector2(1f, -3f), SheetFace.Front)), "the Front over it is not it");
        }

        [Test]
        public void Collect_GrantsToThePlayer_AndDeactivatesThePickup()
        {
            var pickup = Create(front, Vector2.zero, Vector2.one);
            var abilities = Track(new GameObject("Player")).AddComponent<PlayerAbilities>();
            var changed = 0;
            abilities.Changed += () =>
            {
                changed++;
                Assert.IsTrue(pickup.IsCollected, "collected is set before the grant, so a listener cannot re-enter a second collection");
            };
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(Unlockable).GetField("player", flags).SetValue(pickup, abilities);
            typeof(Unlockable).GetMethod("Collect", flags).Invoke(pickup, null);

            Assert.IsTrue(pickup.IsCollected);
            Assert.AreEqual(Ability.Push, abilities.Abilities, "the player holds what it grants");
            Assert.AreEqual(1, changed);
            Assert.IsFalse(pickup.gameObject.activeSelf, "gone for the session");
        }

        [Test]
        public void NotUnderASheet_IsReportedAndDisabled()
        {
            var loose = Track(new GameObject("Loose")).transform;
            LogAssert.Expect(LogType.Error, new Regex("is not under a Sheet"));
            var pickup = Create(loose, Vector2.zero, Vector2.one);
            Assert.IsFalse(pickup.enabled);
        }

        [Test]
        public void UnderTheSheetButNotAFaceRoot_IsReportedAndDisabled()
        {
            LogAssert.Expect(LogType.Error, new Regex("must be under the sheet's Front or Back root"));
            var pickup = Create(sheet.transform, Vector2.zero, Vector2.one);
            Assert.IsFalse(pickup.enabled);
        }

        [Test]
        public void GrantsNothing_IsReportedAndDisabled()
        {
            LogAssert.Expect(LogType.Error, new Regex("grants nothing"));
            var pickup = Create(front, Vector2.zero, Vector2.one, Ability.None);
            Assert.IsFalse(pickup.enabled);
        }

        [Test]
        public void NonTriggerBox_IsReportedAndFixed()
        {
            LogAssert.Expect(LogType.Error, new Regex("must be a trigger"));
            var pickup = Create(front, Vector2.zero, Vector2.one, trigger: false);
            Assert.IsTrue(pickup.GetComponent<BoxCollider2D>().isTrigger, "fixed at runtime");
            Assert.IsTrue(pickup.enabled, "still collects");
        }
    }
}
