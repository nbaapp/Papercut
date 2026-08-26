using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Frames the current Screen: one Sheet fills the view vertically with a margin of Desk above and
    /// below, and neighbouring sheets peek in at the sides.
    /// </summary>
    /// <remarks>
    /// The camera never moves. Changing Screen slides the sheets under it (Bible §7; see
    /// <see cref="DeskSlider"/>), so this component only owns the framing size. It also keeps the
    /// <see cref="FoldLayers"/> out of the culling mask: face content is seen only through each sheet's
    /// fold renderer, which composites it from per-face render textures.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class ScreenCamera : MonoBehaviour
    {
        [SerializeField, Min(0f), Tooltip("Desk visible above and below the framed sheet, in world units.")]
        float verticalMargin = 0.75f;

        Camera cam;

        public Camera Camera => cam != null ? cam : cam = GetComponent<Camera>();

        void Awake()
        {
            ApplyFraming();
        }

        void OnValidate()
        {
            ApplyFraming();
        }

        void ApplyFraming()
        {
            var camera = Camera;
            if (camera == null)
                return;
            camera.orthographic = true;
            camera.orthographicSize = SheetGeometry.Height * 0.5f + verticalMargin;
            if (FoldLayers.Valid)
                camera.cullingMask &= ~FoldLayers.Mask;
        }
    }
}
