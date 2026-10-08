using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// A block the player takes hold of (<see cref="BlockPusher"/>: interact key down while touching a face) and
    /// pushes or pulls along that face's axis (Design Doc: pushable elements; Bible §9 - built on Aaron's request,
    /// 2026-08-27; hold-to-push-or-pull 2026-09-24). It is content of the sheet like terrain: a rect on the
    /// flat sheet plus a side, so folds cover, lift, expose and carry it exactly as they do the sheet under it,
    /// and the block's physical presence is the parts of it on a visible surface (<see cref="SheetPlacement"/>).
    /// Pushing moves the flat rect through the layer the pushed part lies on.
    /// </summary>
    /// <remarks>
    /// Rules decided by Aaron (2026-08-27): a crease may pass through a block (the part that comes around is
    /// shown and pushable; pushed across the crease it rolls onto the other face); a block partly under a landed
    /// Flap belongs under it and can be pushed fully under; a block wholly clear of a Flap that is pushed into
    /// its edge climbs on top; a block hanging past its Flap's Seam refuses the unfold (it would leave the
    /// sheet); leaving the sheet resets the block to where it started; blocks stop at walls, gated terrain, sheet
    /// edges and other blocks. A fold or unfold never changes the block's rect or face: it rides its piece of the
    /// sheet all the way around, so a block pushed onto a Flap and unfolded is on the Back at the mirrored place.
    /// A block with Pushable off (the fixed Paperweight, 2026-09-14) is the same content that never moves.
    /// The transform is never rotated or scaled: every desk footprint is an axis-aligned rect, and the polygon
    /// collider paths and the drawing are computed explicitly in block-local space (sheet-local minus the desk
    /// centre; the block must sit unrotated and unscaled under a face root that is at identity - asserted).
    /// The drawing is a mesh on the Default layer, clipped to the visible pieces and placed at each piece's
    /// surface depth (<see cref="IFoldRenderer.SurfaceZ"/>), so a Flap that lands on part of it draws over that part.
    /// The picture on that mesh is the current frame of a <see cref="SketchAnimator"/> on a child (the hand-drawn
    /// line boil): the child's own SpriteRenderer shows the block in the editor and is switched off in play; the
    /// frame is mapped at its own scale with its pivot at the block's centre (<see cref="SpriteMapping"/>), so the
    /// parts of the frame outside the block's rect are cut, and the parts of the rect outside the frame are bare
    /// (<see cref="DrawingRect"/>: the mesh never samples past the image's edge) - author the rect to the drawing. The drawing is
    /// <em>not</em> painted with the paper: it is laid out upright in Desk space around the block's centre wherever
    /// its layer carries that centre (<see cref="DrawingUv"/>), so a block on a Flap, or revealed on the Back, reads
    /// the same way up as on the flat Front - an object sits on the paper rather than being drawn on it (Aaron,
    /// 2026-09-24, on a paperweight mirroring as it climbed a Flap: <em>"It shouldn't do that since its still on the
    /// 'front' side"</em>; blocks too). Only the clip to the visible pieces follows the paper.
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(PolygonCollider2D))]
    [DefaultExecutionOrder(-5)]
    public sealed class PushableBlock : MonoBehaviour, IFoldOccludee, IFoldConstraint, IArrivalObstacle
    {
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static bool reportedNonAxisAligned;
        /// <summary>Layer the drawing lives on: the main camera's, not a face layer.</summary>
        const int DefaultLayer = 0;

        [Header("Pushing")]
        [SerializeField, Tooltip("Off: the block never moves - fixed content of the sheet that still folds, covers, exposes and resets like any " +
            "block, and is a wall to the player (no push slowdown) and to other blocks. The fixed Paperweight is a block with this off.")]
        bool pushable = true;

        [SerializeField, Range(0f, 1f), Tooltip("The pusher's speed while pushing this block, as a fraction of their normal speed. 1 = no slowdown.")]
        float pushSpeedFactor = 0.5f;

        [SerializeField, Tooltip("Only a pusher holding Push Ability can move this block.")]
        bool requiresAbility = false;

        [SerializeField, Tooltip("The ability needed when Requires Ability is on.")]
        Ability pushAbility = Ability.Push;

        [Header("Drawing")]
        [SerializeField, Tooltip("Child that draws the block, clipped to the parts of it that are visible; posed by the block. Needs a MeshRenderer.")]
        MeshFilter visual;

        [SerializeField, Tooltip("URP/Unlit transparent material. Its _BaseMap is replaced by the drawing's current frame and its _BaseColor by Tint at runtime.")]
        Material material;

        [SerializeField, Tooltip("The block's drawing: a SketchAnimator on a direct child at the block's centre whose SpriteRenderer shows " +
            "the block in the editor. In play that renderer is switched off and the frame it shows is drawn on the clipped mesh instead, " +
            "at the frame's own scale, with its pivot at the block's centre.")]
        SketchAnimator drawing;

        [SerializeField, Tooltip("Tint of the drawing.")]
        Color tint = Color.white;

        [Header("Physics")]
        [SerializeField, Min(0f), Tooltip("Gap kept between the block and what it is pushed against, in world units. Author blocks at least this far from walls: " +
            "something the block already overlaps does not stop it.")]
        float skin = 0.01f;

        Sheet sheet;
        SheetFolds folds;
        IFoldRenderer foldRenderer;
        ScreenNavigator navigator;
        Rigidbody2D body;
        BoxCollider2D authored;
        PolygonCollider2D shape;
        MaterialPropertyBlock block;
        SpriteRenderer drawingRenderer;
        /// <summary>The frame the mesh was last drawn with; a new frame from the animator triggers a redraw.</summary>
        Sprite shownSprite;
        bool reportedNoSprite;
        /// <summary>One mesh per surface layer the block is drawn on (URP sorts renderers by their bounds' depth, so pieces at different depths need separate renderers). [0] is the assigned Visual.</summary>
        readonly List<(MeshFilter filter, MeshRenderer renderer)> parts = new();
        readonly List<int> surfaces = new();
        Transform authoredRoot;
        SheetFace authoredFace;
        readonly PolygonMeshBuilder builder = new();
        readonly List<RaycastHit2D> hits = new();
        readonly List<SheetPlacement.Piece> pieces = new();
        /// <summary>Sheet-space pieces of the universal walls that stop blocks; null until first asked (see <see cref="BlockWalls"/>).</summary>
        List<ConvexPolygon> blockWalls;

        Rect startFlatRect;
        SheetFace startSide;
        Rect flatRect;
        SheetFace side;
        int glueLayer = -1;
        int climbTarget = -1;
        /// <summary>Sheet-local desk centre the body was last placed at; the drawing's origin.</summary>
        Vector2 placedCentre;
        bool started;
        bool ok;

        /// <summary>The block's centre on the flat sheet and the face it is on.</summary>
        public SheetPoint Centre => new(flatRect.center, side);

        public Rect FlatRect => flatRect;

        public Sheet Sheet => sheet;

        /// <summary>True while some part of the block is on a visible surface (and so can be pushed).</summary>
        public bool IsVisible => ok && pieces.Count > 0;

        /// <summary>
        /// The parts of the block on a visible surface, where they lie (sheet-local), from the committed layers as of
        /// the last placement. What a <see cref="Paperweight"/> holds folds away from.
        /// </summary>
        public IReadOnlyList<SheetPlacement.Piece> VisiblePieces => pieces;

        public float PushSpeedFactor => pushSpeedFactor;

        /// <summary>True if this block can be pushed at all (see Pushable) and <paramref name="pusher"/> holds what it asks for (see Requires Ability).</summary>
        public bool CanBePushedBy(PlayerAbilities pusher)
            => PushRules.CanPush(pushable, pusher != null ? pusher.Abilities : Ability.None, requiresAbility, pushAbility);

        SheetLayers Layers => folds.Layers;

        void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            authored = GetComponent<BoxCollider2D>();
            shape = GetComponent<PolygonCollider2D>();
            block = new MaterialPropertyBlock();

            ok = true;
            sheet = GetComponentInParent<Sheet>();
            if (sheet == null) { Debug.LogError($"PushableBlock '{name}' is not under a Sheet.", this); ok = false; }
            else
            {
                folds = sheet.GetComponent<SheetFolds>();
                foldRenderer = sheet.GetComponent<IFoldRenderer>();
                navigator = sheet.GetComponentInParent<ScreenNavigator>();
                if (folds == null) { Debug.LogError($"PushableBlock '{name}': sheet '{sheet.name}' has no SheetFolds.", this); ok = false; }
                if (foldRenderer == null) { Debug.LogError($"PushableBlock '{name}': sheet '{sheet.name}' has no IFoldRenderer; the block cannot be placed in depth.", this); ok = false; }
                if (sheet.Front != null && transform.IsChildOf(sheet.Front)) { authoredRoot = sheet.Front; authoredFace = SheetFace.Front; }
                else if (sheet.Back != null && transform.IsChildOf(sheet.Back)) { authoredRoot = sheet.Back; authoredFace = SheetFace.Back; }
                else { Debug.LogError($"PushableBlock '{name}' must be under the sheet's Front or Back root.", this); ok = false; }
                if (authoredRoot != null && !IsIdentityUnder(authoredRoot, sheet.transform))
                {
                    Debug.LogError($"PushableBlock '{name}': the face root '{authoredRoot.name}' is not at identity relative to the sheet; the block's placement assumes it is.", this);
                    ok = false;
                }
                if (authoredRoot != null && (transform.parent != authoredRoot || transform.localRotation != Quaternion.identity || transform.localScale != Vector3.one))
                {
                    Debug.LogError($"PushableBlock '{name}' must be a direct, unrotated, unscaled child of the face root.", this);
                    ok = false;
                }
            }
            if (visual == null) { Debug.LogError($"PushableBlock '{name}' has no Visual assigned.", this); ok = false; }
            else if (!visual.TryGetComponent(out MeshRenderer _)) { Debug.LogError($"PushableBlock '{name}': the Visual has no MeshRenderer.", this); ok = false; }
            if (material == null) { Debug.LogError($"PushableBlock '{name}' needs a material.", this); ok = false; }
            if (drawing == null) { Debug.LogError($"PushableBlock '{name}' has no Drawing assigned.", this); ok = false; }
            else
            {
                // SketchAnimator requires a SpriteRenderer, so this cannot be null. Never shown in play, whatever else is
                // wrong: the face camera would render it as Sheet content at the authored place.
                drawingRenderer = drawing.GetComponent<SpriteRenderer>();
                drawingRenderer.enabled = false;
                var drawingTransform = drawing.transform;
                var offset = (Vector2)drawingTransform.localPosition;
                if (drawingTransform.parent != transform || offset != Vector2.zero || drawingTransform.localRotation != Quaternion.identity || drawingTransform.localScale != Vector3.one)
                {
                    Debug.LogError($"PushableBlock '{name}': the Drawing '{drawing.name}' must be a direct child of the block at its centre (x/y 0), unrotated and unscaled; the frame's pivot is drawn at the block's centre.", this);
                    ok = false;
                }
            }
            if (authored.isTrigger) { Debug.LogError($"PushableBlock '{name}': the authoring BoxCollider2D must not be a trigger. Fixing at runtime; please fix the asset.", this); authored.isTrigger = false; }

            if (!ok)
            {
                enabled = false;
                return;
            }

            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.None;
            body.useFullKinematicContacts = true; // So the (dynamic) player's contacts with this kinematic body are reported.
            authored.enabled = false; // Only ever read: the authored footprint.
            shape.isTrigger = false;
            shape.pathCount = 0;
            shape.enabled = false;

            var faceRect = FoldFootprint.FaceLocalRect(authored, authoredRoot);
            startFlatRect = authoredFace == SheetFace.Front ? faceRect : SheetGeometry.BackToFront(faceRect);
            startSide = authoredFace;
            flatRect = startFlatRect;
            side = startSide;

            parts.Clear();
            parts.Add(SetUpPart(visual));
        }

        (MeshFilter, MeshRenderer) SetUpPart(MeshFilter filter)
        {
            filter.sharedMesh = new Mesh { name = $"{name} drawing" };
            filter.sharedMesh.MarkDynamic();
            var renderer = filter.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return (filter, renderer);
        }

        /// <summary>The <paramref name="index"/>th drawing part, created as a sibling of the Visual when needed.</summary>
        (MeshFilter filter, MeshRenderer renderer) Part(int index)
        {
            while (parts.Count <= index)
            {
                var go = new GameObject($"{visual.name} {parts.Count}") { layer = DefaultLayer, hideFlags = HideFlags.DontSave };
                go.transform.SetParent(visual.transform.parent, false);
                go.transform.localPosition = visual.transform.localPosition;
                go.transform.localRotation = visual.transform.localRotation;
                go.transform.localScale = visual.transform.localScale;
                var filter = go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                parts.Add(SetUpPart(filter));
            }
            return parts[index];
        }

        static bool IsIdentityUnder(Transform root, Transform ancestor)
            => root.parent == ancestor && root.localPosition == Vector3.zero && root.localRotation == Quaternion.identity && root.localScale == Vector3.one;

        void OnEnable()
        {
            if (!ok)
                return;
            sheet.PlayerLeft += ResetToStart;
            folds.Displayed += Redraw;
            if (started)
                Place(pose: true);
        }

        void OnDisable()
        {
            if (!ok)
                return;
            sheet.PlayerLeft -= ResetToStart;
            folds.Displayed -= Redraw;
            shape.enabled = false;
            foreach (var part in parts)
                part.renderer.enabled = false; // Disabled = neither solid nor drawn; OnEnable places it again.
        }

        void Start()
        {
            // Sheet.Awake puts everything under a face root on the face layers, which the main camera does not draw.
            // The block is drawn by the main camera, over the composited sheet: back to Default (Start runs after every Awake).
            SetLayerRecursively(transform, DefaultLayer);
            started = true;
            Place(pose: true);
        }

        static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }

        void Update()
        {
            // While the sheet grid slides an interpolated body would fight the parent's motion (see PlayerMover.MovementEnabled).
            var want = sheet.IsScreen && (navigator == null || !navigator.IsTransitioning)
                ? RigidbodyInterpolation2D.Interpolate
                : RigidbodyInterpolation2D.None;
            if (body.interpolation != want)
                body.interpolation = want;
        }

        void LateUpdate()
        {
            // The animator changes frames in its Update, which runs after this component's (execution order -5).
            if (ok && drawingRenderer.sprite != shownSprite)
                Redraw();
        }

        void OnDestroy()
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].filter != null && parts[i].filter.sharedMesh != null)
                    Destroy(parts[i].filter.sharedMesh);
                if (i > 0 && parts[i].filter != null)
                    Destroy(parts[i].filter.gameObject);
            }
            parts.Clear();
        }

        // ----- IFoldOccludee -----

        /// <summary>The block's current flat rect in the authored face's space (the block may since have changed side; see <see cref="OnFoldCoverageChanged"/>).</summary>
        public FaceFootprint FaceLocalFootprint(Transform faceRoot)
            => FaceFootprint.FromRect(authoredFace == SheetFace.Front ? flatRect : SheetGeometry.BackToFront(flatRect));

        /// <summary>
        /// The block places itself from the sheet's layers rather than from <paramref name="coverage"/>, which is
        /// computed for the face it was authored on; the notification is its trigger (fold change, Screen enter/leave, start).
        /// </summary>
        public void OnFoldCoverageChanged(in CoverageResult coverage, Transform space)
        {
            if (!ok)
                return;
            climbTarget = -1;
            // The flat rect and side never change because of a fold or unfold: the block rides its piece of the sheet,
            // face and all (Aaron, 2026-08-27). SheetFolds draws right after this notification, so the drawing waits for Displayed.
            Place(pose: true, draw: false);
        }

        // ----- IFoldConstraint -----

        /// <summary>
        /// A Flap may not carry this block under a universal wall that stops blocks (Aaron, 2026-09-28: refused like a
        /// fold over the player). Judged on the stack after the fold, so a block on the face that is down, invisible
        /// now, is refused when the fold would turn it face-up under a wall. The walls are gathered once: universal
        /// regions are static authored content.
        /// </summary>
        public FoldRejection RefuseFold(in FoldEffect effect, SheetLayers after, int foldIndex)
        {
            if (!ok || !isActiveAndEnabled)
                return FoldRejection.None;
            return FoldLandingRules.CarriesUnderWall(flatRect, side, after, foldIndex, BlockWalls())
                ? FoldRejection.CarriesBlockUnderWall
                : FoldRejection.None;
        }

        /// <summary>Sheet-space pieces of every valid universal region of this sheet that stops blocks; gathered on first use, after every Awake.</summary>
        IReadOnlyList<ConvexPolygon> BlockWalls()
        {
            if (blockWalls != null)
                return blockWalls;
            blockWalls = new List<ConvexPolygon>();
            if (sheet.Above == null)
                return blockWalls;
            foreach (var region in sheet.Above.GetComponentsInChildren<TerrainRegion>(false))
            {
                if (region.IsValid && region.IsUniversal && region.StopsBlocks)
                    blockWalls.AddRange(region.FaceLocalFootprint(sheet.Above).Pieces);
            }
            return blockWalls;
        }

        public FoldRejection RefuseUnfold(int foldIndex)
        {
            if (!ok || glueLayer < 0)
                return FoldRejection.None;
            var glue = Layers.Layers[glueLayer];
            return glue.MovedBy == foldIndex && SheetPlacement.OverhangsSheet(flatRect) ? FoldRejection.ObjectOnEdge : FoldRejection.None;
        }

        // ----- IArrivalObstacle -----

        public bool TryGetSolidFootprint(PlayerAbilities player, out FaceFootprint sheetLocal)
        {
            sheetLocal = FaceFootprint.FromRect(flatRect);
            return ok && isActiveAndEnabled && side == SheetFace.Front; // Asked only while the sheet is flat.
        }

        // ----- reset -----

        void ResetToStart()
        {
            flatRect = startFlatRect;
            side = startSide;
            climbTarget = -1;
            Place(pose: true);
        }

        // ----- pushing -----

        /// <summary>
        /// Moves the block <paramref name="distance"/> along <paramref name="cardinal"/> (a unit axis vector, desk
        /// space) as far as nothing solid stops it. <paramref name="pusherBody"/> is ignored as an obstacle, so a
        /// cardinal pointing at the pusher is a pull: the block follows them. Every fold rule (climb, descend,
        /// roll across a crease, stop at gated terrain) is the same either way. False if it could not move at all.
        /// </summary>
        public bool TryPush(Vector2 cardinal, float distance, PlayerAbilities pusher, Rigidbody2D pusherBody, out float moved)
        {
            moved = 0f;
            if (!IsVisible || !sheet.IsScreen || distance <= 0f)
                return false;
            if (!CanBePushedBy(pusher))
                return false;

            var layers = Layers;
            var pushLayer = SheetPlacement.PushLayer(flatRect, side, layers);
            if (pushLayer < 0)
                return false;
            var glue = layers.Layers[glueLayer];
            var oldDesk = SheetPlacement.DeskRect(flatRect, glue.ToDesk);

            // Obstacles: anything solid in the way, except the pusher, crease walls (the sheet continues around a
            // crease), terrain that does not stop blocks (a player-only region, Aaron 2026-09-28) and whatever the
            // block already overlaps (authoring rule: see the skin tooltip).
            var filter = new ContactFilter2D { useTriggers = false, useLayerMask = false };
            hits.Clear();
            body.Cast(cardinal, filter, hits, distance + skin);
            var nearest = float.PositiveInfinity;
            foreach (var hit in hits)
            {
                if (hit.distance <= 0f || (pusherBody != null && hit.rigidbody == pusherBody) || hit.collider.GetComponent<CreaseWall>() != null)
                    continue;
                if (hit.collider.GetComponentInParent<TerrainRegion>() is { StopsBlocks: false })
                    continue;
                nearest = Mathf.Min(nearest, hit.distance);
            }
            distance = PushRules.ClampToHit(distance, nearest, skin);
            if (distance <= 0f)
                return false;

            var newDesk = new Rect(oldDesk.position + cardinal * distance, oldDesk.size);
            var flatDirection = layers.Layers[pushLayer].ToDesk.Inverse.ApplyVector(cardinal);

            var bound = SheetPlacement.PushBound(flatRect, layers, pushLayer, glueLayer); // see SheetPlacement.PushBound

            if (glue.MovedBy >= 0 && SheetPlacement.IsUp(glue, side) && SheetPlacement.OverhangsSheet(flatRect)
                && !SheetPlacement.CentreOn(newDesk.center, glue))
            {
                // Descending: the centre leaves the Flap it hangs past; it now rests on whatever is under it, still
                // drawn on top of the Flap where it overlaps it.
                var under = SheetPlacement.TopLayerAt(layers, newDesk.center);
                if (under < 0)
                    return false;
                Reglue(under, newDesk, climbOver: glueLayer);
            }
            else if (climbTarget >= 0)
            {
                var target = layers.Layers[climbTarget];
                if (!target.Desk.Overlaps(newDesk))
                {
                    climbTarget = -1; // Backed off.
                    if (!MoveFlat(flatDirection, ref distance, bound)) return false;
                }
                else if (SheetPlacement.CentreOn(newDesk.center, target))
                {
                    Reglue(climbTarget, newDesk, climbOver: -1); // On top now.
                }
                else if (!MoveFlat(flatDirection, ref distance, bound))
                {
                    return false;
                }
            }
            else
            {
                var onto = SheetPlacement.ClimbOnto(oldDesk, newDesk, layers, glueLayer, side);
                if (onto >= 0)
                    climbTarget = onto; // Sliding onto a Flap from clear: on top of it, not under it (Aaron, 2026-08-27).
                if (!MoveFlat(flatDirection, ref distance, bound))
                    return false;
            }

            Place(pose: false);
            body.MovePosition(WorldCentre());
            moved = distance;
            return true;
        }

        /// <summary>
        /// Moves the flat rect. Inside <paramref name="bound"/> if one is given; otherwise only its centre must stay on
        /// the sheet (a block on top of a Flap may hang past the Seam). False if it cannot move at all.
        /// </summary>
        bool MoveFlat(Vector2 flatDirection, ref float distance, ConvexPolygon bound)
        {
            if (bound != null)
            {
                distance = Mathf.Min(distance, SheetPlacement.MaxTravelInside(flatRect, flatDirection, bound));
            }
            else
            {
                var sheetRect = FoldGeometry.Sheet;
                var centre = flatRect.center;
                if (!sheetRect.Contains(centre + flatDirection * distance))
                {
                    var limit = flatDirection.x > 0f ? (sheetRect.xMax - centre.x) / flatDirection.x
                        : flatDirection.x < 0f ? (sheetRect.xMin - centre.x) / flatDirection.x
                        : flatDirection.y > 0f ? (sheetRect.yMax - centre.y) / flatDirection.y
                        : (sheetRect.yMin - centre.y) / flatDirection.y;
                    distance = Mathf.Min(distance, Mathf.Max(0f, limit));
                }
            }
            if (distance <= 1e-6f)
                return false;
            flatRect.position += flatDirection * distance;
            return true;
        }

        void Reglue(int layerIndex, Rect deskRect, int climbOver)
        {
            var layer = Layers.Layers[layerIndex];
            side = SheetPlacement.UpSide(layer);
            flatRect = SheetPlacement.FlatRect(deskRect, layer.ToDesk);
            climbTarget = climbOver;
        }

        // ----- placement -----

        /// <summary>
        /// Recomputes where the block is from its flat rect and side: its glue layer, visible pieces, colliders
        /// and drawing. <paramref name="pose"/> also moves the body there (never in the step it is pushed, which
        /// moves it with MovePosition instead).
        /// </summary>
        void Place(bool pose, bool draw = true)
        {
            var layers = Layers;
            glueLayer = SheetPlacement.LayerContaining(layers, flatRect.center);
            if (glueLayer < 0)
            {
                Debug.LogError($"PushableBlock '{name}': its centre {flatRect.center} is on no layer of the sheet; the block is inert until it is placed again.", this);
                pieces.Clear();
                shape.enabled = false;
                foreach (var part in parts)
                    part.renderer.enabled = false;
                return;
            }
            var glue = layers.Layers[glueLayer];
            if (!SheetPlacement.IsAxisAligned(glue.ToDesk) && !reportedNonAxisAligned)
            {
                reportedNonAxisAligned = true;
                Debug.LogError($"PushableBlock '{name}': layer transform {glue.ToDesk} is not axis-aligned; block footprints are taken as their bounds.", this);
            }

            pieces.Clear();
            pieces.AddRange(SheetPlacement.VisiblePieces(flatRect, side, layers, climbTarget));
            var deskCentre = glue.ToDesk.Apply(flatRect.center);

            if (pose)
            {
                // A Rigidbody2D.position write reaches the Transform only at the next step; the paths below are
                // relative to the desk centre, so both are set now (as PlayerMover.PlaceAt does).
                var world = sheet.transform.TransformPoint(new Vector3(deskCentre.x, deskCentre.y, 0f));
                transform.position = world;
                body.position = world;
            }

            // Colliders: one path per visible piece, in block-local space (sheet-local minus the desk centre).
            var live = sheet.IsScreen && pieces.Count > 0;
            if (live)
            {
                shape.pathCount = pieces.Count;
                for (int i = 0; i < pieces.Count; i++)
                {
                    var verts = pieces[i].Desk.Vertices;
                    var path = new Vector2[verts.Count];
                    for (int v = 0; v < verts.Count; v++)
                        path[v] = verts[v] - deskCentre;
                    shape.SetPath(i, path);
                }
            }
            shape.enabled = live;

            placedCentre = deskCentre;
            if (draw)
                DrawVisual();
        }

        /// <summary>Redraws from the displayed folds (a drag preview or unfold retreat included) without touching physics.</summary>
        void Redraw()
        {
            if (ok && glueLayer >= 0)
                DrawVisual();
        }

        /// <summary>
        /// Draws the block as it lies on the sheet <em>as displayed</em> (<see cref="SheetFolds.DisplayLayers"/>): during a
        /// drag the block follows the previewed Flap; during an unfold it swings back with it. Physics uses the committed
        /// layers, so the mesh is built relative to the body's committed place. One mesh per surface layer at that
        /// surface's depth, textured by flat coordinates through the current frame (see the class remarks).
        /// </summary>
        void DrawVisual()
        {
            var stack = folds.DisplayLayers;
            // The Flap being climbed, found again in the displayed stack (a preview may have reordered or cut the layers).
            var climb = climbTarget < 0 ? -1
                : folds.DisplayIsCommitted ? climbTarget
                : SheetPlacement.SameLayer(stack, Layers.Layers[climbTarget]);
            var shown = SheetPlacement.VisiblePieces(flatRect, side, stack, climb);
            var origin = placedCentre;

            surfaces.Clear();
            foreach (var piece in shown)
            {
                if (!surfaces.Contains(piece.SurfaceLayer))
                    surfaces.Add(piece.SurfaceLayer);
            }
            var sprite = drawingRenderer.sprite;
            shownSprite = sprite;
            if (sprite == null)
            {
                if (!reportedNoSprite)
                {
                    reportedNoSprite = true;
                    Debug.LogError($"PushableBlock '{name}': its Drawing '{drawing.name}' shows no sprite (is the SketchAnimator's idle assigned?); the block is invisible.", this);
                }
                foreach (var part in parts)
                    part.renderer.enabled = false;
                return;
            }
            var frame = SpriteFrame.Of(sprite);
            block.Clear();
            block.SetTexture(BaseMap, sprite.texture);
            block.SetColor(BaseColor, tint);
            for (int s = 0; s < surfaces.Count; s++)
            {
                var surface = surfaces[s];
                var z = foldRenderer.SurfaceZ(surface);
                builder.Clear();
                foreach (var piece in shown)
                {
                    if (piece.SurfaceLayer != surface)
                        continue;
                    // Upright in Desk space, the frame's pivot at the block's centre as this piece's layer carries it: an object
                    // sits on the paper rather than being painted on it, so no Flap ever mirrors it (Aaron, 2026-09-24).
                    var deskCentre = stack.Layers[piece.LayerIndex].ToDesk.Apply(flatRect.center);
                    var polygon = piece.Desk.ClipToRect(DrawingRect(frame, deskCentre));
                    builder.AddPolygon(polygon, v => DrawingUv(frame, deskCentre, v), z, origin);
                }
                var part = Part(s);
                builder.Apply(part.filter.sharedMesh);
                part.renderer.SetPropertyBlock(block);
                part.renderer.enabled = true;
            }
            for (int s = surfaces.Count; s < parts.Count; s++)
                parts[s].renderer.enabled = false;
        }

        /// <summary>
        /// Where a sheet-local Desk point lands on the block's drawing frame: the frame's pivot sits at
        /// <paramref name="deskCentre"/> (the block's centre as its layer carries it) and the frame is laid out
        /// upright in Desk space whatever face the block is on and whatever Flap it rides - an object sits on the
        /// paper rather than being painted on it, so it is never mirrored (Aaron, 2026-09-24). Pure.
        /// </summary>
        public static Vector2 DrawingUv(in SpriteFrame frame, Vector2 deskCentre, Vector2 desk)
            => SpriteMapping.Uv(frame, deskCentre, desk);

        /// <summary>
        /// The sheet-local Desk rect the drawing frame covers when <see cref="DrawingUv"/> lays it out: the frame at its
        /// own scale, upright, with its pivot at <paramref name="deskCentre"/>. The drawn mesh is clipped to it, so the
        /// texture is never sampled past the frame's edge (a clamped edge would repeat as a bar wherever the block's
        /// rect is wider than the image) and a frame smaller than the rect leaves the rest of the rect bare. Pure.
        /// </summary>
        public static Rect DrawingRect(in SpriteFrame frame, Vector2 deskCentre)
            => new(deskCentre - frame.Pivot / frame.PixelsPerUnit, frame.Rect.size / frame.PixelsPerUnit);

        Vector2 WorldCentre()
        {
            var deskCentre = Layers.Layers[glueLayer].ToDesk.Apply(flatRect.center);
            return sheet.transform.TransformPoint(new Vector3(deskCentre.x, deskCentre.y, 0f));
        }

        void OnDrawGizmos()
        {
            if (!TryGetComponent(out BoxCollider2D gizmoBox))
                return;
            Gizmos.color = new Color(0.8f, 0.5f, 0.2f, 0.5f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(gizmoBox.offset, gizmoBox.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
