using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class FoldValidityTests
    {
        static Rect PlayerAt(float x, float y) => new(x - 0.22f, y - 0.35f, 0.44f, 0.7f);

        [Test]
        public void PlayerOnFlap_IsInvalid()
        {
            Assert.IsFalse(FoldValidity.IsValid(new Fold(FoldAnchor.EdgeSouth, 2f), PlayerAt(0f, -3.5f)));
        }

        [Test]
        public void PlayerUnderLandedFlap_IsInvalid()
        {
            // Crease y = -2.25; landed strip y in [-2.25, -0.25].
            Assert.IsFalse(FoldValidity.IsValid(new Fold(FoldAnchor.EdgeSouth, 2f), PlayerAt(0f, -1f)));
        }

        [Test]
        public void PlayerClear_IsValid()
        {
            Assert.IsTrue(FoldValidity.IsValid(new Fold(FoldAnchor.EdgeSouth, 2f), PlayerAt(0f, 2f)));
            Assert.IsTrue(FoldValidity.IsValid(new Fold(FoldAnchor.CornerNorthEast, 3f), PlayerAt(-2f, -2f)));
        }

        [Test]
        public void DeepFold_LandingOnFarSide_IsInvalid()
        {
            // South fold d = 4.5: lands on y in [0.25, 4.25] - covers a player standing near the north edge.
            Assert.IsFalse(FoldValidity.IsValid(new Fold(FoldAnchor.EdgeSouth, 4.5f), PlayerAt(0f, 3.5f)));
        }

        [Test]
        public void PlayerOnOverhang_CountsAsOnFlap()
        {
            // Geometry only: unreachable through input (no overhang).
            // Corner fold d = 10 hangs below the sheet; a player standing there (outside the sheet rect) is on the flap.
            var fold = new Fold(FoldAnchor.CornerNorthEast, 10f);
            var onOverhang = PlayerAt(0f, -5f);

            Assert.IsFalse(FoldGeometry.HiddenRect(fold).Overlaps(onOverhang), "sanity: outside the sheet");
            Assert.IsTrue(FoldValidity.PlayerOverlapsFlap(fold, onOverhang));
        }
    }
}
