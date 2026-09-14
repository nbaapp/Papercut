using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Papercut.EditorTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Papercut.Tests
{
    /// <summary>
    /// The folded-view ↔ authored-space mapping, checked against the runtime fold math it inverts, plus the
    /// fold pane's press-priority chain driven headlessly.
    /// </summary>
    public sealed class StudioFoldMappingTests
    {
        const float MinDepth = 0.25f;

        readonly List<GameObject> sceneObjects = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in sceneObjects)
                if (go != null)
                    Object.DestroyImmediate(go);
            sceneObjects.Clear();
        }

        GameObject Track(GameObject go)
        {
            sceneObjects.Add(go);
            return go;
        }

        static SheetLayers FoldedEast(float depth, out FoldEffect effect)
            => SheetLayers.Flat.Apply(new Fold(FoldAnchor.EdgeEast, depth), 0, out effect);

        // ----- Point mapping -----

        [Test]
        public void FlatSheet_MapsIdentityToFront()
        {
            var point = new Vector2(2.5f, -1.25f);
            Assert.IsTrue(StudioFoldMapping.TryMapToAuthored(SheetLayers.Flat, point, out var face, out var authored));
            Assert.AreEqual(SheetFace.Front, face);
            Assert.AreEqual(point.x, authored.x, 1e-5f);
            Assert.AreEqual(point.y, authored.y, 1e-5f);
        }

        [Test]
        public void PointOnLandedFlap_MapsToBack_AtTheReflectedMirroredSpot()
        {
            // East fold, depth 2: crease x = 3.5. Desk point (3, 1) is on the landed flap; the piece of Sheet there
            // came from (4, 1) (reflection about the crease), which is Back-space (−4, 1).
            var layers = FoldedEast(2f, out _);
            var deskPoint = new Vector2(3f, 1f);
            Assert.IsTrue(StudioFoldMapping.TryMapToAuthored(layers, deskPoint, out var face, out var authored));
            Assert.AreEqual(SheetFace.Back, face);
            Assert.AreEqual(-4f, authored.x, 1e-4f, "reflected about crease x=3.5 then mirrored by BackToFront");
            Assert.AreEqual(1f, authored.y, 1e-4f);

            // Truth check straight through the layer's own isometry.
            var top = layers.Layers[^1];
            Assert.IsFalse(top.FrontUp);
            var original = top.ToDesk.Inverse.Apply(deskPoint);
            Assert.AreEqual(SheetGeometry.BackToFront(original).x, authored.x, 1e-5f);
        }

        [Test]
        public void TwiceFoldedPiece_IsFrontUpAgain_AndTheTopmostLayerDecidesTheMapping()
        {
            // Fold east twice. A fold flips the lifted pile, so the piece folded TWICE is Front-up again —
            // but it lands underneath (the pile reverses), so the visible top at that spot is the once-folded
            // base piece, Back-up. The mapping must follow the topmost layer.
            var layers = SheetLayers.Flat.Apply(new Fold(FoldAnchor.EdgeEast, 2f), 0, out _);
            layers = layers.Apply(new Fold(FoldAnchor.EdgeEast, 3f), 1, out var second);
            Assert.AreEqual(FoldOutcome.None, second.Outcome);

            var frontUpAgain = false;
            foreach (var layer in layers.Layers)
                if (!layer.IsBase && layer.MovedBy == 1 && layer.FrontUp)
                    frontUpAgain = true;
            Assert.IsTrue(frontUpAgain, "the twice-folded piece exists in the stack, Front-up again");

            // First-principles truth for the two-pile region (no layer fields consulted — code review S4):
            // the top piece there is the base Sheet folded once by fold 2 (crease x = 2.5), so a desk probe p
            // shows the Back at Back-space BackToFront(reflect(p about x = 2.5)) = (p.x − 5, p.y).
            var probe = new Vector2(2f, 0.5f); // Inside fold 2's landed strip [1.5, 2.5].
            Assert.IsTrue(StudioFoldMapping.TryMapToAuthored(layers, probe, out var face, out var authored));
            Assert.AreEqual(SheetFace.Back, face, "the once-folded base piece tops the flipped pile");
            Assert.AreEqual(probe.x - 5f, authored.x, 1e-4f);
            Assert.AreEqual(probe.y, authored.y, 1e-4f);
        }

        [Test]
        public void EmptyDesk_MapsToNothing()
        {
            var layers = FoldedEast(2f, out _); // Region east of the crease is now bare desk.
            Assert.IsFalse(StudioFoldMapping.TryMapToAuthored(layers, new Vector2(5f, 0f), out _, out _));
            Assert.AreEqual(-1, StudioFoldMapping.TopLayerAt(layers, new Vector2(5f, 0f)));
        }

        [Test]
        public void AuthoredToDesk_RoundTripsThroughTheTopLayer()
        {
            var layers = FoldedEast(2f, out _);
            // Back point (−4, 1) currently shows on the flap at (3, 1).
            Assert.IsTrue(StudioFoldMapping.TryMapAuthoredToDesk(layers, SheetFace.Back, new Vector2(-4f, 1f), out var desk, out _));
            Assert.AreEqual(3f, desk.x, 1e-4f);
            Assert.AreEqual(1f, desk.y, 1e-4f);
        }

        // ----- Footprint pieces -----

        [Test]
        public void FrontElementStraddlingTheCrease_HasOneFaceUpAndOneFaceDownPiece()
        {
            // East fold depth 2 (crease x=3.5); a Front element spanning x in [3, 4] straddles it: its west
            // half stays on the base (face-up), its east half was lifted and is now face-down on the flap.
            var layers = FoldedEast(2f, out _);
            var pieces = new List<StudioFoldMapping.DeskPiece>();
            StudioFoldMapping.AuthoredBoxToDeskPieces(layers, SheetFace.Front, Rect.MinMaxRect(3f, -0.5f, 4f, 0.5f), pieces);

            Assert.AreEqual(2, pieces.Count, "one piece per layer carrying part of the element");
            var faceUp = pieces.Find(p => p.FaceUp);
            var faceDown = pieces.Find(p => !p.FaceUp);
            Assert.IsNotNull(faceUp.Piece, "base piece is face-up");
            Assert.IsNotNull(faceDown.Piece, "flap piece is face-down (round-1 B1's corrected expectation)");
            // The face-up piece stays where authored; the face-down piece lies reflected west of the crease.
            Assert.LessOrEqual(faceUp.Piece.Bounds.xMax, 3.5f + 1e-4f);
            Assert.LessOrEqual(faceDown.Piece.Bounds.xMax, 3.5f + 1e-4f, "lifted half now lies west of the crease");
        }

        [Test]
        public void BackElementRect_GoesThroughBackToFrontFirst()
        {
            // Flat sheet: a Back element at Back-space x in [2, 3] lies beneath Front x in [−3, −2]; its
            // single piece is face-down (the Back is not up anywhere on a flat sheet).
            var pieces = new List<StudioFoldMapping.DeskPiece>();
            StudioFoldMapping.AuthoredBoxToDeskPieces(SheetLayers.Flat, SheetFace.Back, Rect.MinMaxRect(2f, 0f, 3f, 1f), pieces);
            Assert.AreEqual(1, pieces.Count);
            Assert.IsFalse(pieces[0].FaceUp);
            Assert.AreEqual(-3f, pieces[0].Piece.Bounds.xMin, 1e-4f);
            Assert.AreEqual(-2f, pieces[0].Piece.Bounds.xMax, 1e-4f);
        }

        // ----- Press-priority chain (headless; round-2 N5) -----

        Sheet CreateTestSheet(out Transform front, out Transform back)
        {
            var go = Track(new GameObject("TestSheet"));
            front = new GameObject("Front").transform;
            front.SetParent(go.transform, false);
            back = new GameObject("Back").transform;
            back.SetParent(go.transform, false);

            LogAssert.Expect(LogType.Error, new Regex("no Front root"));
            LogAssert.Expect(LogType.Error, new Regex("no Back root"));
            var sheet = go.AddComponent<Sheet>();
            var serialized = new SerializedObject(sheet);
            serialized.FindProperty("front").objectReferenceValue = front;
            serialized.FindProperty("back").objectReferenceValue = back;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return sheet;
        }

        StudioFoldPane.Context PressContext(Sheet sheet, StudioFoldModel model, StudioPalette palette, bool linksActive = false)
            => new()
            {
                Model = model,
                Settings = StudioFoldSettings.Read(sheet),
                Sheet = sheet,
                Palette = palette,
                SnapIncrement = 0f,
                LinksActive = linksActive,
            };

        [Test]
        public void Press_ArmedPalette_PlacesThroughTheFold_OntoTheBack()
        {
            var sheet = CreateTestSheet(out _, out var backRoot);
            var model = new StudioFoldModel();
            Assert.IsTrue(model.TryCommit(new Fold(FoldAnchor.EdgeEast, 2f), MinDepth, out _));
            var palette = new StudioPalette();
            palette.Arm(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Papercut/Prefabs/Terrain/Wall.prefab"));

            var pane = new StudioFoldPane();
            var outcome = pane.BeginPress(PressContext(sheet, model, palette), new Vector2(3f, 1f)); // On the flap.
            Assert.AreEqual(StudioFoldPane.PressOutcome.Placed, outcome);
            Assert.AreEqual(1, backRoot.childCount, "the element landed on the Back root");
            var placed = backRoot.GetChild(0);
            Assert.AreEqual(-4f, placed.localPosition.x, 1e-3f, "at the reflected, mirrored authored spot");
            Assert.AreEqual(1f, placed.localPosition.y, 1e-3f);
        }

        [Test]
        public void Press_ArmedPalette_OverEmptyDesk_Refuses()
        {
            var sheet = CreateTestSheet(out var frontRoot, out var backRoot);
            var model = new StudioFoldModel();
            Assert.IsTrue(model.TryCommit(new Fold(FoldAnchor.EdgeEast, 2f), MinDepth, out _));
            var palette = new StudioPalette();
            palette.Arm(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Papercut/Prefabs/Terrain/Wall.prefab"));

            var pane = new StudioFoldPane();
            var outcome = pane.BeginPress(PressContext(sheet, model, palette), new Vector2(5f, 0f)); // Bare desk.
            Assert.AreEqual(StudioFoldPane.PressOutcome.PlaceRefusedNoSheet, outcome);
            Assert.AreEqual(0, frontRoot.childCount);
            Assert.AreEqual(0, backRoot.childCount);
        }

        [Test]
        public void Press_FoldGesturesBeatElements_AndSeamBeatsEdge()
        {
            var sheet = CreateTestSheet(out var frontRoot, out _);
            var model = new StudioFoldModel();
            Assert.IsTrue(model.TryCommit(new Fold(FoldAnchor.EdgeEast, 2f), MinDepth, out _));
            // An element sitting right on the Seam (landed edge x = 1.5).
            var wall = StudioPlacement.Place(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Papercut/Prefabs/Terrain/Wall.prefab"),
                frontRoot, SheetFace.Front, new Vector2(1.5f, 0f), 0f);
            Assert.IsNotNull(wall);

            var pane = new StudioFoldPane();
            var outcome = pane.BeginPress(PressContext(sheet, model, new StudioPalette()), new Vector2(1.5f, 0f));
            Assert.AreEqual(StudioFoldPane.PressOutcome.Unfolded, outcome, "the Seam wins over the element (Aaron: fold gestures first)");
            Assert.AreEqual(0, model.Folds.Count);
        }

        [Test]
        public void Press_CreaseGrabBeatsElement()
        {
            var sheet = CreateTestSheet(out var frontRoot, out _);
            var model = new StudioFoldModel();
            Assert.IsTrue(model.TryCommit(new Fold(FoldAnchor.EdgeEast, 2f), MinDepth, out _)); // Crease x = 3.5.
            var wall = StudioPlacement.Place(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Papercut/Prefabs/Terrain/Wall.prefab"),
                frontRoot, SheetFace.Front, new Vector2(3.4f, 0f), 0f);
            Assert.IsNotNull(wall);

            var pane = new StudioFoldPane();
            var outcome = pane.BeginPress(PressContext(sheet, model, new StudioPalette()), new Vector2(3.4f, 0f));
            Assert.AreEqual(StudioFoldPane.PressOutcome.FoldDragStarted, outcome, "the crease grab wins over the element");
            pane.CancelDrag(model);
        }

        [Test]
        public void Press_EdgeAnchorBeatsElement()
        {
            var sheet = CreateTestSheet(out var frontRoot, out _);
            var model = new StudioFoldModel();
            var wall = StudioPlacement.Place(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Papercut/Prefabs/Terrain/Wall.prefab"),
                frontRoot, SheetFace.Front, new Vector2(5.3f, 0f), 0f); // Inside the east edge grab margin.
            Assert.IsNotNull(wall);

            var pane = new StudioFoldPane();
            var outcome = pane.BeginPress(PressContext(sheet, model, new StudioPalette()), new Vector2(5.3f, 0f));
            Assert.AreEqual(StudioFoldPane.PressOutcome.FoldDragStarted, outcome,
                "the edge grab wins — elements in the dead zone are face-pane business");
            pane.CancelDrag(model);
        }

        [Test]
        public void Press_GhostBeatsElement()
        {
            var sheet = CreateTestSheet(out var frontRoot, out _);
            var model = new StudioFoldModel();
            model.SetGhostEnabled(true); // At the sheet centre.
            var wall = StudioPlacement.Place(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Papercut/Prefabs/Terrain/Wall.prefab"),
                frontRoot, SheetFace.Front, Vector2.zero, 0f);
            Assert.IsNotNull(wall);

            var pane = new StudioFoldPane();
            var outcome = pane.BeginPress(PressContext(sheet, model, new StudioPalette()), Vector2.zero);
            Assert.AreEqual(StudioFoldPane.PressOutcome.GhostDragStarted, outcome, "the ghost wins over the element under it");
            pane.CancelDrag(model);
        }

        [Test]
        public void Press_CreaseStraddler_GrabOffsetUsesThePressLayersIsometry()
        {
            // M1's regression case: a Back-face element whose CENTRE's flat point lies on the base layer,
            // grabbed through its piece visible on the flap. The offset must extrapolate the press layer's
            // isometry — mixing in the centre's own layer teleports the element on the first drag update.
            var sheet = CreateTestSheet(out _, out var backRoot);
            var model = new StudioFoldModel();
            Assert.IsTrue(model.TryCommit(new Fold(FoldAnchor.EdgeEast, 2f), MinDepth, out _)); // Flap shows Back x in [−5.5, −3.5].
            var wall = StudioPlacement.Place(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Papercut/Prefabs/Terrain/Wall.prefab"),
                backRoot, SheetFace.Back, new Vector2(-3.2f, 1f), 0f); // Centre's flat point (3.2, 1) is on the BASE.
            Assert.IsNotNull(wall);

            var press = new Vector2(3.0f, 1.05f); // On the flap, inside the wall's visible piece, off-centre.
            var pane = new StudioFoldPane();
            var outcome = pane.BeginPress(PressContext(sheet, model, new StudioPalette()), press);
            Assert.AreEqual(StudioFoldPane.PressOutcome.ElementDragStarted, outcome);

            // The first drag update at the press point must reproduce the element's position exactly.
            var offset = pane.ElementGrabOffsetDesk;
            var layerIndex = StudioFoldMapping.TopLayerAt(model.Layers, press);
            StudioFoldMapping.MapThroughLayer(model.Layers, layerIndex, press + offset, out var face, out var authored);
            Assert.AreEqual(SheetFace.Back, face);
            Assert.AreEqual(-3.2f, authored.x, 1e-3f, "press offset and drag mapping must share one isometry");
            Assert.AreEqual(1f, authored.y, 1e-3f);
            pane.CancelDrag(model);
        }

        [Test]
        public void Press_Element_SelectsAndStartsDrag_ButIsInertInLinkMode()
        {
            var sheet = CreateTestSheet(out var frontRoot, out _);
            var model = new StudioFoldModel();
            var wall = StudioPlacement.Place(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Papercut/Prefabs/Terrain/Wall.prefab"),
                frontRoot, SheetFace.Front, new Vector2(0f, 2f), 0f);
            Assert.IsNotNull(wall);

            var pane = new StudioFoldPane();
            var inert = pane.BeginPress(PressContext(sheet, model, new StudioPalette(), linksActive: true), new Vector2(0f, 2f));
            Assert.AreEqual(StudioFoldPane.PressOutcome.ElementInertLinkMode, inert, "Link mode: fold pane element branch is inert");

            var outcome = pane.BeginPress(PressContext(sheet, model, new StudioPalette()), new Vector2(0f, 2f));
            Assert.AreEqual(StudioFoldPane.PressOutcome.ElementDragStarted, outcome);
            Assert.AreEqual(wall, Selection.activeGameObject);
            Assert.IsTrue(pane.IsDragging);
            pane.CancelDrag(model);
        }
    }
}
