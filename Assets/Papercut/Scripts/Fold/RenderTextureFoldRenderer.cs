using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Papercut
{
    /// <summary>
    /// Draws a Sheet by render-texture compositing (Bible decision #2, chosen by Aaron): a camera per face
    /// renders that face's content into a texture; the visible sheet is one mesh per layer of the folded sheet
    /// (<see cref="SheetLayers"/>), each textured from the Front or Back texture through the inverse of the
    /// layer's transform, stacked in order. Wherever no layer lies the Desk shows through.
    /// </summary>
    /// <remarks>
    /// Face content sits on the <see cref="FoldLayers"/> layers, which the main camera does not render, so a sheet is
    /// visible only through this component. The face cameras are children of the (never moved) face roots and
    /// render each face's authored space. Textures and cameras exist only while the sheet is in view. No readbacks.
    /// Colours are per renderer (a material property block): the Seam material is URP/Unlit, transparent; the face
    /// material is Papercut/Paper Face (face texture × tint). Each layer's tint follows the state of the fold that
    /// last moved it, so a dragged fold tints only the pieces it lifts. Layer meshes are pooled; each sits at its own
    /// z so the transparent sort draws them bottom to top.
    /// The paper's shading — its grain normal map and the crease normal maps — is a second pass, Papercut/Sheet
    /// Shading, over everything the main camera has drawn on the sheet: the faces, and the player, blocks and
    /// universal walls on them (Aaron, 2026-10-07: "They live on the paper too"). It redraws each pixel from the 2D
    /// renderer's copy of the screen after the Default sorting layer, sampled a little way off - drawn in toward each
    /// crease by a smooth pinch and moved by the grain's broad wrinkles - so what is on the sheet bends with its
    /// creases and wrinkles as well as taking their light (Aaron, 2026-10-08; <see cref="SheetWarp"/>). The shading parts sit on their own sorting layer, after Default. Each layer part carries a
    /// shading child that shares its mesh, so the shading covers exactly that piece and uses its face's texture
    /// coordinates: the grain and creases ride the paper through folds, and their normals are carried into Desk space
    /// by the piece's pose, so the light stays fixed to the Desk and a landed Flap is lit from the same side as the
    /// Base. The shading children sort on the Sheet Shading sorting layer, topmost layer first, and the shader's
    /// stencil bit lets only the topmost piece redraw a pixel; each is held inside its outline (its layer's edges and
    /// those of every layer above), so nothing is pulled across an edge. Creases are marks on the sheet, not overlays: each is a segment
    /// in one face's authored space, shaded through a crease normal map (the inside map on the face that was up when
    /// the fold was made, the outside map on the other — Aaron, 2026-10-07), so a later fold lifts, hides or brings a
    /// crease around like the rest of the face (<see cref="CreaseShading"/>). The Seam border is an overlay (it
    /// marks a position), drawn under the player like before, and shaded and warped like everything on the sheet.
    /// Anything the main camera draws that should lie on the paper must stay on the Default sorting layer. The face
    /// cameras use the pipeline's second renderer, without the screen copy (<see cref="FaceCameraRenderer"/>).
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
        const float CameraDistance = 5f;
        const float CameraDepth = -100f;
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int GrainMap = Shader.PropertyToID("_GrainMap");
        static readonly int GrainFrame = Shader.PropertyToID("_GrainFrame");
        static readonly int GrainStrength = Shader.PropertyToID("_GrainStrength");
        static readonly int GrainAmbient = Shader.PropertyToID("_GrainAmbient");
        static readonly int LightDir = Shader.PropertyToID("_LightDir");
        static readonly int SheetSize = Shader.PropertyToID("_SheetSize");
        static readonly int CreaseInsideMap = Shader.PropertyToID("_CreaseInsideMap");
        static readonly int CreaseOutsideMap = Shader.PropertyToID("_CreaseOutsideMap");
        static readonly int CreaseCount = Shader.PropertyToID("_CreaseCount");
        static readonly int CreaseLines = Shader.PropertyToID("_CreaseLines");
        static readonly int CreaseInfo = Shader.PropertyToID("_CreaseInfo");
        static readonly int CreaseLineV = Shader.PropertyToID("_CreaseLineV");
        static readonly int CreaseFlip = Shader.PropertyToID("_CreaseFlip");
        static readonly int CreaseMapWidth = Shader.PropertyToID("_CreaseMapWidth");
        static readonly int CreaseHalfWidth = Shader.PropertyToID("_CreaseHalfWidth");
        static readonly int CreaseFadeStart = Shader.PropertyToID("_CreaseFadeStart");
        static readonly int CreaseStrength = Shader.PropertyToID("_CreaseStrength");
        static readonly int RememberedCreaseStrength = Shader.PropertyToID("_RememberedCreaseStrength");

        /// <summary>
        /// Sorting order of the topmost layer's shading part; a part k layers below the top sorts at this + k, so the
        /// topmost draws first and its stencil claims the pixel (<see cref="ShadingOrderOf"/>). The parts' own sorting
        /// layer (<see cref="ShadingSortingLayerName"/>) puts them after everything on Default.
        /// </summary>
        const int ShadingSortingOrder = 1;

        /// <summary>The sorting layer of the shading parts: after Default, whose end the 2D renderer copies the screen at.</summary>
        public const string ShadingSortingLayerName = "Sheet Shading";

        static readonly int GrainWarp = Shader.PropertyToID("_GrainWarp");
        static readonly int GrainWarpMip = Shader.PropertyToID("_GrainWarpMip");
        static readonly int CreaseInsidePinch = Shader.PropertyToID("_CreaseInsidePinch");
        static readonly int CreaseOutsidePinch = Shader.PropertyToID("_CreaseOutsidePinch");
        static readonly int RememberedCreasePinch = Shader.PropertyToID("_RememberedCreasePinch");
        static readonly int OutlineCount = Shader.PropertyToID("_OutlineCount");
        static readonly int Outline = Shader.PropertyToID("_Outline");

        /// <summary>
        /// The id of the <see cref="ShadingSortingLayerName"/> sorting layer, if it exists, is not Default, and comes
        /// after Default (so the screen copy taken at the end of Default holds everything on the sheet).
        /// </summary>
        public static bool TryGetShadingSortingLayer(out int id)
        {
            id = 0;
            var layers = SortingLayer.layers;
            var defaultIndex = -1;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].id == 0)
                    defaultIndex = i;
                if (layers[i].name == ShadingSortingLayerName)
                {
                    if (layers[i].id == 0 || defaultIndex < 0)
                        return false;
                    id = layers[i].id;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Sorting order of layer <paramref name="layerIndex"/>'s shading part (0 = bottom) in a stack of
        /// <paramref name="layerCount"/>: topmost first, so the stencil lets only the topmost piece shade a pixel.
        /// </summary>
        public static int ShadingOrderOf(int layerIndex, int layerCount) => ShadingSortingOrder + (layerCount - 1 - layerIndex);

        /// <summary>Every material property the face material is given, so a test can check each exists on Papercut/Paper Face.</summary>
        public static readonly string[] FaceShaderProperties = { "_BaseMap", "_BaseColor" };

        /// <summary>Every material property (not array) the shading parts are given, so a test can check each exists on Papercut/Sheet Shading.</summary>
        public static readonly string[] ShadingShaderProperties =
        {
            "_GrainMap", "_GrainFrame", "_GrainStrength", "_GrainAmbient", "_LightDir", "_SheetSize",
            "_CreaseInsideMap", "_CreaseOutsideMap", "_CreaseCount", "_CreaseLineV", "_CreaseFlip", "_CreaseMapWidth",
            "_CreaseHalfWidth", "_CreaseFadeStart", "_CreaseStrength", "_RememberedCreaseStrength",
            "_GrainWarp", "_GrainWarpMip", "_CreaseInsidePinch", "_CreaseOutsidePinch", "_RememberedCreasePinch", "_OutlineCount",
        };
        static readonly Vector4 IdentityFrame = new(1f, 0f, 0f, 1f);

        [Header("Materials")]
        [SerializeField, Tooltip("Material for the Base and Flap meshes (Papercut/Paper Face, transparent). Its _BaseMap is replaced by the face render texture and its _BaseColor by the tint at runtime.")]
        Material faceMaterial;

        [SerializeField, Tooltip("Material for the sheet's shading and warp (Papercut/Sheet Shading): redraws everything on the sheet - its drawings, the player, blocks - bent by its creases and grain and lit by them. Its properties are replaced by the Paper grain, Crease and Warp values at runtime. None = no shading or warp (an error).")]
        Material shadingMaterial;

        [SerializeField, FormerlySerializedAs("creaseMaterial"), Tooltip("Material for the Seam border (URP/Unlit, transparent). Its _BaseColor is replaced by the Seam colour at runtime.")]
        Material seamMaterial;

        [Header("Textures")]
        [SerializeField, Min(8), Tooltip("Resolution of each face's render texture, in pixels per sheet unit (inch).")]
        int pixelsPerUnit = 128;

        [Header("Paper grain")]
        [SerializeField, Tooltip("Normal map of the Front's paper surface, stretched over the whole sheet and shades everything on a Front-up piece - what is drawn on the Front, and the player and blocks on it. None = flat paper.")]
        Texture2D frontGrain;

        [SerializeField, Tooltip("Normal map of the Back's paper surface, authored in Back-space like all Back content (the sheet turned over about its vertical edge). None = flat paper.")]
        Texture2D backGrain;

        [SerializeField, Tooltip("Direction toward the light that shades the grain, in Desk space (x right, y up, z toward the camera). Fixed to the Desk, so a landed Flap is lit from the same side as the Base.")]
        Vector3 grainLight = new(-0.5f, 0.6f, 1f);

        [SerializeField, Range(0f, 4f), Tooltip("How pronounced the grain's bumps are. 0 = flat paper, 1 = the normal map as authored.")]
        float grainStrength = 1f;

        [SerializeField, Range(0f, 1f), Tooltip("Light reaching the shadowed side of a bump: 0 = a fully shadowed bump goes black, 1 = no shading at all.")]
        float grainAmbient = 0.6f;

        [Header("Tints")]
        [SerializeField, Tooltip("Tint of the landed Flap while it is being dragged and could be committed.")]
        Color previewTint = new(1f, 1f, 1f, 0.85f);

        [SerializeField, Tooltip("Tint of the landed Flap while the drag would cover the player.")]
        Color invalidTint = new(1f, 0.35f, 0.35f, 0.85f);

        [SerializeField, Tooltip("Tint of the Flap while it swings back after unfolding.")]
        Color retreatingTint = Color.white;

        [Header("Crease")]
        [SerializeField, Tooltip("Normal map of a crease on the face that was up when the fold was made (the inside of the fold): an image of a whole sheet with one horizontal crease across it, laid along every crease and shaded like the grain. None = no shading on that side of a crease.")]
        Texture2D creaseInsideMap;

        [SerializeField, Tooltip("Normal map of a crease on the face that was down when the fold was made (the outside of the fold). Same layout as the inside map.")]
        Texture2D creaseOutsideMap;

        [SerializeField, Range(0f, 1f), Tooltip("Height of the crease line in the inside map's image (0 = bottom, 1 = top). This row is laid on the fold line.")]
        float creaseInsideLine = 0.5f;

        [SerializeField, Range(0f, 1f), Tooltip("Height of the crease line in the outside map's image (0 = bottom, 1 = top).")]
        float creaseOutsideLine = 0.5f;

        [SerializeField, Tooltip("Off: the top of the inside map's image faces the side of the crease that lifted. On: the image is turned half round.")]
        bool flipCreaseInside;

        [SerializeField, Tooltip("Off: the top of the outside map's image faces the side of the crease that lifted. On: the image is turned half round.")]
        bool flipCreaseOutside;

        [SerializeField, Min(0.1f), Tooltip("Sheet units the crease maps' image width spans along a crease (11 = the image is a whole sheet wide). Across the crease it spans the same sheet's height, as a picture of a whole sheet.")]
        float creaseMapWidth = SheetGeometry.Width;

        [SerializeField, Min(0.01f), Tooltip("How far either side of a crease its map is used, in sheet units.")]
        float creaseHalfWidth = 0.5f;

        [SerializeField, Range(0f, 0.99f), Tooltip("Where across the band a crease starts fading out: 0 = fades from its line, 0.99 = a hard edge at Half Width.")]
        float creaseFadeStart = 0.5f;

        [SerializeField, Range(0f, 4f), Tooltip("How pronounced the crease of a live fold is. 0 = invisible, 1 = the map as authored.")]
        float creaseStrength = 1f;

        [SerializeField, Range(0f, 4f), Tooltip("How pronounced a crease left behind by an undone fold is.")]
        float rememberedCreaseStrength = 0.4f;

        [Header("Warp")]
        [SerializeField, Range(SheetWarp.MinPinch, SheetWarp.MaxPinch), Tooltip("How much a crease on the inside of its fold draws what is on the sheet (its drawings, the player, blocks) in toward it, across Crease Half Width. 1 = up to about 15% of the half width; 0 = no bend; negative bulges out instead.")]
        float creaseInsidePinch = 1f;

        [SerializeField, Range(SheetWarp.MinPinch, SheetWarp.MaxPinch), Tooltip("The same for a crease on the outside of its fold.")]
        float creaseOutsidePinch = 1f;

        [SerializeField, Range(0f, 1f), Tooltip("Multiplier on the pinch of a crease left behind by an undone fold. 1 = as much as a live crease.")]
        float rememberedCreasePinch = 0.4f;

        [SerializeField, Range(-2f, 2f), Tooltip("How far the grain's broad wrinkles move what is drawn on the sheet, in sheet units per unit of their slope (the map blurred by Grain Warp Blur, less its average lean). 0 = none; negative = the other way.")]
        float grainWarp = 0.6f;

        [SerializeField, Range(0f, 8f), Tooltip("How blurred the grain is before it moves things (a mip level): higher follows only the broadest wrinkles, lower follows finer ones and starts to smear.")]
        float grainWarpBlur = 4f;

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
        readonly Vector4[] frontCreaseLines = new Vector4[CreaseShading.MaxCreases];
        readonly Vector4[] frontCreaseInfo = new Vector4[CreaseShading.MaxCreases];
        readonly Vector4[] backCreaseLines = new Vector4[CreaseShading.MaxCreases];
        readonly Vector4[] backCreaseInfo = new Vector4[CreaseShading.MaxCreases];
        int frontCreaseCount;
        int backCreaseCount;
        MaterialPropertyBlock block;
        readonly List<FoldVisual> shownFolds = new();
        readonly List<CreaseMark> shownCreases = new();
        readonly PolygonMeshBuilder builder = new();
        readonly List<FoldEffect> replayEffects = new();
        SheetLayers shownLayers;
        ShadingSettings boundShading;
        bool inView;
        bool anyBackUp;
        bool reportedTooManyLayers;
        bool reportedTooManyCreases;
        bool reportedTooManyEdges;
        int shadingSortingLayer;
        bool hasShadingSortingLayer;

        /// <summary>The Paper grain and Crease values as last bound to the shading parts, so Inspector edits in Play Mode take effect live.</summary>
        readonly struct ShadingSettings : System.IEquatable<ShadingSettings>
        {
            public readonly Texture2D FrontGrain;
            public readonly Texture2D BackGrain;
            public readonly Vector3 Light;
            public readonly float Strength;
            public readonly float Ambient;
            public readonly Texture2D InsideMap;
            public readonly Texture2D OutsideMap;
            public readonly Vector4 LineV;
            public readonly Vector4 Flip;
            public readonly Vector4 Crease; // map width, half width, fade start, unused
            public readonly Vector2 CreaseStrengths; // live, remembered
            public readonly Vector4 Warps; // grain, crease inside pinch, crease outside pinch, remembered multiplier
            public readonly float WarpBlur;

            public ShadingSettings(Texture2D frontGrain, Texture2D backGrain, Vector3 light, float strength, float ambient,
                Texture2D insideMap, Texture2D outsideMap, Vector4 lineV, Vector4 flip, Vector4 crease, Vector2 creaseStrengths, Vector4 warps, float warpBlur)
            {
                Warps = warps;
                WarpBlur = warpBlur;
                FrontGrain = frontGrain;
                BackGrain = backGrain;
                Light = light;
                Strength = strength;
                Ambient = ambient;
                InsideMap = insideMap;
                OutsideMap = outsideMap;
                LineV = lineV;
                Flip = flip;
                Crease = crease;
                CreaseStrengths = creaseStrengths;
            }

            public bool Equals(ShadingSettings other)
                => FrontGrain == other.FrontGrain && BackGrain == other.BackGrain && Light == other.Light && Strength == other.Strength
                   && Ambient == other.Ambient && InsideMap == other.InsideMap && OutsideMap == other.OutsideMap && LineV == other.LineV
                   && Flip == other.Flip && Crease == other.Crease && CreaseStrengths == other.CreaseStrengths && Warps == other.Warps && WarpBlur == other.WarpBlur;

            public override bool Equals(object obj) => obj is ShadingSettings other && Equals(other);
            public override int GetHashCode()
                => System.HashCode.Combine(FrontGrain, BackGrain, Light, Strength, Ambient, InsideMap, OutsideMap, System.HashCode.Combine(LineV, Flip, Crease, CreaseStrengths, Warps, WarpBlur));
        }

        ShadingSettings CurrentShading => new(frontGrain, backGrain, grainLight, grainStrength, grainAmbient,
            creaseInsideMap, creaseOutsideMap,
            new Vector4(creaseInsideLine, creaseOutsideLine, 0f, 0f),
            new Vector4(flipCreaseInside ? -1f : 1f, flipCreaseOutside ? -1f : 1f, 0f, 0f),
            new Vector4(creaseMapWidth, creaseHalfWidth, creaseFadeStart, 0f),
            new Vector2(creaseStrength, rememberedCreaseStrength),
            new Vector4(grainWarp, creaseInsidePinch, creaseOutsidePinch, rememberedCreasePinch), grainWarpBlur);

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
            if (faceMaterial == null || seamMaterial == null)
                Debug.LogError($"RenderTextureFoldRenderer on '{name}' is missing a material.", this);
            if (shadingMaterial == null)
                Debug.LogError($"RenderTextureFoldRenderer on '{name}' has no shading material; the sheet is drawn without its grain and creases.", this);
            hasShadingSortingLayer = TryGetShadingSortingLayer(out shadingSortingLayer);
            if (shadingMaterial != null && !hasShadingSortingLayer)
                Debug.LogError($"RenderTextureFoldRenderer on '{name}': there is no '{ShadingSortingLayerName}' sorting layer after Default; the sheet is drawn without its grain, creases and warp.", this);

            seamPart = CreateMeshPart("Seam", transform, SeamZ, seamMaterial, gameObject.layer);
            layerParts.Clear();
            inView = false;
        }

        void OnDisable()
        {
            ReleaseView();
            seamPart?.Destroy();
            foreach (var part in layerParts)
                part.Destroy();
            layerParts.Clear();
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

            // The grain and crease values are Inspector tunables read live: rebind the shading blocks when they change.
            if (!CurrentShading.Equals(boundShading))
            {
                foreach (var part in layerParts)
                    BindShading(part);
            }
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
            FaceCameraRenderer.Use(cam); // no screen copy: that is the main camera's, for the warp
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
            PackCreases();

            // One mesh per layer, bottom to top, each textured from its face through the inverse of its transform.
            var layers = stack.Layers;
            var maxLayers = Mathf.FloorToInt((BaseZ - SeamZ) / LayerZStep);
            if (layers.Count > maxLayers && !reportedTooManyLayers)
            {
                reportedTooManyLayers = true;
                Debug.LogError($"Sheet '{sheet.name}' has {layers.Count} layers; only {maxLayers} fit between the Base and the Seam overlay. Upper layers share a depth and may draw in the wrong order.", this);
            }
            while (layerParts.Count < layers.Count)
            {
                var part = CreateMeshPart($"Layer {layerParts.Count}", transform, BaseZ, faceMaterial, gameObject.layer);
                if (shadingMaterial != null && hasShadingSortingLayer)
                    part.AddShading(shadingMaterial, shadingSortingLayer);
                layerParts.Add(part);
            }
            for (int k = 0; k < layerParts.Count; k++)
            {
                builder.Clear();
                if (k >= layers.Count)
                {
                    layerParts[k].SetLayer(builder, null, Color.white, true, IdentityFrame);
                    layerParts[k].SetShadingOrder(ShadingSortingOrder); // empty mesh: draws nothing
                    layerParts[k].ClearOutline();
                    BindFace(layerParts[k]);
                    BindShading(layerParts[k]);
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
                layerParts[k].SetLayer(builder, frontUp ? frontTexture : backTexture, tint, frontUp, GrainFrameOf(layer));
                layerParts[k].SetShadingOrder(ShadingOrderOf(k, layers.Count));
                if (layerParts[k].HasShading && !layerParts[k].PackOutline(stack, k) && !reportedTooManyEdges
                    && (Application.isEditor || Debug.isDebugBuild))
                {
                    reportedTooManyEdges = true;
                    Debug.LogWarning($"Sheet '{sheet.name}' has a layer under more than {SheetWarp.MaxOutlineSegments} fold edges; that part of the sheet is shaded but not warped.", this);
                }
                BindFace(layerParts[k]);
                BindShading(layerParts[k]);
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

        /// <summary>
        /// Packs each face's creases (the live folds' marks, then the remembered ones) for the layer blocks. Past the
        /// shader's capacity the oldest remembered creases are not drawn - reported once, in the editor and
        /// development builds, since folding and unfolding many times on one sheet is ordinary play.
        /// </summary>
        void PackCreases()
        {
            frontCreaseCount = CreaseShading.Pack(replayEffects, shownCreases, SheetFace.Front, frontCreaseLines, frontCreaseInfo, out var frontTruncated);
            backCreaseCount = CreaseShading.Pack(replayEffects, shownCreases, SheetFace.Back, backCreaseLines, backCreaseInfo, out var backTruncated);
            if ((frontTruncated || backTruncated) && !reportedTooManyCreases && (Application.isEditor || Debug.isDebugBuild))
            {
                reportedTooManyCreases = true;
                Debug.LogWarning($"Sheet '{sheet.name}' has more than {CreaseShading.MaxCreases} creases on a face; the oldest remembered creases are not drawn until the sheet resets.", this);
            }
        }

        static Vector2 UvOf(Vector2 local) => new(local.x / SheetGeometry.Width + 0.5f, local.y / SheetGeometry.Height + 0.5f);

        /// <summary>
        /// The linear part of the map from the layer's up face's authored space to Desk space, as the shader's
        /// _GrainFrame (columns: the images of the x and y axes). A Front-up piece's grain is authored in Front-space,
        /// which the pose maps to the Desk directly; a Back-up piece's is authored in Back-space, which
        /// <see cref="SheetGeometry.BackToFront"/> (a mirror in x) takes to Front-space first.
        /// </summary>
        static Vector4 GrainFrameOf(SheetLayers.Layer layer)
        {
            var pose = layer.ToDesk;
            var ax = layer.FrontUp ? pose.Ax : -pose.Ax;
            return new Vector4(ax.x, ax.y, pose.Ay.x, pose.Ay.y);
        }

        /// <summary>Binds a layer part's face texture and tint.</summary>
        void BindFace(MeshPart part)
        {
            block.Clear();
            if (part.FaceTexture != null)
                block.SetTexture(BaseMap, part.FaceTexture);
            block.SetColor(BaseColor, part.Tint);
            part.SetBlock(block);
        }

        /// <summary>Binds a layer part's grain and crease shading, reading the Paper grain and Crease tunables live.</summary>
        void BindShading(MeshPart part)
        {
            boundShading = CurrentShading;
            if (!part.HasShading)
                return;
            block.Clear();
            var grain = part.FrontUp ? frontGrain : backGrain;
            if (grain != null)
                block.SetTexture(GrainMap, grain);
            block.SetFloat(GrainStrength, grain != null ? grainStrength : 0f);
            block.SetFloat(GrainAmbient, grainAmbient);
            block.SetVector(LightDir, grainLight.sqrMagnitude > 1e-6f ? grainLight : Vector3.forward);
            block.SetVector(GrainFrame, part.GrainFrame);
            block.SetVector(SheetSize, new Vector4(SheetGeometry.Width, SheetGeometry.Height, 0f, 0f));

            // Creases: the arrays of the face this piece shows. Always the full-length arrays: a block fixes an
            // array's size the first time it is set.
            var anyMap = creaseInsideMap != null || creaseOutsideMap != null;
            if (creaseInsideMap != null)
                block.SetTexture(CreaseInsideMap, creaseInsideMap);
            if (creaseOutsideMap != null)
                block.SetTexture(CreaseOutsideMap, creaseOutsideMap);
            block.SetFloat(CreaseCount, anyMap ? (part.FrontUp ? frontCreaseCount : backCreaseCount) : 0);
            block.SetVectorArray(CreaseLines, part.FrontUp ? frontCreaseLines : backCreaseLines);
            block.SetVectorArray(CreaseInfo, part.FrontUp ? frontCreaseInfo : backCreaseInfo);
            block.SetVector(CreaseLineV, new Vector4(creaseInsideLine, creaseOutsideLine, 0f, 0f));
            block.SetVector(CreaseFlip, new Vector4(flipCreaseInside ? -1f : 1f, flipCreaseOutside ? -1f : 1f, 0f, 0f));
            block.SetFloat(CreaseMapWidth, creaseMapWidth);
            block.SetFloat(CreaseHalfWidth, creaseHalfWidth);
            block.SetFloat(CreaseFadeStart, creaseFadeStart);
            block.SetFloat(CreaseStrength, creaseStrength);
            block.SetFloat(RememberedCreaseStrength, rememberedCreaseStrength);
            block.SetFloat(GrainWarp, grainWarp);
            block.SetFloat(GrainWarpMip, grainWarpBlur);
            block.SetFloat(CreaseInsidePinch, creaseInsidePinch);
            block.SetFloat(CreaseOutsidePinch, creaseOutsidePinch);
            block.SetFloat(RememberedCreasePinch, rememberedCreasePinch);
            // The part's own outline, always the full-length array (a block fixes an array's size the first time).
            block.SetFloat(OutlineCount, part.OutlineCount);
            block.SetVectorArray(Outline, part.Outline);
            part.SetShadingBlock(block);
        }

        /// <summary>One mesh + renderer pair with its own texture and colour.</summary>
        sealed class MeshPart
        {
            readonly MeshFilter filter;
            readonly MeshRenderer renderer;
            MeshRenderer shading;

            /// <summary>Face render texture of a layer part; null for the Seam part and unused layer parts.</summary>
            public Texture FaceTexture { get; private set; }
            public Color Tint { get; private set; } = Color.white;
            /// <summary>True if this layer part has a shading child (none without a shading material or sorting layer).</summary>
            public bool HasShading => shading != null;

            /// <summary>The edges this part's warp must stay inside (sheet-local), and how many; −1 = do not warp.</summary>
            public Vector4[] Outline { get; } = new Vector4[SheetWarp.MaxOutlineSegments];
            public int OutlineCount { get; private set; } = -1;

            /// <summary>Packs the outline of layer <paramref name="layer"/>: its edges and those above. False if they do not fit (no warp).</summary>
            public bool PackOutline(SheetLayers stack, int layer)
            {
                OutlineCount = SheetWarp.PackOutline(stack, layer, Outline, out var truncated);
                return !truncated;
            }

            public void ClearOutline() => OutlineCount = -1;

            /// <summary>Which face of the sheet a layer part shows, and so which grain map and creases shade it.</summary>
            public bool FrontUp { get; private set; } = true;
            public Vector4 GrainFrame { get; private set; } = IdentityFrame;

            public MeshPart(MeshFilter filter, MeshRenderer renderer)
            {
                this.filter = filter;
                this.renderer = renderer;
            }

            /// <summary>The Seam part: mesh, colour, no texture or grain.</summary>
            public void Apply(PolygonMeshBuilder builder, MaterialPropertyBlock block, Texture texture, Color color)
            {
                builder.Apply(filter.sharedMesh);
                block.Clear();
                if (texture != null)
                    block.SetTexture(BaseMap, texture);
                block.SetColor(BaseColor, color);
                renderer.SetPropertyBlock(block);
            }

            /// <summary>Layer parts: the mesh and what to draw it with; bind it with <see cref="BindFace"/> and <see cref="BindShading"/>.</summary>
            public void SetLayer(PolygonMeshBuilder builder, Texture faceTexture, Color tint, bool frontUp, Vector4 grainFrame)
            {
                builder.Apply(filter.sharedMesh);
                FaceTexture = faceTexture;
                Tint = tint;
                FrontUp = frontUp;
                GrainFrame = grainFrame;
            }

            public void SetBlock(MaterialPropertyBlock block) => renderer.SetPropertyBlock(block);

            /// <summary>
            /// Gives a layer part its shading child: a renderer on a child object sharing this part's mesh (so it covers
            /// exactly this piece, with the same face texture coordinates), destroyed with the part.
            /// </summary>
            public void AddShading(Material material, int sortingLayerID)
            {
                var go = new GameObject("Shading") { layer = filter.gameObject.layer };
                go.transform.SetParent(filter.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                shading = go.AddComponent<MeshRenderer>();
                shading.sharedMaterial = material;
                shading.sortingLayerID = sortingLayerID;
                shading.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shading.receiveShadows = false;
                shading.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                shading.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            public void SetShadingOrder(int order)
            {
                if (shading != null)
                    shading.sortingOrder = order;
            }

            public void SetShadingBlock(MaterialPropertyBlock block)
            {
                if (shading != null)
                    shading.SetPropertyBlock(block);
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
