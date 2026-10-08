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
        public void BlockDrawing_IsLaidOutUprightAroundTheDeskCentre()
        {
            var centre = new Vector2(2.87f, -1.16f);
            AssertUv(new Vector2(0.5f, 0.5f), PushableBlock.DrawingUv(Canvas, centre, centre), "pivot at the centre");
            var right = PushableBlock.DrawingUv(Canvas, centre, centre + new Vector2(0.2625f, 0f));
            AssertUv(new Vector2(0.75f, 0.5f), right, "a quarter canvas to the right of the centre reads the right half of the frame");
            var up = PushableBlock.DrawingUv(Canvas, centre, centre + new Vector2(0f, 0.2625f));
            AssertUv(new Vector2(0.5f, 0.75f), up, "a quarter canvas above the centre reads the top half of the frame");
        }

        [Test]
        public void BlockDrawing_OnAFlap_IsNotMirrored()
        {
            // A Front block at flat (3, 0) lifted by an east fold of depth 2 (crease x = 3.5) lands at desk (4, 0), Back-up.
            // Its drawing is laid out in Desk space around the landed centre, so desk +x still reads the frame's right
            // half: the Flap's reflection carries the block but never its picture (Aaron, 2026-09-24).
            var stack = SheetLayers.Flat.Apply(new Fold(FoldAnchor.EdgeEast, 2f), 0, out _);
            var flatCentre = new Vector2(3f, 0f);
            var layer = SheetPlacement.LayerContaining(stack, new Vector2(4.5f, 0f)); // The lifted piece's flat coordinates are its Original's.
            var deskCentre = stack.Layers[layer].ToDesk.Apply(flatCentre);
            Assert.AreEqual(4f, deskCentre.x, 1e-4f, "landed mirror of x = 3 across the crease at 3.5");
            var right = PushableBlock.DrawingUv(Canvas, deskCentre, deskCentre + new Vector2(0.2625f, 0f));
            AssertUv(new Vector2(0.75f, 0.5f), right, "desk +x reads the frame's right half on the Flap too");
        }

        [Test]
        public void DrawingRect_IsTheFrameAtItsOwnScale_PivotAtTheCentre()
        {
            var centre = new Vector2(2.87f, -1.16f);
            var drawn = PushableBlock.DrawingRect(Canvas, centre);
            Assert.AreEqual(centre.x, drawn.center.x, 1e-5f, "centred on the block");
            Assert.AreEqual(centre.y, drawn.center.y, 1e-5f, "centred on the block");
            Assert.AreEqual(1.05f, drawn.width, 1e-5f, "2100 px / 2000 ppu");
            Assert.AreEqual(1.05f, drawn.height, 1e-5f, "2100 px / 2000 ppu");
        }

        [Test]
        public void DrawingRect_OffCentrePivot_ShiftsTheFrame_AndAgreesWithTheUvMapping()
        {
            // A 400 x 200 px frame at 400 ppu (1 x 0.5 units) whose pivot is 100 px from its left edge and 50 px from
            // its bottom: the frame reaches 0.25 units left of the centre and 0.75 right, 0.125 below and 0.375 above.
            var frame = new SpriteFrame(new Rect(0f, 0f, 400f, 200f), new Vector2(100f, 50f), 400f, new Vector2(400f, 200f));
            var centre = new Vector2(2.87f, -1.16f);
            var drawn = PushableBlock.DrawingRect(frame, centre);
            Assert.AreEqual(centre.x - 0.25f, drawn.xMin, 1e-5f, "100 px left of the pivot");
            Assert.AreEqual(centre.x + 0.75f, drawn.xMax, 1e-5f, "300 px right of the pivot");
            Assert.AreEqual(centre.y - 0.125f, drawn.yMin, 1e-5f, "50 px below the pivot");
            Assert.AreEqual(centre.y + 0.375f, drawn.yMax, 1e-5f, "150 px above the pivot");
            // The rect and the UV mapping agree: the frame's corners map to the frame's UV corners.
            AssertUv(new Vector2(0f, 0f), PushableBlock.DrawingUv(frame, centre, new Vector2(drawn.xMin, drawn.yMin)), "xMin/yMin is the frame's bottom-left");
            AssertUv(new Vector2(1f, 1f), PushableBlock.DrawingUv(frame, centre, new Vector2(drawn.xMax, drawn.yMax)), "xMax/yMax is the frame's top-right");
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
