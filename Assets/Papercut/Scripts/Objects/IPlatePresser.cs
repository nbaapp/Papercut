namespace Papercut
{
    /// <summary>
    /// Anything that can stand on a <see cref="PressurePlate"/>: the player, a block, whatever comes later.
    /// A presser reports where on the sheet it is (<see cref="SheetPoint"/>); the plate compares that with its
    /// own place (<see cref="PressureRules"/>).
    /// </summary>
    public interface IPlatePresser
    {
        /// <summary>True with this presser's place on <paramref name="sheet"/>; false if it is not on that sheet right now.</summary>
        bool TryGetSheetPoint(Sheet sheet, out SheetPoint point);
    }
}
