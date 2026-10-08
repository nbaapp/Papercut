using System;
using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The warp half of the Papercut/Sheet Shading shader, on the CPU side (Aaron, 2026-10-08: what is on the sheet
    /// should bend with its wrinkles, not sit behind them like a window). Each shading part redraws its pixels from a
    /// copy of the screen sampled a little way off: drawn in toward each crease by a smooth pinch (what a fold does to
    /// a drawing seen from above), and moved by the grain's broad wrinkles. A first version moved pixels by the maps'
    /// slopes and read as a smear (Aaron: "a smearing effect instead of the things just following the folds"). This
    /// packs the outline a part's sample must never cross and is the tested reference for the shader's warp maths.
    /// </summary>
    /// <remarks>
    /// A part shades the pixels where its layer is the topmost; the edges of its own layer and of every layer above
    /// bound that region, so the offset is held short of all of them (by a screen pixel's diagonal, the bilinear
    /// footprint whatever the edge's angle) and nothing is pulled across a Flap's edge, a Seam or the sheet's edge. Crease warps combine as
    /// <c>Σ|v|²·v / Σ|v|²</c>: continuous where creases cross, and a repeated crease averages to itself.
    /// </remarks>
    public static class SheetWarp
    {
        /// <summary>Length of the shader's outline array (MAX_OUTLINE): the most edges one part can be held inside.</summary>
        public const int MaxOutlineSegments = 64;

        /// <summary>Two edges whose ends match within this (either way round) are one edge.</summary>
        const float SameEdgeDistance = 1e-4f;
        const float MinEdgeLength = 1e-4f;

        /// <summary>
        /// Packs the edges of layers <paramref name="fromLayer"/>..top of <paramref name="layers"/> (sheet-local, as
        /// (a.xy, b.xy)), each edge once. Returns the count, or −1 when they do not fit (<paramref name="truncated"/>
        /// set): that part must not warp.
        /// </summary>
        public static int PackOutline(SheetLayers layers, int fromLayer, Vector4[] segments, out bool truncated)
        {
            if (layers == null || fromLayer < 0 || fromLayer >= layers.Layers.Count)
                throw new ArgumentOutOfRangeException(nameof(fromLayer));
            var pieces = new List<ConvexPolygon>(layers.Layers.Count - fromLayer);
            for (int k = fromLayer; k < layers.Layers.Count; k++)
                pieces.Add(layers.Layers[k].Desk);
            return PackEdges(pieces, segments, out truncated);
        }

        /// <summary>
        /// Packs the edges of <paramref name="pieces"/> (as (a.xy, b.xy)), each edge once, skipping zero-length ones.
        /// Returns the count, or −1 when they do not fit (<paramref name="truncated"/> set).
        /// </summary>
        public static int PackEdges(IReadOnlyList<ConvexPolygon> pieces, Vector4[] segments, out bool truncated)
        {
            if (segments == null || segments.Length < MaxOutlineSegments)
                throw new ArgumentException($"The outline array must hold {MaxOutlineSegments} entries.");

            var count = 0;
            truncated = false;
            foreach (var piece in pieces)
            {
                var vertices = piece.Vertices;
                for (int i = 0; i < vertices.Count; i++)
                {
                    var a = vertices[i];
                    var b = vertices[(i + 1) % vertices.Count];
                    if (Vector2.Distance(a, b) < MinEdgeLength || Contains(segments, count, a, b))
                        continue;
                    if (count >= MaxOutlineSegments)
                    {
                        truncated = true;
                        return -1;
                    }
                    segments[count++] = new Vector4(a.x, a.y, b.x, b.y);
                }
            }
            return count;
        }

        static bool Contains(Vector4[] segments, int count, Vector2 a, Vector2 b)
        {
            for (int i = 0; i < count; i++)
            {
                var sa = new Vector2(segments[i].x, segments[i].y);
                var sb = new Vector2(segments[i].z, segments[i].w);
                if ((Vector2.Distance(sa, a) <= SameEdgeDistance && Vector2.Distance(sb, b) <= SameEdgeDistance)
                    || (Vector2.Distance(sa, b) <= SameEdgeDistance && Vector2.Distance(sb, a) <= SameEdgeDistance))
                    return true;
            }
            return false;
        }

        /// <summary>Distance from <paramref name="p"/> to the nearest packed edge; 0 when <paramref name="count"/> is negative (no warp).</summary>
        public static float DistanceToOutline(Vector2 p, Vector4[] segments, int count)
        {
            if (count < 0)
                return 0f;
            var best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var a = new Vector2(segments[i].x, segments[i].y);
                var ab = new Vector2(segments[i].z, segments[i].w) - a;
                var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        /// <summary>
        /// <paramref name="offset"/> held short of the outline: no longer than the distance to it less
        /// <paramref name="margin"/> (sheet units; the shader passes a screen pixel's diagonal,
        /// <c>|ddx| + |ddy|</c>, the reach of a bilinear sample at any edge angle), so a sample at <c>p + offset</c>
        /// stays inside.
        /// </summary>
        public static Vector2 FadedOffset(Vector2 p, Vector2 offset, Vector4[] segments, int count, float margin)
        {
            var maxLength = Mathf.Max(DistanceToOutline(p, segments, count) - margin, 0f);
            var length = offset.magnitude;
            return length > maxLength ? offset * (maxLength / Mathf.Max(length, 1e-6f)) : offset;
        }

        /// <summary>
        /// A face-space vector (a slope or a warp) carried into sheet-local space by a piece's pose, as the shader's
        /// _GrainFrame (columns: the images of the face's x and y axes; see RenderTextureFoldRenderer.GrainFrameOf).
        /// </summary>
        public static Vector2 FaceToDesk(Vector2 face, Vector4 frame)
            => new Vector2(frame.x, frame.y) * face.x + new Vector2(frame.z, frame.w) * face.y;

        /// <summary>
        /// How much of a crease's warp survives near the ends of its segment: 1 inside, easing to 0 at a closed end
        /// (one not on the sheet border) over <paramref name="halfWidth"/>, so a crease ending mid-sheet does not tear;
        /// 1 toward an open end. <paramref name="unflippedAlong"/> is measured along the unflipped frame
        /// (<see cref="CreaseShading.MapPoint"/>).
        /// </summary>
        public static float EndTaper(float unflippedAlong, float halfLength, float openEnds, float halfWidth)
        {
            var flags = Mathf.RoundToInt(openEnds);
            var taper = 1f;
            if ((flags & CreaseShading.OpenStartFlag) == 0)
                taper = Mathf.Min(taper, Mathf.Clamp01((halfLength + unflippedAlong) / halfWidth));
            if ((flags & CreaseShading.OpenEndFlag) == 0)
                taper = Mathf.Min(taper, Mathf.Clamp01((halfLength - unflippedAlong) / halfWidth));
            return taper;
        }

        /// <summary>Largest pinch whose mapping stays one-to-one: past 3 the drawing would fold over itself at the band's outer third.</summary>
        public const float MaxPinch = 2.5f;

        /// <summary>Smallest (most negative: a bulge) pinch whose mapping stays one-to-one: at −1 the crease line itself folds over.</summary>
        public const float MinPinch = -0.9f;

        /// <summary>
        /// The pinch profile across a crease's band: how far from the crease a point <paramref name="across"/> from it
        /// samples beyond itself, <c>sign(a)·w·x·(1−x)²</c> with <c>x = |a|/w</c>. Zero on the crease and at the band's
        /// edge, outward between, so what is drawn is drawn in toward the crease, smoothly; at pinch strength k the
        /// mapping <c>a → a + k·Pinch(a)</c> is one-to-one for −1 &lt; k &lt; 3.
        /// </summary>
        public static float Pinch(float across, float halfWidth)
        {
            var x = Mathf.Clamp01(Mathf.Abs(across) / halfWidth);
            return (across < 0f ? -1f : across > 0f ? 1f : 0f) * halfWidth * x * (1f - x) * (1f - x);
        }

        /// <summary>
        /// One crease's warp at a point, in face space: where to sample, along the crease's normal
        /// <paramref name="n"/>, by the pinch profile × the end taper × the map's pinch strength × (for a remembered
        /// crease) <paramref name="rememberedPinch"/>. Independent of the maps' pixels and the shading strengths.
        /// </summary>
        public static Vector2 CreaseWarp(float across, Vector2 n, float halfWidth, float taper, float pinch, bool remembered, float rememberedPinch)
            => n * (Pinch(across, halfWidth) * taper * pinch * (remembered ? rememberedPinch : 1f));

        /// <summary>
        /// The offset (sheet-local) a pixel samples the screen copy at, before the outline clamp: the combined crease
        /// warp, less the grain's broad slope (its blurred mip less its average) × <paramref name="grainWarp"/> — what is
        /// drawn slides down the broad wrinkles — carried from the face into the sheet by the piece's frame.
        /// </summary>
        public static Vector2 Offset(Vector2 broadGrainSlope, float grainWarp, Vector2 creaseWarp, Vector4 frame)
            => FaceToDesk(creaseWarp - broadGrainSlope * grainWarp, frame);

        /// <summary>Crease warps combined as <c>Σ|v|²·v / Σ|v|²</c>: continuous, and a repeat averages to itself.</summary>
        public static Vector2 CombineWarps(IReadOnlyList<Vector2> warps)
        {
            var sum = Vector2.zero;
            var weight = 0f;
            foreach (var v in warps)
            {
                var w = v.sqrMagnitude;
                sum += v * w;
                weight += w;
            }
            return weight > 1e-12f ? sum / weight : Vector2.zero;
        }
    }
}
