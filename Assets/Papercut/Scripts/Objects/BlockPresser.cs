using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// A <see cref="PushableBlock"/> as something that stands on a <see cref="PressurePlate"/>: its centre on the
    /// flat sheet and its side, wherever that part of the sheet lies — a block under a Flap still holds the plate under it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PushableBlock))]
    public sealed class BlockPresser : MonoBehaviour, IPlatePresser
    {
        PushableBlock block;

        void Awake()
        {
            block = GetComponent<PushableBlock>();
        }

        public bool TryGetSheetPoint(Sheet sheet, out SheetPoint point)
        {
            point = block.Centre;
            return isActiveAndEnabled && block.isActiveAndEnabled && block.Sheet == sheet;
        }
    }
}
