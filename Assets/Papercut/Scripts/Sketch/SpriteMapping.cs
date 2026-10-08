using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// What a mesh needs to know about a sprite frame to texture itself with it: where the frame sits on its
    /// texture, its pivot, and its scale. Plain data so the mapping is testable without a <see cref="Sprite"/>.
    /// </summary>
    public readonly struct SpriteFrame
    {
        /// <summary>The frame's rect on its texture, in texture pixels.</summary>
        public readonly Rect Rect;

        /// <summary>The pivot, in pixels from <see cref="Rect"/>'s min corner.</summary>
        public readonly Vector2 Pivot;

        public readonly float PixelsPerUnit;

        /// <summary>The whole texture's size, in pixels.</summary>
        public readonly Vector2 TextureSize;

        public SpriteFrame(Rect rect, Vector2 pivot, float pixelsPerUnit, Vector2 textureSize)
        {
            Rect = rect;
            Pivot = pivot;
            PixelsPerUnit = pixelsPerUnit;
            TextureSize = textureSize;
        }

        /// <summary>
        /// The frame of a sprite. <c>rect</c>, <c>pivot</c> and <c>pixelsPerUnit</c> are all in the imported
        /// texture's pixels (Unity rescales the pixels-per-unit with a max-size reduction, so world size holds).
        /// </summary>
        public static SpriteFrame Of(Sprite sprite)
            => new(sprite.rect, sprite.pivot, sprite.pixelsPerUnit, new Vector2(sprite.texture.width, sprite.texture.height));
    }

    /// <summary>
    /// Maps points of a flat surface onto a sprite frame drawn at its own scale: the frame's pivot sits at an
    /// anchor point, and one world unit is <see cref="SpriteFrame.PixelsPerUnit"/> texture pixels.
    /// Used by <see cref="PushableBlock"/> to texture its clipped mesh with a hand-drawn frame. Pure.
    /// </summary>
    public static class SpriteMapping
    {
        /// <summary>
        /// The texture UV (0..1 over the whole texture) of <paramref name="point"/> when the frame's pivot is at
        /// <paramref name="anchor"/>. Points outside the frame give UVs outside its rect; a mesh must not draw them
        /// (<see cref="PushableBlock.DrawingRect"/>), as a clamped texture repeats its edge pixels there.
        /// </summary>
        public static Vector2 Uv(in SpriteFrame frame, Vector2 anchor, Vector2 point)
        {
            var pixel = frame.Rect.position + frame.Pivot + (point - anchor) * frame.PixelsPerUnit;
            return new Vector2(pixel.x / frame.TextureSize.x, pixel.y / frame.TextureSize.y);
        }
    }
}
