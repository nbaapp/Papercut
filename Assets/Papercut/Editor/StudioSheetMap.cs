using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// The Sheet Studio's sheet map: the open Desk's sheet set drawn as a small grid, one cell per grid
    /// position, in which a sheet is selected by a click, opened by a double-click, and moved (or swapped
    /// with the sheet already there) by dragging it onto another cell. Aaron, 2026-09-09: "Grid map with
    /// drag-and-drop"; "Swap" on an occupied target.
    /// </summary>
    /// <remarks>
    /// The map holds no asset or scene references: it is drawn from the <see cref="StudioSheetOps.SheetSlot"/>
    /// list passed in each frame and reports intent through events; <see cref="SheetStudioWindow"/> runs
    /// the operations and shows dialogs. The drawn grid is the occupied bounds plus <see cref="Ring"/>
    /// empty cells on every side, so a sheet can always be dragged one cell outward and the grid grows with
    /// the set. Grid dimensions are open (Bible §7, decision #14) — nothing here bounds them. The layout
    /// math is pure (<see cref="Layout"/>) so it is unit-testable.
    /// </remarks>
    public sealed class StudioSheetMap
    {
        /// <summary>Empty cells drawn around the occupied bounds, per side.</summary>
        public const int Ring = 1;
        public const float MinCellWidth = 40f;
        public const float MaxCellWidth = 110f;
        public const float CellGap = 3f;
        /// <summary>Pixels the mouse must travel after a press before it is a drag rather than a click.</summary>
        public const float DragThreshold = 6f;
        const float OutlineThickness = 2f;
        const float GhostAlpha = 0.65f;
        const float ScrollbarWidth = 16f;

        static readonly Color EmptyCellColor = new(0.5f, 0.5f, 0.5f, 0.12f);
        static readonly Color OnDeskColor = new(0.32f, 0.5f, 0.78f, 0.9f);
        static readonly Color OffDeskColor = new(0.45f, 0.45f, 0.45f, 0.7f);
        static readonly Color WarningColor = new(0.82f, 0.45f, 0.18f, 0.9f);
        static readonly Color SelectedOutline = Color.white;
        static readonly Color MoveTargetOutline = new(0.3f, 0.85f, 0.3f, 1f);
        static readonly Color SwapTargetOutline = new(0.95f, 0.75f, 0.2f, 1f);
        static readonly Color RefusedTargetOutline = new(0.9f, 0.25f, 0.25f, 1f);

        /// <summary>Which cells are drawn and where; pixel ↔ grid position mapping. Pure.</summary>
        public readonly struct MapLayout
        {
            /// <summary>Grid positions drawn (max exclusive, as RectInt has it): occupied bounds plus the ring.</summary>
            public readonly RectInt Cells;

            /// <summary>Pixel rect of the whole grid.</summary>
            public readonly Rect Area;

            /// <summary>Pixel size of one cell; its aspect is the sheet's (11 : 8.5).</summary>
            public readonly Vector2 CellSize;

            public MapLayout(RectInt cells, Rect area, Vector2 cellSize)
            {
                Cells = cells;
                Area = area;
                CellSize = cellSize;
            }

            public bool Contains(Vector2Int gridPosition) => Cells.Contains(gridPosition);

            /// <summary>Pixel rect of a cell. North (+y) is up: the row for <c>Cells.yMax - 1</c> is topmost.</summary>
            public Rect CellRect(Vector2Int gridPosition)
            {
                var column = gridPosition.x - Cells.xMin;
                var rowFromTop = Cells.yMax - 1 - gridPosition.y;
                return new Rect(
                    Area.x + column * (CellSize.x + CellGap),
                    Area.y + rowFromTop * (CellSize.y + CellGap),
                    CellSize.x, CellSize.y);
            }

            /// <summary>The drawn cell under a pixel; false over a gap or outside the grid.</summary>
            public bool TryCellAt(Vector2 point, out Vector2Int gridPosition)
            {
                gridPosition = default;
                if (!Area.Contains(point))
                    return false;
                var column = Mathf.FloorToInt((point.x - Area.x) / (CellSize.x + CellGap));
                var rowFromTop = Mathf.FloorToInt((point.y - Area.y) / (CellSize.y + CellGap));
                var candidate = new Vector2Int(Cells.xMin + column, Cells.yMax - 1 - rowFromTop);
                if (!Cells.Contains(candidate) || !CellRect(candidate).Contains(point))
                    return false;
                gridPosition = candidate;
                return true;
            }
        }

        /// <summary>The occupied bounds (or (0,0) alone when nothing is occupied) grown by <see cref="Ring"/> on every side.</summary>
        public static RectInt CellBounds(IReadOnlyList<Vector2Int> occupied)
        {
            var min = Vector2Int.zero;
            var max = Vector2Int.zero;
            for (int i = 0; i < occupied.Count; i++)
            {
                var p = occupied[i];
                if (i == 0)
                {
                    min = p;
                    max = p;
                    continue;
                }
                min = Vector2Int.Min(min, p);
                max = Vector2Int.Max(max, p);
            }
            min -= Vector2Int.one * Ring;
            max += Vector2Int.one * Ring;
            return new RectInt(min.x, min.y, max.x - min.x + 1, max.y - min.y + 1);
        }

        /// <summary>
        /// Lays the grid out in <paramref name="available"/>: the largest cell that fits at the sheet aspect,
        /// clamped to [<see cref="MinCellWidth"/>, <see cref="MaxCellWidth"/>], centred horizontally. The
        /// grid may exceed the available rect (then <see cref="Draw"/> scrolls).
        /// </summary>
        public static MapLayout Layout(Rect available, IReadOnlyList<Vector2Int> occupied)
        {
            var cells = CellBounds(occupied);
            var columns = cells.width;
            var rows = cells.height;
            var aspect = SheetGeometry.Height / SheetGeometry.Width;
            var widthByWidth = (available.width - (columns - 1) * CellGap) / columns;
            var widthByHeight = (available.height - (rows - 1) * CellGap) / rows / aspect;
            var cellWidth = Mathf.Clamp(Mathf.Min(widthByWidth, widthByHeight), MinCellWidth, MaxCellWidth);
            var cellSize = new Vector2(cellWidth, cellWidth * aspect);
            var gridWidth = columns * cellSize.x + (columns - 1) * CellGap;
            var gridHeight = rows * cellSize.y + (rows - 1) * CellGap;
            var area = new Rect(available.x + Mathf.Max(0f, (available.width - gridWidth) * 0.5f), available.y, gridWidth, gridHeight);
            return new MapLayout(cells, area, cellSize);
        }

        /// <summary>Pixel height the grid wants at <paramref name="width"/> (unbounded height), for the window's layout.</summary>
        public static float PreferredHeight(float width, IReadOnlyList<Vector2Int> occupied)
            => Layout(new Rect(0f, 0f, width, float.MaxValue), occupied).Area.height;

        // ----- State -----

        Vector2Int? selected;
        Vector2Int? dragCandidate;
        Vector2 pressPosition;
        Vector2 dragMouse;
        bool dragging;
        Vector2 scroll;
        GUIStyle cellStyle;

        /// <summary>The selected cell; the window's action row acts on it.</summary>
        public Vector2Int? Selected
        {
            get => selected;
            set
            {
                if (selected == value)
                    return;
                selected = value;
                SelectionChanged?.Invoke();
            }
        }

        public bool IsDragging => dragging;

        /// <summary>Double-click on a cell that has a variant.</summary>
        public event Action<Vector2Int> OpenRequested;

        /// <summary>Click on an empty cell.</summary>
        public event Action<Vector2Int> EmptyCellClicked;

        /// <summary>A drag dropped on another cell: (from, to). The window runs the move and reports.</summary>
        public event Action<Vector2Int, Vector2Int> MoveRequested;

        /// <summary>The selection changed (by click or by the window).</summary>
        public event Action SelectionChanged;

        /// <summary>Visual state changed (drag started, moved, ended, cancelled); the window should repaint.</summary>
        public event Action Changed;

        public void CancelDrag()
        {
            if (dragCandidate == null)
                return;
            dragCandidate = null;
            dragging = false;
            Changed?.Invoke();
        }

        // ----- Drawing & input -----

        /// <summary>Draws the map into <paramref name="rect"/> and handles its mouse events.</summary>
        public void Draw(Rect rect, IReadOnlyList<StudioSheetOps.SheetSlot> slots)
        {
            var byPosition = new Dictionary<Vector2Int, StudioSheetOps.SheetSlot>(slots.Count);
            var occupied = new List<Vector2Int>(slots.Count);
            foreach (var slot in slots)
            {
                if (slot.IsEmpty)
                    continue;
                byPosition[slot.GridPosition] = slot;
                occupied.Add(slot.GridPosition);
            }

            var layout = Layout(new Rect(0f, 0f, rect.width, rect.height), occupied);
            var contentWidth = rect.width;
            if (layout.Area.height > rect.height)
            {
                // A vertical scrollbar takes width; lay out (and size the content) inside what is left so it
                // does not force a horizontal scrollbar as well.
                contentWidth = rect.width - ScrollbarWidth;
                layout = Layout(new Rect(0f, 0f, contentWidth, rect.height), occupied);
            }
            var content = new Rect(0f, 0f, Mathf.Max(layout.Area.xMax, contentWidth), Mathf.Max(layout.Area.yMax, rect.height));
            scroll = GUI.BeginScrollView(rect, scroll, content);
            HandleMouse(layout, byPosition);
            if (Event.current.type == EventType.Repaint)
                Paint(layout, byPosition);
            GUI.EndScrollView();
        }

        void HandleMouse(MapLayout layout, Dictionary<Vector2Int, StudioSheetOps.SheetSlot> byPosition)
        {
            var e = Event.current;
            // Inside the scroll view's clip a mouse event outside the visible map arrives as Ignore. A
            // press must respect that (e.type): when the map scrolls, a click on the buttons just below
            // it lands on a hidden cell in content space. Drag and release use rawType instead, so a drag
            // released past the edge still ends rather than leaving a stuck ghost.
            switch (e.rawType)
            {
                case EventType.MouseDown when e.button == 0 && e.type == EventType.MouseDown:
                {
                    if (!layout.TryCellAt(e.mousePosition, out var cell))
                        return;
                    GUIUtility.keyboardControl = 0; // Like the panes: a map click takes focus from the window's fields.
                    if (!byPosition.TryGetValue(cell, out var slot))
                    {
                        Selected = null;
                        EmptyCellClicked?.Invoke(cell);
                    }
                    else
                    {
                        Selected = cell;
                        if (e.clickCount == 2)
                        {
                            dragCandidate = null;
                            Changed?.Invoke();
                            e.Use();
                            if (slot.AssetPath != null)
                                OpenRequested?.Invoke(cell); // Last: the window's handler ends the pass (ExitGUI).
                            return;
                        }
                        if (slot.Movable)
                        {
                            dragCandidate = cell;
                            pressPosition = e.mousePosition;
                            dragMouse = e.mousePosition;
                        }
                    }
                    Changed?.Invoke();
                    e.Use();
                    return;
                }
                case EventType.MouseDown when e.button == 1:
                {
                    if (!dragging)
                        return;
                    CancelDrag();
                    e.Use();
                    return;
                }
                case EventType.MouseDrag:
                {
                    if (dragCandidate == null)
                        return;
                    dragMouse = e.mousePosition;
                    if (!dragging && (dragMouse - pressPosition).magnitude >= DragThreshold)
                        dragging = true;
                    if (!dragging)
                        return;
                    Changed?.Invoke();
                    e.Use();
                    return;
                }
                case EventType.MouseUp when e.button == 0:
                {
                    if (dragCandidate == null)
                        return;
                    var from = dragCandidate.Value;
                    var wasDragging = dragging;
                    // A release outside the visible map (type Ignore under the clip) ends the drag but drops
                    // nowhere: in content space it could map to a cell scrolled out of view.
                    Vector2Int to = default;
                    var dropped = e.type == EventType.MouseUp && layout.TryCellAt(e.mousePosition, out to);
                    dragCandidate = null;
                    dragging = false;
                    Changed?.Invoke();
                    e.Use();
                    if (wasDragging && dropped && to != from)
                        MoveRequested?.Invoke(from, to);
                    return;
                }
            }
        }

        void Paint(MapLayout layout, Dictionary<Vector2Int, StudioSheetOps.SheetSlot> byPosition)
        {
            cellStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                normal = { textColor = Color.white },
            };

            var dropTarget = dragging && layout.TryCellAt(dragMouse, out var under) && under != dragCandidate.Value
                ? under
                : (Vector2Int?)null;

            for (int y = layout.Cells.yMin; y < layout.Cells.yMax; y++)
            for (int x = layout.Cells.xMin; x < layout.Cells.xMax; x++)
            {
                var cell = new Vector2Int(x, y);
                var cellRect = layout.CellRect(cell);
                byPosition.TryGetValue(cell, out var slot);
                var isDragSource = dragging && dragCandidate == cell;

                var fill = FillColor(slot);
                if (isDragSource)
                    fill.a *= 0.35f;
                EditorGUI.DrawRect(cellRect, fill);
                GUI.Label(cellRect, LabelFor(cell, slot), cellStyle);

                if (selected == cell)
                    DrawOutline(cellRect, SelectedOutline);
                if (dropTarget == cell)
                    DrawOutline(cellRect, DropOutline(slot));
            }

            if (dragging && byPosition.TryGetValue(dragCandidate.Value, out var dragged))
            {
                var ghost = new Rect(dragMouse - layout.CellSize * 0.5f, layout.CellSize);
                var ghostFill = FillColor(dragged);
                ghostFill.a *= GhostAlpha;
                EditorGUI.DrawRect(ghost, ghostFill);
                GUI.Label(ghost, LabelFor(dragged.GridPosition, dragged), cellStyle);
            }
        }

        static Color FillColor(StudioSheetOps.SheetSlot slot)
        {
            if (slot == null)
                return EmptyCellColor;
            if (slot.HasWarning)
                return WarningColor;
            return slot.Instance != null ? OnDeskColor : OffDeskColor;
        }

        static Color DropOutline(StudioSheetOps.SheetSlot target)
        {
            if (target == null)
                return MoveTargetOutline;
            return target.Movable ? SwapTargetOutline : RefusedTargetOutline;
        }

        static string LabelFor(Vector2Int cell, StudioSheetOps.SheetSlot slot)
        {
            var head = $"({cell.x},{cell.y})";
            if (slot == null)
                return head;
            if (slot.Duplicate)
                return $"{head}\n⚠ duplicate ×{slot.InstanceCount}";
            if (slot.Mismatch)
                return $"{head}\n⚠ mismatch";
            if (slot.AssetPath == null)
                return $"{head}\n⚠ no variant";
            return slot.Instance == null ? $"{head}\noff Desk" : head;
        }

        static void DrawOutline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, OutlineThickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - OutlineThickness, rect.width, OutlineThickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, OutlineThickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - OutlineThickness, rect.y, OutlineThickness, rect.height), color);
        }
    }
}
