using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace Papercut
{
    /// <summary>
    /// How the player folds (Bible decision #3, scheme chosen by Aaron for prototyping): press near an edge or
    /// corner of the Screen and drag inward — the grabbed edge or corner follows the cursor and the fold
    /// previews live; release commits; right-click or Escape cancels. A fold that would cover the player shows
    /// red and is refused on release. Clicking near the crease or the Seam of a folded Screen unfolds it. Folds never
    /// extend past the sheet: the drag holds at the depth where the Flap's edge reaches the far edge.
    /// </summary>
    /// <remarks>
    /// Lives on the Desk. Mouse and keyboard only for now. Reads the Input System "Fold" action map: Point
    /// (cursor position), Grab (left button), Cancel (right button / Escape). Cursor positions are converted
    /// screen → world → sheet-local every frame; nothing is cached across frames because the grid slides.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ScreenNavigator))]
    public sealed class FoldDragInput : MonoBehaviour
    {
        const string MapName = "Fold";
        const string PointActionName = "Point";
        const string GrabActionName = "Grab";
        const string CancelActionName = "Cancel";

        [SerializeField]
        InputActionAsset actions;

        [SerializeField, Tooltip("Camera the cursor is read through. Normally the Main Camera.")]
        Camera view;

        [SerializeField]
        PlayerMover player;

        [Header("Grab")]
        [SerializeField, Min(0f), Tooltip("A press within this distance of a corner grabs the corner (corner fold), in sheet units.")]
        float cornerGrabRadius = 0.6f;

        [SerializeField, Min(0f), Tooltip("A press within this distance of an edge, inside or outside the sheet, grabs the edge (edge fold). " +
            "Keep it below the Desk's sheet gap or a press between two sheets is ambiguous.")]
        float edgeGrabMargin = 0.35f;

        [SerializeField, FormerlySerializedAs("creaseGrabDistance"), Min(0f),
         Tooltip("A press within this distance of a folded sheet's crease or Seam (where the Flap's edge meets the Front) unfolds it, in sheet units.")]
        float unfoldGrabDistance = 0.3f;

        [Header("Depth")]
        [SerializeField, Min(0f), Tooltip("Fold depth is rounded to a multiple of this while dragging. 0 = continuous (Bible decision #10 is open; try both).")]
        float depthSnap = 0f;

        ScreenNavigator navigator;
        Desk desk;
        Collider2D playerCollider;
        InputActionMap foldMap;
        InputAction point, grab, cancel;
        bool dragging;
        FoldAnchor dragAnchor;
        SheetFolds dragTarget;

        void Awake()
        {
            navigator = GetComponent<ScreenNavigator>();
            desk = GetComponent<Desk>();

            var ok = true;
            if (actions == null) { Debug.LogError("FoldDragInput has no InputActionAsset assigned.", this); ok = false; }
            if (view == null) { Debug.LogError("FoldDragInput has no camera assigned.", this); ok = false; }
            if (player == null) { Debug.LogError("FoldDragInput has no PlayerMover assigned.", this); ok = false; }
            else if (!player.TryGetComponent(out playerCollider)) { Debug.LogError("FoldDragInput: the player has no Collider2D.", this); ok = false; }
            if (!ok)
            {
                enabled = false;
                return;
            }

            foldMap = actions.FindActionMap(MapName, throwIfNotFound: true);
            point = foldMap.FindAction(PointActionName, throwIfNotFound: true);
            grab = foldMap.FindAction(GrabActionName, throwIfNotFound: true);
            cancel = foldMap.FindAction(CancelActionName, throwIfNotFound: true);

            if (desk != null && edgeGrabMargin >= desk.SheetGap)
                Debug.LogWarning($"FoldDragInput: edgeGrabMargin ({edgeGrabMargin}) is not below the Desk's sheet gap ({desk.SheetGap}); a press between sheets could grab either.", this);
        }

        void OnEnable()
        {
            foldMap?.Enable();
        }

        void OnDisable()
        {
            foldMap?.Disable();
            EndDrag();
        }

        void Update()
        {
            var screen = navigator.CurrentScreen;
            if (screen == null || navigator.IsTransitioning)
            {
                EndDrag();
                return;
            }

            if (dragging && (dragTarget == null || dragTarget != screen.Folds || cancel.WasPressedThisFrame()))
            {
                EndDrag();
                return;
            }

            var local = CursorLocal(screen);
            var playerLocal = PlayerFootprintLocal(screen);

            if (dragging)
            {
                var fold = DragFold(dragAnchor, local);
                if (grab.WasReleasedThisFrame() || !grab.IsPressed())
                {
                    dragTarget.SetPreview(null, playerLocal);
                    // CoversPlayer was already shown red; TooShallow is just a click that never became a drag.
                    if (!dragTarget.TryCommit(fold, playerLocal, out var rejection)
                        && rejection != FoldRejection.CoversPlayer && rejection != FoldRejection.TooShallow)
                        Debug.Log($"Fold not made: {rejection}.", this);
                    dragging = false;
                    dragTarget = null;
                }
                else
                {
                    dragTarget.SetPreview(fold, playerLocal);
                }
                return;
            }

            if (!grab.WasPressedThisFrame())
                return;

            var folds = screen.Folds;
            if (folds.IsFolded)
            {
                if (!folds.TryUnfoldAt(local, playerLocal, unfoldGrabDistance, out var rejection) && rejection != FoldRejection.None)
                    Debug.Log($"Not unfolded: {rejection}.", this); // The player is on the flap; there is no visual cue yet.
                return;
            }

            if (TryResolveAnchor(local, out dragAnchor))
            {
                dragging = true;
                dragTarget = folds;
                dragTarget.SetPreview(DragFold(dragAnchor, local), playerLocal);
            }
        }

        void EndDrag()
        {
            if (!dragging)
                return;
            dragging = false;
            if (dragTarget != null)
                dragTarget.SetPreview(null, Rect.zero);
            dragTarget = null;
        }

        /// <summary>The fold under the cursor: depth from the drag point, snapped, then clamped so it never overhangs.</summary>
        Fold DragFold(FoldAnchor anchor, Vector2 local)
            => new Fold(anchor, Snap(FoldGeometry.DepthForDragPoint(anchor, local))).Clamped;

        float Snap(float depth) => depthSnap > 0f ? Mathf.Round(depth / depthSnap) * depthSnap : depth;

        Vector2 CursorLocal(Sheet sheet)
        {
            var screenPoint = point.ReadValue<Vector2>();
            Vector2 world = view.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, 0f));
            return world - sheet.Centre;
        }

        Rect PlayerFootprintLocal(Sheet sheet)
        {
            var bounds = playerCollider.bounds;
            return new Rect((Vector2)bounds.min - sheet.Centre, bounds.size);
        }

        /// <summary>Nearest corner within the corner radius wins; otherwise the nearest edge within the margin.</summary>
        bool TryResolveAnchor(Vector2 local, out FoldAnchor anchor)
        {
            var half = SheetGeometry.HalfSize;
            anchor = default;
            var best = float.PositiveInfinity;

            foreach (var corner in new[] { FoldAnchor.CornerNorthEast, FoldAnchor.CornerNorthWest, FoldAnchor.CornerSouthEast, FoldAnchor.CornerSouthWest })
            {
                var d = Vector2.Distance(local, Vector2.Scale(corner.CornerSigns(), half));
                if (d <= cornerGrabRadius && d < best)
                {
                    best = d;
                    anchor = corner;
                }
            }
            if (best < float.PositiveInfinity)
                return true;

            foreach (var edge in new[] { FoldAnchor.EdgeNorth, FoldAnchor.EdgeEast, FoldAnchor.EdgeSouth, FoldAnchor.EdgeWest })
            {
                var outward = edge.EdgeDirection().ToVector();
                var halfExtent = Mathf.Abs(Vector2.Dot(half, outward));
                var halfAlong = Mathf.Abs(Vector2.Dot(half, new Vector2(outward.y, outward.x)));
                var d = Mathf.Abs(Vector2.Dot(local, outward) - halfExtent);
                var along = Mathf.Abs(Vector2.Dot(local, new Vector2(outward.y, outward.x)));
                if (d <= edgeGrabMargin && along <= halfAlong + edgeGrabMargin && d < best)
                {
                    best = d;
                    anchor = edge;
                }
            }
            return best < float.PositiveInfinity;
        }
    }
}
