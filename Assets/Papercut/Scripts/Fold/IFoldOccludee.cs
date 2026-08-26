using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Content on a Sheet's face that folding can cover (Front) or expose (Back). Occlusion tells an occludee
    /// what is left of it and lets it respond; it never disables objects itself (Bible §9).
    /// </summary>
    /// <remarks>
    /// Implemented by components under a Sheet's Front or Back root. An object under a face root that has a
    /// <see cref="Collider2D"/> but no occludee in its parents is an authoring error, reported by
    /// <see cref="SheetOcclusion"/>: the fold system could not see it, so it would keep colliding under a Flap.
    /// </remarks>
    public interface IFoldOccludee
    {
        /// <summary>
        /// The object's footprint in face-local space (Front-space under Front, Back-space under Back) —
        /// see <see cref="FoldFootprint.FaceLocalRect"/>. Exact under any pose of the face root.
        /// </summary>
        Rect FaceLocalFootprint(Transform faceRoot);

        /// <summary>
        /// Called by <see cref="SheetOcclusion"/> whenever the committed folds change, the sheet becomes or stops
        /// being the Screen, and once at start. <paramref name="faceRoot"/> is the root the coverage is expressed in.
        /// </summary>
        void OnFoldCoverageChanged(in CoverageResult coverage, Transform faceRoot);
    }
}
