using UnityEngine;

namespace Papercut.EditorTools
{
    /// <summary>
    /// Pure mapping between a Sheet Studio pane's GUI pixels and sheet-local space. The single place
    /// pixel-to-sheet math lives; pane rendering and pane input both go through it so they cannot disagree.
    /// </summary>
    /// <remarks>
    /// GUI space is y-down with the origin at the window's top-left; sheet-local space is y-up with the
    /// origin at the sheet's centre. <see cref="MirrorX"/> implements the Back pane's display-only mirror
    /// (Aaron, 2026-08-31): authored data is untouched, only the view flips.
    /// </remarks>
    public readonly struct PaneView
    {
        /// <summary>GUI-space rect the pane occupies.</summary>
        public readonly Rect PaneRect;

        /// <summary>Sheet-local point shown at the pane's centre.</summary>
        public readonly Vector2 Centre;

        /// <summary>Pixels per world unit. Always positive.</summary>
        public readonly float Zoom;

        /// <summary>Display-only horizontal mirror (the Back pane's align-with-Front toggle).</summary>
        public readonly bool MirrorX;

        public PaneView(Rect paneRect, Vector2 centre, float zoom, bool mirrorX)
        {
            PaneRect = paneRect;
            Centre = centre;
            Zoom = Mathf.Max(zoom, 0.0001f);
            MirrorX = mirrorX;
        }

        /// <summary>World units visible across the pane, per axis.</summary>
        public Vector2 VisibleSize => new(PaneRect.width / Zoom, PaneRect.height / Zoom);

        public Vector2 SheetLocalToPane(Vector2 sheetLocal)
        {
            var d = sheetLocal - Centre;
            if (MirrorX)
                d.x = -d.x;
            return new Vector2(PaneRect.center.x + d.x * Zoom, PaneRect.center.y - d.y * Zoom);
        }

        public Vector2 PaneToSheetLocal(Vector2 guiPoint)
        {
            var dx = (guiPoint.x - PaneRect.center.x) / Zoom;
            var dy = (PaneRect.center.y - guiPoint.y) / Zoom;
            if (MirrorX)
                dx = -dx;
            return Centre + new Vector2(dx, dy);
        }

        /// <summary>A view of the same spot through a different pane rect (e.g. after a window resize).</summary>
        public PaneView WithRect(Rect paneRect) => new(paneRect, Centre, Zoom, MirrorX);

        public PaneView WithMirror(bool mirrorX) => new(PaneRect, Centre, Zoom, mirrorX);

        /// <summary>
        /// Zooms about a GUI-space pivot: the sheet point under the pivot stays under it.
        /// </summary>
        public PaneView ZoomedAbout(float newZoom, Vector2 guiPivot)
        {
            var pinned = PaneToSheetLocal(guiPivot);
            var zoomed = new PaneView(PaneRect, Centre, newZoom, MirrorX);
            var drift = pinned - zoomed.PaneToSheetLocal(guiPivot);
            return new PaneView(PaneRect, zoomed.Centre + drift, zoomed.Zoom, MirrorX);
        }

        /// <summary>
        /// Pans by a GUI-space pixel delta: content follows the cursor.
        /// </summary>
        public PaneView PannedBy(Vector2 guiDelta)
        {
            var dx = guiDelta.x / Zoom * (MirrorX ? 1f : -1f);
            var dy = guiDelta.y / Zoom;
            return new PaneView(PaneRect, Centre + new Vector2(dx, dy), Zoom, MirrorX);
        }

        /// <summary>
        /// A view framing the whole sheet in <paramref name="paneRect"/> with a margin around it.
        /// </summary>
        public static PaneView FitSheet(Rect paneRect, bool mirrorX, float marginUnits = 0.75f)
        {
            var zoom = Mathf.Min(
                paneRect.width / (SheetGeometry.Width + 2f * marginUnits),
                paneRect.height / (SheetGeometry.Height + 2f * marginUnits));
            return new PaneView(paneRect, Vector2.zero, zoom, mirrorX);
        }
    }
}
