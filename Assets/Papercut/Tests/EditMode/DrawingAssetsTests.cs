using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>
    /// The hand-drawn element art (2026-09-03): the three line-boil animations are playable, imported at the
    /// shared scale as single full-canvas sprites, and each element prefab is wired to its drawing.
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

        /// <summary>Every hand-drawn frame is a 2100 px canvas at 2000 px per unit (the Scuffy convention).</summary>
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
                Assert.AreEqual(CanvasUnits, sprite.rect.width / sprite.pixelsPerUnit, 0.01f, $"{path} frame {i}: shared scale");
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
            Assert.AreEqual(-0.05f, drawing.transform.localPosition.z, 1e-5f,
                "at the element z: at 0 it would sit on the Surface quad and behind the face art, invisible in the Studio and Scene view");
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
