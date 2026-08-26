using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The world space holding the grid of Sheets. Owns the grid-to-world layout and sheet lookup.
    /// Sheets are discovered from the children of this object and laid out under <see cref="SheetGrid"/>.
    /// </summary>
    /// <remarks>
    /// The Desk itself never moves; its surface and (later) UI live directly under it. Changing Screen
    /// slides <see cref="SheetGrid"/> (see <see cref="DeskSlider"/>), so every Sheet must be a direct child
    /// of that grid. Grid dimensions are open (Bible §7, decision #14); the Desk accepts any set of grid positions.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class Desk : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("Desk surface visible between neighbouring sheets, in world units.")]
        float sheetGap = 0.5f;

        [SerializeField, Tooltip("Root the sheets are laid out under. Sliding between Screens moves this, not the Desk.")]
        Transform sheetGrid;

        Dictionary<Vector2Int, Sheet> sheetsByGridPosition;

        /// <summary>Distance between the centres of neighbouring sheets, per axis.</summary>
        public Vector2 Pitch => SheetGeometry.Size + Vector2.one * sheetGap;

        /// <summary>Desk surface visible between neighbouring sheets, in world units.</summary>
        public float SheetGap => sheetGap;

        /// <summary>The transform every Sheet is a direct child of; the thing that slides between Screens.</summary>
        public Transform SheetGrid => sheetGrid;

        public IReadOnlyCollection<Sheet> Sheets
        {
            get
            {
                EnsureRegistry();
                return sheetsByGridPosition.Values;
            }
        }

        /// <summary>Position of a grid cell's centre in <see cref="SheetGrid"/>-local space.</summary>
        public Vector3 GridToLocal(Vector2Int gridPosition)
        {
            var pitch = Pitch;
            return new Vector3(gridPosition.x * pitch.x, gridPosition.y * pitch.y, 0f);
        }

        /// <summary>
        /// True if <paramref name="sheet"/> is a direct child of <see cref="SheetGrid"/>; otherwise logs an error
        /// and returns false. A sheet anywhere else would not slide with the grid.
        /// </summary>
        public bool ValidateSheetParent(Sheet sheet)
        {
            if (sheetGrid != null && sheet.transform.parent == sheetGrid)
                return true;
            var gridName = sheetGrid == null ? "unassigned" : sheetGrid.name;
            Debug.LogError($"Sheet '{sheet.name}' must be a direct child of the Desk's SheetGrid ('{gridName}').", sheet);
            return false;
        }

        public bool TryGetSheet(Vector2Int gridPosition, out Sheet sheet)
        {
            EnsureRegistry();
            return sheetsByGridPosition.TryGetValue(gridPosition, out sheet);
        }

        public bool TryGetNeighbour(Sheet from, GridDirection direction, out Sheet neighbour)
            => TryGetSheet(from.GridPosition + direction.ToOffset(), out neighbour);

        /// <summary>Finds the sheet whose Base contains <paramref name="worldPoint"/>.</summary>
        public bool TryGetSheetAt(Vector2 worldPoint, out Sheet sheet)
        {
            EnsureRegistry();
            foreach (var candidate in sheetsByGridPosition.Values)
            {
                if (candidate.Contains(worldPoint))
                {
                    sheet = candidate;
                    return true;
                }
            }
            sheet = null;
            return false;
        }

        /// <summary>Places every sheet under <see cref="SheetGrid"/> at the position its grid position implies.</summary>
        public void LayoutSheets()
        {
            foreach (var sheet in GetComponentsInChildren<Sheet>(true))
            {
                if (ValidateSheetParent(sheet))
                    sheet.transform.localPosition = GridToLocal(sheet.GridPosition);
            }
        }

        void Awake()
        {
            ValidateSheetGrid();
            LayoutSheets();
            EnsureRegistry();
        }

        void OnValidate()
        {
            ValidateSheetGrid();
            if (!Application.isPlaying)
                LayoutSheets();
        }

        void ValidateSheetGrid()
        {
            if (sheetGrid == null)
                Debug.LogError("Desk has no SheetGrid assigned.", this);
            else if (!sheetGrid.IsChildOf(transform) || sheetGrid == transform)
                Debug.LogError($"Desk: SheetGrid '{sheetGrid.name}' must be a descendant of the Desk.", this);
        }

        void EnsureRegistry()
        {
            if (sheetsByGridPosition != null)
                return;

            sheetsByGridPosition = new Dictionary<Vector2Int, Sheet>();
            foreach (var sheet in GetComponentsInChildren<Sheet>(true))
            {
                if (sheetsByGridPosition.TryGetValue(sheet.GridPosition, out var existing))
                {
                    Debug.LogError(
                        $"Sheets '{existing.name}' and '{sheet.name}' both claim grid position {sheet.GridPosition}. Ignoring '{sheet.name}'.",
                        sheet);
                    continue;
                }
                sheetsByGridPosition.Add(sheet.GridPosition, sheet);
            }
        }
    }
}
