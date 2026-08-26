using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// A free-placed, static region of a Sheet's face that the player either can never cross (a wall) or can
    /// cross only while holding a required <see cref="Ability"/> (e.g. water with Swim). Ground is the absence
    /// of a region. Wall and Water are prefabs of this one component differing only in data.
    /// </summary>
    /// <remarks>
    /// Terrain representation is Bible decision #6: placed objects with box colliders over hand-drawn art.
    /// Blocking is plain Physics2D between the region's solid collider and the player's body; gating tells
    /// Physics2D to ignore that pair while the player holds the ability. Must sit under a <see cref="Sheet"/>'s
    /// Front or Back root. Folding reaches it through <see cref="IFoldOccludee"/>: a covered Front region is
    /// gone for collision (Aaron, 2026-08-26), a partially covered one is clipped to its visible part, and a
    /// Back region exists only where the landed Flap exposes it. The clipped shape is an
    /// <see cref="OccludedBoxCollider"/>.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class TerrainRegion : MonoBehaviour, IFoldOccludee
    {
        [SerializeField, Tooltip("Ability the player must hold to cross this region. None means it is a wall and " +
            "can never be crossed. If several flags are set, the player needs all of them.")]
        Ability requiredAbility = Ability.None;

        static readonly Color WallFill = new(0.3f, 0.3f, 0.3f, 0.4f);
        static readonly Color WallOutline = new(0.15f, 0.15f, 0.15f, 1f);
        static readonly Color GatedFill = new(0.3f, 0.55f, 0.9f, 0.4f);
        static readonly Color GatedOutline = new(0.15f, 0.35f, 0.8f, 1f);

        BoxCollider2D box;
        OccludedBoxCollider occluded;
        Sheet sheet;
        PlayerAbilities player;
        Collider2D playerCollider;

        public bool IsPassableBy(PlayerAbilities abilities)
            => abilities != null && TerrainRules.IsPassable(abilities.Abilities, requiredAbility);

        BoxCollider2D Box => box != null ? box : box = GetComponent<BoxCollider2D>();

        OccludedBoxCollider Occluded => occluded ??= new OccludedBoxCollider(Box);

        void Awake()
        {
            sheet = GetComponentInParent<Sheet>();
            if (sheet == null)
                Debug.LogError($"TerrainRegion '{name}' is not under a Sheet.", this);

            if (Box.isTrigger)
            {
                Debug.LogError($"TerrainRegion '{name}' collider must not be a trigger. Fixing at runtime; please fix the asset.", this);
                Box.isTrigger = false;
            }
        }

        void OnEnable()
        {
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

        // ----- IFoldOccludee -----

        public Rect FaceLocalFootprint(Transform faceRoot) => FoldFootprint.FaceLocalRect(Box, faceRoot);

        public void OnFoldCoverageChanged(in CoverageResult coverage, Transform faceRoot)
        {
            Occluded.Apply(coverage, faceRoot);
            Apply();
        }

        void Apply()
        {
            if (player == null || playerCollider == null)
                return;

            var passable = IsPassableBy(player);
            foreach (var collider in Occluded.LiveColliders)
                Physics2D.IgnoreCollision(collider, playerCollider, passable);
        }

        void OnDrawGizmos()
        {
            if (!TryGetComponent(out BoxCollider2D gizmoBox))
                return;

            var wall = requiredAbility == Ability.None;
            Gizmos.color = wall ? WallFill : GatedFill;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(gizmoBox.offset, gizmoBox.size);
            Gizmos.color = wall ? WallOutline : GatedOutline;
            Gizmos.DrawWireCube(gizmoBox.offset, gizmoBox.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
