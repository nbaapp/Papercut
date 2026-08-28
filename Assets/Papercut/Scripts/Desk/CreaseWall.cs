using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Marks a boundary wall (built by <see cref="SheetBoundary"/>) that stands on a crease line. The player
    /// cannot cross it (there is no sheet beyond a crease on the lifted side), but the sheet itself continues
    /// around a crease, so a <see cref="PushableBlock"/> ignores these walls and rolls onto the other face.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CreaseWall : MonoBehaviour
    {
    }
}
