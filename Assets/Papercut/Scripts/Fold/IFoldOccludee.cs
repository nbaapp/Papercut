using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Content on a Sheet's face that folding can cover (Front) or expose (Back). Occlusion tells an occludee
    /// what is left of it and where; it never disables objects itself (Bible §9).
    /// </summary>
    /// <remarks>
    /// Implemented by components under a Sheet's Front or Back root. An object under a face root that has a
    /// <see cref="Collider2D"/> but no occludee in its parents is an authoring error, reported by
    /// <see cref="SheetOcclusion"/>: the fold system could not see it, so it would keep colliding under a Flap.
    /// The face roots are never moved: a piece of Back content that a fold exposes is "somewhere" only through
    /// the parts its occludee is given. Anything that must itself move with a Flap would do so from here.
    /// </remarks>
    public interface IFoldOccludee
    {
        /// <summary>
        /// The object's footprint in face-local space (Front-space under Front, Back-space under Back) —
        /// see <see cref="FoldFootprint"/>. Empty when the object occupies nothing (e.g. an invalid region).
        /// </summary>
        FaceFootprint FaceLocalFootprint(Transform faceRoot);

        /// <summary>
        /// Called by <see cref="SheetOcclusion"/> whenever the committed folds change, the sheet becomes or stops
        /// being the Screen, and once at start. The coverage's parts are relative to <paramref name="space"/>
        /// (the Sheet's transform): where each visible piece physically lies.
        /// </summary>
        void OnFoldCoverageChanged(in CoverageResult coverage, Transform space);
    }
}
