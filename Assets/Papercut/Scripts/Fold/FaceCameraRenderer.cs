using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Papercut
{
    /// <summary>
    /// Puts a camera that renders a face (or the Sheet Studio's panes) on the pipeline's second renderer, a copy of
    /// the main 2D renderer without the Camera Sorting Layer Texture. The main renderer copies the screen after the
    /// Default sorting layer for the Sheet Shading warp (2026-10-08); a face camera has no use for that copy, and
    /// sharing it would force an intermediate colour target on every face texture and reallocate the shared copy
    /// every frame as the cameras' sizes alternate.
    /// </summary>
    public static class FaceCameraRenderer
    {
        /// <summary>Index of the face renderer in the URP asset's renderer list (Renderer2D Faces).</summary>
        public const int RendererIndex = 1;

        static bool reportedMissing;

        /// <summary>
        /// Switches <paramref name="camera"/> to the face renderer. If the active pipeline has none, reports it once
        /// and leaves the camera on the default renderer (it still draws, at the cost described above).
        /// </summary>
        public static void Use(Camera camera)
        {
            if (camera == null)
                return;
            if (GraphicsSettings.currentRenderPipeline is not UniversalRenderPipelineAsset asset
                || asset.rendererDataList.Length <= RendererIndex || asset.rendererDataList[RendererIndex] == null)
            {
                if (!reportedMissing)
                {
                    reportedMissing = true;
                    Debug.LogError($"The URP asset has no face renderer at index {RendererIndex} (Renderer2D Faces); face cameras use the default renderer and pay its screen copy.");
                }
                return;
            }
            camera.GetUniversalAdditionalCameraData().SetRenderer(RendererIndex);
        }
    }
}
