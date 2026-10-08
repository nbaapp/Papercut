using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// A free-placed, static region of a Sheet that is solid to the player (unless they hold a required
    /// <see cref="Ability"/>, e.g. water with Swim) and/or to pushable blocks (<see cref="TerrainBlocks"/>).
    /// Ground is the absence of a region. Wall, Water, Filter Wall and their Universal kinds are prefabs of
    /// this one component differing only in data; their (Polygon) variants differ only in the collider's shape.
    /// </summary>
    /// <remarks>
    /// Terrain representation is Bible decision #6: placed objects over hand-drawn art, with a box collider or
    /// (since 2026-09-10) a <see cref="PolygonCollider2D"/> holding one simple outline of any shape. Blocking the
    /// player is plain Physics2D between the region's solid collider and the player's body; passability tells
    /// Physics2D to ignore that pair. Blocks judge the region by a cast (<see cref="PushableBlock"/>) and skip it
    /// when it does not <see cref="StopsBlocks"/>. A region is face content under a <see cref="Sheet"/>'s Front
    /// or Back root, or - <see cref="IsUniversal"/> (Aaron, 2026-09-28) - content <em>above</em> the sheet under
    /// its Above root: folds slide under it, it is never lifted, mirrored, covered or carried, only clipped to the
    /// sheet's footprint. Folding reaches both through <see cref="IFoldOccludee"/>: a covered Front region is gone
    /// for collision (Aaron, 2026-08-26), a partially covered one is clipped to its visible part, a Back region
    /// exists only where a landed Flap exposes it, a region folded twice comes back Front-up wherever it landed,
    /// and a universal region is clipped to the union of the sheet's layers (<see cref="SheetLayers.CoverageAbove"/>).
    /// The clipped shape is an <see cref="OccludedCollider"/>. A region whose collider is not one simple outline,
    /// or whose root disagrees with its Universal flag, reports itself at Awake and stays inert (no live collider)
    /// until the asset is fixed.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TerrainRegion : MonoBehaviour, IFoldOccludee, IArrivalObstacle
    {
        [Header("Blocking")]
        [SerializeField, Tooltip("Who this region is solid to. Player: the player, unless they hold Required Ability. Blocks: every " +
            "pushable block and paperweight. Both is an ordinary wall; Blocks only is a filter wall the player walks through.")]
        TerrainBlocks blocks = TerrainBlocks.Player | TerrainBlocks.Blocks;

        [SerializeField, Tooltip("Ability the player must hold to cross this region. None means it is a wall and " +
            "can never be crossed. If several flags are set, the player needs all of them. Only meaningful while Blocks includes Player.")]
        Ability requiredAbility = Ability.None;

        [Header("Placement")]
        [SerializeField, Tooltip("Sits above the sheet (under the Sheet's Above root) instead of on a face: folds slide under it; it is " +
            "never lifted, mirrored, covered or carried, only clipped to the sheet's footprint. A universal region must be a child of " +
            "the Above root; the Sheet Studio places it there.")]
        bool universal = false;

        static readonly Color WallFill = new(0.3f, 0.3f, 0.3f, 0.4f);
        static readonly Color WallOutline = new(0.15f, 0.15f, 0.15f, 1f);
        static readonly Color GatedFill = new(0.3f, 0.55f, 0.9f, 0.4f);
        static readonly Color GatedOutline = new(0.15f, 0.35f, 0.8f, 1f);
        static readonly Color FilterFill = new(0.6f, 0.35f, 0.85f, 0.4f);
        static readonly Color FilterOutline = new(0.45f, 0.2f, 0.7f, 1f);
        static readonly Color UniversalFill = new(0.1f, 0.75f, 0.7f, 0.4f);
        static readonly Color UniversalOutline = new(0.05f, 0.5f, 0.45f, 1f);

        Collider2D authored;
        OccludedCollider occluded;
        Sheet sheet;
        Transform root;
        PlayerAbilities player;
        Collider2D playerCollider;
        bool valid;

        /// <summary>
        /// True if <paramref name="abilities"/> may cross this region. Null (no Player on the Desk; the Studio's
        /// "no abilities") passes a region that is not solid to the player at all and fails the ability gate.
        /// </summary>
        public bool IsPassableBy(PlayerAbilities abilities)
            => TerrainRules.IsPassable(blocks, abilities != null ? abilities.Abilities : Ability.None, requiredAbility);

        /// <summary>True if pushable blocks stop at this region.</summary>
        public bool StopsBlocks => TerrainRules.StopsBlocks(blocks);

        /// <summary>True if this region sits above the sheet (folds slide under it); see the class remarks.</summary>
        public bool IsUniversal => universal;

        /// <summary>True once Awake found one usable authored collider under the right root; false leaves the region inert.</summary>
        public bool IsValid => valid;

        /// <summary>
        /// The region's one authored collider: the one Awake resolved, else (edit mode, or before Awake) by the same
        /// rule - a box, else a polygon; both or neither is null. Never the runtime clip polygon that
        /// <see cref="OccludedCollider"/> adds beside a box once a fold clips it, which a fresh GetComponent lookup
        /// would mistake for a second authored collider.
        /// </summary>
        public Collider2D AuthoredCollider
        {
            get
            {
                if (authored != null)
                    return authored;
                var box = GetComponent<BoxCollider2D>();
                var polygon = GetComponent<PolygonCollider2D>();
                if (box != null && polygon != null)
                    return null;
                return box != null ? box : polygon;
            }
        }

        void Awake()
        {
            sheet = GetComponentInParent<Sheet>();
            valid = TryResolveAuthoredCollider();
            if (!valid)
            {
                enabled = false;
                return;
            }

            if (authored.isTrigger)
            {
                Debug.LogError($"TerrainRegion '{name}' collider must not be a trigger. Fixing at runtime; please fix the asset.", this);
                authored.isTrigger = false;
            }
            occluded = new OccludedCollider(authored);
        }

        /// <summary>
        /// Exactly one of BoxCollider2D / PolygonCollider2D on this object, a polygon's outline must be one
        /// simple polygon, and the root must match the Universal flag. Anything else is reported and the
        /// collider is left disabled (an invalid region must not block by accident, nor be fold-proof by accident).
        /// </summary>
        bool TryResolveAuthoredCollider()
        {
            var box = GetComponent<BoxCollider2D>();
            var polygon = GetComponent<PolygonCollider2D>();
            if (box != null && polygon != null)
                return Refuse(box, polygon, "has both a BoxCollider2D and a PolygonCollider2D; it needs exactly one");
            if (box == null && polygon == null)
                return Refuse(null, null, "has no BoxCollider2D or PolygonCollider2D");

            authored = box != null ? box : polygon;
            if (sheet == null)
                return Refuse(box, polygon, "is not under a Sheet"); // Folding could never see it, and its outline cannot be checked.
            root = RootOf(sheet, transform, out var rootError);
            if (root == null)
                return Refuse(box, polygon, rootError);
            if (polygon != null)
            {
                FoldFootprint.Of(polygon, root, out var error);
                if (error != null)
                    return Refuse(box, polygon, error);
            }
            return true;
        }

        /// <summary>
        /// The root this region is authored under, or null with a reason: a universal region must be under the
        /// sheet's Above root, any other under Front or Back.
        /// </summary>
        Transform RootOf(Sheet owner, Transform t, out string error)
        {
            error = null;
            var underAbove = owner.Above != null && t.IsChildOf(owner.Above);
            var faceRoot = owner.Front != null && t.IsChildOf(owner.Front) ? owner.Front
                : owner.Back != null && t.IsChildOf(owner.Back) ? owner.Back : null;
            if (universal)
            {
                if (underAbove)
                    return owner.Above;
                error = owner.Above == null
                    ? "is marked Universal but the sheet has no Above root; re-create the sheet from the Sheet prefab"
                    : faceRoot != null
                        ? "is marked Universal but is under a face root; it belongs under the sheet's Above root"
                        : "is marked Universal but is not under the sheet's Above root";
                return null;
            }
            if (faceRoot != null)
                return faceRoot;
            error = underAbove
                ? "is under the Above root but is not marked Universal"
                : "must be under the sheet's Front or Back root (folding could not see it)";
            return null;
        }

        bool Refuse(Collider2D box, Collider2D polygon, string reason)
        {
            Debug.LogError($"TerrainRegion '{name}' {reason}; the region is inert until the asset is fixed.", this);
            if (box != null) box.enabled = false;
            if (polygon != null) polygon.enabled = false;
            authored = null;
            root = null;
            return false;
        }

        void OnEnable()
        {
            if (!valid)
                return;
            // Physics2D forgets ignore-pairs when either collider is disabled, so the pair is re-established on every
            // enable and after every coverage change. The player side is covered by PlayerAbilities raising Changed
            // from its own OnEnable.
            player = FindAnyObjectByType<PlayerAbilities>();
            if (player == null)
            {
                Debug.LogError($"TerrainRegion '{name}' found no PlayerAbilities in the scene; it will block regardless of ability.", this);
                return;
            }

            playerCollider = player.GetComponent<Collider2D>();
            if (playerCollider == null)
            {
                Debug.LogError($"TerrainRegion '{name}': the player '{player.name}' has no Collider2D; the region will block regardless of ability.", this);
                player = null;
                return;
            }

            player.Changed += Apply;
            Apply();
        }

        void OnDisable()
        {
            if (player != null)
                player.Changed -= Apply;
            player = null;
            playerCollider = null;
        }

        // ----- IArrivalObstacle -----

        public bool TryGetSolidFootprint(PlayerAbilities player, out FaceFootprint sheetLocal)
        {
            // Asked while the sheet is flat: Front-space and the Above root's sheet space are both sheet-local.
            sheetLocal = valid && root != null && root != sheet.Back ? FaceLocalFootprint(root) : FaceFootprint.Empty;
            return !sheetLocal.IsEmpty && !IsPassableBy(player);
        }

        // ----- IFoldOccludee -----

        public FaceFootprint FaceLocalFootprint(Transform faceRoot)
            => valid ? FoldFootprint.Of(authored, faceRoot, out _) : FaceFootprint.Empty;

        public void OnFoldCoverageChanged(in CoverageResult coverage, Transform space)
        {
            if (!valid)
                return; // Inert by decision, not by accident: nothing of an invalid region ever goes live.
            occluded.Apply(coverage, space);
            Apply();
        }

        void Apply()
        {
            if (!valid || player == null || playerCollider == null)
                return;

            var passable = IsPassableBy(player);
            foreach (var collider in occluded.LiveColliders)
                Physics2D.IgnoreCollision(collider, playerCollider, passable);
        }

        void OnDrawGizmos()
        {
            Color fill, outline;
            if (universal) { fill = UniversalFill; outline = UniversalOutline; }
            else if ((blocks & TerrainBlocks.Player) == 0) { fill = FilterFill; outline = FilterOutline; }
            else if (requiredAbility == Ability.None) { fill = WallFill; outline = WallOutline; }
            else { fill = GatedFill; outline = GatedOutline; }
            Gizmos.matrix = transform.localToWorldMatrix;
            // Edit mode has no cached collider; Play Mode uses the cached one so the runtime clip polygon is never drawn as authored.
            Collider2D shape = authored;
            if (shape == null && !TryGetComponent(out shape))
            {
                Gizmos.matrix = Matrix4x4.identity;
                return;
            }
            switch (shape)
            {
                case BoxCollider2D gizmoBox:
                    Gizmos.color = fill;
                    Gizmos.DrawCube(gizmoBox.offset, gizmoBox.size);
                    Gizmos.color = outline;
                    Gizmos.DrawWireCube(gizmoBox.offset, gizmoBox.size);
                    break;
                case PolygonCollider2D gizmoPolygon when gizmoPolygon.pathCount > 0:
                {
                    Gizmos.color = outline;
                    var path = gizmoPolygon.GetPath(0);
                    for (int i = 0; i < path.Length; i++)
                        Gizmos.DrawLine(path[i] + gizmoPolygon.offset, path[(i + 1) % path.Length] + gizmoPolygon.offset);
                    break;
                }
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
