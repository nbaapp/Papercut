using System;
using System.Collections.Generic;
using UnityEngine;

namespace Papercut
{
    /// <summary>
    /// The crease half of the Papercut/Sheet Shading shader, on the CPU side: packs one face's crease marks into the
    /// shader's fixed-size uniform arrays, and <see cref="MapPoint"/> is the reference for how the shader lays a
    /// crease normal map along a mark (the shader mirrors it line for line, so the maths can be tested here).
    /// </summary>
    /// <remarks>
    /// A crease map is an image of a whole sheet with one horizontal crease across it. It is laid along each mark
    /// at <c>mapWidth</c> sheet units to the image's width, centred on the mark's midpoint, with the image's crease
    /// line on the mark and the image's top toward the side that lifted (turned half round when flipped). Past the
    /// image's ends — a 45° corner crease can be longer than the sheet is wide — it repeats mirrored. A crease's band
    /// is cut square at the ends of its segment, except at an end on the sheet's border: there the paper past the end
    /// (the wedge between a 45° crease and the edge) is still beside the crease, and the piece's own outline clips
    /// whatever lies off the sheet.
    /// </remarks>
    public static class CreaseShading
    {
        /// <summary>Length of the shader's crease arrays: the most creases one face can shade at once.</summary>
        public const int MaxCreases = 64;

        /// <summary>Two marks whose midpoints, half lengths and lifted sides each differ by no more than this are the same crease.</summary>
        const float SameMarkDistance = 1e-3f;
        const float MinMarkLength = 1e-4f;
        /// <summary>A mark's end this close to the sheet's border lies on it.</summary>
        const float BorderDistance = 1e-3f;

        /// <summary><c>info.w</c> flags: the end toward −t (t = the lifted side turned a quarter clockwise) lies on the sheet border.</summary>
        public const int OpenStartFlag = 1;
        /// <summary><c>info.w</c> flags: the end toward +t lies on the sheet border.</summary>
        public const int OpenEndFlag = 2;

        /// <summary>
        /// Packs <paramref name="face"/>'s creases for the shader: every live mark of <paramref name="effects"/>
        /// first, in fold order, then the remembered marks newest first, skipping any that repeats a crease already
        /// packed. <c>lines[i]</c> = (midpoint.xy, lifted side.xy) and <c>info[i]</c> = (half length, map: 0 inside / 1
        /// outside, remembered: 0 / 1, open ends: <see cref="OpenStartFlag"/> | <see cref="OpenEndFlag"/>), in the
        /// face's authored space. Returns the count; past
        /// <see cref="MaxCreases"/> the rest (the oldest remembered first) are dropped and <paramref name="truncated"/> is set.
        /// </summary>
        public static int Pack(IReadOnlyList<FoldEffect> effects, IReadOnlyList<CreaseMark> remembered, SheetFace face,
            Vector4[] lines, Vector4[] info, out bool truncated)
        {
            if (lines == null || info == null || lines.Length < MaxCreases || info.Length < MaxCreases)
                throw new ArgumentException($"Crease arrays must hold {MaxCreases} entries.");

            var count = 0;
            truncated = false;
            if (effects != null)
            {
                foreach (var effect in effects)
                {
                    foreach (var mark in effect.CreaseMarks)
                    {
                        if (mark.Face == face)
                            Add(mark, false, lines, info, ref count, ref truncated);
                    }
                }
            }
            if (remembered != null)
            {
                for (int i = remembered.Count - 1; i >= 0; i--)
                {
                    var mark = remembered[i];
                    if (mark.Face == face && !AlreadyPacked(mark, lines, info, count))
                        Add(mark, true, lines, info, ref count, ref truncated);
                }
            }
            return count;
        }

        static void Add(in CreaseMark mark, bool isRemembered, Vector4[] lines, Vector4[] info, ref int count, ref bool truncated)
        {
            var halfLength = Vector2.Distance(mark.A, mark.B) * 0.5f;
            if (halfLength * 2f < MinMarkLength)
                return;
            if (count >= MaxCreases)
            {
                truncated = true;
                return;
            }
            var mid = (mark.A + mark.B) * 0.5f;
            var side = mark.LiftedSide.normalized;
            var t = new Vector2(side.y, -side.x);
            var aTowardEnd = Vector2.Dot(mark.A - mid, t) > 0f;
            var openEnds = 0;
            if (OnBorder(mark.A)) openEnds |= aTowardEnd ? OpenEndFlag : OpenStartFlag;
            if (OnBorder(mark.B)) openEnds |= aTowardEnd ? OpenStartFlag : OpenEndFlag;
            lines[count] = new Vector4(mid.x, mid.y, side.x, side.y);
            info[count] = new Vector4(halfLength, mark.Inside ? 0f : 1f, isRemembered ? 1f : 0f, openEnds);
            count++;
        }

        /// <summary>True if a face-space point lies on the sheet's border (Front- and Back-space share the sheet rect).</summary>
        static bool OnBorder(Vector2 p)
        {
            var half = SheetGeometry.HalfSize;
            return Mathf.Abs(Mathf.Abs(p.x) - half.x) <= BorderDistance || Mathf.Abs(Mathf.Abs(p.y) - half.y) <= BorderDistance;
        }

        /// <summary>
        /// True if a packed entry is the same crease as <paramref name="mark"/>: same map, same segment (either way
        /// round) and folded the same way. A crease folded the other way along the same line shows its image turned
        /// half round, so it is a different crease and both are kept.
        /// </summary>
        static bool AlreadyPacked(in CreaseMark mark, Vector4[] lines, Vector4[] info, int count)
        {
            var map = mark.Inside ? 0f : 1f;
            var mid = (mark.A + mark.B) * 0.5f;
            var halfLength = Vector2.Distance(mark.A, mark.B) * 0.5f;
            var side = mark.LiftedSide.normalized;
            for (int i = 0; i < count; i++)
            {
                if (info[i].y != map)
                    continue;
                if (Vector2.Distance(new Vector2(lines[i].x, lines[i].y), mid) > SameMarkDistance
                    || Mathf.Abs(info[i].x - halfLength) > SameMarkDistance)
                    continue;
                // Same midpoint and length, and the same lifted side (so the same line, folded the same way).
                if (Vector2.Distance(new Vector2(lines[i].z, lines[i].w), side) <= SameMarkDistance)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Where face point <paramref name="p"/> samples a crease map laid along a packed crease (the shader's
        /// reference). <paramref name="line"/>, <paramref name="halfLength"/> and <paramref name="openEnds"/> are a packed entry;
        /// <paramref name="flip"/> is +1 or −1; <paramref name="mapWidth"/> / <paramref name="mapHeight"/> are the
        /// image's size in sheet units; <paramref name="lineV"/> is the image's crease line height (0 bottom, 1 top).
        /// Returns false outside the crease's band (past an end not on the sheet border, or more than
        /// <paramref name="halfWidth"/> from it).
        /// <paramref name="alongSign"/> is −1 where the image repeats mirrored, so the map's x normal must be negated;
        /// (<paramref name="t"/>, <paramref name="n"/>) is the face-space frame of the image's (x, y) normal axes.
        /// </summary>
        public static bool MapPoint(Vector2 p, Vector4 line, float halfLength, float openEnds, float flip, float mapWidth, float mapHeight,
            float lineV, float halfWidth, out Vector2 uv, out float alongSign, out float across, out Vector2 t, out Vector2 n)
        {
            n = new Vector2(line.z, line.w) * flip;
            t = new Vector2(n.y, -n.x); // (t, n) has the image's handedness: a flip turns it half round, never mirrors it
            var rel = p - new Vector2(line.x, line.y);
            var along = Vector2.Dot(rel, t);
            across = Vector2.Dot(rel, n);
            uv = default;
            alongSign = 1f;
            var unflippedAlong = along * flip; // the open-end flags are relative to the unflipped frame
            var flags = Mathf.RoundToInt(openEnds);
            if ((unflippedAlong < -halfLength && (flags & OpenStartFlag) == 0)
                || (unflippedAlong > halfLength && (flags & OpenEndFlag) == 0)
                || Mathf.Abs(across) > halfWidth)
                return false;

            var x = 0.5f + along / mapWidth;
            var m = Mathf.Abs(x) % 2f;
            var forward = m <= 1f;
            alongSign = (forward ? 1f : -1f) * (x >= 0f ? 1f : -1f);
            uv = new Vector2(forward ? m : 2f - m, lineV + across / mapHeight);
            return true;
        }

        /// <summary>Weight of a crease at <paramref name="across"/> from its line: 1 out to <paramref name="fadeStart"/> of the band, easing to 0 at its edge.</summary>
        public static float Fade(float across, float halfWidth, float fadeStart)
            => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, 1f, Mathf.Abs(across) / halfWidth));
    }
}
