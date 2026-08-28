using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Removes an object from the world (a gate, a block, anything): deactivates it; revert brings it back.
    /// A deactivated <see cref="TerrainRegion"/> keeps receiving fold coverage, so it returns with the current clip.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RemoveObjectEffect : PlateEffect
    {
        [SerializeField, Tooltip("Deactivated when the effect applies, reactivated when it reverts. Authoring rule: a Hold plate's " +
            "target must not be able to come back on top of the player or a block - place plates so nothing can stand where " +
            "the target was while it is removed.")]
        GameObject target;

        void Awake()
        {
            if (target == null)
                Debug.LogError($"RemoveObjectEffect on '{name}' has no target; it will do nothing.", this);
        }

        public override void Apply()
        {
            if (target != null)
                target.SetActive(false);
        }

        public override void Revert()
        {
            if (target != null)
                target.SetActive(true);
        }
    }
}
