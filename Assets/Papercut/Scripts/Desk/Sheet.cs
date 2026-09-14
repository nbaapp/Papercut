using System;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// One physical piece of paper on the Desk. The persistent world object; the Sheet the
    /// player currently occupies is the Screen (see <see cref="ScreenNavigator"/>).
    /// </summary>
    /// <remarks>
    /// A Sheet must be a direct child of a <see cref="Desk"/>'s <see cref="Desk.SheetGrid"/>, which lays it out
    /// from <see cref="GridPosition"/> and slides it between Screens.
    /// Everything authored on a sheet lives under one of its two face roots, <see cref="Front"/> and
    /// <see cref="Back"/>. Each authored sheet is a prefab variant of the Sheet prefab (Assets/Papercut/Sheets/&lt;Desk scene name&gt;/),
    /// built in isolation and placed on the Desk by parenting it and setting <see cref="GridPosition"/>.
    /// Face content is put on the <see cref="FoldLayers"/> layers at Awake and is visible only through the sheet's
    /// <see cref="IFoldRenderer"/>. Both face roots stay active and never move; what exists physically, and
    /// where, is decided by <see cref="SheetOcclusion"/> from the sheet's fold layers.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class Sheet : MonoBehaviour
    {
        [SerializeField, Tooltip("Position of this sheet in the Desk grid. North is +y, East is +x.")]
        Vector2Int gridPosition;

        [SerializeField, Tooltip("Root for everything authored on the Front face. Local space is the sheet's space.")]
        Transform front;

        [SerializeField, Tooltip("Root for everything authored on the Back face, in Back-space: point (x, y) here lies " +
            "beneath Front point (-x, y) - the sheet turned over about its vertical edge. Never moved at runtime.")]
        Transform back;

        [Header("Testing")]
        [SerializeField, Tooltip("Show this sheet's collision: every terrain region draws its collider as a solid fill (its " +
            "TerrainFill colour), in the game and in the Sheet Studio. For playing and designing a sheet before its map art " +
            "is drawn - off for the shipped look. Saved per sheet.")]
        bool showCollision;

        Desk desk;
        SheetFolds folds;

        public Vector2Int GridPosition => gridPosition;

        /// <summary>True while this sheet draws its terrain collision as solid fills (see <see cref="TerrainFill"/>).</summary>
        public bool ShowCollision => showCollision;

        /// <summary>
        /// Raised from OnValidate (editor only) whenever the sheet's fields are edited - the Inspector or the Sheet
        /// Studio toggling <see cref="ShowCollision"/> included. Listeners refresh idempotently, so an unrelated
        /// edit costs nothing but a cheap recheck.
        /// </summary>
        public event Action ShowCollisionChanged;

        /// <summary>Root of the Front face's authored content. Children are in sheet-local space.</summary>
        public Transform Front => front;

        /// <summary>
        /// Root of the Back face's authored content, in Back-space (see <see cref="SheetGeometry.BackToFront"/>).
        /// Never moved; exposed Back content is placed by <see cref="SheetOcclusion"/> through its occludees.
        /// </summary>
        public Transform Back => back;

        /// <summary>This sheet's fold state.</summary>
        public SheetFolds Folds => folds;

        /// <summary>True while the player is on this sheet. Only the Screen has live physics.</summary>
        public bool IsScreen { get; private set; }

        /// <summary>The Desk this sheet lies on. Null (with an error logged) if the sheet is not under a Desk.</summary>
        public Desk Desk
        {
            get
            {
                if (desk == null)
                {
                    desk = GetComponentInParent<Desk>();
                    if (desk == null)
                        Debug.LogError($"Sheet '{name}' is not a child of a Desk.", this);
                }
                return desk;
            }
        }

        public Vector2 Centre => transform.position;

        /// <summary>World-space bounds of this sheet's Base.</summary>
        public Rect Bounds => SheetGeometry.BoundsAt(Centre);

        public bool Contains(Vector2 worldPoint) => Bounds.Contains(worldPoint);

        /// <summary>Raised when the player arrives on this sheet and it becomes the Screen.</summary>
        public event Action PlayerEntered;

        /// <summary>
        /// Raised when the player leaves this sheet. Folds do not persist across this
        /// (Bible §8 [LOCKED]); <see cref="SheetFolds"/> resets from here.
        /// </summary>
        public event Action PlayerLeft;

        internal void NotifyPlayerEntered()
        {
            IsScreen = true;
            PlayerEntered?.Invoke();
        }

        internal void NotifyPlayerLeft()
        {
            IsScreen = false;
            PlayerLeft?.Invoke();
        }

        void Awake()
        {
            _ = Desk; // Surface a missing Desk immediately rather than on first lookup.
            ValidateFaceRoots();

            folds = GetComponent<SheetFolds>();
            if (folds == null)
                Debug.LogError($"Sheet '{name}' has no SheetFolds component.", this);
            if (GetComponent<IFoldRenderer>() == null)
                Debug.LogError($"Sheet '{name}' has no IFoldRenderer component; face content is invisible to the main camera.", this);

            if (FoldLayers.Valid)
            {
                SetLayerRecursively(front, FoldLayers.Front);
                SetLayerRecursively(back, FoldLayers.Back);
            }
        }

        static void SetLayerRecursively(Transform root, int layer)
        {
            if (root == null)
                return;
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }

        void OnValidate()
        {
            // Keep the sheet where its grid position says it is while authoring.
            var parentDesk = GetComponentInParent<Desk>();
            if (parentDesk != null && parentDesk.ValidateSheetParent(this))
                transform.localPosition = parentDesk.GridToLocal(gridPosition);

            ValidateFaceRoots();
            ShowCollisionChanged?.Invoke();
        }

        void ValidateFaceRoots()
        {
            ValidateFaceRoot(front, "Front");
            ValidateFaceRoot(back, "Back");
        }

        void ValidateFaceRoot(Transform root, string faceName)
        {
            if (root == null)
                Debug.LogError($"Sheet '{name}' has no {faceName} root assigned.", this);
            else if (!root.IsChildOf(transform) || root == transform)
                Debug.LogError($"Sheet '{name}': {faceName} root '{root.name}' must be a descendant of the sheet.", this);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
            Gizmos.DrawWireCube(transform.position, SheetGeometry.Size);
        }
    }
}
