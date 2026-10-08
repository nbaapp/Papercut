using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Papercut.EditorTools
{
    /// <summary>
    /// The fold pane's rendering rig: rebuilds the game's render-texture composite in an editor-owned preview
    /// scene. Two face cameras render the Prefab Stage's Front/Back content into full-sheet textures; one mesh
    /// per layer of the folded sheet (same <see cref="PolygonMeshBuilder"/> geometry, same UV math through each
    /// layer's inverse isometry, same z-step stacking as <c>RenderTextureFoldRenderer</c>) lives in the preview
    /// scene; a composite camera renders that scene into the pane's texture.
    /// </summary>
    public sealed class StudioFoldScene
    {
        // The runtime's depth layout (RenderTextureFoldRenderer.BaseZ/LayerZStep/SeamZ equivalents).
        const float BaseZ = 0.5f;
        const float LayerZStep = 0.01f;
        const float MinLayerZ = 0.005f;
        const float CameraDistance = 5f;
        /// <summary>Editor cap on face-texture resolution, to bound editor VRAM (the game may author higher).</summary>
        const int MaxPixelsPerUnit = 96;

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        Scene previewScene;
        bool sceneCreated;
        Camera frontCamera;
        Camera backCamera;
        Camera compositeCamera;
        RenderTexture frontTexture;
        RenderTexture backTexture;
        RenderTexture paneTexture;
        Material fallbackMaterial;
        MaterialPropertyBlock block;
        readonly List<(GameObject go, MeshFilter filter, MeshRenderer renderer)> layerMeshes = new();
        readonly PolygonMeshBuilder builder = new();
        bool meshesDirty = true;
        bool textureFailed;

        /// <summary>The composite as last rendered; null until the first render.</summary>
        public RenderTexture Texture => paneTexture;

        /// <summary>True when the stack has more layers than the z-steps can order (upper layers share a depth).</summary>
        public bool TooManyLayers { get; private set; }

        /// <summary>Call when the model changed so the next render rebuilds the layer meshes.</summary>
        public void MarkDirty() => meshesDirty = true;

        public void Dispose()
        {
            foreach (var (go, filter, _) in layerMeshes)
            {
                if (filter != null && filter.sharedMesh != null)
                    Object.DestroyImmediate(filter.sharedMesh);
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            layerMeshes.Clear();
            DestroyCamera(ref frontCamera);
            DestroyCamera(ref backCamera);
            DestroyCamera(ref compositeCamera);
            ReleaseTexture(ref frontTexture);
            ReleaseTexture(ref backTexture);
            ReleaseTexture(ref paneTexture);
            if (fallbackMaterial != null)
                Object.DestroyImmediate(fallbackMaterial);
            fallbackMaterial = null;
            if (sceneCreated)
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
                sceneCreated = false;
            }
            textureFailed = false;
            meshesDirty = true;
        }

        /// <summary>
        /// Renders the composite for the current model state into <see cref="Texture"/>. Returns false (for a
        /// help-box, never a silent blank) when resources cannot be created or the render request is unsupported.
        /// </summary>
        public bool Render(PrefabStage stage, StudioFoldModel model, in StudioFoldSettings settings, in PaneView view)
        {
            if (stage == null || !FoldLayers.Valid)
                return false;
            if (!EnsureResources(settings, view))
                return false;

            // Aim the face cameras at the stage every render — they may have just been created, and the
            // first composite must never sample the main scenes' content (code review S1).
            frontCamera.scene = stage.scene;
            backCamera.scene = stage.scene;

            // Face passes first, composite after: the composite samples the face textures this same repaint.
            if (!Submit(frontCamera, frontTexture) || !Submit(backCamera, backTexture))
                return false;

            if (meshesDirty)
            {
                RebuildLayerMeshes(model, settings);
                meshesDirty = false;
            }

            compositeCamera.transform.position = new Vector3(view.Centre.x, view.Centre.y, -CameraDistance);
            compositeCamera.orthographicSize = view.PaneRect.height / (2f * view.Zoom);
            compositeCamera.aspect = view.PaneRect.width / view.PaneRect.height;
            return Submit(compositeCamera, paneTexture);
        }

        bool Submit(Camera camera, RenderTexture target)
        {
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            if (!RenderPipeline.SupportsRenderRequest(camera, request))
                return false;
            RenderPipeline.SubmitRenderRequest(camera, request);
            return true;
        }

        // ----- Resources -----

        bool EnsureResources(in StudioFoldSettings settings, in PaneView view)
        {
            if (textureFailed)
                return false;

            if (!sceneCreated)
            {
                previewScene = EditorSceneManager.NewPreviewScene();
                sceneCreated = true;
            }
            block ??= new MaterialPropertyBlock();

            var ppu = Mathf.Clamp(settings.PixelsPerUnit, 8, MaxPixelsPerUnit);
            var faceWidth = Mathf.RoundToInt(SheetGeometry.Width * ppu);
            var faceHeight = Mathf.RoundToInt(SheetGeometry.Height * ppu);
            if (!EnsureTexture(ref frontTexture, faceWidth, faceHeight, "Fold Preview Front")
                || !EnsureTexture(ref backTexture, faceWidth, faceHeight, "Fold Preview Back"))
                return false;

            var paneWidth = Mathf.Max(1, (int)view.PaneRect.width);
            var paneHeight = Mathf.Max(1, (int)view.PaneRect.height);
            if (!EnsureTexture(ref paneTexture, paneWidth, paneHeight, "Fold Preview Pane"))
                return false;

            if (frontCamera == null)
                frontCamera = CreateFaceCamera(SheetFace.Front);
            if (backCamera == null)
                backCamera = CreateFaceCamera(SheetFace.Back);
            if (compositeCamera == null)
            {
                compositeCamera = CreateCamera("Fold Preview Composite Camera");
                compositeCamera.backgroundColor = settings.PaneBackground;
                compositeCamera.cullingMask = -1;
                compositeCamera.scene = previewScene;
            }
            return true;
        }

        bool EnsureTexture(ref RenderTexture texture, int width, int height, string name)
        {
            if (texture != null && (texture.width != width || texture.height != height))
                ReleaseTexture(ref texture);
            if (texture != null)
                return true;
            texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = name,
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
            };
            if (!texture.Create())
            {
                ReleaseTexture(ref texture);
                textureFailed = true;
                return false;
            }
            return true;
        }

        Camera CreateFaceCamera(SheetFace face)
        {
            var camera = CreateCamera($"Fold Preview {face} Camera");
            camera.backgroundColor = Color.clear;
            camera.orthographicSize = SheetGeometry.Height * 0.5f;
            camera.aspect = SheetGeometry.Width / SheetGeometry.Height;
            camera.cullingMask = 1 << FoldLayers.LayerOf(face);
            camera.transform.position = new Vector3(0f, 0f, -CameraDistance);
            return camera;
        }

        Camera CreateCamera(string name)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            FaceCameraRenderer.Use(camera); // like the game's face cameras: no screen copy
            camera.orthographic = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = CameraDistance * 2f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.useOcclusionCulling = false;
            return camera;
        }

        static void DestroyCamera(ref Camera camera)
        {
            if (camera != null)
                Object.DestroyImmediate(camera.gameObject);
            camera = null;
        }

        static void ReleaseTexture(ref RenderTexture texture)
        {
            if (texture != null)
            {
                texture.Release();
                Object.DestroyImmediate(texture);
            }
            texture = null;
        }

        // ----- Layer meshes -----

        void RebuildLayerMeshes(StudioFoldModel model, in StudioFoldSettings settings)
        {
            var stack = model.DisplayLayers;
            var layers = stack.Layers;
            var committedCount = model.Folds.Count;
            var maxLayers = Mathf.FloorToInt((BaseZ - MinLayerZ) / LayerZStep);
            TooManyLayers = layers.Count > maxLayers;

            while (layerMeshes.Count < layers.Count)
                layerMeshes.Add(CreateLayerMesh(layerMeshes.Count, settings));

            for (int k = 0; k < layerMeshes.Count; k++)
            {
                var (go, filter, renderer) = layerMeshes[k];
                builder.Clear();
                if (k >= layers.Count)
                {
                    Apply(filter, renderer, null, Color.white);
                    continue;
                }
                // Live-read: the face material may have been re-assigned on the sheet's renderer since this
                // mesh was created (settings are re-read on every object change).
                renderer.sharedMaterial = settings.FaceMaterial != null ? settings.FaceMaterial : FallbackMaterial;
                var layer = layers[k];
                var inverse = layer.ToDesk.Inverse;
                var frontUp = layer.FrontUp;
                builder.AddPolygon(layer.Desk, v =>
                {
                    var original = inverse.Apply(v);
                    return UvOf(frontUp ? original : SheetGeometry.BackToFront(original));
                });
                // Same tint rule as the runtime: base and committed layers white, preview layers tinted.
                var tint = Color.white;
                if (!layer.IsBase && layer.MovedBy >= committedCount)
                    tint = model.PreviewValid ? settings.PreviewTint : settings.InvalidTint;
                go.transform.position = new Vector3(0f, 0f, BaseZ - Mathf.Min(k, maxLayers) * LayerZStep);
                Apply(filter, renderer, frontUp ? frontTexture : backTexture, tint);
            }
        }

        (GameObject, MeshFilter, MeshRenderer) CreateLayerMesh(int index, in StudioFoldSettings settings)
        {
            var go = new GameObject($"Fold Preview Layer {index}") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(go, previewScene);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = new Mesh { name = go.name, hideFlags = HideFlags.HideAndDontSave };
            filter.sharedMesh.MarkDynamic();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = settings.FaceMaterial != null ? settings.FaceMaterial : FallbackMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return (go, filter, renderer);
        }

        void Apply(MeshFilter filter, MeshRenderer renderer, Texture texture, Color color)
        {
            builder.Apply(filter.sharedMesh);
            block.Clear();
            if (texture != null)
                block.SetTexture(BaseMap, texture);
            block.SetColor(BaseColor, color);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// The rare fallback when the sheet's renderer has no face material authored: an editor-created
        /// URP/Unlit transparent material (there is no meaningful default Material).
        /// </summary>
        Material FallbackMaterial
        {
            get
            {
                if (fallbackMaterial != null)
                    return fallbackMaterial;
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                fallbackMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                fallbackMaterial.SetFloat("_Surface", 1f); // Transparent.
                fallbackMaterial.SetFloat("_ZWrite", 0f);
                fallbackMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                fallbackMaterial.renderQueue = (int)RenderQueue.Transparent;
                return fallbackMaterial;
            }
        }

        static Vector2 UvOf(Vector2 local) => new(local.x / SheetGeometry.Width + 0.5f, local.y / SheetGeometry.Height + 0.5f);
    }
}
