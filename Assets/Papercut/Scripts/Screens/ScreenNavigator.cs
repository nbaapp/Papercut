using System.Collections;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Tracks which Sheet is the current Screen and moves the player between Sheets through their edges.
    /// Lives on the Desk alongside <see cref="Desk"/>.
    /// </summary>
    /// <remarks>
    /// Exits are not authored: a <see cref="SheetEdge"/> is any edge of the walkable area (see
    /// <see cref="SheetBoundary"/>). Leaving through one succeeds if there is a neighbouring sheet in that
    /// direction and room for the player where they would arrive (<see cref="TravelRules"/>); otherwise the wall
    /// behind the edge simply holds. A transition places the player on the destination sheet immediately, then
    /// slides the sheets under the fixed camera (see <see cref="DeskSlider"/>) while player movement is held.
    /// Sheets are notified as the player leaves and arrives, so per-sheet state (folds, physics) resets.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Desk), typeof(DeskSlider))]
    public sealed class ScreenNavigator : MonoBehaviour
    {
        [SerializeField]
        PlayerMover player;

        [SerializeField, Min(0f), Tooltip("How far inside the destination sheet's edge the player arrives, in world units.")]
        float entryInset = 0.75f;

        Desk desk;
        DeskSlider slider;
        Collider2D playerCollider;
        PlayerAbilities playerAbilities;
        Sheet currentScreen;
        Coroutine transition;

        /// <summary>The Sheet the player is on. Null until <see cref="Start"/> has located the player.</summary>
        public Sheet CurrentScreen => currentScreen;

        public bool IsTransitioning => transition != null;

        void Awake()
        {
            desk = GetComponent<Desk>();
            slider = GetComponent<DeskSlider>();

            if (player == null)
            {
                Debug.LogError("ScreenNavigator has no PlayerMover assigned.", this);
                return;
            }
            if (!player.TryGetComponent(out playerCollider))
                Debug.LogError($"ScreenNavigator: the player '{player.name}' has no Collider2D; every edge will be treated as having room.", this);
            if (!player.TryGetComponent(out playerAbilities))
                Debug.LogError($"ScreenNavigator: the player '{player.name}' has no PlayerAbilities; gated terrain will count as solid at arrival.", this);
        }

        void Start()
        {
            if (player == null)
                return;

            if (!desk.TryGetSheetAt(player.Position, out var startSheet))
            {
                Debug.LogError($"Player at {player.Position} is not on any sheet; cannot pick a starting Screen.", this);
                return;
            }

            currentScreen = startSheet;
            currentScreen.NotifyPlayerEntered();
            // Moves the player too (a child of the sheet grid). Held so the body is not interpolating
            // while its transform is moved. PlayerMover.Position reads the body, which only picks the
            // move up at the next physics step; nothing reads it before then.
            player.MovementEnabled = false;
            slider.Centre(currentScreen);
            player.MovementEnabled = true;
        }

        void OnDisable()
        {
            if (transition == null)
                return;

            StopCoroutine(transition);
            transition = null;
            if (player != null)
                player.MovementEnabled = true;
        }

        /// <summary>
        /// Carries <paramref name="traveller"/> through <paramref name="edge"/> to the neighbouring sheet, if there
        /// is one and there is room to arrive. Returns quietly otherwise: the wall behind the edge holds.
        /// </summary>
        public void TravelThrough(SheetEdge edge, PlayerMover traveller)
        {
            if (IsTransitioning)
                return;

            if (edge.Sheet != currentScreen)
            {
                Debug.LogError($"Edge '{edge.name}' is on sheet '{edge.Sheet.name}', but the current Screen is '{currentScreen.name}'.", edge);
                return;
            }

            if (!desk.TryGetNeighbour(currentScreen, edge.Direction, out var destination))
                return;

            var entry = EntryPosition(currentScreen.Bounds, destination.Bounds, edge.Direction, traveller.Position, entryInset);
            if (!HasRoom(destination, entry))
                return;

            transition = StartCoroutine(Transition(entry, destination, traveller));
        }

        bool HasRoom(Sheet destination, Vector2 entry)
        {
            if (playerCollider == null)
                return true;
            var size = (Vector2)playerCollider.bounds.size;
            var box = new Rect(entry - size * 0.5f, size);
            var occlusion = destination.GetComponent<SheetOcclusion>();
            return occlusion == null || TravelRules.HasRoom(box, occlusion.SolidFootprints(playerAbilities));
        }

        IEnumerator Transition(Vector2 entry, Sheet destination, PlayerMover traveller)
        {
            var origin = currentScreen;

            traveller.MovementEnabled = false;
            traveller.PlaceAt(entry);

            origin.NotifyPlayerLeft();
            currentScreen = destination;
            destination.NotifyPlayerEntered();

            slider.SlideTo(destination);
            while (slider.IsSliding)
                yield return null;

            traveller.MovementEnabled = true;
            transition = null;
        }

        /// <summary>
        /// Where the player arrives on <paramref name="to"/> after leaving <paramref name="from"/> toward
        /// <paramref name="direction"/>: the same along-edge coordinate, <paramref name="inset"/> inside
        /// the destination's near edge, clamped so the player is at least <paramref name="inset"/> from every edge.
        /// </summary>
        public static Vector2 EntryPosition(Rect from, Rect to, GridDirection direction, Vector2 playerPosition, float inset)
        {
            var step = direction.ToVector();
            var innerHalf = Vector2.Max(to.size * 0.5f - Vector2.one * inset, Vector2.zero);

            var local = playerPosition - from.center;
            if (step.x != 0f)
                local.x = -step.x * innerHalf.x;
            else
                local.y = -step.y * innerHalf.y;

            local.x = Mathf.Clamp(local.x, -innerHalf.x, innerHalf.x);
            local.y = Mathf.Clamp(local.y, -innerHalf.y, innerHalf.y);
            return to.center + local;
        }
    }
}
