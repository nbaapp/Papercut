using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class ScreenNavigatorTests
    {
        const float Inset = 0.75f;
        static readonly Rect Origin = SheetGeometry.BoundsAt(Vector2.zero);
        static readonly Rect EastNeighbour = SheetGeometry.BoundsAt(new Vector2(11.5f, 0f));
        static readonly Rect NorthNeighbour = SheetGeometry.BoundsAt(new Vector2(0f, 9f));

        [Test]
        public void EntryPosition_East_KeepsAlongEdgeCoordinateAndArrivesInsideWestEdge()
        {
            var entry = ScreenNavigator.EntryPosition(Origin, EastNeighbour, GridDirection.East, new Vector2(5.2f, 1.3f), Inset);

            var expectedX = EastNeighbour.xMin + Inset;
            Assert.AreEqual(expectedX, entry.x, 1e-4f);
            Assert.AreEqual(1.3f, entry.y, 1e-4f);
        }

        [Test]
        public void EntryPosition_North_KeepsAlongEdgeCoordinateAndArrivesInsideSouthEdge()
        {
            var entry = ScreenNavigator.EntryPosition(Origin, NorthNeighbour, GridDirection.North, new Vector2(-2f, 4.1f), Inset);

            Assert.AreEqual(-2f, entry.x, 1e-4f);
            Assert.AreEqual(NorthNeighbour.yMin + Inset, entry.y, 1e-4f);
        }

        [Test]
        public void EntryPosition_ClampsAlongEdgeCoordinateAwayFromCorners()
        {
            var entry = ScreenNavigator.EntryPosition(Origin, EastNeighbour, GridDirection.East, new Vector2(5.4f, 4.2f), Inset);

            Assert.AreEqual(EastNeighbour.yMax - Inset, entry.y, 1e-4f);
        }

        [Test]
        public void EntryPosition_ResultIsInsideDestination()
        {
            foreach (var direction in new[] { GridDirection.North, GridDirection.East, GridDirection.South, GridDirection.West })
            {
                var to = SheetGeometry.BoundsAt(direction.ToVector() * 12f);
                var entry = ScreenNavigator.EntryPosition(Origin, to, direction, new Vector2(40f, -40f), Inset);
                Assert.IsTrue(to.Contains(entry), $"{direction}: {entry} not inside {to}");
            }
        }
    }
}
