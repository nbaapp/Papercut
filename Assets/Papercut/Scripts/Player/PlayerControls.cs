using UnityEngine;
using UnityEngine.InputSystem;

namespace Papercut
{
    /// <summary>
    /// Reads the Input System "Player" action map and feeds it to <see cref="PlayerMover"/>.
    /// Keyboard, arrows and gamepad bindings live in the actions asset, not here.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMover))]
    public sealed class PlayerControls : MonoBehaviour
    {
        const string MapName = "Player";
        const string MoveActionName = "Move";

        [SerializeField]
        InputActionAsset actions;

        PlayerMover mover;
        InputActionMap playerMap;
        InputAction move;

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
        }

        void OnEnable()
        {
            playerMap?.Enable();
        }

        void OnDisable()
        {
            playerMap?.Disable();
            mover.SetMoveInput(Vector2.zero);
        }

        void Update()
        {
            mover.SetMoveInput(move.ReadValue<Vector2>());
        }
    }
}
