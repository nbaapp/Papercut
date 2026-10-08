using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// What a <see cref="PressurePlate"/> does to the world. Three operations, chosen by the plate's mode: a Hold
    /// plate <see cref="Apply"/>s when pressed and <see cref="Revert"/>s when released, a Latch plate applies
    /// once, a Toggle plate <see cref="Toggle"/>s on every press. A new kind of effect is a new subclass
    /// referenced from the plate's Inspector; nothing anywhere branches on effect kinds.
    /// </summary>
    /// <remarks>
    /// An abstract MonoBehaviour rather than an interface only so the plate can hold Inspector references to an
    /// open set of effects. Keep the base empty of behaviour.
    /// </remarks>
    public abstract class PlateEffect : MonoBehaviour
    {
        public abstract void Apply();

        /// <summary>Restores what <see cref="Apply"/> changed.</summary>
        public abstract void Revert();

        /// <summary>A press on a Toggle plate: switches the effect's outcome the other way and leaves it there.</summary>
        public abstract void Toggle();
    }
}
