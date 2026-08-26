using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class GridDirectionTests
    {
        [Test]
        public void ToOffset_NorthIsPlusY_EastIsPlusX()
        {
            Assert.AreEqual(new Vector2Int(0, 1), GridDirection.North.ToOffset());
            Assert.AreEqual(new Vector2Int(1, 0), GridDirection.East.ToOffset());
            Assert.AreEqual(new Vector2Int(0, -1), GridDirection.South.ToOffset());
            Assert.AreEqual(new Vector2Int(-1, 0), GridDirection.West.ToOffset());
        }

        [TestCase(GridDirection.North, GridDirection.South)]
        [TestCase(GridDirection.East, GridDirection.West)]
        [TestCase(GridDirection.South, GridDirection.North)]
        [TestCase(GridDirection.West, GridDirection.East)]
        public void Opposite_ReversesOffset(GridDirection direction, GridDirection expected)
        {
            Assert.AreEqual(expected, direction.Opposite());
            Assert.AreEqual(-direction.ToOffset(), direction.Opposite().ToOffset());
        }
    }
}
