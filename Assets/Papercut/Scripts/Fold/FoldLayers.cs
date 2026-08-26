using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The two layers face content lives on so the per-face cameras can render one face each and the main
    /// camera renders neither (it sees only the composited sheets). Resolved by name on first use.
    /// </summary>
    public static class FoldLayers
    {
        public const string FrontLayerName = "SheetFront";
        public const string BackLayerName = "SheetBack";

        static bool resolved;
        static int front = -1;
        static int back = -1;

        public static int Front { get { Resolve(); return front; } }
        public static int Back { get { Resolve(); return back; } }

        /// <summary>False (with one error logged) if either layer is missing from Tags &amp; Layers.</summary>
        public static bool Valid { get { Resolve(); return front >= 0 && back >= 0; } }

        /// <summary>Bit mask of both layers; 0 if they are missing.</summary>
        public static int Mask => Valid ? (1 << front) | (1 << back) : 0;

        public static int LayerOf(SheetFace face) => face == SheetFace.Front ? Front : Back;

        static void Resolve()
        {
            if (resolved)
                return;
            resolved = true;
            front = LayerMask.NameToLayer(FrontLayerName);
            back = LayerMask.NameToLayer(BackLayerName);
            if (front < 0 || back < 0)
                Debug.LogError($"Layers '{FrontLayerName}' and '{BackLayerName}' must exist (Project Settings > Tags and Layers). " +
                               "Without them folding renders wrongly: the main camera draws face content directly.");
        }
    }
}
