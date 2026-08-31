using NUnit.Framework;
using Papercut.EditorTools;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>
    /// The Sheet Studio's pixel-to-sheet mapping. A bug here misplaces every element Aaron places, so the
    /// round trip, the y-axis orientation (GUI y-down vs sheet y-up), and the mirror are tested exhaustively.
    /// </summary>
    public sealed class PaneViewTests
    {
        static readonly Rect Pane = new(100f, 50f, 400f, 300f);

        [Test]
        public void RoundTrip_IsIdentity_AcrossZoomsPansAndMirror()
        {
            foreach (var mirror in new[] { false, true })
                foreach (var zoom in new[] { 4f, 30f, 400f })
                    foreach (var centre in new[] { Vector2.zero, new Vector2(3.5f, -2f), new Vector2(-5.5f, 4.25f) })
                    {
                        var view = new PaneView(Pane, centre, zoom, mirror);
                        foreach (var point in new[] { Vector2.zero, new Vector2(1.25f, -3.75f), new Vector2(-5.5f, 4.25f) })
                        {
                            var there = view.SheetLocalToPane(point);
                            var back = view.PaneToSheetLocal(there);
                            Assert.That(back.x, Is.EqualTo(point.x).Within(1e-4f), $"x (mirror={mirror}, zoom={zoom}, centre={centre})");
                            Assert.That(back.y, Is.EqualTo(point.y).Within(1e-4f), $"y (mirror={mirror}, zoom={zoom}, centre={centre})");
                        }
                    }
        }

        [Test]
        public void CentreOfPane_ShowsViewCentre()
        {
            var view = new PaneView(Pane, new Vector2(2f, 1f), 20f, false);
            var gui = view.SheetLocalToPane(new Vector2(2f, 1f));
            Assert.That(gui.x, Is.EqualTo(Pane.center.x).Within(1e-4f));
            Assert.That(gui.y, Is.EqualTo(Pane.center.y).Within(1e-4f));
        }

        [Test]
        public void SheetUp_IsGuiUp()
        {
            // Sheet-local +y (world up) must map to a SMALLER GUI y (GUI is y-down).
            var view = new PaneView(Pane, Vector2.zero, 20f, false);
            var low = view.SheetLocalToPane(new Vector2(0f, 0f));
            var high = view.SheetLocalToPane(new Vector2(0f, 2f));
            Assert.Less(high.y, low.y);
            Assert.That(low.y - high.y, Is.EqualTo(2f * 20f).Within(1e-4f));
        }

        [Test]
        public void SheetRight_IsGuiRight_AndMirrorFlipsIt()
        {
            var plain = new PaneView(Pane, Vector2.zero, 20f, false);
            var mirrored = new PaneView(Pane, Vector2.zero, 20f, true);
            var origin = plain.SheetLocalToPane(Vector2.zero);
            Assert.Greater(plain.SheetLocalToPane(new Vector2(2f, 0f)).x, origin.x, "+x should be GUI right unmirrored");
            Assert.Less(mirrored.SheetLocalToPane(new Vector2(2f, 0f)).x, origin.x, "+x should be GUI left mirrored");
            // Mirror leaves y untouched.
            Assert.That(mirrored.SheetLocalToPane(new Vector2(2f, 1f)).y,
                Is.EqualTo(plain.SheetLocalToPane(new Vector2(2f, 1f)).y).Within(1e-4f));
        }

        [Test]
        public void FitSheet_ShowsTheWholeSheet()
        {
            foreach (var mirror in new[] { false, true })
            {
                var view = PaneView.FitSheet(Pane, mirror);
                foreach (var corner in new[]
                         {
                             new Vector2(-SheetGeometry.HalfSize.x, -SheetGeometry.HalfSize.y),
                             new Vector2(SheetGeometry.HalfSize.x, SheetGeometry.HalfSize.y),
                             new Vector2(-SheetGeometry.HalfSize.x, SheetGeometry.HalfSize.y),
                             new Vector2(SheetGeometry.HalfSize.x, -SheetGeometry.HalfSize.y),
                         })
                {
                    var gui = view.SheetLocalToPane(corner);
                    Assert.IsTrue(Pane.Contains(gui), $"corner {corner} (mirror={mirror}) landed at {gui}, outside {Pane}");
                }
            }
        }

        [Test]
        public void ZoomedAbout_KeepsThePivotPointFixed()
        {
            foreach (var mirror in new[] { false, true })
            {
                var view = new PaneView(Pane, new Vector2(1f, -0.5f), 25f, mirror);
                var pivot = new Vector2(Pane.x + 40f, Pane.y + 220f);
                var pinned = view.PaneToSheetLocal(pivot);
                var zoomed = view.ZoomedAbout(60f, pivot);
                var after = zoomed.PaneToSheetLocal(pivot);
                Assert.That(after.x, Is.EqualTo(pinned.x).Within(1e-4f), $"mirror={mirror}");
                Assert.That(after.y, Is.EqualTo(pinned.y).Within(1e-4f), $"mirror={mirror}");
            }
        }

        [Test]
        public void PannedBy_MovesContentWithTheCursor()
        {
            foreach (var mirror in new[] { false, true })
            {
                var view = new PaneView(Pane, Vector2.zero, 20f, mirror);
                var probe = new Vector2(1f, 1f);
                var before = view.SheetLocalToPane(probe);
                var delta = new Vector2(30f, -12f);
                var after = view.PannedBy(delta).SheetLocalToPane(probe);
                Assert.That(after.x, Is.EqualTo(before.x + delta.x).Within(1e-3f), $"mirror={mirror}");
                Assert.That(after.y, Is.EqualTo(before.y + delta.y).Within(1e-3f), $"mirror={mirror}");
            }
        }

        [Test]
        public void Zoom_IsNeverZeroOrNegative()
        {
            var view = new PaneView(Pane, Vector2.zero, -5f, false);
            Assert.Greater(view.Zoom, 0f);
        }
    }
}
