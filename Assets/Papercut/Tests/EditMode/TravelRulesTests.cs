using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class TravelRulesTests
    {
        static readonly Rect Player = new(0f, 0f, 0.44f, 0.7f);

        static ConvexPolygon Box(float xMin, float yMin, float xMax, float yMax) => ConvexPolygon.FromRect(Rect.MinMaxRect(xMin, yMin, xMax, yMax));

        static ConvexPolygon Triangle(Vector2 a, Vector2 b, Vector2 c) => new(new List<Vector2> { a, b, c });

        [Test]
        public void NoSolids_HasRoom()
        {
            Assert.IsTrue(TravelRules.HasRoom(Player, new ConvexPolygon[0]));
        }

        [Test]
        public void OverlappingSolid_NoRoom()
        {
            Assert.IsFalse(TravelRules.HasRoom(Player, new[] { Box(0.3f, 0.3f, 2f, 2f) }));
        }

        [Test]
        public void TouchingSolid_HasRoom()
        {
            Assert.IsTrue(TravelRules.HasRoom(Player, new[] { Box(0.44f, 0f, 2f, 2f), Box(-2f, -2f, 2f, 0f) }));
        }

        [Test]
        public void PolygonSolids_UseTheirTrueShape()
        {
            // A triangle whose bounding box covers the player but whose area does not touch them.
            var beside = Triangle(new Vector2(0.44f, 0f), new Vector2(3f, 0f), new Vector2(3f, 3f));
            Assert.IsTrue(TravelRules.HasRoom(Player, new[] { beside }), "the hypotenuse passes the player's corner without sharing area");

            var over = Triangle(new Vector2(0.2f, 0.2f), new Vector2(3f, 0.2f), new Vector2(3f, 3f));
            Assert.IsFalse(TravelRules.HasRoom(Player, new[] { over }));

            var touchingEdge = Triangle(new Vector2(0.44f, 0f), new Vector2(0.44f, 0.7f), new Vector2(2f, 0.35f));
            Assert.IsTrue(TravelRules.HasRoom(Player, new[] { touchingEdge }), "touching along an edge is fine");
        }
    }
}
