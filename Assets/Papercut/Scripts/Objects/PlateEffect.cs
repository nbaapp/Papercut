using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// What a <see cref="PressurePlate"/> does to the world. One <see cref="Apply"/> / <see cref="Revert"/> pair:
    /// a Hold plate applies when pressed and reverts when released, a Latch plate applies once. A new kind of
    /// effect is a new subclass referenced from the plate's Inspector; nothing anywhere branches on effect kinds.
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
    }
}
