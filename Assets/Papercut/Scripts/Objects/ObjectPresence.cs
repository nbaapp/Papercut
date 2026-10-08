using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Whether an object (a Gate, a block, anything a plate effect targets) is present in the world, from its
    /// authored resting state and what the plates wired to it are doing (<see cref="SwitchPresenceEffect"/>).
    /// A resting-present object (a Gate) is removed while switched; a resting-absent one (a Gate (Off),
    /// <see cref="startsAbsent"/>) is present while switched. Switched means: held by any Hold or Latch effect
    /// currently applied, <em>or</em> toggled by an odd number of Toggle-plate presses (Aaron, 2026-09-21: every
    /// press on any wired Toggle switches it again). A hold and a toggle never cancel each other: a held target
    /// stays switched through toggles and shows the toggled state when the last hold lets go; a fired Latch
    /// therefore masks toggles for good.
    /// </summary>
    /// <remarks>
    /// Authored only on prefabs that rest absent (the Gate (Off) variant); otherwise added to the target at
    /// runtime by the first effect that acts on it (<see cref="Of"/>). Each holder counts once however often it
    /// asks, so a repeated hold or a release without a hold is harmless. Nothing here resets on leaving the sheet:
    /// plate effects persist (Bible §8, Aaron 2026-08-27: "Gates shouldn't reset").
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ObjectPresence : MonoBehaviour
    {
        [SerializeField, Tooltip("On: the object rests absent and is present only while a wired plate switches it (a Gate (Off)). " +
            "Off: it rests present and is removed while switched (a Gate).")]
        bool startsAbsent;

        readonly HashSet<Object> holders = new();
        bool toggled;

        /// <summary>The authored resting state: true if the object is absent until switched.</summary>
        public bool StartsAbsent => startsAbsent;

        /// <summary>True while any Hold/Latch effect holds the object or Toggle presses have left it toggled.</summary>
        public bool IsSwitched => holders.Count > 0 || toggled;

        /// <summary>True while the object should be active: its resting state, inverted while switched.</summary>
        public bool IsPresent => startsAbsent == IsSwitched;

        /// <summary>The presence of <paramref name="target"/>, added (resting present) if it has none yet.</summary>
        public static ObjectPresence Of(GameObject target)
        {
            return target.TryGetComponent(out ObjectPresence presence) ? presence : target.AddComponent<ObjectPresence>();
        }

        void Start()
        {
            // Not Awake: the object's own components (a TerrainRegion caching its collider) must have run theirs
            // before a resting-absent object is deactivated, or fold coverage could never reach it. Start runs
            // before the first physics step and the first rendered frame, so the present-by-default moment is
            // never seen or touched. For a component Of() added at runtime this may run late (after a Hold has
            // already deactivated the object and it is next active): Sync is idempotent, so that is a redundant
            // SetActive(true), never a second switch.
            Sync();
        }

        /// <summary>Switches the object on behalf of <paramref name="holder"/> until it <see cref="Release"/>s.</summary>
        public void Hold(Object holder)
        {
            if (holders.Add(holder))
                Sync();
        }

        /// <summary>Withdraws <paramref name="holder"/>'s hold.</summary>
        public void Release(Object holder)
        {
            if (holders.Remove(holder))
                Sync();
        }

        /// <summary>A Toggle-plate press: switches the object the other way and leaves it there.</summary>
        public void Toggle()
        {
            toggled = !toggled;
            Sync();
        }

        void Sync()
        {
            var present = IsPresent;
            if (gameObject.activeSelf != present)
                gameObject.SetActive(present);
        }
    }
}
