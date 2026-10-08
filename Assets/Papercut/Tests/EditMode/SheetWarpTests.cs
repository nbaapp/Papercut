using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class SheetWarpTests
    {
        const float Eps = 1e-4f;
        const float HW = 5.5f, HH = 4.25f;

        static readonly Vector4[] Segments = new Vector4[SheetWarp.MaxOutlineSegments];

        static SheetLayers Replay(params Fold[] folds)
        {
            var stack = SheetLayers.Flat;
            for (int i = 0; i < folds.Length; i++)
                stack = stack.Apply(folds[i], i, out _);
            return stack;
        }

        // ----- outline -----

        [Test]
        public void PackOutline_FlatSheet_IsItsFourEdges()
        {
            var count = SheetWarp.PackOutline(SheetLayers.Flat, 0, Segments, out var truncated);

            Assert.AreEqual(4, count);
            Assert.IsFalse(truncated);
        }

        [Test]
        public void PackOutline_TopLayer_IsOnlyItsOwnEdges_BaseAlsoGetsTheFlaps()
        {
            var stack = Replay(new Fold(FoldAnchor.EdgeSouth, 2f));

            var flapCount = SheetWarp.PackOutline(stack, 1, Segments, out _);
            Assert.AreEqual(4, flapCount, "the landed Flap is a rect");

            var baseCount = SheetWarp.PackOutline(stack, 0, Segments, out _);
            Assert.Greater(baseCount, flapCount, "the Base part is held inside its own edges and the Flap's");
            Assert.Less(baseCount, 8, "the edge they share along the crease is packed once");
        }

        [Test]
        public void PackEdges_TooManyEdges_IsMinusOne_AndTruncated()
        {
            // 17 separate squares: 68 edges, more than fit.
            var squares = Enumerable.Range(0, 17)
                .Select(i => ConvexPolygon.FromRect(new Rect(i * 2f, 0f, 1f, 1f))).ToList();

            var count = SheetWarp.PackEdges(squares, Segments, out var truncated);

            Assert.AreEqual(-1, count);
            Assert.IsTrue(truncated);
            Assert.AreEqual(64, SheetWarp.PackEdges(squares.Take(16).ToList(), Segments, out truncated), "16 squares fit exactly");
            Assert.IsFalse(truncated);
        }

        [Test]
        public void PackOutline_BadArguments_Throw()
        {
            Assert.Throws<ArgumentException>(() => SheetWarp.PackOutline(SheetLayers.Flat, 0, new Vector4[4], out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => SheetWarp.PackOutline(SheetLayers.Flat, 1, Segments, out _));
        }

        [Test]
        public void DistanceToOutline_FlatSheet_IsTheNearestEdge_ZeroWhenNoWarp()
        {
            var count = SheetWarp.PackOutline(SheetLayers.Flat, 0, Segments, out _);

            Assert.AreEqual(HH - 1f, SheetWarp.DistanceToOutline(new Vector2(0f, 1f), Segments, count), Eps);
            Assert.AreEqual(0.5f, SheetWarp.DistanceToOutline(new Vector2(HW - 0.5f, 0f), Segments, count), Eps);
            Assert.AreEqual(0f, SheetWarp.DistanceToOutline(Vector2.zero, Segments, -1), "count -1: the part must not warp");
        }

        [Test]
        public void FadedOffset_FarFromAnEdge_IsWhole_AtTheEdge_IsNone()
        {
            var count = SheetWarp.PackOutline(SheetLayers.Flat, 0, Segments, out _);
            var offset = new Vector2(0.05f, 0f);

            Assert.AreEqual(offset, SheetWarp.FadedOffset(Vector2.zero, offset, Segments, count, 0.01f));
            Assert.AreEqual(Vector2.zero, SheetWarp.FadedOffset(new Vector2(HW, 0f), offset, Segments, count, 0.01f));
        }

        [Test]
        public void FadedOffset_FlapPart_NeverLeavesTheFlap_StaysAMarginInside()
        {
            var stack = Replay(new Fold(FoldAnchor.EdgeSouth, 2f));
            var count = SheetWarp.PackOutline(stack, 1, Segments, out _);
            var flap = stack.Layers[1].Desk;
            const float margin = 0.014f;
            for (float x = -HW + 0.05f; x < HW; x += 0.7f)
            for (float y = -2.2f; y < -0.3f; y += 0.13f)
            for (int a = 0; a < 8; a++)
            {
                var p = new Vector2(x, y);
                var direction = new Vector2(Mathf.Cos(a * Mathf.PI / 4f), Mathf.Sin(a * Mathf.PI / 4f));
                var sample = p + SheetWarp.FadedOffset(p, direction * 0.3f, Segments, count, margin);
                Assert.IsTrue(flap.Contains(sample), $"{p} + offset left the Flap at {sample}");
                Assert.GreaterOrEqual(SheetWarp.DistanceToOutline(sample, Segments, count), margin - Eps, $"{sample} is within a margin of the outline");
            }
        }

        [Test]
        public void FadedOffset_BasePart_NeverReachesIntoTheFlapAbove()
        {
            // The Base part is held inside its own edges and the Flap's: a Base pixel beside the Flap must not
            // sample the Flap (the per-part outline is what keeps it out).
            var stack = Replay(new Fold(FoldAnchor.CornerNorthEast, 3f));
            var count = SheetWarp.PackOutline(stack, 0, Segments, out _);
            var flap = stack.Layers[1].Desk;
            const float margin = 0.014f;
            for (float x = -HW + 0.05f; x < HW; x += 0.37f)
            for (float y = -HH + 0.05f; y < HH; y += 0.29f)
            {
                var p = new Vector2(x, y);
                if (flap.Contains(p) || !stack.Layers[0].Desk.Contains(p))
                    continue; // not a pixel the Base part shades
                for (int a = 0; a < 8; a++)
                {
                    var direction = new Vector2(Mathf.Cos(a * Mathf.PI / 4f), Mathf.Sin(a * Mathf.PI / 4f));
                    var sample = p + SheetWarp.FadedOffset(p, direction * 0.4f, Segments, count, margin);
                    Assert.IsFalse(flap.Contains(sample), $"Base pixel {p} sampled the Flap at {sample}");
                    Assert.IsTrue(stack.Layers[0].Desk.Contains(sample), $"Base pixel {p} sampled off the Base at {sample}");
                }
            }
        }

        // ----- slope to offset -----

        [Test]
        public void FaceToDesk_FrontUp_IsThePose_BackUp_IsMirrored()
        {
            // A south edge fold: the Flap is Back-up, mirrored across the crease (y flips); its Back-space x is
            // Front-space -x (GrainFrameOf negates the x column), so a Back-space +x slope points to Desk -x.
            var stack = Replay(new Fold(FoldAnchor.EdgeSouth, 2f));
            var flap = stack.Layers[1];
            Assert.IsFalse(flap.FrontUp);
            var frame = new Vector4(-flap.ToDesk.Ax.x, -flap.ToDesk.Ax.y, flap.ToDesk.Ay.x, flap.ToDesk.Ay.y);

            var east = SheetWarp.FaceToDesk(Vector2.right, frame);
            var north = SheetWarp.FaceToDesk(Vector2.up, frame);
            Assert.AreEqual(-1f, east.x, Eps, "Back-space +x is Desk -x");
            Assert.AreEqual(-1f, north.y, Eps, "the fold mirrors y");
            Assert.AreEqual(Vector2.up, SheetWarp.FaceToDesk(Vector2.up, new Vector4(1f, 0f, 0f, 1f)), "the Base: identity");
        }

        // ----- crease warps -----

        [Test]
        public void Pinch_IsZeroOnTheCreaseAndAtTheBandEdge_OutwardBetween()
        {
            Assert.AreEqual(0f, SheetWarp.Pinch(0f, 0.5f), Eps, "on the crease");
            Assert.AreEqual(0f, SheetWarp.Pinch(0.5f, 0.5f), Eps, "at the band's edge");
            Assert.AreEqual(0f, SheetWarp.Pinch(0.8f, 0.5f), Eps, "past it");
            Assert.Greater(SheetWarp.Pinch(0.2f, 0.5f), 0f, "samples farther out: drawn in toward the crease");
            Assert.AreEqual(-SheetWarp.Pinch(0.2f, 0.5f), SheetWarp.Pinch(-0.2f, 0.5f), Eps, "the same on both sides");
        }

        [Test]
        public void Pinch_MappingStaysOneToOne_AcrossTheWholeRange()
        {
            // a -> a + k * Pinch(a) must keep increasing, or the drawing folds over itself.
            foreach (var k in new[] { SheetWarp.MinPinch, -0.5f, 0f, 1f, 2f, SheetWarp.MaxPinch })
            {
                var previous = float.NegativeInfinity;
                for (int i = -100; i <= 100; i++)
                {
                    var a = i * 0.006f;
                    var mapped = a + k * SheetWarp.Pinch(a, 0.5f);
                    Assert.Greater(mapped, previous, $"pinch {k} folds over at {a}");
                    previous = mapped;
                }
            }
        }

        [Test]
        public void CreaseWarp_IsThePinchAlongTheNormal_TimesItsFactors()
        {
            var warp = SheetWarp.CreaseWarp(0.2f, Vector2.up, 0.5f, 1f, 2f, false, 0.4f);
            Assert.AreEqual(0f, warp.x, Eps);
            Assert.AreEqual(2f * SheetWarp.Pinch(0.2f, 0.5f), warp.y, Eps);

            var remembered = SheetWarp.CreaseWarp(0.2f, Vector2.up, 0.5f, 1f, 2f, true, 0.4f);
            Assert.AreEqual(warp.y * 0.4f, remembered.y, Eps, "a remembered crease pinches by its multiplier");

            Assert.AreEqual(Vector2.zero, SheetWarp.CreaseWarp(0.2f, Vector2.up, 0.5f, 0f, 2f, false, 0.4f), "nothing at a closed end");

            var flipped = SheetWarp.CreaseWarp(-0.2f, Vector2.down, 0.5f, 1f, 2f, false, 0.4f);
            Assert.AreEqual(warp.y, flipped.y, Eps, "turning the crease's frame round changes nothing");
        }

        [Test]
        public void Offset_AddsThePinch_AndSlidesDownTheBroadWrinkles_MirroredOnABackUpPiece()
        {
            var identity = new Vector4(1f, 0f, 0f, 1f);
            var grain = SheetWarp.Offset(new Vector2(0.5f, 0f), 0.1f, Vector2.zero, identity);
            Assert.AreEqual(-0.05f, grain.x, Eps, "a slope facing east samples from the west: content moves east, downhill");

            var crease = SheetWarp.Offset(Vector2.zero, 0.1f, new Vector2(0f, 0.02f), identity);
            Assert.AreEqual(0.02f, crease.y, Eps, "the pinch is where to sample, as is");

            var backUp = new Vector4(-1f, 0f, 0f, -1f); // the Flap of a south fold, Back-up
            var mirrored = SheetWarp.Offset(new Vector2(0.5f, 0f), 0.1f, Vector2.zero, backUp);
            Assert.AreEqual(0.05f, mirrored.x, Eps, "Back-space east is Desk west");
        }

        [Test]
        public void EndTaper_ClosedEndEases_OpenEndDoesNot()
        {
            Assert.AreEqual(1f, SheetWarp.EndTaper(0f, 2f, 0f, 0.5f), Eps, "mid-crease");
            Assert.AreEqual(0.5f, SheetWarp.EndTaper(1.75f, 2f, 0f, 0.5f), Eps, "a quarter unit before a closed end");
            Assert.AreEqual(0f, SheetWarp.EndTaper(2f, 2f, 0f, 0.5f), Eps, "at a closed end");
            Assert.AreEqual(1f, SheetWarp.EndTaper(2.2f, 2f, CreaseShading.OpenEndFlag, 0.5f), Eps, "past an open end");
            Assert.AreEqual(0f, SheetWarp.EndTaper(-2f, 2f, CreaseShading.OpenEndFlag, 0.5f), Eps, "the other end is still closed");
        }

        [Test]
        public void CombineWarps_ARepeatAveragesToItself_NothingIsNothing()
        {
            var v = new Vector2(0.03f, -0.01f);
            var combined = SheetWarp.CombineWarps(new[] { v, v });
            Assert.AreEqual(v.x, combined.x, Eps);
            Assert.AreEqual(v.y, combined.y, Eps);
            Assert.AreEqual(Vector2.zero, SheetWarp.CombineWarps(Array.Empty<Vector2>()));
        }

        [Test]
        public void CombineWarps_CrossingCreases_IsContinuous()
        {
            // Two crossing creases' warps as one of them fades in: the combined warp moves smoothly, never jumps.
            var a = new Vector2(0.05f, 0f);
            var b = new Vector2(0f, 0.05f);
            var previous = SheetWarp.CombineWarps(new[] { a, b * 0f });
            for (int i = 1; i <= 100; i++)
            {
                var current = SheetWarp.CombineWarps(new[] { a, b * (i / 100f) });
                Assert.Less(Vector2.Distance(previous, current), 0.003f, $"jump at step {i}");
                previous = current;
            }
        }

        // ----- the shader and the project settings -----

        static string SheetShadingSource()
        {
            var path = AssetDatabase.GetAssetPath(Shader.Find("Papercut/Sheet Shading"));
            Assert.IsNotEmpty(path, "Papercut/Sheet Shading not found");
            return System.IO.File.ReadAllText(path);
        }

        [Test]
        public void SheetShadingShader_OutlineLength_MatchesMaxOutlineSegments()
        {
            var match = System.Text.RegularExpressions.Regex.Match(SheetShadingSource(), @"#define\s+MAX_OUTLINE\s+(\d+)");
            Assert.IsTrue(match.Success, "MAX_OUTLINE define not found");
            Assert.AreEqual(SheetWarp.MaxOutlineSegments, int.Parse(match.Groups[1].Value));
        }

        [Test]
        public void SheetShadingShader_RedrawsFromTheScreenCopy()
        {
            var source = System.Text.RegularExpressions.Regex.Replace(SheetShadingSource(), @"\s+", " ");
            StringAssert.Contains("Blend One Zero", source);
            StringAssert.Contains("_CameraSortingLayerTexture", source);
        }

        [Test]
        public void ShadingSortingLayer_ExistsAfterDefault_WithItsOwnId()
        {
            Assert.IsTrue(RenderTextureFoldRenderer.TryGetShadingSortingLayer(out var id),
                $"no '{RenderTextureFoldRenderer.ShadingSortingLayerName}' sorting layer after Default");
            Assert.AreNotEqual(0, id);
            Assert.AreEqual(1, SortingLayer.layers.Count(l => l.id == id), "its id is unique");
            Assert.Greater(SortingLayer.GetLayerValueFromID(id), SortingLayer.GetLayerValueFromID(0), "after Default");
        }

        [Test]
        public void Renderers_MainCopiesTheScreenAfterDefault_FaceRendererDoesNot()
        {
            // The active pipeline asset, as FaceCameraRenderer finds it.
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            Assert.IsNotNull(pipeline, "no render pipeline asset is active");
            var list = new SerializedObject(pipeline).FindProperty("m_RendererDataList");
            Assert.Greater(list.arraySize, FaceCameraRenderer.RendererIndex, "the face renderer is missing from the pipeline asset");

            var main = new SerializedObject(list.GetArrayElementAtIndex(0).objectReferenceValue);
            Assert.IsTrue(main.FindProperty("m_UseCameraSortingLayersTexture").boolValue, "main renderer: Camera Sorting Layer Texture on");
            Assert.AreEqual(0, main.FindProperty("m_CameraSortingLayersTextureBound").intValue, "copied after Default");
            Assert.AreEqual(0, main.FindProperty("m_CameraSortingLayerDownsamplingMethod").enumValueIndex, "full resolution");

            var faces = new SerializedObject(list.GetArrayElementAtIndex(FaceCameraRenderer.RendererIndex).objectReferenceValue);
            Assert.IsFalse(faces.FindProperty("m_UseCameraSortingLayersTexture").boolValue, "face renderer: no screen copy");
        }
    }
}
