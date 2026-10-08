using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// An unlockable on a sheet face (Design Doc: Unlockables; Bible §9 - built on Aaron's request, 2026-09-17,
    /// the Power Croissant that grants Push). Collected the moment the player stands on it: their place on the
    /// flat sheet (<see cref="SheetPlacement.TryGetSheetPoint"/>) is on this face inside the pickup zone - the
    /// same shape of rule a <see cref="PressurePlate"/> uses. Collecting grants <see cref="Grants"/> to the
    /// player (<see cref="PlayerAbilities.Grant"/>) and deactivates the object for the rest of the play session.
    /// One component for every unlockable: prefabs differ in <see cref="Grants"/> and their drawing.
    /// </summary>
    /// <remarks>
    /// Face content like a plate: the trigger <see cref="BoxCollider2D"/> is the authored pickup zone (it never
    /// drives collection itself) and is clipped by folding like any face content; the drawing is hidden by a Flap
    /// through the fold renderer. Authored on the Back, it is collected by standing on the landed Flap that shows
    /// it (the player's place is then a Back-side point). Under a Flap it is unreachable because the player cannot
    /// be there. Leaving the sheet resets blocks and folds, not this: a collected unlockable stays collected
    /// (assumption stated to Aaron, uncorrected, 2026-09-17); whether a future reset button restores one is open
    /// with that button (Bible §11.16). One edge to know: on a screen transition the player is placed in the entry
    /// strip before the sheets slide, so an unlockable authored there is collected during the slide - harmless.
    /// Runs after the player and blocks have moved this step, like a plate.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    [DefaultExecutionOrder(10)]
    public sealed class Unlockable : MonoBehaviour, IFoldOccludee
    {
        [SerializeField, Tooltip("The ability (or abilities) the player gains on collecting this. Another unlockable is this " +
            "prefab with another value. None is an authoring error: the pickup is reported and never collects.")]
        Ability grants = Ability.Push;

        BoxCollider2D box;
        OccludedCollider occluded;
        Sheet sheet;
        Transform faceRoot;
        Rect flatRect;
        SheetFace side;
        PlayerAbilities player;
        PlayerMover mover;
        bool ok;
        static bool reportedNoPlayer;
        static bool reportedNoMover;

        /// <summary>What collecting this grants.</summary>
        public Ability Grants => grants;

        /// <summary>True once the player has collected it (the object is then inactive).</summary>
        public bool IsCollected { get; private set; }

        void Awake()
        {
            box = GetComponent<BoxCollider2D>();
            occluded = new OccludedCollider(box);
            if (!box.isTrigger)
            {
                Debug.LogError($"Unlockable '{name}' collider must be a trigger (it is the pickup zone, not a wall). Fixing at runtime; please fix the asset.", this);
                box.isTrigger = true;
            }

            ok = FaceContent.TryResolveFace(this, out sheet, out faceRoot, out side, out var error);
            if (!ok)
                Debug.LogError($"Unlockable '{name}' {error}.", this);

            if (grants == Ability.None)
            {
                Debug.LogError($"Unlockable '{name}' grants nothing; set Grants. It will not collect.", this);
                ok = false;
            }

            if (!ok)
            {
                enabled = false;
                return;
            }
            flatRect = FaceContent.FlatRect(box, faceRoot, side);
        }

        void OnEnable()
        {
            // Every scene object exists by the time any OnEnable runs, so one lookup here is enough; a pickup
            // instantiated later (the Studio, a probe) runs it then.
            if (ok)
                FindPlayer();
        }

        void FindPlayer()
        {
            if (player != null)
                return;
            player = FindAnyObjectByType<PlayerAbilities>();
            if (player == null)
            {
                if (!reportedNoPlayer)
                {
                    reportedNoPlayer = true;
                    Debug.LogError($"Unlockable '{name}' found no PlayerAbilities in the scene; nothing can collect it.", this);
                }
                return;
            }
            mover = player.GetComponent<PlayerMover>();
            if (mover == null)
            {
                if (!reportedNoMover)
                {
                    reportedNoMover = true;
                    Debug.LogError($"Unlockable '{name}': the player '{player.name}' has no PlayerMover; nothing can collect it.", this);
                }
                player = null;
            }
        }

        /// <summary>The collection rule: <paramref name="playerPlace"/> is on this pickup's face, inside its zone on the flat sheet.</summary>
        public bool IsReachedBy(in SheetPoint playerPlace) => playerPlace.Side == side && flatRect.Contains(playerPlace.Point);

        void FixedUpdate()
        {
            if (IsCollected || player == null || !sheet.IsScreen || sheet.Folds == null)
                return;
            if (!SheetPlacement.TryGetSheetPoint(sheet.Folds.Layers, mover.Position - sheet.Centre, out var point))
                return;
            if (!IsReachedBy(point))
                return;
            Collect();
        }

        void Collect()
        {
            IsCollected = true;
            player.Grant(grants);
            gameObject.SetActive(false);
        }

        // ----- IFoldOccludee -----

        public FaceFootprint FaceLocalFootprint(Transform root) => FaceFootprint.FromRect(FoldFootprint.FaceLocalRect(box, root));

        public void OnFoldCoverageChanged(in CoverageResult coverage, Transform space) => occluded.Apply(coverage, space);

        void OnDrawGizmos()
        {
            if (!TryGetComponent(out BoxCollider2D gizmoBox))
                return;
            Gizmos.color = new Color(0.95f, 0.8f, 0.25f, 0.4f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(gizmoBox.offset, gizmoBox.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
