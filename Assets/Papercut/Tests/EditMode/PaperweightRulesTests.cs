using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>
    /// The paperweight unfold rule (Aaron, 2026-09-24): a weight is always on top of the sheet, so a fold whose
    /// Flap any part of it rests on cannot be undone. Managed math only: the weight's visible pieces come from
    /// <see cref="SheetPlacement.VisiblePieces"/> exactly as <see cref="PushableBlock"/> computes them.
    /// </summary>
    public sealed class PaperweightRulesTests
    {
        static readonly Vector2 WeightSize = new(0.74f, 0.7f); // The Block footprint.

        static Rect WeightAt(float x, float y) => new(x - WeightSize.x * 0.5f, y - WeightSize.y * 0.5f, WeightSize.x, WeightSize.y);

        static SheetLayers Replay(params Fold[] folds)
        {
            var stack = SheetLayers.Flat;
            for (int i = 0; i < folds.Length; i++)
                stack = stack.Apply(folds[i], i, out _);
            return stack;
        }

        /// <summary>South edge fold of depth 3: crease y = −1.25; the Flap lands over y ∈ [−1.25, 1.75], Back-up, moved by fold 0.</summary>
        static SheetLayers SouthFold() => Replay(new Fold(FoldAnchor.EdgeSouth, 3f));

        static bool Rides(Rect weight, SheetFace side, SheetLayers stack, int foldIndex, int climbTarget = -1)
            => PaperweightRules.RidesFold(SheetPlacement.VisiblePieces(weight, side, stack, climbTarget), stack, foldIndex);

        [Test]
        public void FlatSheet_WeightOnBase_RidesNothing()
        {
            Assert.IsFalse(Rides(WeightAt(0f, 0f), SheetFace.Front, SheetLayers.Flat, 0));
        }

        [Test]
        public void WeightOnBase_ClearOfFlap_DoesNotRide()
        {
            Assert.IsFalse(Rides(WeightAt(0f, 3f), SheetFace.Front, SouthFold(), 0));
        }

        [Test]
        public void WeightOnFlap_Rides()
        {
            // A Back-side weight at flat y = −3 was lifted by the south fold and now lies face-up on the Flap at y = 0.5.
            Assert.IsTrue(Rides(WeightAt(0f, -3f), SheetFace.Back, SouthFold(), 0));
        }

        [Test]
        public void WeightUnderFlap_Covered_DoesNotRide()
        {
            // A Front weight at y = 0 lies wholly under the landed Flap: no visible piece, so nothing to carry.
            var pieces = SheetPlacement.VisiblePieces(WeightAt(0f, 0f), SheetFace.Front, SouthFold());
            Assert.AreEqual(0, pieces.Count);
            Assert.IsFalse(PaperweightRules.RidesFold(pieces, SouthFold(), 0));
        }

        [Test]
        public void WeightStraddlingCrease_PartOnFlap_Rides()
        {
            // A Back weight centred at y = −1.5 (rect y ∈ [−1.85, −1.15]) straddles the crease at −1.25: its lower part was
            // lifted face-up onto the Flap, its upper part stays on the Base face-down. Its centre is on the Flap side,
            // but the rule is any part (Aaron): push it fully clear before unfolding.
            Assert.IsTrue(Rides(WeightAt(0f, -1.5f), SheetFace.Back, SouthFold(), 0));
        }

        [Test]
        public void WeightClimbingOntoFlap_CountsAsOnIt()
        {
            // A Front weight centred at y = 2.0 (rect y ∈ [1.65, 2.35]) overlaps the Flap's landed edge at y = 1.75 while
            // climbing onto it (climb target = the Flap layer, index 1): the part over the Flap is on its surface.
            var stack = SouthFold();
            Assert.IsTrue(Rides(WeightAt(0f, 2f), SheetFace.Front, stack, 0, climbTarget: 1));
            // The same weight without a climb belongs under the Flap (partly covered) and rides nothing.
            Assert.IsFalse(Rides(WeightAt(0f, 2f), SheetFace.Front, stack, 0));
        }

        [Test]
        public void WeightOnFlap_OtherFold_DoesNotRide()
        {
            // South fold (0) and a west fold (1, depth 2: lifts x ∈ [−5.5, −3.5], lands over [−3.5, −1.5]) that do not touch.
            var stack = Replay(new Fold(FoldAnchor.EdgeSouth, 3f), new Fold(FoldAnchor.EdgeWest, 2f));
            var weight = WeightAt(3f, -3f); // On the south Flap.
            Assert.IsTrue(Rides(weight, SheetFace.Back, stack, 0));
            Assert.IsFalse(Rides(weight, SheetFace.Back, stack, 1));
        }
    }
}
