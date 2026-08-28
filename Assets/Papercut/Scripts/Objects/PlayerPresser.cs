using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The player as something that stands on a <see cref="PressurePlate"/>: their place on the sheet is the
    /// point under their centre on the topmost layer there, on that layer's up face. Present only on the Screen.
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
            var local = mover.Position - sheet.Centre;
            var layers = sheet.Folds.Layers;
            var top = SheetPlacement.TopLayerAt(layers, local);
            if (top < 0)
                return false;
            var layer = layers.Layers[top];
            point = new SheetPoint(layer.ToDesk.Inverse.Apply(local), SheetPlacement.UpSide(layer));
            return true;
        }
    }
}
