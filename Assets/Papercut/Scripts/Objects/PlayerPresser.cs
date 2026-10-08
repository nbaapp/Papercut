using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The player as something that stands on a <see cref="PressurePlate"/>: their place on the sheet is the
    /// point under their centre on the topmost layer there, on that layer's up face
    /// (<see cref="SheetPlacement.TryGetSheetPoint"/>). Present only on the Screen.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMover))]
    public sealed class PlayerPresser : MonoBehaviour, IPlatePresser
    {
        PlayerMover mover;

        void Awake()
        {
            mover = GetComponent<PlayerMover>();
        }

        public bool TryGetSheetPoint(Sheet sheet, out SheetPoint point)
        {
            point = default;
            if (!isActiveAndEnabled || sheet == null || !sheet.IsScreen || sheet.Folds == null)
                return false;
            return SheetPlacement.TryGetSheetPoint(sheet.Folds.Layers, mover.Position - sheet.Centre, out point);
        }
    }
}
