namespace Papercut
{
    /// <summary>What a plate does to its effects on one tick.</summary>
    public enum PlateAction
    {
        None,
        /// <summary>Every effect applies, in order.</summary>
        Apply,
        /// <summary>Every effect reverts, in reverse order.</summary>
        Revert,
        /// <summary>Every effect toggles, in order.</summary>
        Toggle,
    }

    /// <summary>A plate's state between ticks: whether something is on it, and whether it holds its effects applied.</summary>
    public readonly struct PlateState
    {
        public readonly bool Pressed;
        public readonly bool Applied;

        public PlateState(bool pressed, bool applied)
        {
            Pressed = pressed;
            Applied = applied;
        }
    }

    /// <summary>One tick's result: the state to keep and the action to take.</summary>
    public readonly struct PlateTick
    {
        public readonly PlateState State;
        public readonly PlateAction Action;

        public PlateTick(PlateState state, PlateAction action)
        {
            State = state;
            Action = action;
        }
    }

    /// <summary>
    /// The mode table of a <see cref="PressurePlate"/>: given the mode, the last state and whether something is on
    /// the plate now, what to keep and what to do. Pure, so the modes are tested as a table.
    /// </summary>
    /// <remarks>
    /// A <paramref name="seed"/> tick — the plate's first, its first after being enabled, and its first after the
    /// sheet reset put every block back where it was authored — takes the pressed state as the plate's condition,
    /// not as a press (Aaron, 2026-09-21): a block authored on a Toggle plate leaves the puzzle as authored, and a
    /// block reset onto one does not switch anything. Hold and Latch have no edges to swallow, so a seed is an
    /// ordinary tick for them (a block resting on a Hold plate holds it; one authored on a Latch fires it).
    /// </remarks>
    public static class PlateRules
    {
        public static PlateTick Step(PlateMode mode, PlateState previous, bool pressedNow, bool seed)
        {
            switch (mode)
            {
                case PlateMode.Hold:
                {
                    var action = pressedNow == previous.Applied ? PlateAction.None
                        : pressedNow ? PlateAction.Apply : PlateAction.Revert;
                    return new PlateTick(new PlateState(pressedNow, pressedNow), action);
                }
                case PlateMode.Latch:
                {
                    var applied = previous.Applied || pressedNow;
                    var action = applied && !previous.Applied ? PlateAction.Apply : PlateAction.None;
                    return new PlateTick(new PlateState(pressedNow, applied), action);
                }
                case PlateMode.Toggle:
                {
                    // The plate holds nothing itself: the toggled state lives on what it switches (ObjectPresence).
                    var edge = pressedNow && !previous.Pressed && !seed;
                    return new PlateTick(new PlateState(pressedNow, false), edge ? PlateAction.Toggle : PlateAction.None);
                }
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(mode), mode, "unknown plate mode");
            }
        }
    }
}
