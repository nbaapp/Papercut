using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>The flat-point-to-texture mapping a mesh uses to draw a sprite frame at its own scale.</summary>
    public sealed class SpriteMappingTests
    {
        /// <summary>A 2100 px canvas, centre pivot, 2000 px per unit: the hand-drawn element frames.</summary>
        static readonly SpriteFrame Canvas = new(new Rect(0f, 0f, 2100f, 2100f), new Vector2(1050f, 1050f), 2000f, new Vector2(2100f, 2100f));

        static void AssertUv(Vector2 expected, Vector2 actual, string why)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-5f, why + " (u)");
            Assert.AreEqual(expected.y, actual.y, 1e-5f, why + " (v)");
        }

        [Test]
        public void Anchor_MapsToThePivot()
        {
            var anchor = new Vector2(3.25f, -1.5f);
            AssertUv(new Vector2(0.5f, 0.5f), SpriteMapping.Uv(Canvas, anchor, anchor), "the pivot sits at the anchor");
        }

        [Test]
        public void OneCanvasWidthRight_IsOneWholeTextureAcross()
        {
            var anchor = new Vector2(1f, 1f);
            AssertUv(new Vector2(1.5f, 0.5f), SpriteMapping.Uv(Canvas, anchor, anchor + new Vector2(1.05f, 0f)), "2100 px / 2000 ppu = 1.05 units per texture");
        }

        [Test]
        public void CanvasCorner_MapsToUvCorner()
        {
            var anchor = Vector2.zero;
            AssertUv(new Vector2(0f, 1f), SpriteMapping.Uv(Canvas, anchor, new Vector2(-0.525f, 0.525f)), "top-left of the canvas");
            AssertUv(new Vector2(1f, 0f), SpriteMapping.Uv(Canvas, anchor, new Vector2(0.525f, -0.525f)), "bottom-right of the canvas");
        }

        [Test]
        public void FrameRectInsideALargerTexture_ShiftsByTheRectOrigin()
        {
            var frame = new SpriteFrame(new Rect(100f, 200f, 400f, 400f), new Vector2(200f, 200f), 400f, new Vector2(1000f, 1000f));
            var anchor = new Vector2(-2f, 5f);
            AssertUv(new Vector2(0.3f, 0.4f), SpriteMapping.Uv(frame, anchor, anchor), "rect origin + pivot");
            AssertUv(new Vector2(0.5f, 0.2f), SpriteMapping.Uv(frame, anchor, anchor + new Vector2(0.5f, -0.5f)), "half a unit = 200 px");
        }

        [Test]
        public void BlockDrawing_OnTheFront_IsLaidOutInFlatSpace()
        {
            var rect = new Rect(2.5f, -1.5f, 0.74f, 0.68f);
            var centre = rect.center;
            AssertUv(new Vector2(0.5f, 0.5f), PushableBlock.DrawingUv(Canvas, rect, SheetFace.Front, centre), "pivot at the centre");
            var right = PushableBlock.DrawingUv(Canvas, rect, SheetFace.Front, centre + new Vector2(0.2625f, 0f));
            AssertUv(new Vector2(0.75f, 0.5f), right, "a quarter canvas to the right of the centre reads the right half of the frame");
        }

        [Test]
        public void BlockDrawing_OnTheBack_IsLaidOutInBackSpace_LikeEveryOtherBackContent()
        {
            // Bible §6: Back content is authored in Back-space, where point (x, y) lies beneath Front point (−x, y).
            // A Back-side block's drawing must read as authored (as the Studio's Back pane shows it), so a flat
            // point to the RIGHT of the centre (Front-space +x) is to the LEFT in Back-space and reads the frame's
            // left half — the mirror of the Front case above. The layer's reflection then carries it with the paper.
            var rect = new Rect(2.5f, -1.5f, 0.74f, 0.68f);
            var centre = rect.center;
            AssertUv(new Vector2(0.5f, 0.5f), PushableBlock.DrawingUv(Canvas, rect, SheetFace.Back, centre), "pivot at the centre");
            var right = PushableBlock.DrawingUv(Canvas, rect, SheetFace.Back, centre + new Vector2(0.2625f, 0f));
            AssertUv(new Vector2(0.25f, 0.5f), right, "Front +x is Back −x");
            var up = PushableBlock.DrawingUv(Canvas, rect, SheetFace.Back, centre + new Vector2(0f, 0.2625f));
            AssertUv(new Vector2(0.5f, 0.75f), up, "y is not mirrored");
        }

        [Test]
        public void SpriteFrame_Of_ReadsRectPivotScaleAndTextureSize()
        {
            var texture = new Texture2D(64, 32);
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 32f), new Vector2(0.25f, 0.5f), 16f);
            try
            {
                var frame = SpriteFrame.Of(sprite);
                Assert.AreEqual(new Rect(0f, 0f, 64f, 32f), frame.Rect);
                Assert.AreEqual(16f, frame.Pivot.x, 1e-4f, "pivot 0.25 of 64 px");
                Assert.AreEqual(16f, frame.Pivot.y, 1e-4f, "pivot 0.5 of 32 px");
                Assert.AreEqual(16f, frame.PixelsPerUnit, 1e-4f);
                Assert.AreEqual(new Vector2(64f, 32f), frame.TextureSize);
                // The frame spans 4 x 2 units; its pivot is 1 unit from the left edge.
                var uv = SpriteMapping.Uv(frame, Vector2.zero, new Vector2(-1f, -1f));
                AssertUv(Vector2.zero, uv, "bottom-left of the frame");
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }
    }
}
