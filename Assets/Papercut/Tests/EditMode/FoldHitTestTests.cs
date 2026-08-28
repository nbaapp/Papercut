using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class FoldHitTestTests
    {
        const float Grab = 0.3f;
        static readonly Rect PlayerFarAway = new(4f, 3f, 0.44f, 0.7f); // near the north edge, clear of the south folds below

        static List<FoldEffect> Effects(params Fold[] folds)
        {
            var stack = SheetLayers.Flat;
            var effects = new List<FoldEffect>();
            for (int i = 0; i < folds.Length; i++)
            {
                stack = stack.Apply(folds[i], i, out var effect);
                effects.Add(effect);
            }
            return effects;
        }

        // South d=2: Seam y = -0.25. Then south d=4: crease y = -0.25 (lifts the first Flap whole), Seam y = 1.75.
        static readonly Fold[] SouthThenSouth = { new(FoldAnchor.EdgeSouth, 2f), new(FoldAnchor.EdgeSouth, 4f) };

        [Test]
        public void OneFold_SeamHits_CreaseDoesNot()
        {
            var effects = Effects(new Fold(FoldAnchor.EdgeSouth, 2f));

            Assert.AreEqual(0, FoldHitTest.SeamAt(effects, new Vector2(0f, -0.25f), Grab, PlayerFarAway, out var rejection));
            Assert.AreEqual(FoldRejection.None, rejection);
            Assert.AreEqual(-1, FoldHitTest.SeamAt(effects, new Vector2(0f, -2.25f), Grab, PlayerFarAway, out rejection), "the crease is not the Seam");
            Assert.AreEqual(FoldRejection.None, rejection);
            Assert.AreEqual(0, FoldHitTest.CreaseAt(effects, new Vector2(0f, -2.25f), Grab));
            Assert.IsFalse(FoldHitTest.IsPinned(effects, 0));
        }

        [Test]
        public void PinnedSeamOnALaterCrease_IsNotAHit_TheCreaseGrabWins()
        {
            var effects = Effects(SouthThenSouth);
            Assert.IsTrue(FoldHitTest.IsPinned(effects, 0));
            Assert.IsFalse(FoldHitTest.IsPinned(effects, 1));

            var press = new Vector2(0f, -0.25f);
            Assert.AreEqual(-1, FoldHitTest.SeamAt(effects, press, Grab, PlayerFarAway, out var rejection));
            Assert.AreEqual(FoldRejection.CoveredByLaterFold, rejection, "advice only; the caller tries a grab next");
            Assert.AreEqual(1, FoldHitTest.CreaseAt(effects, press, Grab), "the later fold's crease is grabbed");
        }

        [Test]
        public void PinnedSeam_NothingElseNear_ReportsCoveredByLaterFold()
        {
            // North d=1 (Seam y = 2.25) then south d=4 lands on y in [-0.25, 3.75], covering it: north is pinned.
            var effects = Effects(new Fold(FoldAnchor.EdgeNorth, 1f), new Fold(FoldAnchor.EdgeSouth, 4f));
            Assert.IsTrue(FoldHitTest.IsPinned(effects, 0));

            var press = new Vector2(0f, 2.25f);
            Assert.AreEqual(-1, FoldHitTest.SeamAt(effects, press, Grab, PlayerFarAway, out var rejection));
            Assert.AreEqual(FoldRejection.CoveredByLaterFold, rejection);
            Assert.AreEqual(-1, FoldHitTest.CreaseAt(effects, press, Grab), "no unpinned crease there");
        }

        [Test]
        public void UnpinnedSeam_StillWins_WhenAPinnedSeamCoincides()
        {
            var effects = Effects(SouthThenSouth);
            Assert.AreEqual(1, FoldHitTest.SeamAt(effects, new Vector2(0f, 1.75f), Grab, PlayerFarAway, out var rejection));
            Assert.AreEqual(FoldRejection.None, rejection);
        }

        [Test]
        public void PlayerOnTheFold_RefusesAndStops()
        {
            var effects = Effects(SouthThenSouth);
            var playerOnLanded = new Rect(-0.22f, 0.5f, 0.44f, 0.7f); // on the second Flap, y in [-0.25, 1.75]
            Assert.AreEqual(-1, FoldHitTest.SeamAt(effects, new Vector2(0f, 1.75f), Grab, playerOnLanded, out var rejection));
            Assert.AreEqual(FoldRejection.PlayerOnFlap, rejection);
        }

        [Test]
        public void IndependentFolds_AreNeverPinned_AndBothCreasesGrab()
        {
            var effects = Effects(new Fold(FoldAnchor.EdgeNorth, 1f), new Fold(FoldAnchor.EdgeSouth, 2f));
            Assert.IsFalse(FoldHitTest.IsPinned(effects, 0));
            Assert.IsFalse(FoldHitTest.IsPinned(effects, 1));
            Assert.AreEqual(0, FoldHitTest.CreaseAt(effects, new Vector2(1f, 3.25f), Grab));
            Assert.AreEqual(1, FoldHitTest.CreaseAt(effects, new Vector2(1f, -2.25f), Grab));
            var playerBetween = new Rect(4f, 0.5f, 0.44f, 0.7f); // between the two Flaps (PlayerFarAway would stand on the north one)
            Assert.AreEqual(0, FoldHitTest.SeamAt(effects, new Vector2(1f, 2.25f), Grab, playerBetween, out _));
            Assert.AreEqual(1, FoldHitTest.SeamAt(effects, new Vector2(1f, -0.25f), Grab, playerBetween, out _));
        }
    }
}
