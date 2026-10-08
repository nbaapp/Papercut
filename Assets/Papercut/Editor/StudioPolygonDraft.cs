using System.Collections.Generic;
using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// The outline being drawn for a polygon terrain region: which face it is on, which armed prefab it is
    /// for, and its points so far. Shared by both face panes through the window (like link mode), so a draft
    /// on one face is never silently lost by a click on the other. Pure until <see cref="TryFinish"/>.
    /// </summary>
    public sealed class StudioPolygonDraft
    {
        readonly List<Vector2> points = new();

        /// <summary>The face the draft is being drawn on. Meaningless while not <see cref="IsActive"/>.</summary>
        public SheetFace Face { get; private set; }

        /// <summary>The polygon terrain prefab the outline is for (the armed palette prefab when it started).</summary>
        public GameObject Prefab { get; private set; }

        public IReadOnlyList<Vector2> Points => points;

        public bool IsActive => points.Count > 0;

        /// <summary>Begins a new outline on <paramref name="face"/> for <paramref name="prefab"/>, discarding any previous one.</summary>
        public void Start(GameObject prefab, SheetFace face)
        {
            points.Clear();
            Prefab = prefab;
            Face = face;
        }

        /// <summary>Adds a vertex; refuses one on top of an existing vertex (a near-miss on the first vertex with too few points to close is not a phantom point).</summary>
        public bool TryAdd(Vector2 point, out string reason)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (Vector2.Distance(points[i], point) <= PolygonDecomposition.DuplicateEpsilon)
                {
                    reason = i == points.Count - 1 ? "that point is already the last vertex" : "that point is already a vertex";
                    return false;
                }
            }
            points.Add(point);
            reason = null;
            return true;
        }

        /// <summary>Keeps the outline but changes the prefab it will place (arming the other polygon kind mid-draw).</summary>
        public void Retarget(GameObject prefab) => Prefab = prefab;

        /// <summary>Removes the last vertex; false if there was none.</summary>
        public bool RemoveLast()
        {
            if (points.Count == 0)
                return false;
            points.RemoveAt(points.Count - 1);
            return true;
        }

        public void Clear()
        {
            points.Clear();
            Prefab = null;
        }

        /// <summary>True when the outline closed as it stands is a simple polygon with at least three vertices.</summary>
        public bool CanFinish(out string reason)
        {
            if (points.Count < 3)
            {
                reason = "a region needs at least three vertices";
                return false;
            }
            return PolygonDecomposition.Validate(points, out reason);
        }

        /// <summary>
        /// Places the region for the outline under the sheet's face root and clears the draft. False, with
        /// the reason, if the outline cannot be finished or the sheet has no such face root; the draft is kept
        /// so the outline can be corrected.
        /// </summary>
        public bool TryFinish(Sheet sheet, out GameObject placed, out string reason)
        {
            placed = null;
            if (!CanFinish(out reason))
                return false;
            var faceRoot = sheet == null ? null : Face == SheetFace.Front ? sheet.Front : sheet.Back;
            if (faceRoot == null)
            {
                reason = "the sheet has no root for that face";
                return false;
            }
            // A universal polygon goes under the Above root, from the Front pane only (Above content is edited there).
            var root = StudioPlacement.TargetRoot(Prefab, sheet, faceRoot, aboveEditable: Face == SheetFace.Front, out var refusal);
            if (root == null)
            {
                reason = refusal.TrimEnd('.');
                return false;
            }
            placed = StudioPlacement.PlacePolygon(Prefab, root, Face, points, 0f); // Points are already snapped.
            if (placed == null)
            {
                reason = "the region could not be placed (see the console)";
                return false;
            }
            Clear();
            return true;
        }
    }
}
