using UnityEngine;
using UnityEngine.InputSystem;

namespace Papercut
{
    /// <summary>
    /// Reads the Input System "Player" action map and feeds it to <see cref="PlayerMover"/>: the move vector and
    /// whether the interact key is down. Keyboard, arrows and gamepad bindings live in the actions asset, not here.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMover))]
    public sealed class PlayerControls : MonoBehaviour
    {
        const string MapName = "Player";
        const string MoveActionName = "Move";
        const string InteractActionName = "Interact";

        [SerializeField]
        InputActionAsset actions;

        PlayerMover mover;
        InputActionMap playerMap;
        InputAction move;
        InputAction interact;

        void Awake()
        {
            mover = GetComponent<PlayerMover>();

            if (actions == null)
            {
                Debug.LogError("PlayerControls has no InputActionAsset assigned.", this);
                enabled = false;
                return;
            }

            playerMap = actions.FindActionMap(MapName, throwIfNotFound: true);
            move = playerMap.FindAction(MoveActionName, throwIfNotFound: true);
            interact = playerMap.FindAction(InteractActionName, throwIfNotFound: true);
        }

        void OnEnable()
        {
            playerMap?.Enable();
        }

        void OnDisable()
        {
            playerMap?.Disable();
            mover.SetMoveInput(Vector2.zero);
            mover.SetInteractInput(false);
        }

        void Update()
        {
            mover.SetMoveInput(move.ReadValue<Vector2>());
            // The action has no initial state check: a key already down when the map enables is seen once it is pressed again.
            mover.SetInteractInput(interact.IsPressed());
        }
    }
}
