using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Applies a Sheet's committed folds to its content: tells every <see cref="IFoldOccludee"/> under Front
    /// and Back what is left of it and where, from the stack of layers the folds produce
    /// (<see cref="SheetFolds.Layers"/>), and every universal region under Above what is left of it over the
    /// sheet's footprint (<see cref="SheetLayers.CoverageAbove"/>). Notifies; never disables anything itself (Bible §9).
    /// </summary>
    /// <remarks>
    /// Only the Screen has live physics: the player is never on any sheet but the Screen, so when this sheet
    /// is not the Screen every occludee is told nothing of it is present. The face roots are never moved: Back
    /// content exists physically only through its occludees' responses, placed by the sheet-local parts they
    /// are given (one root could not be posed for two Flaps). The Above root is never moved either: folds slide
    /// under its content, which is clipped in sheet space.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Sheet), typeof(SheetFolds))]
    public sealed class SheetOcclusion : MonoBehaviour
    {
        Sheet sheet;
        SheetFolds folds;
        readonly List<IFoldOccludee> occludees = new();
        /// <summary>Occludees under Above that are not universal regions, reported once (they are skipped on every Apply).</summary>
        readonly HashSet<IFoldOccludee> reportedUnderAbove = new();

        void Awake()
        {
            sheet = GetComponent<Sheet>();
            folds = GetComponent<SheetFolds>();
            ReportUnseenColliders();
        }

        void OnEnable()
        {
            folds.Changed += Apply;
            sheet.PlayerEntered += Apply;
            sheet.PlayerLeft += Apply;
        }

        void OnDisable()
        {
            folds.Changed -= Apply;
            sheet.PlayerEntered -= Apply;
            sheet.PlayerLeft -= Apply;
        }

        void Start()
        {
            // Not Awake: occludees on other objects may not have cached their colliders yet.
            Apply();
        }

        /// <summary>Re-evaluates every occludee from the current layers and Screen state.</summary>
        public void Apply()
        {
            Notify(sheet.Front, SheetFace.Front);
            Notify(sheet.Back, SheetFace.Back);
            NotifyAbove(sheet.Above);
        }

        /// <summary>
        /// World-space convex pieces of this sheet's active Front and Above content the player cannot pass
        /// (<see cref="IArrivalObstacle"/>), for the room test of a transition onto it. The sheet is flat when
        /// it is not the Screen, so sheet-local and world differ only by the sheet's position.
        /// </summary>
        public IEnumerable<ConvexPolygon> SolidFootprints(PlayerAbilities player)
        {
            var centre = sheet.Centre;
            foreach (var root in new[] { sheet.Front, sheet.Above })
            {
                if (root == null)
                    continue;
                foreach (var obstacle in root.GetComponentsInChildren<IArrivalObstacle>(false))
                {
                    if (!obstacle.TryGetSolidFootprint(player, out var local))
                        continue;
                    foreach (var piece in local.Pieces)
                        yield return piece.Translated(centre);
                }
            }
        }

        void Notify(Transform faceRoot, SheetFace face)
        {
            if (faceRoot == null)
                return;

            occludees.Clear();
            faceRoot.GetComponentsInChildren(true, occludees);
            var live = sheet.IsScreen;
            var flat = folds.Folds.Count == 0;
            foreach (var occludee in occludees)
            {
                var footprint = occludee.FaceLocalFootprint(faceRoot);
                CoverageResult result;
                if (!live)
                    result = CoverageResult.None(face == SheetFace.Front ? FoldCoverage.Covered : FoldCoverage.Uncovered);
                else if (flat)
                    result = face == SheetFace.Front
                        ? CoverageResult.Whole(FoldCoverage.Uncovered, footprint)
                        : CoverageResult.None(FoldCoverage.Uncovered);
                else
                    result = folds.Layers.Coverage(footprint, face);
                occludee.OnFoldCoverageChanged(result, sheet.transform);
            }
        }

        /// <summary>
        /// Above content is judged against the sheet's footprint in sheet space. Only a universal
        /// <see cref="TerrainRegion"/> is written for that; anything else parented there is reported and skipped
        /// rather than handed a result it would misread.
        /// </summary>
        void NotifyAbove(Transform aboveRoot)
        {
            if (aboveRoot == null)
                return;

            occludees.Clear();
            aboveRoot.GetComponentsInChildren(true, occludees);
            var live = sheet.IsScreen;
            foreach (var occludee in occludees)
            {
                if (occludee is not TerrainRegion { IsUniversal: true })
                {
                    if (reportedUnderAbove.Add(occludee))
                    {
                        var component = occludee as Component;
                        Debug.LogError($"'{(component != null ? component.name : occludee.GetType().Name)}' on sheet '{sheet.name}' is under the Above root but is not a universal TerrainRegion; only universal regions may sit above the sheet. Skipped.", component);
                    }
                    continue;
                }
                var footprint = occludee.FaceLocalFootprint(aboveRoot);
                var result = live ? folds.Layers.CoverageAbove(footprint) : CoverageResult.None(FoldCoverage.Covered);
                occludee.OnFoldCoverageChanged(result, sheet.transform);
            }
        }

        void ReportUnseenColliders()
        {
            foreach (var root in new[] { sheet.Front, sheet.Back, sheet.Above })
            {
                if (root == null)
                    continue;
                foreach (var collider in root.GetComponentsInChildren<Collider2D>(true))
                {
                    if (collider.GetComponentInParent<IFoldOccludee>() == null)
                        Debug.LogError($"'{collider.name}' on sheet '{sheet.name}' has a Collider2D but no IFoldOccludee; folding cannot see it and it would keep colliding under a Flap.", collider);
                }
            }
        }
    }
}
