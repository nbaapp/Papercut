using System;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The single source of truth for which <see cref="Ability"/> flags the player currently holds.
    /// Elements (e.g. <see cref="TerrainRegion"/>) read <see cref="Abilities"/> and react to <see cref="Changed"/>.
    /// </summary>
    /// <remarks>
    /// Editable in the Inspector during Play Mode so an ability can be toggled for testing; the field is the live
    /// set, so it also shows what pickups have granted. <see cref="Grant"/> is the one mutator: an
    /// <see cref="Unlockable"/> calls it when collected (the first pickup, 2026-09-17). Nothing revokes an ability.
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

        /// <summary>
        /// Adds <paramref name="ability"/> (a flag or several) to the set and raises <see cref="Changed"/> at once.
        /// Granting nothing new - <see cref="Ability.None"/>, or flags already held - is a no-op and raises nothing.
        /// Safe to call from FixedUpdate: listeners may touch physics from Changed (unlike from OnValidate).
        /// </summary>
        public void Grant(Ability ability)
        {
            var granted = abilities | ability;
            if (granted == abilities)
                return;
            abilities = granted;
            Changed?.Invoke();
        }

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
