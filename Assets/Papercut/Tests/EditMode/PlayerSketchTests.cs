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
            var c = PlayerSketch.Choose(Vector2.zero, true, Threshold, false);
            Assert.IsFalse(c.Running);
        }

        [Test]
        public void CardinalInputRuns()
        {
            var c = PlayerSketch.Choose(Vector2.right, true, Threshold, false);
            Assert.IsTrue(c.Running);
        }

        [Test]
        public void ClampedDiagonalRuns()
        {
            var input = Vector2.ClampMagnitude(new Vector2(1f, 1f), 1f);
            var c = PlayerSketch.Choose(input, true, Threshold, false);
            Assert.IsTrue(c.Running);
        }

        [Test]
        public void InputAtThresholdDoesNotRun()
        {
            var c = PlayerSketch.Choose(new Vector2(1f, 0f), true, 1f, false);
            Assert.IsFalse(c.Running);
        }

        [Test]
        public void InputJustAboveThresholdRuns()
        {
            var c = PlayerSketch.Choose(new Vector2(0.25f, 0f), true, Threshold, false);
            Assert.IsTrue(c.Running);
        }

        [Test]
        public void MovementHeldDoesNotRunAndKeepsFacing()
        {
            var c = PlayerSketch.Choose(Vector2.right, false, Threshold, true);
            Assert.IsFalse(c.Running);
            Assert.IsTrue(c.FacingLeft, "facing must not change while movement is held");
        }

        [Test]
        public void LeftInputFacesLeft()
        {
            var c = PlayerSketch.Choose(Vector2.left, true, Threshold, false);
            Assert.IsTrue(c.FacingLeft);
        }

        [Test]
        public void RightInputFacesRight()
        {
            var c = PlayerSketch.Choose(Vector2.right, true, Threshold, true);
            Assert.IsFalse(c.FacingLeft);
        }

        [Test]
        public void VerticalInputKeepsFacing()
        {
            Assert.IsTrue(PlayerSketch.Choose(Vector2.up, true, Threshold, true).FacingLeft);
            Assert.IsFalse(PlayerSketch.Choose(Vector2.down, true, Threshold, false).FacingLeft);
        }

        [Test]
        public void IdleInputKeepsFacing()
        {
            Assert.IsTrue(PlayerSketch.Choose(Vector2.zero, true, Threshold, true).FacingLeft);
            // Sub-threshold leftward drift must not flip the facing either.
            Assert.IsFalse(PlayerSketch.Choose(new Vector2(-0.1f, 0f), true, Threshold, false).FacingLeft);
        }
    }
}
