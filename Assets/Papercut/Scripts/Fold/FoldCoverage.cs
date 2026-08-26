using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// How much of an object a fold affects. For Front content "Covered" means hidden under the Flap (or
    /// lifted with it); for Back content it means exposed on the landed Flap.
    /// </summary>
    public enum FoldCoverage
    {
        Uncovered,
        Partial,
        Covered,
    }

    /// <summary>
    /// What <see cref="SheetOcclusion"/> tells an <see cref="IFoldOccludee"/>: the coverage, and the parts of
    /// its footprint that still exist in the world (face-local space) — the whole footprint, nothing, or the
    /// clipped remainder(s). An occludee acts on <see cref="VisibleParts"/>; <see cref="Coverage"/> is context.
    /// </summary>
    public readonly struct CoverageResult
    {
        static readonly ConvexPolygon[] NoParts = System.Array.Empty<ConvexPolygon>();

        public FoldCoverage Coverage { get; }

        /// <summary>Face-local convex pieces of the footprint that are present in the world. Empty when none is.</summary>
        public IReadOnlyList<ConvexPolygon> VisibleParts { get; }

        /// <summary>True when the entire footprint is present (the collider needs no clipping).</summary>
        public bool IsWhole { get; }

        CoverageResult(FoldCoverage coverage, IReadOnlyList<ConvexPolygon> visibleParts, bool isWhole)
        {
            Coverage = coverage;
            VisibleParts = visibleParts;
            IsWhole = isWhole;
        }

        public bool IsNone => VisibleParts.Count == 0;

        /// <summary>The whole footprint is present.</summary>
        public static CoverageResult Whole(FoldCoverage coverage, Rect footprint)
            => new(coverage, new[] { ConvexPolygon.FromRect(footprint) }, true);

        /// <summary>Nothing of the footprint is present.</summary>
        public static CoverageResult None(FoldCoverage coverage) => new(coverage, NoParts, false);

        /// <summary>Only <paramref name="parts"/> of the footprint are present.</summary>
        public static CoverageResult Clipped(IReadOnlyList<ConvexPolygon> parts) => new(FoldCoverage.Partial, parts, false);
    }
}
