using System;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The single source of truth for which <see cref="Ability"/> flags the player currently holds.
    /// Elements (e.g. <see cref="TerrainRegion"/>) read <see cref="Abilities"/> and react to <see cref="Changed"/>.
    /// </summary>
    /// <remarks>
    /// Editable in the Inspector during Play Mode so an ability can be toggled for testing. There is no mutator
    /// yet because nothing grants abilities (unlockable pickups are outside the prototype); one is added with
    /// the first pickup.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class PlayerAbilities : MonoBehaviour
    {
        [SerializeField, Tooltip("Abilities the player holds. The value saved on the prefab is the player's STARTING " +
            "set - leave it at None unless the game should begin with an ability. Toggle in Play Mode to test gated " +
            "terrain.")]
        Ability abilities = Ability.None;

        bool inspectorChanged;

        /// <summary>The current ability set.</summary>
        public Ability Abilities => abilities;

        /// <summary>Raised after the ability set changes.</summary>
        public event Action Changed;

        void OnEnable()
        {
            // Physics2D drops ignore-pairs when the player's collider is disabled; listeners re-apply on Changed.
            Changed?.Invoke();
        }

        void OnValidate()
        {
            // Inspector edits arrive here, including during Play Mode. Physics must not be touched from OnValidate,
            // so listeners are told from Update instead.
            inspectorChanged = true;
        }

        void Update()
        {
            if (!inspectorChanged)
                return;

            inspectorChanged = false;
            Changed?.Invoke();
        }
    }
}
