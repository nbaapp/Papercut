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
    /// <see cref="SwitchPresenceEffect"/> and <see cref="ObjectPresence"/>: a target's presence from its resting
    /// state and the plates on it. Holds combine as any-hold (the 2026-09-16 rule: a Gate wired to a Hold and a Latch
    /// stays gone once the Latch fires, whatever the Hold does); Toggle presses switch the target again every time
    /// (Aaron, 2026-09-21); a resting-absent target (a Gate (Off)) is the mirror of a resting-present one.
    /// </summary>
    public sealed class ObjectPresenceTests
    {
        readonly List<GameObject> sceneObjects = new();
        GameObject gate;

        [SetUp]
        public void SetUp()
        {
            gate = Track(new GameObject("Gate"));
        }

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

        /// <summary>An effect on its own object, wired to its targets the way the Studio wires them (through the serialized list).</summary>
        SwitchPresenceEffect CreateEffect(string name, params GameObject[] targets)
        {
            var go = Track(new GameObject(name));
            go.SetActive(false);
            var effect = go.AddComponent<SwitchPresenceEffect>();
            var serialized = new SerializedObject(effect);
            var list = serialized.FindProperty("targets");
            list.arraySize = targets.Length;
            for (int i = 0; i < targets.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(true);
            return effect;
        }

        /// <summary>A target that rests absent, as the Gate (Off) prefab authors it; its Start (which applies the rest state) is called by hand.</summary>
        GameObject CreateRestingAbsent(string name)
        {
            var go = Track(new GameObject(name));
            var presence = go.AddComponent<ObjectPresence>();
            var serialized = new SerializedObject(presence);
            serialized.FindProperty("startsAbsent").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            // Edit Mode never calls Start (the game does, before the first physics step); SendMessage would assert
            // "ShouldRunBehaviour()" for an edit-mode behaviour, so it is invoked directly.
            typeof(ObjectPresence).GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(presence, null);
            return go;
        }

        // ----- one resting-present target (the original seven cases) -----

        [Test]
        public void SingleHold_RemovesOnApply_RestoresOnRevert()
        {
            var hold = CreateEffect("Hold", gate);

            hold.Apply();
            Assert.IsFalse(gate.activeSelf, "Apply removes a resting-present target.");

            hold.Revert();
            Assert.IsTrue(gate.activeSelf, "Revert brings it back.");
        }

        [Test]
        public void LatchFired_ThenHoldPressedAndReleased_TargetStaysRemoved()
        {
            var latch = CreateEffect("Latch", gate);
            var hold = CreateEffect("Hold", gate);

            latch.Apply();
            Assert.IsFalse(gate.activeSelf);

            hold.Apply();
            Assert.IsFalse(gate.activeSelf, "A Hold press on a removed target changes nothing.");

            hold.Revert();
            Assert.IsFalse(gate.activeSelf, "A Hold release after the Latch fired must not bring the target back.");
        }

        [Test]
        public void HoldPressed_ThenLatchFires_ThenHoldReleased_TargetStaysRemoved()
        {
            var latch = CreateEffect("Latch", gate);
            var hold = CreateEffect("Hold", gate);

            hold.Apply();
            latch.Apply();
            hold.Revert();

            Assert.IsFalse(gate.activeSelf, "The Latch keeps the target removed once the Hold lets go.");
        }

        [Test]
        public void TwoHolds_TargetReturnsOnlyWhenBothRelease()
        {
            var a = CreateEffect("Hold A", gate);
            var b = CreateEffect("Hold B", gate);

            a.Apply();
            b.Apply();
            a.Revert();
            Assert.IsFalse(gate.activeSelf, "One Hold still pressed keeps the target removed.");

            b.Revert();
            Assert.IsTrue(gate.activeSelf, "The last release restores it.");
        }

        [Test]
        public void RepeatedApply_CountsOnce()
        {
            var hold = CreateEffect("Hold", gate);

            hold.Apply();
            hold.Apply();
            hold.Revert();

            Assert.IsTrue(gate.activeSelf, "One holder counts once however often it applies.");
        }

        [Test]
        public void RevertWithoutApply_IsHarmless()
        {
            var hold = CreateEffect("Hold", gate);
            var latch = CreateEffect("Latch", gate);

            latch.Apply();
            hold.Revert();

            Assert.IsFalse(gate.activeSelf, "A stray Revert cannot withdraw another effect's hold.");
            Assert.IsTrue(ObjectPresence.Of(gate).IsSwitched);
        }

        [Test]
        public void Of_AddsOnePresenceComponent_RestingPresent()
        {
            var first = ObjectPresence.Of(gate);
            var second = ObjectPresence.Of(gate);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, gate.GetComponents<ObjectPresence>().Length);
            Assert.IsFalse(first.StartsAbsent);
            Assert.IsFalse(first.IsSwitched);
            Assert.IsTrue(first.IsPresent);
        }

        // ----- several targets -----

        [Test]
        public void OneEffect_SwitchesEveryTarget_AndSwapsAGateWithAGateOff()
        {
            var gateOff = CreateRestingAbsent("Gate (Off)");
            Assert.IsFalse(gateOff.activeSelf, "a resting-absent target starts absent");
            var hold = CreateEffect("Hold", gate, gateOff);

            hold.Apply();
            Assert.IsFalse(gate.activeSelf, "the Gate is removed");
            Assert.IsTrue(gateOff.activeSelf, "the Gate (Off) is present");

            hold.Revert();
            Assert.IsTrue(gate.activeSelf);
            Assert.IsFalse(gateOff.activeSelf);
        }

        [Test]
        public void NullTarget_IsReportedAndSkipped()
        {
            LogAssert.Expect(LogType.Error, new Regex("target 1 is not assigned"));
            var hold = CreateEffect("Hold", gate, null);

            hold.Apply();
            Assert.IsFalse(gate.activeSelf, "the other targets still switch");
            hold.Revert();
            Assert.IsTrue(gate.activeSelf);
        }

        [Test]
        public void DuplicateTarget_IsReportedAndCountedOnce()
        {
            // A repeat would toggle the target twice per press and so do nothing (code review S2).
            LogAssert.Expect(LogType.Error, new Regex("target 1 .* is listed twice"));
            var toggle = CreateEffect("Toggle", gate, gate);

            toggle.Toggle();
            Assert.IsFalse(gate.activeSelf, "one press toggles once");
            toggle.Toggle();
            Assert.IsTrue(gate.activeSelf);
        }

        [Test]
        public void NoTargets_IsReported()
        {
            LogAssert.Expect(LogType.Error, new Regex("has no targets"));
            var hold = CreateEffect("Hold");
            hold.Apply();
            hold.Toggle();
            hold.Revert();
        }

        // ----- resting absent -----

        [Test]
        public void RestingAbsent_PresentOnlyWhileSwitched_AnyHold()
        {
            var gateOff = CreateRestingAbsent("Gate (Off)");
            var a = CreateEffect("Hold A", gateOff);
            var latch = CreateEffect("Latch", gateOff);

            a.Apply();
            Assert.IsTrue(gateOff.activeSelf);
            latch.Apply();
            a.Revert();
            Assert.IsTrue(gateOff.activeSelf, "present for good once the Latch fired");
            Assert.IsTrue(ObjectPresence.Of(gateOff).StartsAbsent);
        }

        // ----- Toggle -----

        [Test]
        public void Toggle_SwitchesAGateAwayAndBack()
        {
            var toggle = CreateEffect("Toggle", gate);

            toggle.Toggle();
            Assert.IsFalse(gate.activeSelf, "first press removes the Gate");
            toggle.Toggle();
            Assert.IsTrue(gate.activeSelf, "second press brings it back");
        }

        [Test]
        public void Toggle_OnRestingAbsent_IsTheMirror()
        {
            var gateOff = CreateRestingAbsent("Gate (Off)");
            var toggle = CreateEffect("Toggle", gateOff);

            toggle.Toggle();
            Assert.IsTrue(gateOff.activeSelf, "first press presents the Gate (Off)");
            toggle.Toggle();
            Assert.IsFalse(gateOff.activeSelf, "second press removes it again");
        }

        [Test]
        public void TwoToggles_EveryPressSwitchesAgain_WhicheverPlate()
        {
            // Two switches in a hallway (Aaron, 2026-09-21): a second Toggle undoes what the first did.
            var a = CreateEffect("Toggle A", gate);
            var b = CreateEffect("Toggle B", gate);

            a.Toggle();
            Assert.IsFalse(gate.activeSelf);
            b.Toggle();
            Assert.IsTrue(gate.activeSelf, "B's press switches it back");
            a.Toggle();
            Assert.IsFalse(gate.activeSelf, "A's press switches it again");
        }

        [Test]
        public void HeldTarget_IgnoresTogglesUntilReleased_ThenShowsToggledState()
        {
            var hold = CreateEffect("Hold", gate);
            var toggle = CreateEffect("Toggle", gate);

            hold.Apply();
            toggle.Toggle();
            Assert.IsFalse(gate.activeSelf, "held: still removed");
            hold.Revert();
            Assert.IsFalse(gate.activeSelf, "the hold let go; the toggle keeps it removed");
            toggle.Toggle();
            Assert.IsTrue(gate.activeSelf, "toggled back to rest");
        }

        [Test]
        public void FiredLatch_MasksTogglesForGood()
        {
            var latch = CreateEffect("Latch", gate);
            var toggle = CreateEffect("Toggle", gate);

            latch.Apply();
            toggle.Toggle();
            toggle.Toggle();
            toggle.Toggle();
            Assert.IsFalse(gate.activeSelf, "a fired Latch keeps the target removed whatever the toggles do");
        }
    }
}
