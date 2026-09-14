using System.Collections.Generic;
using NUnit.Framework;
using Papercut.EditorTools;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>The sheet map's pure layout: which cells are drawn, and the pixel ↔ grid position mapping.</summary>
    public sealed class StudioSheetMapTests
    {
        static Vector2Int P(int x, int y) => new(x, y);

        [Test]
        public void CellBounds_WithNothingOccupied_IsARingAroundTheOrigin()
        {
            var bounds = StudioSheetMap.CellBounds(new List<Vector2Int>());
            Assert.AreEqual(new RectInt(-StudioSheetMap.Ring, -StudioSheetMap.Ring, 2 * StudioSheetMap.Ring + 1, 2 * StudioSheetMap.Ring + 1), bounds);
        }

        [Test]
        public void CellBounds_GrowsAroundTheOccupiedCellsByTheRing()
        {
            var bounds = StudioSheetMap.CellBounds(new List<Vector2Int> { P(0, 0), P(2, 1), P(-1, 0) });
            // x −1..2, y 0..1, plus the ring on every side.
            Assert.AreEqual(-1 - StudioSheetMap.Ring, bounds.xMin);
            Assert.AreEqual(2 + StudioSheetMap.Ring + 1, bounds.xMax);
            Assert.AreEqual(0 - StudioSheetMap.Ring, bounds.yMin);
            Assert.AreEqual(1 + StudioSheetMap.Ring + 1, bounds.yMax);
            Assert.IsTrue(bounds.Contains(P(3, 2)), "the ring cell beyond the far corner is drawn");
            Assert.IsFalse(bounds.Contains(P(4, 2)), "nothing beyond the ring is drawn");
        }

        [Test]
        public void Layout_CellsHaveTheSheetsAspect_AndNorthIsUp()
        {
            var layout = StudioSheetMap.Layout(new Rect(0f, 0f, 600f, 400f), new List<Vector2Int> { P(0, 0), P(1, 1) });
            Assert.AreEqual(SheetGeometry.Height / SheetGeometry.Width, layout.CellSize.y / layout.CellSize.x, 1e-4f);
            Assert.Less(layout.CellRect(P(0, 1)).y, layout.CellRect(P(0, 0)).y, "+y (north) is drawn above");
            Assert.AreEqual(layout.CellRect(P(0, 1)).x, layout.CellRect(P(0, 0)).x, 1e-4f, "same column, same x");
            Assert.Greater(layout.CellRect(P(1, 0)).x, layout.CellRect(P(0, 0)).x, "+x (east) is drawn to the right");
            Assert.AreEqual(layout.CellRect(P(layout.Cells.xMin, layout.Cells.yMax - 1)).position, layout.Area.position, "the north-west cell sits at the area's origin");
        }

        [Test]
        public void TryCellAt_RoundTripsEveryDrawnCell_AndRejectsGapsAndOutside()
        {
            var layout = StudioSheetMap.Layout(new Rect(20f, 10f, 500f, 300f), new List<Vector2Int> { P(0, 0), P(2, -1) });
            for (int y = layout.Cells.yMin; y < layout.Cells.yMax; y++)
            for (int x = layout.Cells.xMin; x < layout.Cells.xMax; x++)
            {
                var cell = P(x, y);
                var rect = layout.CellRect(cell);
                Assert.IsTrue(layout.TryCellAt(rect.center, out var hit), $"centre of {cell}");
                Assert.AreEqual(cell, hit);
                Assert.IsTrue(layout.TryCellAt(rect.min + Vector2.one * 0.5f, out hit), $"corner of {cell}");
                Assert.AreEqual(cell, hit);
            }

            var first = layout.CellRect(P(layout.Cells.xMin, layout.Cells.yMax - 1));
            var inGap = new Vector2(first.xMax + StudioSheetMap.CellGap * 0.5f, first.center.y);
            Assert.IsFalse(layout.TryCellAt(inGap, out _), "the gap between cells is nobody's");
            Assert.IsFalse(layout.TryCellAt(layout.Area.min - Vector2.one, out _), "outside the area");
            Assert.IsFalse(layout.TryCellAt(layout.Area.max + Vector2.one, out _), "outside the area");
        }

        [Test]
        public void Layout_ClampsTheCellWidth()
        {
            var occupied = new List<Vector2Int> { P(0, 0) };
            var tiny = StudioSheetMap.Layout(new Rect(0f, 0f, 30f, 30f), occupied);
            Assert.AreEqual(StudioSheetMap.MinCellWidth, tiny.CellSize.x, 1e-4f, "never below the minimum (the map scrolls instead)");
            Assert.Greater(tiny.Area.width, 30f, "the grid may exceed the available rect");

            var huge = StudioSheetMap.Layout(new Rect(0f, 0f, 4000f, 4000f), occupied);
            Assert.AreEqual(StudioSheetMap.MaxCellWidth, huge.CellSize.x, 1e-4f, "never above the maximum");
            Assert.AreEqual((4000f - huge.Area.width) * 0.5f, huge.Area.x, 1e-3f, "centred horizontally");
        }

        [Test]
        public void PreferredHeight_IsTheUnboundedGridHeight()
        {
            var occupied = new List<Vector2Int> { P(0, 0), P(0, 3) };
            var height = StudioSheetMap.PreferredHeight(800f, occupied);
            var layout = StudioSheetMap.Layout(new Rect(0f, 0f, 800f, 10000f), occupied);
            Assert.AreEqual(layout.Area.height, height, 1e-4f);
            var rows = layout.Cells.height;
            Assert.AreEqual(rows * layout.CellSize.y + (rows - 1) * StudioSheetMap.CellGap, height, 1e-3f);
        }
    }
}
