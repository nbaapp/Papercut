using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class PlayerSketchTests
    {
        const float Threshold = 0.2f;

        static SketchChoice Free(Vector2 input, bool movementEnabled, bool facingLeft)
            => PlayerSketch.Choose(input, movementEnabled, holding: false, heldPressing: false, heldX: 0f, Threshold, facingLeft);

        static SketchChoice Holding(Vector2 input, bool pressing, float heldX, bool facingLeft, bool movementEnabled = true)
            => PlayerSketch.Choose(input, movementEnabled, holding: true, pressing, heldX, Threshold, facingLeft);

        [Test]
        public void ZeroInputIdles()
        {
            var c = Free(Vector2.zero, true, false);
            Assert.IsFalse(c.Running);
        }

        [Test]
        public void CardinalInputRuns()
        {
            var c = Free(Vector2.right, true, false);
            Assert.IsTrue(c.Running);
        }

        [Test]
        public void ClampedDiagonalRuns()
        {
            var input = Vector2.ClampMagnitude(new Vector2(1f, 1f), 1f);
            var c = Free(input, true, false);
            Assert.IsTrue(c.Running);
        }

        [Test]
        public void InputAtThresholdDoesNotRun()
        {
            var c = PlayerSketch.Choose(new Vector2(1f, 0f), true, false, false, 0f, 1f, false);
            Assert.IsFalse(c.Running);
        }

        [Test]
        public void InputJustAboveThresholdRuns()
        {
            var c = Free(new Vector2(0.25f, 0f), true, false);
            Assert.IsTrue(c.Running);
        }

        [Test]
        public void MovementHeldDoesNotRunAndKeepsFacing()
        {
            var c = Free(Vector2.right, false, true);
            Assert.IsFalse(c.Running);
            Assert.IsTrue(c.FacingLeft, "facing must not change while movement is held");
        }

        [Test]
        public void LeftInputFacesLeft()
        {
            var c = Free(Vector2.left, true, false);
            Assert.IsTrue(c.FacingLeft);
        }

        [Test]
        public void RightInputFacesRight()
        {
            var c = Free(Vector2.right, true, true);
            Assert.IsFalse(c.FacingLeft);
        }

        [Test]
        public void VerticalInputKeepsFacing()
        {
            Assert.IsTrue(Free(Vector2.up, true, true).FacingLeft);
            Assert.IsFalse(Free(Vector2.down, true, false).FacingLeft);
        }

        [Test]
        public void NotHoldingNeverPushes()
        {
            // Pushing is a hold state: without a held block, walking into a block is just walking into a wall.
            Assert.AreEqual(SketchState.Running, Free(Vector2.right, true, false).State);
            Assert.AreEqual(SketchState.Idle, Free(Vector2.zero, true, false).State);
        }

        [Test]
        public void IdleInputKeepsFacing()
        {
            Assert.IsTrue(Free(Vector2.zero, true, true).FacingLeft);
            // Sub-threshold leftward drift must not flip the facing either.
            Assert.IsFalse(Free(new Vector2(-0.1f, 0f), true, false).FacingLeft);
        }

        [Test]
        public void HoldingStillHolds()
        {
            var c = Holding(Vector2.zero, pressing: false, heldX: 1f, facingLeft: false);
            Assert.AreEqual(SketchState.Holding, c.State);
            Assert.IsTrue(c.Holding);
            Assert.IsFalse(c.Running);
        }

        [Test]
        public void HoldingAndPressingPushes()
        {
            // Pressing along the axis plays the push whether or not the block moved (a blocked push is a strain).
            var c = Holding(Vector2.right, pressing: true, heldX: 1f, facingLeft: false);
            Assert.AreEqual(SketchState.Pushing, c.State);
            Assert.IsTrue(c.Pushing);
        }

        [Test]
        public void PullingPushesToo()
        {
            // Pulling a block on the left by pressing right: the push loop, still facing the block.
            var c = Holding(Vector2.right, pressing: true, heldX: -1f, facingLeft: false);
            Assert.AreEqual(SketchState.Pushing, c.State);
            Assert.IsTrue(c.FacingLeft);
        }

        [Test]
        public void HoldingPressingWithZeroInputHolds()
        {
            // BlockPusher's state is a physics step old; it must never play a push from a standstill.
            var c = Holding(Vector2.zero, pressing: true, heldX: 1f, facingLeft: false);
            Assert.AreEqual(SketchState.Holding, c.State);
        }

        [Test]
        public void HoldingWithSidewaysInputHolds()
        {
            // Sideways input is not pressing along the axis: it does nothing, and the drawing shows the hold.
            var c = Holding(Vector2.up, pressing: false, heldX: 1f, facingLeft: true);
            Assert.AreEqual(SketchState.Holding, c.State);
            Assert.IsFalse(c.FacingLeft, "still faces the block on the right");
        }

        [Test]
        public void HoldingFacesTheBlock()
        {
            Assert.IsTrue(Holding(Vector2.zero, false, heldX: -1f, facingLeft: false).FacingLeft, "block on the left");
            Assert.IsFalse(Holding(Vector2.zero, false, heldX: 1f, facingLeft: true).FacingLeft, "block on the right");
            Assert.IsTrue(Holding(Vector2.left, true, heldX: -1f, facingLeft: false).FacingLeft, "pushing left");
            Assert.IsFalse(Holding(Vector2.left, true, heldX: 1f, facingLeft: true).FacingLeft, "pulling a block on the right by pressing left");
        }

        [Test]
        public void HoldingVerticallyKeepsFacing()
        {
            Assert.IsTrue(Holding(Vector2.up, true, heldX: 0f, facingLeft: true).FacingLeft);
            Assert.IsFalse(Holding(Vector2.down, true, heldX: 0f, facingLeft: false).FacingLeft);
        }

        [Test]
        public void HoldingWithMovementHeldIdles()
        {
            var c = Holding(Vector2.right, pressing: true, heldX: 1f, facingLeft: true, movementEnabled: false);
            Assert.AreEqual(SketchState.Idle, c.State);
            Assert.IsTrue(c.FacingLeft);
        }
    }
}
