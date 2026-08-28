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
    }

    /// <summary>
    /// A pressure plate on a sheet face (Design Doc: buttons; Bible §9 - built on Aaron's request, 2026-08-27).
    /// Pressed while any <see cref="IPlatePresser"/> stands on it - the player, a block, whatever comes later -
    /// judged on the flat sheet (<see cref="PressureRules"/>), so a plate under a Flap with a block on it stays
    /// pressed. Hold and Latch are one component differing in data. What a press does is an open set of
    /// <see cref="PlateEffect"/>s.
    /// </summary>
    /// <remarks>
    /// The BoxCollider2D is the plate's authored footprint (a trigger; it never drives pressing) and is clipped by
    /// folding like any face content so nothing physics-driven sees a covered plate. Runs after blocks have moved.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider2D))]
    [DefaultExecutionOrder(10)]
    public sealed class PressurePlate : MonoBehaviour, IFoldOccludee
    {
        [SerializeField, Tooltip("Hold: pressed while something is on it; effects revert when it is released. Latch: the first press applies the effects for good.")]
        PlateMode mode = PlateMode.Hold;

        [SerializeField, Tooltip("Applied when pressed, in order; a Hold plate reverts them in reverse order when released. " +
            "Something is 'on' the plate when its centre is inside the plate's box.")]
        PlateEffect[] effects = Array.Empty<PlateEffect>();

        [Header("Drawing")]
        [SerializeField, Tooltip("Optional: tinted with the colours below as the plate is pressed and released.")]
        SpriteRenderer spriteRenderer;

        [SerializeField, Tooltip("Colour while released.")]
        Color releasedColor = new(0.85f, 0.75f, 0.2f, 0.9f);

        [SerializeField, Tooltip("Colour while pressed (a Latch plate keeps it once fired).")]
        Color pressedColor = new(0.45f, 0.4f, 0.15f, 0.9f);

        BoxCollider2D box;
        OccludedBoxCollider occluded;
        Sheet sheet;
        Transform faceRoot;
        Rect flatRect;
        SheetFace side;
        PlayerPresser player;
        readonly List<IPlatePresser> sheetPressers = new();
        readonly List<SheetPoint> points = new();
        bool ok;
        static bool reportedNoPlayer;

        public bool IsPressed { get; private set; }

        /// <summary>True once a Latch plate has fired.</summary>
        public bool IsLatched { get; private set; }

        /// <summary>Raised when <see cref="IsPressed"/> changes.</summary>
        public event Action<bool> PressedChanged;

        void Awake()
        {
            box = GetComponent<BoxCollider2D>();
            occluded = new OccludedBoxCollider(box);
            if (!box.isTrigger)
            {
                Debug.LogError($"PressurePlate '{name}' collider must be a trigger. Fixing at runtime; please fix the asset.", this);
                box.isTrigger = true;
            }

            ok = true;
            sheet = GetComponentInParent<Sheet>();
            if (sheet == null) { Debug.LogError($"PressurePlate '{name}' is not under a Sheet.", this); ok = false; }
            else if (sheet.Front != null && transform.IsChildOf(sheet.Front)) { faceRoot = sheet.Front; side = SheetFace.Front; }
            else if (sheet.Back != null && transform.IsChildOf(sheet.Back)) { faceRoot = sheet.Back; side = SheetFace.Back; }
            else { Debug.LogError($"PressurePlate '{name}' must be under the sheet's Front or Back root.", this); ok = false; }

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
            var faceRect = FoldFootprint.FaceLocalRect(box, faceRoot);
            flatRect = side == SheetFace.Front ? faceRect : SheetGeometry.BackToFront(faceRect);
        }

        void OnEnable()
        {
            if (!ok)
                return;
            CollectPressers();
            ShowPressed(ShownPressed);
        }

        /// <summary>What the drawing shows: a fired Latch stays down; a Hold plate follows the press.</summary>
        bool ShownPressed => mode == PlateMode.Latch ? IsLatched : IsPressed;

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
            if (pressed == IsPressed)
                return;
            IsPressed = pressed;

            if (mode == PlateMode.Latch)
            {
                if (pressed && !IsLatched)
                {
                    IsLatched = true;
                    ApplyEffects();
                }
            }
            else if (pressed)
                ApplyEffects();
            else
                RevertEffects();

            ShowPressed(ShownPressed);
            PressedChanged?.Invoke(pressed);
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

        void ShowPressed(bool pressed)
        {
            if (spriteRenderer == null)
                return;
            spriteRenderer.color = pressed ? pressedColor : releasedColor;
        }

        // ----- IFoldOccludee -----

        public Rect FaceLocalFootprint(Transform root) => FoldFootprint.FaceLocalRect(box, root);

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
