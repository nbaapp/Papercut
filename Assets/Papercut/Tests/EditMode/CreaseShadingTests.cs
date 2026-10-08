using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class CreaseShadingTests
    {
        const float Eps = 1e-4f;
        const float W = 11f, H = 8.5f;

        static readonly Vector4[] Lines = new Vector4[CreaseShading.MaxCreases];
        static readonly Vector4[] Info = new Vector4[CreaseShading.MaxCreases];

        static CreaseMark Mark(SheetFace face, Vector2 a, Vector2 b, Vector2 liftedSide, bool inside = true)
            => new(face, a, b, liftedSide, inside);

        static FoldEffect EffectWith(params CreaseMark[] marks)
            => new(default, FoldOutcome.None, null, null, null, null, marks);

        static int Pack(IReadOnlyList<FoldEffect> effects, IReadOnlyList<CreaseMark> remembered, SheetFace face, out bool truncated)
        {
            Array.Clear(Lines, 0, Lines.Length);
            Array.Clear(Info, 0, Info.Length);
            return CreaseShading.Pack(effects, remembered, face, Lines, Info, out truncated);
        }

        // ----- packing -----

        [Test]
        public void Pack_LiveMark_MidpointSideAndHalfLength()
        {
            var live = Mark(SheetFace.Front, new Vector2(-5.5f, -1.25f), new Vector2(5.5f, -1.25f), Vector2.down, inside: true);

            var count = Pack(new[] { EffectWith(live) }, null, SheetFace.Front, out var truncated);

            Assert.AreEqual(1, count);
            Assert.IsFalse(truncated);
            Assert.AreEqual(new Vector4(0f, -1.25f, 0f, -1f), Lines[0]);
            Assert.AreEqual(5.5f, Info[0].x, Eps, "half length");
            Assert.AreEqual(0f, Info[0].y, "inside map");
            Assert.AreEqual(0f, Info[0].z, "live");
        }

        [Test]
        public void Pack_OnlyTheRequestedFace_OutsideMapForAnOutsideMark()
        {
            var front = Mark(SheetFace.Front, new Vector2(-5.5f, 0f), new Vector2(5.5f, 0f), Vector2.up, inside: true);
            var back = Mark(SheetFace.Back, new Vector2(-5.5f, 0f), new Vector2(5.5f, 0f), Vector2.up, inside: false);

            var count = Pack(new[] { EffectWith(front, back) }, null, SheetFace.Back, out _);

            Assert.AreEqual(1, count);
            Assert.AreEqual(1f, Info[0].y, "outside map");
        }

        [Test]
        public void Pack_LiveFirst_ThenRememberedNewestFirst()
        {
            var live = Mark(SheetFace.Front, new Vector2(0f, -4f), new Vector2(0f, 4f), Vector2.right);
            var older = Mark(SheetFace.Front, new Vector2(-5.5f, 1f), new Vector2(5.5f, 1f), Vector2.up);
            var newer = Mark(SheetFace.Front, new Vector2(-5.5f, 2f), new Vector2(5.5f, 2f), Vector2.up);

            var count = Pack(new[] { EffectWith(live) }, new[] { older, newer }, SheetFace.Front, out _);

            Assert.AreEqual(3, count);
            Assert.AreEqual(0f, Info[0].z, "live first");
            Assert.AreEqual(2f, Lines[1].y, Eps, "newest remembered next");
            Assert.AreEqual(1f, Info[1].z, "remembered");
            Assert.AreEqual(1f, Lines[2].y, Eps, "oldest remembered last");
        }

        [Test]
        public void Pack_RememberedRepeat_IsSkipped_EitherWayRound()
        {
            var first = Mark(SheetFace.Front, new Vector2(-5.5f, 1f), new Vector2(5.5f, 1f), Vector2.up);
            var reversed = Mark(SheetFace.Front, new Vector2(5.5f, 1f), new Vector2(-5.5f, 1f), Vector2.up);

            var count = Pack(null, new[] { first, reversed }, SheetFace.Front, out _);

            Assert.AreEqual(1, count);
        }

        [Test]
        public void Pack_RememberedRepeatOfALiveCrease_IsSkipped()
        {
            var live = Mark(SheetFace.Front, new Vector2(-5.5f, 1f), new Vector2(5.5f, 1f), Vector2.up);
            var remembered = Mark(SheetFace.Front, new Vector2(-5.5f, 1f), new Vector2(5.5f, 1f), Vector2.up);

            var count = Pack(new[] { EffectWith(live) }, new[] { remembered }, SheetFace.Front, out _);

            Assert.AreEqual(1, count);
            Assert.AreEqual(0f, Info[0].z, "the live crease stays");
        }

        [Test]
        public void Pack_SameLineFoldedTheOtherWay_IsNotARepeat()
        {
            var southLifted = Mark(SheetFace.Front, new Vector2(-5.5f, 0f), new Vector2(5.5f, 0f), Vector2.down);
            var northLifted = Mark(SheetFace.Front, new Vector2(-5.5f, 0f), new Vector2(5.5f, 0f), Vector2.up);

            var count = Pack(null, new[] { southLifted, northLifted }, SheetFace.Front, out _);

            Assert.AreEqual(2, count);
        }

        [Test]
        public void Pack_EndsOnTheSheetBorder_AreOpen()
        {
            // The north-east corner fold of depth 2: both ends on the border. t = (side.y, -side.x) = (1, -1)/sqrt2,
            // so the end toward +t is B (5.5, 2.25) and toward -t is A (3.5, 4.25).
            var corner = Mark(SheetFace.Front, new Vector2(3.5f, 4.25f), new Vector2(5.5f, 2.25f), new Vector2(1f, 1f).normalized);
            // A crease cut short inside the sheet (fold-on-fold): only its A end is on the border.
            var inner = Mark(SheetFace.Front, new Vector2(-2.5f, 4.25f), new Vector2(-2.5f, 2.25f), Vector2.left);

            Pack(new[] { EffectWith(corner, inner) }, null, SheetFace.Front, out _);

            Assert.AreEqual(CreaseShading.OpenStartFlag | CreaseShading.OpenEndFlag, (int)Info[0].w, "corner crease: both ends open");
            // inner: side (-1, 0) → t = (0, 1); A (y 4.25) is the +t end, on the border.
            Assert.AreEqual(CreaseShading.OpenEndFlag, (int)Info[1].w, "only the end on the border is open");
        }

        [Test]
        public void Pack_ARealCornerFold_HasBothEndsOpen_OnBothFaces()
        {
            SheetLayers.Flat.Apply(new Fold(FoldAnchor.CornerNorthEast, 2f), 0, out var effect);
            var both = CreaseShading.OpenStartFlag | CreaseShading.OpenEndFlag;

            Assert.AreEqual(1, Pack(new[] { effect }, null, SheetFace.Front, out _));
            Assert.AreEqual(both, (int)Info[0].w, "Front: the clipped crease ends lie on the border");
            Assert.AreEqual(1, Pack(new[] { effect }, null, SheetFace.Back, out _));
            Assert.AreEqual(both, (int)Info[0].w, "Back-space: the mirrored ends lie on the border too");
        }

        [Test]
        public void Pack_SameMidpointAcrossTheLine_IsNotARepeat()
        {
            // Two creases crossing at their midpoints: same midpoint and length, different direction.
            var horizontal = Mark(SheetFace.Front, new Vector2(-2f, 0f), new Vector2(2f, 0f), Vector2.up);
            var vertical = Mark(SheetFace.Front, new Vector2(0f, -2f), new Vector2(0f, 2f), Vector2.right);

            var count = Pack(null, new[] { horizontal, vertical }, SheetFace.Front, out _);

            Assert.AreEqual(2, count);
        }

        [Test]
        public void Pack_OtherMapAtTheSamePlace_IsNotARepeat()
        {
            var inside = Mark(SheetFace.Front, new Vector2(-5.5f, 1f), new Vector2(5.5f, 1f), Vector2.up, inside: true);
            var outside = Mark(SheetFace.Front, new Vector2(-5.5f, 1f), new Vector2(5.5f, 1f), Vector2.up, inside: false);

            var count = Pack(null, new[] { inside, outside }, SheetFace.Front, out _);

            Assert.AreEqual(2, count);
        }

        [Test]
        public void Pack_ZeroLengthMark_IsSkipped()
        {
            var dot = Mark(SheetFace.Front, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.up);

            Assert.AreEqual(0, Pack(new[] { EffectWith(dot) }, null, SheetFace.Front, out _));
        }

        [Test]
        public void Pack_Overflow_KeepsLiveAndNewestRemembered()
        {
            var live = Mark(SheetFace.Front, new Vector2(0f, -4f), new Vector2(0f, 4f), Vector2.right);
            var remembered = new List<CreaseMark>();
            for (int i = 0; i < CreaseShading.MaxCreases + 5; i++)
            {
                var y = -4f + i * 0.1f;
                remembered.Add(Mark(SheetFace.Front, new Vector2(-5.5f, y), new Vector2(5.5f, y), Vector2.up));
            }

            var count = Pack(new[] { EffectWith(live) }, remembered, SheetFace.Front, out var truncated);

            Assert.AreEqual(CreaseShading.MaxCreases, count);
            Assert.IsTrue(truncated);
            Assert.AreEqual(0f, Info[0].z, "the live crease is kept");
            Assert.AreEqual(remembered[^1].A.y, Lines[1].y, Eps, "then the newest remembered");
            Assert.AreEqual(remembered[remembered.Count - (CreaseShading.MaxCreases - 1)].A.y, Lines[CreaseShading.MaxCreases - 1].y, Eps,
                "the oldest are the ones dropped");
        }

        [Test]
        public void Pack_ShortArrays_Throw()
        {
            Assert.Throws<ArgumentException>(() => CreaseShading.Pack(null, null, SheetFace.Front, new Vector4[4], new Vector4[4], out _));
        }

        // ----- MapPoint: the shader's reference -----

        static bool MapPoint(Vector2 p, Vector2 mid, Vector2 side, float halfLength, float flip, out Vector2 uv, out float alongSign,
            out float across, out Vector2 t, out Vector2 n, float lineV = 0.5f, float halfWidth = 0.5f, float openEnds = 0f)
            => CreaseShading.MapPoint(p, new Vector4(mid.x, mid.y, side.x, side.y), halfLength, openEnds, flip, W, H, lineV, halfWidth,
                out uv, out alongSign, out across, out t, out n);

        [Test]
        public void MapPoint_HorizontalCrease_LiftedNorth_IsTheImageUpright()
        {
            // A crease along y = 0 whose north side lifted: the image as authored, its top to the north.
            Assert.IsTrue(MapPoint(new Vector2(2.75f, 0.17f), Vector2.zero, Vector2.up, 5.5f, 1f, out var uv, out var sign, out var across, out var t, out var n));
            Assert.AreEqual(0.75f, uv.x, Eps, "a quarter sheet east of the midpoint");
            Assert.AreEqual(0.5f + 0.17f / H, uv.y, Eps);
            Assert.AreEqual(0.17f, across, Eps);
            Assert.AreEqual(1f, sign);
            Assert.AreEqual(Vector2.right, t, "image x = east");
            Assert.AreEqual(Vector2.up, n, "image y = north");
        }

        [Test]
        public void MapPoint_LineHeight_PutsThatRowOnTheCrease()
        {
            Assert.IsTrue(MapPoint(Vector2.zero, Vector2.zero, Vector2.up, 5.5f, 1f, out var uv, out _, out _, out _, out _, lineV: 0.509f));
            Assert.AreEqual(0.509f, uv.y, Eps);
        }

        [Test]
        public void MapPoint_Flip_TurnsTheImageHalfRound()
        {
            Assert.IsTrue(MapPoint(new Vector2(2.75f, 0.17f), Vector2.zero, Vector2.up, 5.5f, -1f, out var uv, out _, out var across, out var t, out var n));
            Assert.AreEqual(0.25f, uv.x, Eps, "east is now the image's left");
            Assert.AreEqual(-0.17f, across, Eps, "north is now the image's bottom");
            Assert.AreEqual(Vector2.left, t);
            Assert.AreEqual(Vector2.down, n);
            Assert.AreEqual(1f, t.x * n.y - t.y * n.x, Eps, "still right-handed: turned, never mirrored");
        }

        [Test]
        public void MapPoint_CornerCrease_FrameIsRightHandedAlongTheCrease()
        {
            var side = new Vector2(1f, 1f).normalized;
            var along = new Vector2(1f, -1f).normalized;
            var mid = new Vector2(4.5f, 3.25f);
            Assert.IsTrue(MapPoint(mid + along * 1.1f + side * 0.2f, mid, side, 1.5f, 1f, out var uv, out _, out var across, out var t, out var n));
            Assert.AreEqual(0.5f + 1.1f / W, uv.x, Eps);
            Assert.AreEqual(0.2f, across, Eps);
            Assert.AreEqual(along.x, t.x, Eps);
            Assert.AreEqual(along.y, t.y, Eps);
            Assert.AreEqual(1f, t.x * n.y - t.y * n.x, Eps);
        }

        [Test]
        public void MapPoint_OutsideTheBand_IsFalse()
        {
            Assert.IsFalse(MapPoint(new Vector2(0f, 0.6f), Vector2.zero, Vector2.up, 5.5f, 1f, out _, out _, out _, out _, out _), "too far across");
            Assert.IsFalse(MapPoint(new Vector2(3.1f, 0f), Vector2.zero, Vector2.up, 3f, 1f, out _, out _, out _, out _, out _), "past the crease's end");
        }

        [Test]
        public void MapPoint_CornerCrease_ShadesTheWedgePastAnEndOnTheBorder()
        {
            // Corner fold NE, depth 2: the mark runs (3.5, 4.25)-(5.5, 2.25). (3.3, 4.2) is on the sheet, 0.18 from the
            // crease line, but past the segment's end - shaded only because that end lies on the border.
            var side = new Vector2(1f, 1f).normalized;
            var mid = new Vector2(4.5f, 3.25f);
            var halfLength = Mathf.Sqrt(2f);
            var p = new Vector2(3.3f, 4.2f);
            Assert.IsFalse(MapPoint(p, mid, side, halfLength, 1f, out _, out _, out _, out _, out _), "closed ends cut square");
            var open = CreaseShading.OpenStartFlag | CreaseShading.OpenEndFlag;
            Assert.IsTrue(MapPoint(p, mid, side, halfLength, 1f, out _, out _, out var across, out _, out _, openEnds: open));
            Assert.AreEqual(Vector2.Dot(p - mid, side), across, Eps);
            Assert.IsTrue(MapPoint(p, mid, side, halfLength, -1f, out _, out _, out _, out _, out _, openEnds: open), "flipped: the same ends stay open");
        }

        [Test]
        public void MapPoint_OnlyTheFlaggedEndIsOpen()
        {
            // Crease along y = 0 from x = -2 to 2, lifted north: t = (1, 0), so the +t end is east.
            Assert.IsTrue(MapPoint(new Vector2(2.3f, 0f), Vector2.zero, Vector2.up, 2f, 1f, out _, out _, out _, out _, out _, openEnds: CreaseShading.OpenEndFlag));
            Assert.IsFalse(MapPoint(new Vector2(-2.3f, 0f), Vector2.zero, Vector2.up, 2f, 1f, out _, out _, out _, out _, out _, openEnds: CreaseShading.OpenEndFlag));
            Assert.IsTrue(MapPoint(new Vector2(2.3f, 0f), Vector2.zero, Vector2.up, 2f, -1f, out _, out _, out _, out _, out _, openEnds: CreaseShading.OpenEndFlag),
                "flipping the image does not move which end is open");
        }

        [Test]
        public void MapPoint_PastTheImageEnds_RepeatsMirrored()
        {
            // A 12-unit crease: 0.5 units past each end of the 11-unit image.
            Assert.IsTrue(MapPoint(new Vector2(5.8f, 0f), Vector2.zero, Vector2.up, 6f, 1f, out var east, out var eastSign, out _, out _, out _));
            Assert.AreEqual(1f - 0.3f / W, east.x, Eps, "0.3 past the right end reads 0.3 back in");
            Assert.AreEqual(-1f, eastSign, "mirrored: the image's x normal is negated");

            Assert.IsTrue(MapPoint(new Vector2(-5.8f, 0f), Vector2.zero, Vector2.up, 6f, 1f, out var west, out var westSign, out _, out _, out _));
            Assert.AreEqual(0.3f / W, west.x, Eps, "0.3 past the left end reads 0.3 back in");
            Assert.AreEqual(-1f, westSign);

            Assert.IsTrue(MapPoint(new Vector2(-5.5f, 0f), Vector2.zero, Vector2.up, 6f, 1f, out var edge, out var edgeSign, out _, out _, out _));
            Assert.AreEqual(0f, edge.x, Eps);
            Assert.AreEqual(1f, edgeSign, "exactly at the image's edge: not yet mirrored, never zero");
        }

        [Test]
        public void MapPoint_BackSpaceMark_UsesItsOwnSpace()
        {
            // The Back mark of a crease along x = 2 (Front-space) lies along x = -2 in Back-space, its side mirrored.
            var frontSide = Vector2.right;
            var backSide = SheetGeometry.BackToFront(frontSide);
            Assert.IsTrue(MapPoint(new Vector2(-2.1f, 0f), new Vector2(-2f, 0f), backSide, 4.25f, 1f, out _, out _, out var across, out _, out _));
            Assert.AreEqual(0.1f, across, Eps, "0.1 toward the lifted side, which is -x in Back-space");
        }

        [Test]
        public void Fade_FullInsideTheStart_ZeroAtTheEdge()
        {
            Assert.AreEqual(1f, CreaseShading.Fade(0.2f, 0.5f, 0.5f), Eps);
            Assert.AreEqual(0.5f, CreaseShading.Fade(0.375f, 0.5f, 0.5f), Eps, "half way through the fade");
            Assert.AreEqual(0f, CreaseShading.Fade(-0.5f, 0.5f, 0.5f), Eps);
        }

        // ----- the shader -----

        static string SheetShadingSource()
        {
            var path = AssetDatabase.GetAssetPath(Shader.Find("Papercut/Sheet Shading"));
            Assert.IsNotEmpty(path, "Papercut/Sheet Shading not found");
            return System.IO.File.ReadAllText(path);
        }

        [Test]
        public void SheetShadingShader_ArrayLength_MatchesMaxCreases()
        {
            var match = System.Text.RegularExpressions.Regex.Match(SheetShadingSource(), @"#define\s+MAX_CREASES\s+(\d+)");
            Assert.IsTrue(match.Success, "MAX_CREASES define not found");
            Assert.AreEqual(CreaseShading.MaxCreases, int.Parse(match.Groups[1].Value));
        }

        [Test]
        public void SheetShadingShader_ClaimsEachPixelOnce_ThroughTheStencil()
        {
            // Without it a pixel under several layers is redrawn once per layer: the topmost part draws first and
            // must keep the lower ones off its pixels.
            var source = System.Text.RegularExpressions.Regex.Replace(SheetShadingSource(), @"\s+", " ");
            StringAssert.Contains("Ref 128", source);
            StringAssert.Contains("ReadMask 128", source);
            StringAssert.Contains("WriteMask 128", source);
            StringAssert.Contains("Comp NotEqual", source);
            StringAssert.Contains("Pass Replace", source);
            StringAssert.Contains("Blend One Zero", source);
        }

        [Test]
        public void ShadingOrder_TopmostLayerFirst()
        {
            Assert.AreEqual(1, RenderTextureFoldRenderer.ShadingOrderOf(2, 3), "the top layer draws first");
            Assert.AreEqual(3, RenderTextureFoldRenderer.ShadingOrderOf(0, 3), "the Base draws last");
            Assert.Less(RenderTextureFoldRenderer.ShadingOrderOf(1, 3), RenderTextureFoldRenderer.ShadingOrderOf(0, 3));
            Assert.AreEqual(1, RenderTextureFoldRenderer.ShadingOrderOf(0, 1), "flat: the only part (its sorting layer puts it after Default)");
        }

        [Test]
        public void SheetShadingShader_HasEveryPropertyTheRendererSets()
        {
            var shader = Shader.Find("Papercut/Sheet Shading");
            Assert.IsNotNull(shader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "Sheet Shading has compile errors");
            foreach (var name in RenderTextureFoldRenderer.ShadingShaderProperties)
                Assert.GreaterOrEqual(shader.FindPropertyIndex(name), 0, name);
        }

        [Test]
        public void PaperFaceShader_HasEveryPropertyTheRendererSets()
        {
            var shader = Shader.Find("Papercut/Paper Face");
            Assert.IsNotNull(shader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader), "Paper Face has compile errors");
            foreach (var name in RenderTextureFoldRenderer.FaceShaderProperties)
                Assert.GreaterOrEqual(shader.FindPropertyIndex(name), 0, name);
        }
    }
}
