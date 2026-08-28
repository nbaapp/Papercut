using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class PressureRulesTests
    {
        static readonly Rect Plate = new(1f, 1f, 1f, 1f);

        [Test]
        public void PressedBySameSideInside()
        {
            var pressers = new List<SheetPoint> { new(new Vector2(1.5f, 1.5f), SheetFace.Front) };
            Assert.IsTrue(PressureRules.IsPressed(Plate, SheetFace.Front, pressers));
        }

        [Test]
        public void NotPressedByOtherSide()
        {
            var pressers = new List<SheetPoint> { new(new Vector2(1.5f, 1.5f), SheetFace.Back) };
            Assert.IsFalse(PressureRules.IsPressed(Plate, SheetFace.Front, pressers));
        }

        [Test]
        public void NotPressedFromOutside_OrByNothing()
        {
            var pressers = new List<SheetPoint> { new(new Vector2(2.5f, 1.5f), SheetFace.Front) };
            Assert.IsFalse(PressureRules.IsPressed(Plate, SheetFace.Front, pressers));
            Assert.IsFalse(PressureRules.IsPressed(Plate, SheetFace.Front, new List<SheetPoint>()));
        }

        [Test]
        public void AnyOnePresserSuffices()
        {
            var pressers = new List<SheetPoint>
            {
                new(new Vector2(9f, 9f), SheetFace.Front),
                new(new Vector2(1.2f, 1.9f), SheetFace.Front),
            };
            Assert.IsTrue(PressureRules.IsPressed(Plate, SheetFace.Front, pressers));
        }
    }
}
