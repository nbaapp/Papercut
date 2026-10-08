using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>
    /// The hand-drawn element art (2026-09-03): the three line-boil animations are playable, imported at the
    /// shared scale as single full-canvas sprites, and each element prefab is wired to its drawing. The paperweights'
    /// placeholder (2026-09-17) is a one-frame push-pin drawing at its own scale, fitted to the Block's rect.
    /// </summary>
    public sealed class DrawingAssetsTests
    {
        const string BoxIdlePath = "Assets/Papercut/Animations/Box Idle.asset";
        const string ButtonIdlePath = "Assets/Papercut/Animations/Button Idle.asset";
        const string TreeIdlePath = "Assets/Papercut/Animations/Tree Idle.asset";
        const string BlockPath = "Assets/Papercut/Prefabs/Objects/Block.prefab";
        const string HoldPlatePath = "Assets/Papercut/Prefabs/Objects/Hold Plate.prefab";
        const string LatchPlatePath = "Assets/Papercut/Prefabs/Objects/Latch Plate.prefab";
        const string TreePath = "Assets/Papercut/Prefabs/Props/Tree.prefab";

        /// <summary>Every hand-drawn frame is 2100 px tall at 2000 px per unit (the Scuffy convention); elements are square, Scuffy 1500 px wide.</summary>
        const float CanvasUnits = 1.05f;

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, $"asset missing: {path}");
            return asset;
        }

        static Object Field(Object target, string field)
            => new SerializedObject(target).FindProperty(field).objectReferenceValue;

        static void AssertAnimation(string path, int frames)
        {
            var animation = Load<SketchAnimation>(path);
            Assert.AreEqual(frames, animation.FrameCount, path);
            Assert.IsTrue(animation.IsPlayable, $"{path} has an empty frame slot");
            Assert.Greater(animation.FramesPerSecond, 0f, path);
            for (int i = 0; i < frames; i++)
            {
                var sprite = animation.GetFrame(i);
                var texture = sprite.texture;
                Assert.AreEqual(new Rect(0f, 0f, texture.width, texture.height), sprite.rect,
                    $"{path} frame {i}: must be one sprite over the whole canvas (Single sprite mode), not an auto-slice");
                Assert.AreEqual(CanvasUnits, sprite.rect.height / sprite.pixelsPerUnit, 0.01f, $"{path} frame {i}: shared scale");
                Assert.LessOrEqual(texture.width, 512, $"{path} frame {i}: max texture size");
                Assert.AreEqual(new Vector2(0.5f, 0.5f), sprite.pivot / sprite.rect.size, $"{path} frame {i}: centre pivot");
            }
        }

        [Test]
        public void BoxIdle_IsThreeFramesAtTheSharedScale() => AssertAnimation(BoxIdlePath, 3);

        [Test]
        public void ButtonIdle_IsThreeFramesAtTheSharedScale() => AssertAnimation(ButtonIdlePath, 3);

        [Test]
        public void TreeIdle_IsThreeFramesAtTheSharedScale() => AssertAnimation(TreeIdlePath, 3);

        /// <summary>Scuffy's win pose (Aaron, 2026-10-08: for picking things up later). Not played by anything yet.</summary>
        [Test]
        public void ScuffyWin_IsThreeFramesAtTheSharedScale() => AssertAnimation("Assets/Papercut/Animations/Scuffy Win.asset", 3);

        [Test]
        public void Block_DrawsBoxIdle_OnADirectChildAtTheElementDepth()
        {
            var block = Load<GameObject>(BlockPath);
            var idle = Load<SketchAnimation>(BoxIdlePath);
            var drawing = Field(block.GetComponent<PushableBlock>(), "drawing") as SketchAnimator;
            Assert.IsNotNull(drawing, "PushableBlock.drawing must be a SketchAnimator");
            Assert.AreEqual(block.transform, drawing.transform.parent, "a direct child");
            Assert.AreEqual(Vector2.zero, (Vector2)drawing.transform.localPosition, "at the block's centre (the frame's pivot is drawn there)");
            Assert.AreEqual(Quaternion.identity, drawing.transform.localRotation);
            Assert.AreEqual(Vector3.one, drawing.transform.localScale);
            Assert.AreEqual(-0.08f, drawing.transform.localPosition.z, 1e-5f,
                "in front of the element z (-0.05) and behind the creases (-0.1): the Studio draws blocks and paperweights above walls, " +
                "water, gates, plates and pickups, as the game composites them (Aaron, 2026-09-17); at 0 it would sit on the Surface quad");
            Assert.AreEqual(idle, Field(drawing, "idle"), "idle = Box Idle");
            var renderer = drawing.GetComponent<SpriteRenderer>();
            Assert.AreEqual(idle.GetFrame(0), renderer.sprite, "the editor shows frame 1");
            Assert.AreEqual(SpriteDrawMode.Simple, renderer.drawMode);
            Assert.IsNull(block.GetComponent<SpriteRenderer>(), "the root has no renderer of its own (the runtime mesh is the Visual child)");
        }

        /// <summary>
        /// The Box drawing's extent, measured from the frames (plan 2026-09-03 §5.1; frame 1 reaches +0.349, rounded
        /// up to the 0.02 grid the collider is authored on, so the top edge may sit exactly on the collider's).
        /// </summary>
        static readonly Rect BoxDrawn = Rect.MinMaxRect(-0.34f, -0.32f, 0.34f, 0.35f);

        [Test]
        public void Block_ColliderHoldsTheDrawing_InsideTheCanvas()
        {
            // The mesh is clipped to this rect, so the rect must contain the drawing; it may not exceed the canvas.
            // The exact size is Aaron's to tune, so only the relationship is pinned.
            var box = Load<GameObject>(BlockPath).GetComponent<BoxCollider2D>();
            var rect = new Rect(box.offset - box.size * 0.5f, box.size);
            Assert.LessOrEqual(rect.xMin, BoxDrawn.xMin, "left of the drawing");
            Assert.LessOrEqual(rect.yMin, BoxDrawn.yMin, "below the drawing");
            Assert.GreaterOrEqual(rect.xMax, BoxDrawn.xMax, "right of the drawing");
            Assert.GreaterOrEqual(rect.yMax, BoxDrawn.yMax, "above the drawing");
            Assert.Less(box.size.x, CanvasUnits, "inside the canvas");
            Assert.Less(box.size.y, CanvasUnits, "inside the canvas");
        }

        [TestCase(HoldPlatePath)]
        [TestCase(LatchPlatePath)]
        public void Plate_ShowsTheButtonDrawing_TintedDarkerWhenPressed(string path)
        {
            var plate = Load<GameObject>(path);
            var idle = Load<SketchAnimation>(ButtonIdlePath);
            var animator = plate.GetComponent<SketchAnimator>();
            Assert.IsNotNull(animator, "SketchAnimator on the root");
            Assert.AreEqual(idle, Field(animator, "idle"), "idle = Button Idle");
            var renderer = plate.GetComponent<SpriteRenderer>();
            Assert.AreEqual(idle.GetFrame(0), renderer.sprite);
            Assert.AreEqual(SpriteDrawMode.Simple, renderer.drawMode, "a hand-drawn button does not tile");
            Assert.AreEqual(Color.white, renderer.color, "the drawing carries its own colour");

            var pressure = new SerializedObject(plate.GetComponent<PressurePlate>());
            var released = pressure.FindProperty("releasedColor").colorValue;
            var pressed = pressure.FindProperty("pressedColor").colorValue;
            Assert.Less(pressed.grayscale, released.grayscale, "pressed is a darker tint (Aaron); both tints are his to tune");
            Assert.AreEqual(1f, pressed.a, 1e-5f, "opaque tint");

            // Sized to the drawing (≈ 0.65 across, plan §5.1); the exact value is Aaron's to tune.
            var box = plate.GetComponent<BoxCollider2D>();
            Assert.IsTrue(box.isTrigger);
            Assert.Greater(box.size.x, 0.5f, "not far smaller than the drawing");
            Assert.Less(box.size.x, CanvasUnits, "inside the canvas");
            Assert.Greater(box.size.y, 0.5f);
            Assert.Less(box.size.y, CanvasUnits);
        }

        const string PowerCroissantIdlePath = "Assets/Papercut/Animations/Power Croissant Idle.asset";
        const string PowerCroissantPath = "Assets/Papercut/Prefabs/Objects/Power Croissant.prefab";
        const string PushablePaperweightPrefabPath = "Assets/Papercut/Prefabs/Objects/Pushable Paperweight.prefab";

        /// <summary>
        /// The croissant's drawn extent around the canvas centre: the opaque alpha bounds of the three frames Aaron
        /// re-exported on 2026-10-08 (the drawing is now centred), ±0.01.
        /// </summary>
        static readonly Rect CroissantDrawn = Rect.MinMaxRect(-0.43f, -0.26f, 0.43f, 0.29f);

        [Test]
        public void PowerCroissantIdle_IsThreeFramesAtTheSharedScale() => AssertAnimation(PowerCroissantIdlePath, 3);

        [Test]
        public void PowerCroissant_ShowsItsDrawing_AndGrantsPush()
        {
            var croissant = Load<GameObject>(PowerCroissantPath);
            var idle = Load<SketchAnimation>(PowerCroissantIdlePath);
            var animator = croissant.GetComponent<SketchAnimator>();
            Assert.IsNotNull(animator, "SketchAnimator on the root");
            Assert.AreEqual(idle, Field(animator, "idle"), "idle = Power Croissant Idle");
            var renderer = croissant.GetComponent<SpriteRenderer>();
            Assert.AreEqual(idle.GetFrame(0), renderer.sprite, "the editor shows frame 1");
            Assert.AreEqual(SpriteDrawMode.Simple, renderer.drawMode, "a hand-drawn croissant does not tile");
            Assert.AreEqual(Color.white, renderer.color, "the drawing carries its own colour");
            Assert.AreEqual(-0.05f, croissant.transform.localPosition.z, 1e-5f, "at the element z");

            var unlockable = croissant.GetComponent<Unlockable>();
            Assert.IsNotNull(unlockable);
            Assert.AreEqual(Ability.Push, unlockable.Grants, "the Power Croissant grants Push (Aaron, 2026-09-17)");
            Assert.IsNull(croissant.GetComponent<TerrainRegion>(), "not terrain");
            Assert.IsNull(croissant.GetComponent<PushableBlock>(), "not a block");

            // The pickup zone is authored on the drawing's own bounds (plan review N3): the player collects it by
            // standing on the croissant, not beside it. The exact size is Aaron's to tune, so only the relationship is pinned.
            var box = croissant.GetComponent<BoxCollider2D>();
            Assert.IsTrue(box.isTrigger, "a pickup zone, not a wall");
            var rect = new Rect(box.offset - box.size * 0.5f, box.size);
            Assert.AreEqual(CroissantDrawn.xMin, rect.xMin, 0.05f, "left edge on the drawing");
            Assert.AreEqual(CroissantDrawn.yMin, rect.yMin, 0.05f, "bottom edge on the drawing");
            Assert.AreEqual(CroissantDrawn.xMax, rect.xMax, 0.05f, "right edge on the drawing");
            Assert.AreEqual(CroissantDrawn.yMax, rect.yMax, 0.05f, "top edge on the drawing");
            Assert.Less(box.size.x, CanvasUnits, "inside the canvas");
            Assert.Less(box.size.y, CanvasUnits, "inside the canvas");
        }

        [Test]
        public void Blocks_RequirePush_ByDefault()
        {
            // Aaron, 2026-09-17: every block, the Pushable Paperweight included, needs Push; the fixed Paperweight never moves anyway.
            foreach (var path in new[] { BlockPath, PushablePaperweightPrefabPath })
            {
                var block = new SerializedObject(Load<GameObject>(path).GetComponent<PushableBlock>());
                Assert.IsTrue(block.FindProperty("requiresAbility").boolValue, $"{path}: Requires Ability");
                Assert.AreEqual((int)Ability.Push, block.FindProperty("pushAbility").intValue, $"{path}: Push");
            }
        }

        const string PaperweightPath = "Assets/Papercut/Prefabs/Objects/Paperweight.prefab";
        const string PushablePaperweightPath = "Assets/Papercut/Prefabs/Objects/Pushable Paperweight.prefab";

        const string PaperweightIdlePath = "Assets/Papercut/Animations/Paperweight Idle.asset";

        /// <summary>
        /// The push pin's opaque extent around its pivot, in units: the PNG's alpha bounds (1692 x 1309 px, centre
        /// (1074, 859.5) of the 1920 x 1514 image, which the pin touches at its right and top edges) at 2400 px per
        /// unit, measured 2026-09-24 on the unpadded clip art (Aaron replaced the 2026-09-17 padded copy). The pivot is
        /// set on that centre so the pin sits centred in the block's rect; the block's mesh is clipped to the frame
        /// (<see cref="PushableBlock.DrawingRect"/>), so the image needs no transparent margin.
        /// </summary>
        static readonly Rect PinDrawn = Rect.MinMaxRect(-0.3525f, -0.2727f, 0.3525f, 0.2727f);

        /// <summary>
        /// The paperweights' placeholder drawing (Aaron, 2026-09-17: the push pin clip art "for now") is one static
        /// frame at its own scale: clip art is not on the 2100 px canvas, so it is imported to fit the Block's rect.
        /// </summary>
        [Test]
        public void PaperweightIdle_IsOneFrame_ThatFitsTheBlock()
        {
            var animation = Load<SketchAnimation>(PaperweightIdlePath);
            Assert.AreEqual(1, animation.FrameCount, "one static drawing");
            Assert.IsTrue(animation.IsPlayable);
            var sprite = animation.GetFrame(0);
            var texture = sprite.texture;
            // The canvas is not square, so the max-size reduction leaves the rect a fraction of a pixel short of the texture.
            Assert.AreEqual(Vector2.zero, sprite.rect.position, "one sprite over the whole image (Single sprite mode), not an auto-slice");
            Assert.AreEqual(texture.width, sprite.rect.width, 1f, "the whole image, not an auto-slice");
            Assert.AreEqual(texture.height, sprite.rect.height, 1f, "the whole image, not an auto-slice");
            Assert.LessOrEqual(texture.width, 512, "max texture size");
            var pivot = sprite.pivot / sprite.rect.size;
            Assert.AreEqual(0.5594f, pivot.x, 0.005f, "pivot on the pin's centre, not the image's");
            Assert.AreEqual(0.5677f, pivot.y, 0.005f, "pivot on the pin's centre, not the image's");

            // The mesh is clipped to the Block's rect, so the pin must fit inside it.
            var box = Load<GameObject>(BlockPath).GetComponent<BoxCollider2D>();
            var rect = new Rect(box.offset - box.size * 0.5f, box.size);
            Assert.LessOrEqual(rect.xMin, PinDrawn.xMin, "left of the pin");
            Assert.LessOrEqual(rect.yMin, PinDrawn.yMin, "below the pin");
            Assert.GreaterOrEqual(rect.xMax, PinDrawn.xMax, "right of the pin");
            Assert.GreaterOrEqual(rect.yMax, PinDrawn.yMax, "above the pin");
        }

        /// <summary>
        /// The paperweights (Aaron, 2026-09-14) are Block variants: the same physics and plate-pressing plus the
        /// Paperweight component; the fixed one has pushing off. Both draw the push pin, untinted (Aaron, 2026-09-17).
        /// </summary>
        [TestCase(PaperweightPath, false)]
        [TestCase(PushablePaperweightPath, true)]
        public void Paperweight_IsABlockVariant_WithTheComponent(string path, bool pushable)
        {
            var weight = Load<GameObject>(path);
            var block = Load<GameObject>(BlockPath);
            Assert.AreEqual(block, PrefabUtility.GetCorrespondingObjectFromSource(weight), "a variant of Block.prefab");
            Assert.IsNotNull(weight.GetComponent<Paperweight>(), "carries the Paperweight component");
            Assert.IsNotNull(weight.GetComponent<BlockPresser>(), "presses plates like a block (Aaron: both press)");

            var pushableBlock = weight.GetComponent<PushableBlock>();
            Assert.IsNotNull(pushableBlock);
            Assert.AreEqual(pushable, new SerializedObject(pushableBlock).FindProperty("pushable").boolValue, "Pushable");
            var drawing = Field(pushableBlock, "drawing") as SketchAnimator;
            Assert.IsNotNull(drawing, "the Block's drawing child is inherited");
            var idle = Load<SketchAnimation>(PaperweightIdlePath);
            Assert.AreEqual(idle, Field(drawing, "idle"), "idle = Paperweight Idle (the push pin)");
            Assert.AreEqual(idle.GetFrame(0), drawing.GetComponent<SpriteRenderer>().sprite, "the editor shows the pin");
            var tint = new SerializedObject(pushableBlock).FindProperty("tint").colorValue;
            Assert.AreEqual(Color.white, tint, "untinted: the drawing carries its own colour (Aaron)");
        }

        [Test]
        public void HoldAndLatch_LookTheSame()
        {
            var hold = Load<GameObject>(HoldPlatePath).GetComponent<SpriteRenderer>();
            var latch = Load<GameObject>(LatchPlatePath).GetComponent<SpriteRenderer>();
            Assert.AreEqual(hold.sprite, latch.sprite);
            Assert.AreEqual(hold.color, latch.color, "Aaron: no different tints");
        }

        [Test]
        public void Tree_IsAHandDrawnProp_WithNoCollisionOfItsOwn()
        {
            var tree = Load<GameObject>(TreePath);
            var idle = Load<SketchAnimation>(TreeIdlePath);
            Assert.AreEqual(-0.05f, tree.transform.localPosition.z, 1e-5f, "the element z");
            var animator = tree.GetComponent<SketchAnimator>();
            Assert.IsNotNull(animator);
            Assert.AreEqual(idle, Field(animator, "idle"));
            var renderer = tree.GetComponent<SpriteRenderer>();
            Assert.AreEqual(idle.GetFrame(0), renderer.sprite);
            Assert.AreEqual(SpriteDrawMode.Simple, renderer.drawMode);

            // Aaron, 2026-09-10: static scenery carries no collision of its own; Wall regions are laid over it by hand.
            Assert.AreEqual(0, tree.GetComponentsInChildren<Collider2D>(true).Length, "no collider anywhere under the Tree");
            Assert.AreEqual(0, tree.GetComponentsInChildren<TerrainRegion>(true).Length, "no region anywhere under the Tree");
            Assert.AreEqual(0, tree.transform.childCount, "the drawing alone");
        }
    }
}
