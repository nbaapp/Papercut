using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>The shared boil beat (Aaron, 2026-09-03: the whole page boils at once) and which animations follow it.</summary>
    public sealed class SketchClockTests
    {
        [Test]
        public void BeatAt_CountsWholeBeats()
        {
            Assert.AreEqual(0, SketchClock.BeatAt(0f, 3f));
            Assert.AreEqual(0, SketchClock.BeatAt(0.33f, 3f));
            Assert.AreEqual(1, SketchClock.BeatAt(0.34f, 3f));
            Assert.AreEqual(9, SketchClock.BeatAt(3.1f, 3f));
            Assert.AreEqual(300, SketchClock.BeatAt(100f, 3f), "no drift over time");
        }

        [Test]
        public void BeatAt_RateZero_HoldsTheFirstBeat()
        {
            Assert.AreEqual(0, SketchClock.BeatAt(123.4f, 0f));
        }

        [Test]
        public void FrameOf_WrapsOverTheFrameCount_SoEveryDrawingFlipsOnTheSameBeat()
        {
            // A 3-frame and a 2-frame drawing both change frame on every beat.
            Assert.AreEqual(0, SketchClock.FrameOf(0, 3));
            Assert.AreEqual(2, SketchClock.FrameOf(2, 3));
            Assert.AreEqual(0, SketchClock.FrameOf(3, 3));
            Assert.AreEqual(1, SketchClock.FrameOf(7, 3));
            Assert.AreEqual(1, SketchClock.FrameOf(7, 2));
            Assert.AreEqual(0, SketchClock.FrameOf(7, 1), "a single frame never changes");
        }

        [Test]
        public void FrameOf_NoFrames_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => SketchClock.FrameOf(1, 0));
        }

        [TestCase("Assets/Papercut/Animations/Box Idle.asset", true)]
        [TestCase("Assets/Papercut/Animations/Button Idle.asset", true)]
        [TestCase("Assets/Papercut/Animations/Tree Idle.asset", true)]
        [TestCase("Assets/Papercut/Animations/Scuffy Idle.asset", true)]
        [TestCase("Assets/Papercut/Animations/Scuffy Run.asset", false)]
        [TestCase("Assets/Papercut/Animations/Scuffy Push.asset", false)]
        public void IdlesAreSynced_ScuffysMovementLoopsAreNot(string path, bool synced)
        {
            // Aaron: the idles should line up; whether the run and push look right synced is his experiment (toggle on the asset).
            var animation = AssetDatabase.LoadAssetAtPath<SketchAnimation>(path);
            Assert.IsNotNull(animation, path);
            Assert.AreEqual(synced, animation.SyncToClock, path);
        }

        [TestCase("Assets/Scenes/Desk.unity")]
        [TestCase("Assets/Scenes/Test Desk.unity")]
        public void DeskScene_HasOneSketchClock(string scenePath)
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            Assert.IsNotNull(scene, scenePath);
            var text = System.IO.File.ReadAllText(scenePath);
            var guid = AssetDatabase.AssetPathToGUID("Assets/Papercut/Scripts/Sketch/SketchClock.cs");
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf($"guid: {guid}", index, System.StringComparison.Ordinal)) >= 0) { count++; index++; }
            Assert.AreEqual(1, count, $"exactly one SketchClock in {scenePath} (synced drawings need one; two would fight)");
        }
    }
}
