using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class FoldValidityTests
    {
        static Rect PlayerAt(float x, float y) => new(x - 0.22f, y - 0.35f, 0.44f, 0.7f);

        static FoldEffect Effect(Fold fold, params Fold[] before)
        {
            var stack = SheetLayers.Flat;
            for (int i = 0; i < before.Length; i++)
                stack = stack.Apply(before[i], i, out _);
            stack.Apply(fold, before.Length, out var effect);
            return effect;
        }

        [Test]
        public void PlayerOnFlap_IsInvalid()
        {
            Assert.IsFalse(FoldValidity.IsValid(Effect(new Fold(FoldAnchor.EdgeSouth, 2f)), PlayerAt(0f, -3.5f)));
        }

        [Test]
        public void PlayerUnderLandedFlap_IsInvalid()
        {
            // Crease y = -2.25; landed strip y in [-2.25, -0.25].
            Assert.IsFalse(FoldValidity.IsValid(Effect(new Fold(FoldAnchor.EdgeSouth, 2f)), PlayerAt(0f, -1f)));
        }

        [Test]
        public void PlayerClear_IsValid()
        {
            Assert.IsTrue(FoldValidity.IsValid(Effect(new Fold(FoldAnchor.EdgeSouth, 2f)), PlayerAt(0f, 2f)));
            Assert.IsTrue(FoldValidity.IsValid(Effect(new Fold(FoldAnchor.CornerNorthEast, 3f)), PlayerAt(-2f, -2f)));
        }

        [Test]
        public void DeepFold_LandingOnFarSide_IsInvalid()
        {
            // South fold d = 4: lands on y in [-0.25, 3.75] - covers a player standing near the north edge.
            Assert.IsFalse(FoldValidity.IsValid(Effect(new Fold(FoldAnchor.EdgeSouth, 4f)), PlayerAt(0f, 3.5f)));
        }

        [Test]
        public void PlayerOnAnEarlierFlap_LiftedByALaterFold_IsInvalid()
        {
            // North Flap lies at y in [0.25, 2.25]; a west fold across it would carry the player standing on it.
            var effect = Effect(new Fold(FoldAnchor.EdgeWest, 3f), new Fold(FoldAnchor.EdgeNorth, 2f));
            Assert.IsFalse(FoldValidity.IsValid(effect, PlayerAt(-4f, 1f)));
            Assert.IsTrue(FoldValidity.IsValid(effect, PlayerAt(2f, 1f)));
        }

        [Test]
        public void PlayerOnAnEmptiedRegion_DoesNotBlockAFoldThatLandsNothingThere()
        {
            // North strip lifted (y > 2.25 is desk). A NE corner fold's Flap side includes that empty region; its
            // mirror image lies over real ground, but nothing lands there - the rule is exact, not conservative.
            var effect = Effect(new Fold(FoldAnchor.CornerNorthEast, 4f), new Fold(FoldAnchor.EdgeNorth, 2f));
            Assert.IsTrue(FoldValidity.IsValid(effect, PlayerAt(1.75f, 1f)), "the reflected empty region carries no Flap");
        }

        [Test]
        public void Independence()
        {
            var north = Effect(new Fold(FoldAnchor.EdgeNorth, 2f));
            var south = Effect(new Fold(FoldAnchor.EdgeSouth, 1f), new Fold(FoldAnchor.EdgeNorth, 2f)); // lands on y in [-3.25, -2.25], clear of the north Flap
            var west = Effect(new Fold(FoldAnchor.EdgeWest, 3f), new Fold(FoldAnchor.EdgeNorth, 2f));

            Assert.IsTrue(FoldValidity.Independent(north, south));
            Assert.IsFalse(FoldValidity.Independent(north, west));
        }
    }
}
