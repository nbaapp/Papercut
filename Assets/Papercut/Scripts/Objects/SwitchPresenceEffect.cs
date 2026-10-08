using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Switches objects (gates, blocks, anything) away from their resting state: a Gate is removed, a Gate (Off)
    /// is present. What "present" means, and how several plates on one target combine, is the target's
    /// <see cref="ObjectPresence"/>; this effect only holds, releases and toggles it. One effect drives any
    /// number of targets, so one plate can remove Gate A and present Gate B together (Aaron, 2026-09-21).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SwitchPresenceEffect : PlateEffect
    {
        [SerializeField, Tooltip("Each is switched from its resting state while this effect is on it: a Gate is removed, a Gate (Off) is present. " +
            "Authoring rule: a target must never be switched into a place where the player or a block can stand - a Gate (Off) appears " +
            "where it is authored on any press, a Gate comes back on a Hold release or a Toggle press - nothing checks for overlap.")]
        GameObject[] targets = System.Array.Empty<GameObject>();

        readonly List<GameObject> live = new();
        bool resolved;

        /// <summary>
        /// The targets actually driven: the authored list minus unassigned and repeated entries, each reported
        /// once. Resolved at Awake so bad authoring shows at load, and on first use otherwise (Edit Mode tests
        /// have no Awake) - the list never changes at play, so once is enough.
        /// </summary>
        IReadOnlyList<GameObject> Live
        {
            get
            {
                if (!resolved)
                    Resolve();
                return live;
            }
        }

        void Awake()
        {
            Resolve();
        }

        void Resolve()
        {
            resolved = true;
            live.Clear();
            if (targets.Length == 0)
                Debug.LogError($"SwitchPresenceEffect on '{name}' has no targets; it will do nothing.", this);
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] == null)
                    Debug.LogError($"SwitchPresenceEffect on '{name}': target {i} is not assigned; it is skipped.", this);
                else if (live.Contains(targets[i]))
                    // A repeat would toggle the target twice per press (a hold counts once, a toggle does not), so it is dropped, not doubled.
                    Debug.LogError($"SwitchPresenceEffect on '{name}': target {i} ('{targets[i].name}') is listed twice; the repeat is skipped.", this);
                else
                    live.Add(targets[i]);
            }
        }

        public override void Apply()
        {
            foreach (var target in Live)
                ObjectPresence.Of(target).Hold(this);
        }

        public override void Revert()
        {
            foreach (var target in Live)
                ObjectPresence.Of(target).Release(this);
        }

        public override void Toggle()
        {
            foreach (var target in Live)
                ObjectPresence.Of(target).Toggle();
        }
    }
}
