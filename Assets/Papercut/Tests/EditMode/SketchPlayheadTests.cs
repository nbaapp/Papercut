using System;
using NUnit.Framework;

namespace Papercut.Tests
{
    public sealed class SketchPlayheadTests
    {
        [Test]
        public void AdvancesOneFrameAfterOneFramePeriod()
        {
            var p = new SketchPlayhead();
            p.Advance(0.5f, 2f, 3);
            Assert.AreEqual(1, p.Frame);
            Assert.AreEqual(0f, p.Elapsed, 1e-6f);
        }

        [Test]
        public void HoldsFrameBeforePeriodElapses()
        {
            var p = new SketchPlayhead();
            p.Advance(0.25f, 2f, 3);
            Assert.AreEqual(0, p.Frame);
            Assert.AreEqual(0.25f, p.Elapsed, 1e-6f);
        }

        [Test]
        public void WrapsToFirstFrameAtEnd()
        {
            var p = new SketchPlayhead();
            p.Reset(2, 0f);
            p.Advance(0.5f, 2f, 3);
            Assert.AreEqual(0, p.Frame);
        }

        [Test]
        public void LargeDeltaAdvancesSeveralFramesWithoutDrift()
        {
            var p = new SketchPlayhead();
            p.Advance(1.1f, 2f, 3); // 2.2 frame periods -> frame 2, 0.1s into it.
            Assert.AreEqual(2, p.Frame);
            Assert.AreEqual(0.1f, p.Elapsed, 1e-5f);
        }

        [Test]
        public void ZeroFrameRateHoldsCurrentFrame()
        {
            var p = new SketchPlayhead();
            p.Reset(1, 0.3f);
            p.Advance(10f, 0f, 3);
            Assert.AreEqual(1, p.Frame);
            Assert.AreEqual(0f, p.Elapsed);
        }

        [Test]
        public void FrameRateChangeMidLoopKeepsProgress()
        {
            var p = new SketchPlayhead();
            p.Advance(0.3f, 2f, 3);   // 0.3s into a 0.5s frame.
            p.Advance(0.05f, 10f, 3); // frames are now 0.1s: 0.35s elapsed -> 3 frames on (wraps to 0), 0.05s in.
            Assert.AreEqual(0, p.Frame);
            Assert.AreEqual(0.05f, p.Elapsed, 1e-5f);
        }

        [Test]
        public void ShrunkFrameListClampsFrame()
        {
            var p = new SketchPlayhead();
            p.Reset(5, 0f);
            p.Advance(0f, 2f, 3);
            Assert.AreEqual(2, p.Frame);
        }

        [Test]
        public void SingleFrameStaysOnIt()
        {
            var p = new SketchPlayhead();
            p.Advance(3f, 6f, 1);
            Assert.AreEqual(0, p.Frame);
        }

        [Test]
        public void VeryHighFrameRateTerminatesAndStaysInRange()
        {
            var p = new SketchPlayhead();
            p.Advance(0.33f, 1e8f, 3); // 33,000,000 periods in one delta.
            Assert.That(p.Frame, Is.InRange(0, 2));
            Assert.That(p.Elapsed, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f / 1e8f + 1e-9f));
        }

        [Test]
        public void ManyStepsWrapCorrectly()
        {
            var p = new SketchPlayhead();
            p.Advance(2.05f, 10f, 3); // 20 periods -> 20 % 3 = frame 2, 0.05s in.
            Assert.AreEqual(2, p.Frame);
            Assert.AreEqual(0.05f, p.Elapsed, 1e-4f);
        }

        [Test]
        public void NoFramesThrows()
        {
            var p = new SketchPlayhead();
            Assert.Throws<ArgumentOutOfRangeException>(() => p.Advance(0.1f, 6f, 0));
        }
    }
}
