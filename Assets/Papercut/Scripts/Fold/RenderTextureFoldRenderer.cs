using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// Draws a Sheet by render-texture compositing (Bible decision #2, chosen by Aaron): a camera per face
    /// renders that face's content into a texture; the visible sheet is a Base mesh textured from the Front
    /// texture plus a landed-Flap mesh textured from the Back texture through the fold's reflection, plus crease
    /// lines. The lifted region is simply not drawn, so the Desk shows through.
    /// </summary>
    /// <remarks>
    /// Face content sits on the <see cref="FoldLayers"/> layers, which the main camera does not render, so a sheet is
    /// visible only through this component. The Back camera is a child of the Back root and renders Back-space
    /// whatever the root's pose. Textures and cameras exist only while the sheet is in view. No readbacks.
    /// Colours are per renderer (a material property block), so the materials only need a texture and a
    /// colour: URP/Unlit, transparent. The Flap tint follows the first displayed fold - one fold is exposed at a time.
    /// Crease lines are marks on the sheet, not overlays: they are drawn as face content under the Front root and,
    /// mirrored into Back-space, under the Back root, so the textures carry them and a later fold lifts, hides or
    /// brings them around like the rest of the sheet. The Seam border stays an overlay: it marks a position.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Sheet))]
    public sealed class RenderTextureFoldRenderer : MonoBehaviour, IFoldRenderer
    {
        const float BaseZ = 0.05f;
        const float FlapZ = 0.02f;
        const float SeamZ = 0.015f;
        /// <summary>Face-local z of crease lines: in front of every authored face element (surface at 0, terrain sprites at -0.05).</summary>
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
        MeshPart basePart, flapPart, seamPart;
        readonly List<MeshPart> creaseParts = new(4); // [Front live, Front remembered, Back live, Back remembered]
        MaterialPropertyBlock block;
        readonly List<FoldVisual> shownFolds = new();
        readonly List<Crease> shownCreases = new();
        readonly MeshBuilder builder = new();
        bool inView;

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

            basePart = CreateMeshPart("Base", transform, BaseZ, faceMaterial, gameObject.layer);
            flapPart = CreateMeshPart("Flap", transform, FlapZ, faceMaterial, gameObject.layer);
            seamPart = CreateMeshPart("Seam", transform, SeamZ, creaseMaterial, gameObject.layer);
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
            foreach (var part in new[] { basePart, flapPart, seamPart })
                part?.Destroy();
            foreach (var part in creaseParts)
                part?.Destroy();
            creaseParts.Clear();
            basePart = flapPart = seamPart = null;
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
            backCamera.enabled = shownFolds.Count > 0;
        }

        public void Draw(in FoldDisplay display)
        {
            shownFolds.Clear();
            shownFolds.AddRange(display.Folds);
            shownCreases.Clear();
            shownCreases.AddRange(display.RememberedCreases);
            if (basePart == null)
                return;
            BuildMeshes();
        }

        // ----- view gating -----

        /// <summary>True if the sheet can be on screen. A Flap never leaves the sheet, so the sheet's own bounds suffice.</summary>
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
            if (basePart != null)
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
            var sheetRect = FoldGeometry.Sheet;

            // Base: the sheet minus every Flap.
            var basePolygon = ConvexPolygon.FromRect(sheetRect);
            foreach (var visual in shownFolds)
            {
                var crease = FoldGeometry.CreaseOf(visual.Fold);
                basePolygon = basePolygon.ClipToHalfPlane(crease.Point, crease.FlapNormal, keepPositive: false);
            }
            builder.Clear();
            builder.AddPolygon(basePolygon, v => UvOf(v));
            basePart.Apply(builder, block, frontTexture, Color.white);

            // Flap: each fold's landed region, textured from Back-space through the reflection.
            builder.Clear();
            var flapTint = Color.white;
            foreach (var visual in shownFolds)
            {
                var fold = visual.Fold;
                builder.AddPolygon(FoldGeometry.LandedRegion(fold), v => UvOf(FoldGeometry.LandedToBack(fold, v)));
            }
            if (shownFolds.Count > 0)
            {
                flapTint = shownFolds[0].State switch
                {
                    FoldVisualState.Preview => previewTint,
                    FoldVisualState.PreviewInvalid => invalidTint,
                    FoldVisualState.Retreating => retreatingTint,
                    _ => Color.white,
                };
            }
            flapPart.Apply(builder, block, backTexture, flapTint);

            // Creases are marks on both faces of the sheet: Front-space under Front, Back-space (x mirrored) under Back.
            for (int face = 0; face < 2; face++)
            {
                var mirror = face == 1;
                Vector2 F(Vector2 p) => mirror ? SheetGeometry.BackToFront(p) : p;

                builder.Clear();
                foreach (var visual in shownFolds)
                {
                    if (!FoldGeometry.TryCreaseSegment(visual.Fold, out var a, out var b))
                        continue;
                    // A live crease sits on the fold line, where the Base ends and the landed Flap begins. Drawn
                    // centred, only half of it would show (the Flap covers the other half with the mirror image of
                    // this same half). So the Front copy is shifted wholly onto the Base side and the Back copy
                    // wholly onto the Flap side - which the Flap reflects onto that same strip - giving one line of
                    // full width on both faces.
                    var shift = FoldGeometry.CreaseOf(visual.Fold).FlapNormal * (creaseWidth * 0.5f) * (mirror ? 1f : -1f);
                    builder.AddLine(F(a + shift), F(b + shift), creaseWidth);
                }
                creaseParts[face * 2]?.Apply(builder, block, null, creaseColor);

                builder.Clear();
                foreach (var crease in shownCreases)
                {
                    if (FoldGeometry.TryClipLineToRect(crease, sheetRect, out var a, out var b))
                        builder.AddLine(F(a), F(b), creaseWidth);
                }
                creaseParts[face * 2 + 1]?.Apply(builder, block, null, rememberedCreaseColor);
            }

            // Seam border: only while a fold is being dragged, so the Flap's edge is unmistakable.
            builder.Clear();
            foreach (var visual in shownFolds)
            {
                if (visual.State != FoldVisualState.Preview && visual.State != FoldVisualState.PreviewInvalid)
                    continue;
                foreach (var (a, b) in FoldGeometry.SeamSegments(visual.Fold))
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

            public void Apply(MeshBuilder builder, MaterialPropertyBlock block, Texture texture, Color color)
            {
                builder.Apply(filter.sharedMesh);
                block.Clear();
                if (texture != null)
                    block.SetTexture(BaseMap, texture);
                block.SetColor(BaseColor, color);
                renderer.SetPropertyBlock(block);
            }

            public void Destroy()
            {
                if (filter == null)
                    return;
                Object.Destroy(filter.sharedMesh);
                Object.Destroy(filter.gameObject);
            }
        }

        /// <summary>Accumulates fan-triangulated convex polygons and line quads into one mesh.</summary>
        sealed class MeshBuilder
        {
            readonly List<Vector3> vertices = new();
            readonly List<Vector2> uvs = new();
            readonly List<int> triangles = new();

            public void Clear()
            {
                vertices.Clear();
                uvs.Clear();
                triangles.Clear();
            }

            public void AddPolygon(ConvexPolygon polygon, System.Func<Vector2, Vector2> uvOf)
            {
                if (polygon.IsEmpty)
                    return;
                var start = vertices.Count;
                var verts = polygon.Vertices;
                // Unity front faces wind clockwise as seen by the camera, which looks along +z at this xy plane.
                var ccw = SignedArea(verts) > 0f;
                for (int i = 0; i < verts.Count; i++)
                {
                    vertices.Add(verts[i]);
                    uvs.Add(uvOf(verts[i]));
                }
                for (int i = 1; i + 1 < verts.Count; i++)
                {
                    triangles.Add(start);
                    triangles.Add(ccw ? start + i + 1 : start + i);
                    triangles.Add(ccw ? start + i : start + i + 1);
                }
            }

            public void AddLine(Vector2 a, Vector2 b, float width)
            {
                var dir = (b - a).normalized;
                var n = new Vector2(-dir.y, dir.x) * (width * 0.5f);
                AddPolygon(new ConvexPolygon(new List<Vector2> { a - n, b - n, b + n, a + n }), _ => Vector2.zero);
            }

            public void Apply(Mesh mesh)
            {
                mesh.Clear();
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
            }

            static float SignedArea(IReadOnlyList<Vector2> v)
            {
                float twice = 0f;
                for (int i = 0, n = v.Count; i < n; i++)
                    twice += v[i].x * v[(i + 1) % n].y - v[(i + 1) % n].x * v[i].y;
                return twice;
            }
        }
    }
}
