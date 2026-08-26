using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class TravelRulesTests
    {
        static readonly Rect Player = new(0f, 0f, 0.44f, 0.7f);

        [Test]
        public void NoSolids_HasRoom()
        {
            Assert.IsTrue(TravelRules.HasRoom(Player, new Rect[0]));
        }

        [Test]
        public void OverlappingSolid_NoRoom()
        {
            Assert.IsFalse(TravelRules.HasRoom(Player, new[] { Rect.MinMaxRect(0.3f, 0.3f, 2f, 2f) }));
        }

        [Test]
        public void TouchingSolid_HasRoom()
        {
            Assert.IsTrue(TravelRules.HasRoom(Player, new[] { Rect.MinMaxRect(0.44f, 0f, 2f, 2f), Rect.MinMaxRect(-2f, -2f, 2f, 0f) }));
        }
    }
}
