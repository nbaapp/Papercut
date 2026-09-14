using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// A free-placed, static region of a Sheet's face that the player either can never cross (a wall) or can
    /// cross only while holding a required <see cref="Ability"/> (e.g. water with Swim). Ground is the absence
    /// of a region. Wall and Water are prefabs of this one component differing only in data; their (Polygon)
    /// variants differ only in the collider's shape.
    /// </summary>
    /// <remarks>
    /// Terrain representation is Bible decision #6: placed objects over hand-drawn art, with a box collider or
    /// (since 2026-09-10) a <see cref="PolygonCollider2D"/> holding one simple outline of any shape. Blocking is
    /// plain Physics2D between the region's solid collider and the player's body; gating tells Physics2D to
    /// ignore that pair while the player holds the ability. Must sit under a <see cref="Sheet"/>'s Front or
    /// Back root. Folding reaches it through <see cref="IFoldOccludee"/>: a covered Front region is gone for
    /// collision (Aaron, 2026-08-26), a partially covered one is clipped to its visible part, a Back region
    /// exists only where a landed Flap exposes it, and a region folded twice comes back Front-up wherever it
    /// landed. The clipped shape is an <see cref="OccludedCollider"/>. A region whose collider is not one
    /// simple outline reports itself at Awake and stays inert (no live collider) until the asset is fixed.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TerrainRegion : MonoBehaviour, IFoldOccludee, IArrivalObstacle
    {
        [SerializeField, Tooltip("Ability the player must hold to cross this region. None means it is a wall and " +
            "can never be crossed. If several flags are set, the player needs all of them.")]
        Ability requiredAbility = Ability.None;

        static readonly Color WallFill = new(0.3f, 0.3f, 0.3f, 0.4f);
        static readonly Color WallOutline = new(0.15f, 0.15f, 0.15f, 1f);
        static readonly Color GatedFill = new(0.3f, 0.55f, 0.9f, 0.4f);
        static readonly Color GatedOutline = new(0.15f, 0.35f, 0.8f, 1f);

        Collider2D authored;
        OccludedCollider occluded;
        Sheet sheet;
        PlayerAbilities player;
        Collider2D playerCollider;
        bool valid;

        public bool IsPassableBy(PlayerAbilities abilities)
            => abilities != null && TerrainRules.IsPassable(abilities.Abilities, requiredAbility);

        /// <summary>True once Awake found one usable authored collider; false leaves the region inert.</summary>
        public bool IsValid => valid;

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
        /// Exactly one of BoxCollider2D / PolygonCollider2D on this object, and a polygon's outline must be one
        /// simple polygon. Anything else is reported and the collider is left disabled (an invalid region must
        /// not block by accident).
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
            var faceRoot = FaceRoot();
            if (faceRoot == null)
                return Refuse(box, polygon, "must be under the sheet's Front or Back root (folding could not see it)");
            if (polygon != null)
            {
                FoldFootprint.Of(polygon, faceRoot, out var error);
                if (error != null)
                    return Refuse(box, polygon, error);
            }
            return true;
        }

        bool Refuse(Collider2D box, Collider2D polygon, string reason)
        {
            Debug.LogError($"TerrainRegion '{name}' {reason}; the region is inert until the asset is fixed.", this);
            if (box != null) box.enabled = false;
            if (polygon != null) polygon.enabled = false;
            authored = null;
            return false;
        }

        Transform FaceRoot()
        {
            if (sheet == null)
                return null;
            if (sheet.Front != null && transform.IsChildOf(sheet.Front)) return sheet.Front;
            if (sheet.Back != null && transform.IsChildOf(sheet.Back)) return sheet.Back;
            return null;
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
            sheetLocal = valid && sheet != null && sheet.Front != null ? FaceLocalFootprint(sheet.Front) : FaceFootprint.Empty;
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
            var wall = requiredAbility == Ability.None;
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
                    Gizmos.color = wall ? WallFill : GatedFill;
                    Gizmos.DrawCube(gizmoBox.offset, gizmoBox.size);
                    Gizmos.color = wall ? WallOutline : GatedOutline;
                    Gizmos.DrawWireCube(gizmoBox.offset, gizmoBox.size);
                    break;
                case PolygonCollider2D gizmoPolygon when gizmoPolygon.pathCount > 0:
                {
                    Gizmos.color = wall ? WallOutline : GatedOutline;
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
