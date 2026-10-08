using NUnit.Framework;

namespace Papercut.Tests
{
    /// <summary>
    /// <see cref="PlateRules"/>: the mode table of a pressure plate. Hold follows the press, Latch fires once,
    /// Toggle acts on every rising edge — and a seed tick (scene start, enable, the sheet reset) takes what is on
    /// the plate as its condition rather than as a press (Aaron, 2026-09-21), so a block authored on or reset onto
    /// a Toggle plate switches nothing.
    /// </summary>
    public sealed class PlateRulesTests
    {
        static readonly PlateState Idle = new(pressed: false, applied: false);

        static PlateTick Tick(PlateMode mode, PlateState from, bool pressed) => PlateRules.Step(mode, from, pressed, seed: false);
        static PlateTick Seed(PlateMode mode, PlateState from, bool pressed) => PlateRules.Step(mode, from, pressed, seed: true);

        // ----- Hold -----

        [Test]
        public void Hold_AppliesOnPress_RevertsOnRelease_NothingWhileHeld()
        {
            var press = Tick(PlateMode.Hold, Idle, true);
            Assert.AreEqual(PlateAction.Apply, press.Action);
            Assert.IsTrue(press.State.Pressed);
            Assert.IsTrue(press.State.Applied);

            var held = Tick(PlateMode.Hold, press.State, true);
            Assert.AreEqual(PlateAction.None, held.Action);
            Assert.IsTrue(held.State.Applied);

            var release = Tick(PlateMode.Hold, held.State, false);
            Assert.AreEqual(PlateAction.Revert, release.Action);
            Assert.IsFalse(release.State.Pressed);
            Assert.IsFalse(release.State.Applied);
        }

        [Test]
        public void Hold_SeededPressed_Applies()
        {
            // A block resting on a Hold plate holds it, at start and after the sheet reset (as before seeding existed).
            var seed = Seed(PlateMode.Hold, Idle, true);
            Assert.AreEqual(PlateAction.Apply, seed.Action);
            Assert.IsTrue(seed.State.Applied);
        }

        [Test]
        public void Hold_SeededReleased_WhileApplied_Reverts()
        {
            // The sheet reset moved the block off a Hold plate the player had left it on.
            var seed = Seed(PlateMode.Hold, new PlateState(pressed: true, applied: true), false);
            Assert.AreEqual(PlateAction.Revert, seed.Action);
            Assert.IsFalse(seed.State.Applied);
        }

        // ----- Latch -----

        [Test]
        public void Latch_AppliesOnFirstPress_ThenNeverAgain()
        {
            var press = Tick(PlateMode.Latch, Idle, true);
            Assert.AreEqual(PlateAction.Apply, press.Action);
            Assert.IsTrue(press.State.Applied);

            var release = Tick(PlateMode.Latch, press.State, false);
            Assert.AreEqual(PlateAction.None, release.Action, "a Latch never reverts");
            Assert.IsFalse(release.State.Pressed);
            Assert.IsTrue(release.State.Applied, "fired stays fired");

            var again = Tick(PlateMode.Latch, release.State, true);
            Assert.AreEqual(PlateAction.None, again.Action, "a second press does not re-apply");
            Assert.IsTrue(again.State.Applied);
        }

        [Test]
        public void Latch_SeededPressed_Fires()
        {
            // A block authored on a Latch plate fires it at scene start (today's behaviour, kept).
            var seed = Seed(PlateMode.Latch, Idle, true);
            Assert.AreEqual(PlateAction.Apply, seed.Action);
            Assert.IsTrue(seed.State.Applied);
        }

        // ----- Toggle -----

        [Test]
        public void Toggle_TogglesOnEachRisingEdge_NothingOnHoldOrRelease()
        {
            var press = Tick(PlateMode.Toggle, Idle, true);
            Assert.AreEqual(PlateAction.Toggle, press.Action);
            Assert.IsTrue(press.State.Pressed);
            Assert.IsFalse(press.State.Applied, "a Toggle plate holds nothing itself");

            var held = Tick(PlateMode.Toggle, press.State, true);
            Assert.AreEqual(PlateAction.None, held.Action, "holding is not a press");

            var release = Tick(PlateMode.Toggle, held.State, false);
            Assert.AreEqual(PlateAction.None, release.Action, "release does nothing");
            Assert.IsFalse(release.State.Pressed);

            var second = Tick(PlateMode.Toggle, release.State, true);
            Assert.AreEqual(PlateAction.Toggle, second.Action, "the next step-on toggles again");
        }

        [Test]
        public void Toggle_SeededPressed_DoesNotToggle()
        {
            // A block authored on a Toggle plate, or reset onto it on leaving: the plate is pressed, nothing switches.
            var seed = Seed(PlateMode.Toggle, Idle, true);
            Assert.AreEqual(PlateAction.None, seed.Action);
            Assert.IsTrue(seed.State.Pressed, "the seed records the condition");

            var stillThere = Tick(PlateMode.Toggle, seed.State, true);
            Assert.AreEqual(PlateAction.None, stillThere.Action, "an unchanged reading after the seed is not an edge");
        }

        [Test]
        public void Toggle_AfterSeed_ReleaseThenPress_IsAnEdge()
        {
            var seed = Seed(PlateMode.Toggle, Idle, true);
            var off = Tick(PlateMode.Toggle, seed.State, false);
            Assert.AreEqual(PlateAction.None, off.Action);

            var on = Tick(PlateMode.Toggle, off.State, true);
            Assert.AreEqual(PlateAction.Toggle, on.Action, "a genuine step-on after the seed toggles");
        }

        [Test]
        public void Toggle_SeededReleased_ThenPress_IsAnEdge()
        {
            // The reset put the block elsewhere; the next thing to step on is a real press.
            var seed = Seed(PlateMode.Toggle, new PlateState(pressed: true, applied: false), false);
            Assert.AreEqual(PlateAction.None, seed.Action);
            Assert.IsFalse(seed.State.Pressed);

            var on = Tick(PlateMode.Toggle, seed.State, true);
            Assert.AreEqual(PlateAction.Toggle, on.Action);
        }
    }
}
