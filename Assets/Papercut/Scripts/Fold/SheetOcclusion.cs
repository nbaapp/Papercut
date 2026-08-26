using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Applies a Sheet's committed folds to its content: poses the Back root so exposed Back content lands
    /// where the Flap does, and tells every <see cref="IFoldOccludee"/> under Front and Back what is left of it.
    /// Notifies; never disables anything itself (Bible §9).
    /// </summary>
    /// <remarks>
    /// Only the Screen has live physics: a walkable Flap can overhang a neighbour, whose walls and terrain
    /// would otherwise stop the player, and the player is never on any sheet but the Screen. So when this sheet
    /// is not the Screen every occludee is told nothing of it is present. The Back root is always active; Back
    /// content exists physically only through its occludees' responses.
    /// The single-fold piece: the whole Back root is posed by one fold. With several folds each Flap needs its own
    /// Back copy; that would live here, in <see cref="PoseBack"/>.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Sheet), typeof(SheetFolds))]
    public sealed class SheetOcclusion : MonoBehaviour
    {
        /// <summary>
        /// Local z of a posed Back root. A posed root's camera looks at the mirror image of the sheet, which
        /// can lie over a neighbour; moving the whole posed root (and its camera) this far along z keeps every
        /// other sheet's Back content out of its camera's depth slab and it out of theirs. Physics2D, footprints
        /// and the main camera (which culls the face layers) ignore z.
        /// </summary>
        const float PosedBackZ = 100f;

        Sheet sheet;
        SheetFolds folds;
        readonly List<IFoldOccludee> occludees = new();

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

        /// <summary>Re-evaluates the Back pose and every occludee from the current folds and Screen state.</summary>
        public void Apply()
        {
            var fold = folds.Folds.Count > 0 ? folds.Folds[0] : (Fold?)null;
            PoseBack(fold);
            Notify(sheet.Front, SheetFace.Front, fold);
            Notify(sheet.Back, SheetFace.Back, fold);
        }

        /// <summary>
        /// World-space rects of this sheet's Front terrain the player cannot pass, for the room test of a
        /// transition onto it. The sheet is flat when it is not the Screen, so face-local and world differ only
        /// by the sheet's position.
        /// </summary>
        public IEnumerable<Rect> SolidFootprints(PlayerAbilities player)
        {
            if (sheet.Front == null)
                yield break;
            var centre = sheet.Centre;
            foreach (var region in sheet.Front.GetComponentsInChildren<TerrainRegion>(true))
            {
                if (region.IsPassableBy(player))
                    continue;
                var local = region.FaceLocalFootprint(sheet.Front);
                yield return new Rect(local.position + centre, local.size);
            }
        }

        void PoseBack(Fold? fold)
        {
            var back = sheet.Back;
            if (back == null)
                return;
            if (fold.HasValue)
            {
                var (position, rotation) = FoldGeometry.BackPose(fold.Value);
                back.localPosition = new Vector3(position.x, position.y, PosedBackZ);
                back.localRotation = Quaternion.Euler(0f, 0f, rotation);
            }
            else
            {
                back.localPosition = Vector3.zero;
                back.localRotation = Quaternion.identity;
            }
        }

        void Notify(Transform faceRoot, SheetFace face, Fold? fold)
        {
            if (faceRoot == null)
                return;

            occludees.Clear();
            faceRoot.GetComponentsInChildren(true, occludees);
            var live = sheet.IsScreen;
            foreach (var occludee in occludees)
            {
                var footprint = occludee.FaceLocalFootprint(faceRoot);
                CoverageResult result;
                if (!live)
                    result = CoverageResult.None(face == SheetFace.Front ? FoldCoverage.Covered : FoldCoverage.Uncovered);
                else if (!fold.HasValue)
                    result = face == SheetFace.Front
                        ? CoverageResult.Whole(FoldCoverage.Uncovered, footprint)
                        : CoverageResult.None(FoldCoverage.Uncovered);
                else
                    result = FoldGeometry.Coverage(footprint, fold.Value, face);
                occludee.OnFoldCoverageChanged(result, faceRoot);
            }
        }

        void ReportUnseenColliders()
        {
            foreach (var root in new[] { sheet.Front, sheet.Back })
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
