using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// How much of an object folding affects. For Front content "Covered" means hidden (nothing of it is on a
    /// visible Front-up piece of the sheet); for Back content it means fully exposed on Back-up pieces.
    /// </summary>
    public enum FoldCoverage
    {
        Uncovered,
        Partial,
        Covered,
    }

    /// <summary>
    /// What <see cref="SheetOcclusion"/> tells an <see cref="IFoldOccludee"/>: the coverage, and the parts of
    /// its footprint that exist in the world — the whole footprint where it was authored, nothing, or the
    /// pieces that lie on visible layers of the sheet, each placed where that layer lies now. Parts are in
    /// sheet-local space. An occludee acts on <see cref="VisibleParts"/>; <see cref="Coverage"/> is context.
    /// </summary>
    public readonly struct CoverageResult
    {
        static readonly ConvexPolygon[] NoParts = System.Array.Empty<ConvexPolygon>();

        public FoldCoverage Coverage { get; }

        /// <summary>Sheet-local convex pieces of the footprint that are present in the world, where they are. Empty when none is.</summary>
        public IReadOnlyList<ConvexPolygon> VisibleParts { get; }

        /// <summary>True when the entire footprint is present where it was authored (the collider needs no clipping).</summary>
        public bool IsWhole { get; }

        CoverageResult(FoldCoverage coverage, IReadOnlyList<ConvexPolygon> visibleParts, bool isWhole)
        {
            Coverage = coverage;
            VisibleParts = visibleParts;
            IsWhole = isWhole;
        }

        public bool IsNone => VisibleParts.Count == 0;

        /// <summary>
        /// The whole footprint is present, unmoved (Front content on the Base). An empty footprint has nothing
        /// to present and is <see cref="None"/>.
        /// </summary>
        public static CoverageResult Whole(FoldCoverage coverage, FaceFootprint footprint)
            => footprint.IsEmpty ? None(coverage) : new(coverage, footprint.Pieces, true);

        /// <summary>Nothing of the footprint is present.</summary>
        public static CoverageResult None(FoldCoverage coverage) => new(coverage, NoParts, false);

        /// <summary>Only <paramref name="parts"/> of the footprint are present, where they are.</summary>
        public static CoverageResult Clipped(IReadOnlyList<ConvexPolygon> parts, FoldCoverage coverage = FoldCoverage.Partial)
            => new(coverage, parts, false);
    }
}
