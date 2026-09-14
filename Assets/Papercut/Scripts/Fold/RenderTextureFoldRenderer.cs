using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Draws a Sheet by render-texture compositing (Bible decision #2, chosen by Aaron): a camera per face
    /// renders that face's content into a texture; the visible sheet is one mesh per layer of the folded sheet
    /// (<see cref="SheetLayers"/>), each textured from the Front or Back texture through the inverse of the
    /// layer's transform, stacked in order, plus crease lines. Wherever no layer lies the Desk shows through.
    /// </summary>
    /// <remarks>
    /// Face content sits on the <see cref="FoldLayers"/> layers, which the main camera does not render, so a sheet is
    /// visible only through this component. The face cameras are children of the (never moved) face roots and
    /// render each face's authored space. Textures and cameras exist only while the sheet is in view. No readbacks.
    /// Colours are per renderer (a material property block), so the materials only need a texture and a
    /// colour: URP/Unlit, transparent. Each layer's tint follows the state of the fold that last moved it, so a
    /// dragged fold tints only the pieces it lifts. Layer meshes are pooled; each sits at its own z so the
    /// transparent sort draws them bottom to top.
    /// Crease lines are marks on the sheet, not overlays: they are drawn as face content under the Front root and,
    /// mirrored into Back-space, under the Back root, so the textures carry them and a later fold lifts, hides or
    /// brings them around like the rest of the sheet. The Seam border stays an overlay: it marks a position.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Sheet))]
    public sealed class RenderTextureFoldRenderer : MonoBehaviour, IFoldRenderer
    {
        /// <summary>Local z of the bottom layer; each layer above sits <see cref="LayerZStep"/> nearer the camera.</summary>
        const float BaseZ = 0.5f;
        const float LayerZStep = 0.01f;
        /// <summary>Local z of the Seam overlay: in front of every layer that fits below <see cref="BaseZ"/>.</summary>
        const float SeamZ = 0.005f;
        /// <summary>Face-local z of crease lines: in front of every authored face element (surface at 0, art at -0.01, terrain fills at -0.05).</summary>
        const float FaceCreaseZ = -0.1f;
        const float CameraDistance = 5f;
        const float CameraDepth = -100f;
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [Header("Materials")]
        [SerializeField, Tooltip("Material for the Base and Flap meshes (URP/Unlit, transparent). Its _BaseMap is replaced by the face render texture and its _BaseColor by the tint at runtime.")]
        Material faceMaterial;

        [SerializeField, Tooltip("Material for crease lines (URP/Unlit, transparent). Its _BaseColor is replaced by the crease colours at runtime.")]
        Material creaseMaterial;

        [Header("Textures")]
        [SerializeField, Min(8), Tooltip("Resolution of each face's render texture, in pixels per sheet unit (inch).")]
        int pixelsPerUnit = 128;

        [Header("Tints")]
        [SerializeField, Tooltip("Tint of the landed Flap while it is being dragged and could be committed.")]
        Color previewTint = new(1f, 1f, 1f, 0.85f);

        [SerializeField, Tooltip("Tint of the landed Flap while the drag would cover the player.")]
        Color invalidTint = new(1f, 0.35f, 0.35f, 0.85f);

        [SerializeField, Tooltip("Tint of the Flap while it swings back after unfolding.")]
        Color retreatingTint = Color.white;

        [Header("Crease")]
        [SerializeField, Min(0f), Tooltip("Width of a crease line, in sheet units.")]
        float creaseWidth = 0.06f;

        [SerializeField, Tooltip("Colour of the crease of a live fold.")]
        Color creaseColor = new(0.25f, 0.2f, 0.15f, 0.9f);

        [SerializeField, Tooltip("Colour of a crease left behind by an undone fold.")]
        Color rememberedCreaseColor = new(0.25f, 0.2f, 0.15f, 0.35f);

        [Header("Seam")]
        [SerializeField, Min(0f), Tooltip("Width of the border drawn along the Flap's edge (the Seam) while a fold is being dragged, in sheet units.")]
        float seamWidth = 0.08f;

        [SerializeField, Tooltip("Colour of the Seam border while dragging. Shown for valid and invalid previews alike; the Flap tint carries the red.")]
        Color seamColor = new(0.15f, 0.12f, 0.1f, 0.9f);

        Sheet sheet;
        ScreenCamera view;
        Camera frontCamera;
        Camera backCamera;
        RenderTexture frontTexture;
        RenderTexture backTexture;
        MeshPart seamPart;
        readonly List<MeshPart> layerParts = new();
        readonly List<MeshPart> creaseParts = new(4); // [Front live, Front remembered, Back live, Back remembered]
        MaterialPropertyBlock block;
        readonly List<FoldVisual> shownFolds = new();
        readonly List<CreaseMark> shownCreases = new();
        readonly PolygonMeshBuilder builder = new();
        readonly List<FoldEffect> replayEffects = new();
        SheetLayers shownLayers;
        bool inView;
        bool anyBackUp;
        bool reportedTooManyLayers;

        void Awake()
        {
            sheet = GetComponent<Sheet>();
            block = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            view = FindAnyObjectByType<ScreenCamera>();
            if (view == null)
            {
                Debug.LogError($"RenderTextureFoldRenderer on '{name}' found no ScreenCamera; the sheet cannot be drawn.", this);
                enabled = false;
                return;
            }
            if (faceMaterial == null || creaseMaterial == null)
                Debug.LogError($"RenderTextureFoldRenderer on '{name}' is missing a material.", this);

            seamPart = CreateMeshPart("Seam", transform, SeamZ, creaseMaterial, gameObject.layer);
            layerParts.Clear();
            creaseParts.Clear();
            foreach (var face in new[] { SheetFace.Front, SheetFace.Back })
            {
                var root = face == SheetFace.Front ? sheet.Front : sheet.Back;
                if (root == null)
                {
                    creaseParts.Add(null);
                    creaseParts.Add(null);
                    continue; // Sheet already reported the missing root.
                }
                var layer = FoldLayers.Valid ? FoldLayers.LayerOf(face) : root.gameObject.layer;
                creaseParts.Add(CreateMeshPart("Creases", root, FaceCreaseZ, creaseMaterial, layer));
                creaseParts.Add(CreateMeshPart("Remembered Creases", root, FaceCreaseZ, creaseMaterial, layer));
            }
            inView = false;
        }

        void OnDisable()
        {
            ReleaseView();
            seamPart?.Destroy();
            foreach (var part in layerParts)
                part.Destroy();
            layerParts.Clear();
            foreach (var part in creaseParts)
                part?.Destroy();
            creaseParts.Clear();
            seamPart = null;
        }

        void LateUpdate()
        {
            var nowInView = IsInView();
            if (nowInView && !inView)
                AcquireView();
            else if (!nowInView && inView)
                ReleaseView();
            else if (inView && frontTexture != null && frontTexture.width != TextureWidth)
            {
                ReleaseView();
                AcquireView();
            }

            if (!inView)
                return;
            frontCamera.enabled = true;
            backCamera.enabled = anyBackUp;
        }

        public float SurfaceZ(int layerIndex) => BaseZ - Mathf.Max(0, layerIndex) * LayerZStep - LayerZStep * 0.5f;

        public void Draw(in FoldDisplay display)
        {
            shownFolds.Clear();
            shownFolds.AddRange(display.Folds);
            shownCreases.Clear();
            shownCreases.AddRange(display.RememberedCreases);
            shownLayers = display.Layers;
            replayEffects.Clear();
            replayEffects.AddRange(display.Effects);
            if (seamPart == null)
                return;
            BuildMeshes();
        }

        // ----- view gating -----

        /// <summary>True if the sheet can be on screen. No piece ever leaves the sheet rect, so the sheet's own bounds suffice.</summary>
        bool IsInView()
        {
            var cam = view.Camera;
            var halfHeight = cam.orthographicSize;
            var halfWidth = halfHeight * cam.aspect;
            Vector2 centre = cam.transform.position;
            var viewRect = new Rect(centre - new Vector2(halfWidth, halfHeight), new Vector2(halfWidth, halfHeight) * 2f);
            return sheet.Bounds.Overlaps(viewRect);
        }

        int TextureWidth => Mathf.RoundToInt(SheetGeometry.Width * pixelsPerUnit);
        int TextureHeight => Mathf.RoundToInt(SheetGeometry.Height * pixelsPerUnit);

        void AcquireView()
        {
            frontTexture = CreateTexture("Front");
            backTexture = CreateTexture("Back");
            if (frontTexture == null || backTexture == null)
            {
                ReleaseView();
                enabled = false;
                return;
            }
            frontCamera = CreateFaceCamera(sheet.Front, SheetFace.Front, frontTexture);
            backCamera = CreateFaceCamera(sheet.Back, SheetFace.Back, backTexture);
            inView = true;
            BuildMeshes();
        }

        void ReleaseView()
        {
            inView = false;
            if (frontCamera != null) Destroy(frontCamera.gameObject);
            if (backCamera != null) Destroy(backCamera.gameObject);
            frontCamera = backCamera = null;
            if (frontTexture != null) { frontTexture.Release(); Destroy(frontTexture); }
            if (backTexture != null) { backTexture.Release(); Destroy(backTexture); }
            frontTexture = backTexture = null;
            if (seamPart != null)
                BuildMeshes(); // Re-bind the property blocks without the destroyed textures.
        }

        RenderTexture CreateTexture(string face)
        {
            var texture = new RenderTexture(TextureWidth, TextureHeight, 24, RenderTextureFormat.ARGB32) // URP Render Graph requires a depth buffer on camera output textures.
            {
                name = $"{sheet.name} {face}",
                filterMode = FilterMode.Bilinear,
            };
            if (!texture.Create())
            {
                Debug.LogError($"Could not create the {face} render texture ({TextureWidth}x{TextureHeight}) for sheet '{sheet.name}'.", this);
                Destroy(texture);
                return null;
            }
            return texture;
        }

        Camera CreateFaceCamera(Transform faceRoot, SheetFace face, RenderTexture target)
        {
            var go = new GameObject($"{face} Camera");
            go.transform.SetParent(faceRoot, false);
            go.transform.localPosition = new Vector3(0f, 0f, -CameraDistance);
            go.transform.localRotation = Quaternion.identity;
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = SheetGeometry.Height * 0.5f;
            cam.aspect = SheetGeometry.Width / SheetGeometry.Height;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = CameraDistance * 2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.cullingMask = FoldLayers.Valid ? 1 << FoldLayers.LayerOf(face) : 0;
            cam.depth = CameraDepth;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            cam.targetTexture = target;
            cam.enabled = false;
            return cam;
        }

        // ----- meshes -----

        MeshPart CreateMeshPart(string label, Transform parent, float z, Material material, int layer)
        {
            var go = new GameObject(label) { layer = layer };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = new Mesh { name = $"{sheet.name} {label}" };
            filter.sharedMesh.MarkDynamic();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return new MeshPart(filter, renderer);
        }

        void BuildMeshes()
        {
            // The display carries the replayed folds: where every piece of the sheet lies, and what each fold did.
            var stack = shownLayers ?? SheetLayers.Flat;
            anyBackUp = stack.AnyBackUp;

            // One mesh per layer, bottom to top, each textured from its face through the inverse of its transform.
            var layers = stack.Layers;
            var maxLayers = Mathf.FloorToInt((BaseZ - SeamZ) / LayerZStep);
            if (layers.Count > maxLayers && !reportedTooManyLayers)
            {
                reportedTooManyLayers = true;
                Debug.LogError($"Sheet '{sheet.name}' has {layers.Count} layers; only {maxLayers} fit between the Base and the Seam overlay. Upper layers share a depth and may draw in the wrong order.", this);
            }
            while (layerParts.Count < layers.Count)
                layerParts.Add(CreateMeshPart($"Layer {layerParts.Count}", transform, BaseZ, faceMaterial, gameObject.layer));
            for (int k = 0; k < layerParts.Count; k++)
            {
                builder.Clear();
                if (k >= layers.Count)
                {
                    layerParts[k].Apply(builder, block, null, Color.white);
                    continue;
                }
                var layer = layers[k];
                var inverse = layer.ToDesk.Inverse;
                var frontUp = layer.FrontUp;
                builder.AddPolygon(layer.Desk, v =>
                {
                    var original = inverse.Apply(v);
                    return UvOf(frontUp ? original : SheetGeometry.BackToFront(original));
                });
                var tint = layer.IsBase ? Color.white : shownFolds[layer.MovedBy].State switch
                {
                    FoldVisualState.Preview => previewTint,
                    FoldVisualState.PreviewInvalid => invalidTint,
                    FoldVisualState.Retreating => retreatingTint,
                    _ => Color.white,
                };
                layerParts[k].SetLocalZ(BaseZ - Mathf.Min(k, maxLayers) * LayerZStep);
                layerParts[k].Apply(builder, block, frontUp ? frontTexture : backTexture, tint);
            }

            // Creases are marks on both faces of the sheet, in each face's authored space. A live mark is shifted
            // wholly to one side of the fold line (see CreaseMark.Shift) so that the half under the landed Flap is
            // not lost; a remembered mark is drawn centred.
            for (int face = 0; face < 2; face++)
            {
                var which = face == 0 ? SheetFace.Front : SheetFace.Back;

                builder.Clear();
                foreach (var effect in replayEffects)
                {
                    foreach (var mark in effect.CreaseMarks)
                    {
                        if (mark.Face != which)
                            continue;
                        var shift = mark.Shift * (creaseWidth * 0.5f);
                        builder.AddLine(mark.A + shift, mark.B + shift, creaseWidth);
                    }
                }
                creaseParts[face * 2]?.Apply(builder, block, null, creaseColor);

                builder.Clear();
                foreach (var mark in shownCreases)
                {
                    if (mark.Face == which)
                        builder.AddLine(mark.A, mark.B, creaseWidth);
                }
                creaseParts[face * 2 + 1]?.Apply(builder, block, null, rememberedCreaseColor);
            }

            // Seam border: only while a fold is being dragged, so the landed edge is unmistakable.
            builder.Clear();
            for (int i = 0; i < shownFolds.Count; i++)
            {
                var state = shownFolds[i].State;
                if (state != FoldVisualState.Preview && state != FoldVisualState.PreviewInvalid)
                    continue;
                foreach (var (a, b) in replayEffects[i].SeamSegments)
                    builder.AddLine(a, b, seamWidth);
            }
            seamPart.Apply(builder, block, null, seamColor);
        }

        static Vector2 UvOf(Vector2 local) => new(local.x / SheetGeometry.Width + 0.5f, local.y / SheetGeometry.Height + 0.5f);

        /// <summary>One mesh + renderer pair with its own texture and colour.</summary>
        sealed class MeshPart
        {
            readonly MeshFilter filter;
            readonly MeshRenderer renderer;

            public MeshPart(MeshFilter filter, MeshRenderer renderer)
            {
                this.filter = filter;
                this.renderer = renderer;
            }

            public void Apply(PolygonMeshBuilder builder, MaterialPropertyBlock block, Texture texture, Color color)
            {
                builder.Apply(filter.sharedMesh);
                block.Clear();
                if (texture != null)
                    block.SetTexture(BaseMap, texture);
                block.SetColor(BaseColor, color);
                renderer.SetPropertyBlock(block);
            }

            public void SetLocalZ(float z)
            {
                var t = filter.transform;
                var p = t.localPosition;
                t.localPosition = new Vector3(p.x, p.y, z);
            }

            public void Destroy()
            {
                if (filter == null)
                    return;
                Object.Destroy(filter.sharedMesh);
                Object.Destroy(filter.gameObject);
            }
        }
    }
}
