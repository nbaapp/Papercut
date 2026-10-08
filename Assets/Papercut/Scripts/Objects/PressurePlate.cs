using System;
using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>How a plate behaves once pressed.</summary>
    public enum PlateMode
    {
        /// <summary>Pressed while something is on it; its effects revert when it is released.</summary>
        Hold,
        /// <summary>The first press applies its effects for good.</summary>
        Latch,
        /// <summary>Each press switches its effects the other way; nothing happens on release.</summary>
        Toggle,
    }

    /// <summary>
    /// A pressure plate on a sheet face (Design Doc: buttons; Bible §9 - built on Aaron's request, 2026-08-27).
    /// Pressed while any <see cref="IPlatePresser"/> stands on it - the player, a block, whatever comes later -
    /// judged on the flat sheet (<see cref="PressureRules"/>), so a plate under a Flap with a block on it stays
    /// pressed. Hold, Latch and Toggle are one component differing in data (<see cref="PlateRules"/>). What a
    /// press does is an open set of <see cref="PlateEffect"/>s.
    /// </summary>
    /// <remarks>
    /// The BoxCollider2D is the plate's authored footprint (a trigger; it never drives pressing) and is clipped by
    /// folding like any face content so nothing physics-driven sees a covered plate. Runs after blocks have moved.
    /// The first tick after enabling and after the sheet reset (<see cref="Sheet.PlayerLeft"/> puts every block
    /// back) is a seed: what is on the plate then is its condition, not a press (Aaron, 2026-09-21).
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    [DefaultExecutionOrder(10)]
    public sealed class PressurePlate : MonoBehaviour, IFoldOccludee
    {
        [SerializeField, Tooltip("Hold: pressed while something is on it; effects revert when it is released. Latch: the first press applies the effects for good. " +
            "Toggle: each press switches the effects the other way (the state lives on what they switch); nothing happens on release.")]
        PlateMode mode = PlateMode.Hold;

        [SerializeField, Tooltip("Applied when pressed, in order; a Hold plate reverts them in reverse order when released; a Toggle plate toggles them on every press. " +
            "Something is 'on' the plate when its centre is inside the plate's box.")]
        PlateEffect[] effects = Array.Empty<PlateEffect>();

        [Header("Drawing")]
        [SerializeField, Tooltip("Optional: tinted with the colours below as the plate is pressed and released.")]
        SpriteRenderer spriteRenderer;

        [SerializeField, Tooltip("Colour while released.")]
        Color releasedColor = new(0.85f, 0.75f, 0.2f, 0.9f);

        [SerializeField, Tooltip("Colour while pressed (a Latch plate keeps it once fired; a Toggle plate shows it only while pressed - its state is on what it switches).")]
        Color pressedColor = new(0.45f, 0.4f, 0.15f, 0.9f);

        BoxCollider2D box;
        OccludedCollider occluded;
        Sheet sheet;
        Transform faceRoot;
        Rect flatRect;
        SheetFace side;
        PlayerPresser player;
        readonly List<IPlatePresser> sheetPressers = new();
        readonly List<SheetPoint> points = new();
        bool ok;
        PlateState state;
        bool seedNext;
        static bool reportedNoPlayer;

        public bool IsPressed => state.Pressed;

        /// <summary>True while the plate holds its effects applied: a Hold plate while pressed, a Latch plate once fired, a Toggle plate never.</summary>
        public bool IsApplied => state.Applied;

        /// <summary>Raised when <see cref="IsPressed"/> changes.</summary>
        public event Action<bool> PressedChanged;

        void Awake()
        {
            box = GetComponent<BoxCollider2D>();
            occluded = new OccludedCollider(box);
            if (!box.isTrigger)
            {
                Debug.LogError($"PressurePlate '{name}' collider must be a trigger. Fixing at runtime; please fix the asset.", this);
                box.isTrigger = true;
            }

            ok = FaceContent.TryResolveFace(this, out sheet, out faceRoot, out side, out var error);
            if (!ok)
                Debug.LogError($"PressurePlate '{name}' {error}.", this);

            if (effects.Length == 0)
                Debug.LogWarning($"PressurePlate '{name}' has no effects.", this);
            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i] == null)
                    Debug.LogError($"PressurePlate '{name}': effect {i} is not assigned; it is skipped.", this);
            }

            if (!ok)
            {
                enabled = false;
                return;
            }
            flatRect = FaceContent.FlatRect(box, faceRoot, side);
        }

        void OnEnable()
        {
            if (!ok)
                return;
            seedNext = true; // Scene start, or a plate that returns after being absent: what is on it is its condition, not a press.
            sheet.PlayerLeft += RequestSeed;
            CollectPressers();
            ShowPressed(ShownPressed);
        }

        void OnDisable()
        {
            if (ok)
                sheet.PlayerLeft -= RequestSeed;
        }

        /// <summary>
        /// The sheet reset: blocks teleport back to where they were authored in their own PlayerLeft handlers, and
        /// the player no longer counts, so the next tick reads the authored arrangement and must not treat it as
        /// a press (a block reset onto a Toggle plate switches nothing). Handler order does not matter: the
        /// resets are synchronous and the seed happens on the next FixedUpdate.
        /// </summary>
        void RequestSeed() => seedNext = true;

        /// <summary>What the drawing shows: a fired Latch stays down; a Hold or Toggle plate follows the press.</summary>
        bool ShownPressed => mode == PlateMode.Latch ? IsApplied : IsPressed;

        void Start()
        {
            CollectPressers();
        }

        void CollectPressers()
        {
            sheetPressers.Clear();
            sheet.GetComponentsInChildren(true, sheetPressers);
            if (player == null)
            {
                player = FindAnyObjectByType<PlayerPresser>();
                if (player == null && !reportedNoPlayer)
                {
                    reportedNoPlayer = true;
                    Debug.LogError($"PressurePlate '{name}' found no PlayerPresser in the scene; the player cannot press plates.", this);
                }
            }
        }

        void FixedUpdate()
        {
            points.Clear();
            foreach (var presser in sheetPressers)
            {
                if (presser.TryGetSheetPoint(sheet, out var p))
                    points.Add(p);
            }
            if (player != null && player.TryGetSheetPoint(sheet, out var playerPoint))
                points.Add(playerPoint);

            var pressed = PressureRules.IsPressed(flatRect, side, points);
            var tick = PlateRules.Step(mode, state, pressed, seedNext);
            seedNext = false;

            var shownBefore = ShownPressed;
            var pressedBefore = state.Pressed;
            state = tick.State;

            switch (tick.Action)
            {
                case PlateAction.Apply: ApplyEffects(); break;
                case PlateAction.Revert: RevertEffects(); break;
                case PlateAction.Toggle: ToggleEffects(); break;
            }

            if (ShownPressed != shownBefore)
                ShowPressed(ShownPressed);
            if (state.Pressed != pressedBefore)
                PressedChanged?.Invoke(state.Pressed);
        }

        void ApplyEffects()
        {
            foreach (var effect in effects)
            {
                if (effect != null)
                    effect.Apply();
            }
        }

        void RevertEffects()
        {
            for (int i = effects.Length - 1; i >= 0; i--)
            {
                if (effects[i] != null)
                    effects[i].Revert();
            }
        }

        void ToggleEffects()
        {
            foreach (var effect in effects)
            {
                if (effect != null)
                    effect.Toggle();
            }
        }

        void ShowPressed(bool pressed)
        {
            if (spriteRenderer == null)
                return;
            spriteRenderer.color = pressed ? pressedColor : releasedColor;
        }

        // ----- IFoldOccludee -----

        public FaceFootprint FaceLocalFootprint(Transform root) => FaceFootprint.FromRect(FoldFootprint.FaceLocalRect(box, root));

        public void OnFoldCoverageChanged(in CoverageResult coverage, Transform space) => occluded.Apply(coverage, space);

        void OnDrawGizmos()
        {
            if (!TryGetComponent(out BoxCollider2D gizmoBox))
                return;
            Gizmos.color = new Color(0.9f, 0.8f, 0.2f, 0.4f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(gizmoBox.offset, gizmoBox.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
