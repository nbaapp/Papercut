using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Where a piece of authored face content sits: the <see cref="Sheet"/> above it, which face root it is
    /// under, and its rect on the flat sheet. The one answer to "which face am I on" for elements authored
    /// under a sheet's Front or Back root with a single box footprint (a plate, a pickup).
    /// </summary>
    public static class FaceContent
    {
        /// <summary>
        /// The Sheet above <paramref name="content"/> and the face root it is under. False, with
        /// <paramref name="error"/> naming the authoring mistake, if it is not under a Sheet or not under one of
        /// the Sheet's face roots.
        /// </summary>
        public static bool TryResolveFace(Component content, out Sheet sheet, out Transform faceRoot, out SheetFace side, out string error)
        {
            faceRoot = null;
            side = SheetFace.Front;
            error = null;
            sheet = content.GetComponentInParent<Sheet>();
            if (sheet == null)
            {
                error = "is not under a Sheet";
                return false;
            }
            if (sheet.Front != null && content.transform.IsChildOf(sheet.Front))
            {
                faceRoot = sheet.Front;
                side = SheetFace.Front;
                return true;
            }
            if (sheet.Back != null && content.transform.IsChildOf(sheet.Back))
            {
                faceRoot = sheet.Back;
                side = SheetFace.Back;
                return true;
            }
            error = "must be under the sheet's Front or Back root";
            return false;
        }

        /// <summary>
        /// A box's rect on the flat sheet, in Front-space: its face-local rect, mapped through
        /// <see cref="SheetGeometry.BackToFront"/> when it is Back content (Bible §6).
        /// </summary>
        public static Rect FlatRect(BoxCollider2D box, Transform faceRoot, SheetFace side)
        {
            var faceRect = FoldFootprint.FaceLocalRect(box, faceRoot);
            return side == SheetFace.Front ? faceRect : SheetGeometry.BackToFront(faceRect);
        }
    }
}
