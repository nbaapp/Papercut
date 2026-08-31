using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class PlayerSketchTests
    {
        const float Threshold = 0.2f;

        [Test]
        public void ZeroInputIdles()
        {
            var c = PlayerSketch.Choose(Vector2.zero, true, false, Threshold, false);
            Assert.IsFalse(c.Running);
        }

        [Test]
        public void CardinalInputRuns()
        {
            var c = PlayerSketch.Choose(Vector2.right, true, false, Threshold, false);
            Assert.IsTrue(c.Running);
        }

        [Test]
        public void ClampedDiagonalRuns()
        {
            var input = Vector2.ClampMagnitude(new Vector2(1f, 1f), 1f);
            var c = PlayerSketch.Choose(input, true, false, Threshold, false);
            Assert.IsTrue(c.Running);
        }

        [Test]
        public void InputAtThresholdDoesNotRun()
        {
            var c = PlayerSketch.Choose(new Vector2(1f, 0f), true, false, 1f, false);
            Assert.IsFalse(c.Running);
        }

        [Test]
        public void InputJustAboveThresholdRuns()
        {
            var c = PlayerSketch.Choose(new Vector2(0.25f, 0f), true, false, Threshold, false);
            Assert.IsTrue(c.Running);
        }

        [Test]
        public void MovementHeldDoesNotRunAndKeepsFacing()
        {
            var c = PlayerSketch.Choose(Vector2.right, false, false, Threshold, true);
            Assert.IsFalse(c.Running);
            Assert.IsTrue(c.FacingLeft, "facing must not change while movement is held");
        }

        [Test]
        public void LeftInputFacesLeft()
        {
            var c = PlayerSketch.Choose(Vector2.left, true, false, Threshold, false);
            Assert.IsTrue(c.FacingLeft);
        }

        [Test]
        public void RightInputFacesRight()
        {
            var c = PlayerSketch.Choose(Vector2.right, true, false, Threshold, true);
            Assert.IsFalse(c.FacingLeft);
        }

        [Test]
        public void VerticalInputKeepsFacing()
        {
            Assert.IsTrue(PlayerSketch.Choose(Vector2.up, true, false, Threshold, true).FacingLeft);
            Assert.IsFalse(PlayerSketch.Choose(Vector2.down, true, false, Threshold, false).FacingLeft);
        }

        [Test]
        public void PushingWithInputPushes()
        {
            var c = PlayerSketch.Choose(Vector2.right, true, true, Threshold, false);
            Assert.AreEqual(SketchState.Pushing, c.State);
            Assert.IsTrue(c.Pushing);
            Assert.IsFalse(c.Running, "push wins over run");
        }

        [Test]
        public void PushingWithZeroInputIdles()
        {
            // BlockPusher's flag is a physics step old; it must never play a push from a standstill.
            var c = PlayerSketch.Choose(Vector2.zero, true, true, Threshold, false);
            Assert.AreEqual(SketchState.Idle, c.State);
        }

        [Test]
        public void PushingWithMovementHeldIdles()
        {
            var c = PlayerSketch.Choose(Vector2.right, false, true, Threshold, true);
            Assert.AreEqual(SketchState.Idle, c.State);
            Assert.IsTrue(c.FacingLeft);
        }

        [Test]
        public void PushingLeftFacesLeft()
        {
            var c = PlayerSketch.Choose(Vector2.left, true, true, Threshold, false);
            Assert.AreEqual(SketchState.Pushing, c.State);
            Assert.IsTrue(c.FacingLeft);
        }

        [Test]
        public void PushingVerticallyKeepsFacing()
        {
            Assert.IsTrue(PlayerSketch.Choose(Vector2.up, true, true, Threshold, true).FacingLeft);
        }

        [Test]
        public void PushingBelowRunThresholdStillPushes()
        {
            // BlockPusher's own threshold gates the push; a slow-stick band must not show a block sliding while Scuffy stands.
            var c = PlayerSketch.Choose(new Vector2(0.1f, 0f), true, true, Threshold, false);
            Assert.AreEqual(SketchState.Pushing, c.State);
        }

        [Test]
        public void NotPushingWithInputRuns()
        {
            Assert.AreEqual(SketchState.Running, PlayerSketch.Choose(Vector2.right, true, false, Threshold, false).State);
        }

        [Test]
        public void IdleInputKeepsFacing()
        {
            Assert.IsTrue(PlayerSketch.Choose(Vector2.zero, true, false, Threshold, true).FacingLeft);
            // Sub-threshold leftward drift must not flip the facing either.
            Assert.IsFalse(PlayerSketch.Choose(new Vector2(-0.1f, 0f), true, false, Threshold, false).FacingLeft);
        }
    }
}
