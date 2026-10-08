using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Papercut.Tests
{
    /// <summary>
    /// The editor-to-game playtest hand-off: a request is consumed exactly once, and resolving it against a
    /// Desk yields the sheet and the world point (or the reason it cannot). The navigator's own Start is runtime
    /// only and stays on Aaron's play list; the room-box helper is checked here because a degenerate box would
    /// silently pass every room test.
    /// </summary>
    public sealed class PlaytestStartTests
    {
        readonly List<GameObject> sceneObjects = new();

        [SetUp]
        public void SetUp() => PlaytestStart.Clear();

        [TearDown]
        public void TearDown()
        {
            PlaytestStart.Clear();
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

        Desk CreateTestDesk(params Vector2Int[] sheetPositions)
        {
            var go = Track(new GameObject("TestDesk"));
            var grid = new GameObject("SheetGrid").transform;
            grid.SetParent(go.transform, false);

            LogAssert.Expect(LogType.Error, new Regex("no SheetGrid assigned"));
            var desk = go.AddComponent<Desk>();
            var serialized = new SerializedObject(desk);
            serialized.FindProperty("sheetGrid").objectReferenceValue = grid;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            foreach (var position in sheetPositions)
            {
                var sheetGo = new GameObject($"Sheet ({position.x},{position.y})");
                sheetGo.transform.SetParent(grid, false);
                var front = new GameObject("Front").transform;
                front.SetParent(sheetGo.transform, false);
                var back = new GameObject("Back").transform;
                back.SetParent(sheetGo.transform, false);
                // OnValidate reports the missing roots on AddComponent; they are assigned in the same apply as
                // the grid position, so the re-validation that apply triggers finds them and stays quiet.
                LogAssert.Expect(LogType.Error, new Regex("no Front root"));
                LogAssert.Expect(LogType.Error, new Regex("no Back root"));
                var sheet = sheetGo.AddComponent<Sheet>();
                var sheetSerialized = new SerializedObject(sheet);
                sheetSerialized.FindProperty("front").objectReferenceValue = front;
                sheetSerialized.FindProperty("back").objectReferenceValue = back;
                sheetSerialized.FindProperty("gridPosition").vector2IntValue = position;
                sheetSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
            desk.LayoutSheets();
            return desk;
        }

        // ----- Request storage -----

        [Test]
        public void Consume_ReturnsTheRequestOnce()
        {
            Assert.IsFalse(PlaytestStart.IsPending);
            Assert.IsFalse(PlaytestStart.TryConsume(out _), "nothing pending");

            PlaytestStart.Set(new PlaytestStart.Request(new Vector2Int(-2, 3), new Vector2(1.25f, -3.5f)));
            Assert.IsTrue(PlaytestStart.IsPending);

            Assert.IsTrue(PlaytestStart.TryConsume(out var request));
            Assert.AreEqual(new Vector2Int(-2, 3), request.GridPosition);
            Assert.AreEqual(1.25f, request.SheetLocal.x, 1e-6f);
            Assert.AreEqual(-3.5f, request.SheetLocal.y, 1e-6f);

            Assert.IsFalse(PlaytestStart.IsPending, "consumed");
            Assert.IsFalse(PlaytestStart.TryConsume(out _), "one-shot: the next Play starts normally");
        }

        [Test]
        public void Clear_DropsAPendingRequest()
        {
            PlaytestStart.Set(new PlaytestStart.Request(Vector2Int.one, Vector2.zero));
            PlaytestStart.Clear();
            Assert.IsFalse(PlaytestStart.IsPending);
            Assert.IsFalse(PlaytestStart.TryConsume(out _));
        }

        // ----- Resolve -----

        [Test]
        public void Resolve_FindsTheSheetAndTheWorldPoint()
        {
            var desk = CreateTestDesk(new Vector2Int(0, 0), new Vector2Int(2, 1));
            var request = new PlaytestStart.Request(new Vector2Int(2, 1), new Vector2(-1.5f, 2f));

            Assert.IsTrue(PlaytestStart.TryResolve(desk, request, (_, _) => true, out var sheet, out var world, out var reason), reason);
            Assert.AreEqual(new Vector2Int(2, 1), sheet.GridPosition);
            var expected = (Vector2)desk.GridToLocal(new Vector2Int(2, 1)) + new Vector2(-1.5f, 2f);
            Assert.AreEqual(expected.x, world.x, 1e-4f, "sheet centre plus the sheet-local point");
            Assert.AreEqual(expected.y, world.y, 1e-4f);
        }

        [Test]
        public void Resolve_RefusesAMissingSheet()
        {
            var desk = CreateTestDesk(new Vector2Int(0, 0));
            var request = new PlaytestStart.Request(new Vector2Int(5, 5), Vector2.zero);

            Assert.IsFalse(PlaytestStart.TryResolve(desk, request, (_, _) => true, out var sheet, out _, out var reason));
            Assert.IsNull(sheet);
            StringAssert.Contains("(5,5)", reason);
        }

        [Test]
        public void Resolve_RefusesWhenThereIsNoRoom_AndPassesTheWorldPointToTheTest()
        {
            var desk = CreateTestDesk(new Vector2Int(0, 0), new Vector2Int(-1, 0));
            var request = new PlaytestStart.Request(new Vector2Int(-1, 0), new Vector2(3f, -1f));
            Sheet asked = null;
            Vector2 askedAt = default;

            var ok = PlaytestStart.TryResolve(desk, request, (s, p) => { asked = s; askedAt = p; return false; },
                out _, out _, out var reason);

            Assert.IsFalse(ok);
            Assert.AreEqual(new Vector2Int(-1, 0), asked.GridPosition, "the room test sees the resolved sheet");
            var expected = (Vector2)desk.GridToLocal(new Vector2Int(-1, 0)) + new Vector2(3f, -1f);
            Assert.AreEqual(expected.x, askedAt.x, 1e-4f);
            Assert.AreEqual(expected.y, askedAt.y, 1e-4f);
            StringAssert.Contains("no room", reason);
        }

        [Test]
        public void Resolve_RefusesWithoutADesk()
        {
            Assert.IsFalse(PlaytestStart.TryResolve(null, new PlaytestStart.Request(Vector2Int.zero, Vector2.zero), null, out _, out _, out var reason));
            StringAssert.Contains("no Desk", reason);
        }

        // ----- The player's room box -----

        [Test]
        public void PlayerBoxAt_UsesTheAuthoredSizeAndOffset_Scaled()
        {
            // Never Collider2D.bounds: physics-populated, degenerate before the first step (a playtest start).
            var go = Track(new GameObject("Player"));
            go.transform.localScale = new Vector3(2f, 2f, 1f);
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(0.44f, 0.7f);
            box.offset = new Vector2(-0.03f, -0.04f);

            var rect = ScreenNavigator.PlayerBoxAt(box, new Vector2(10f, 20f));

            Assert.AreEqual(0.88f, rect.width, 1e-5f);
            Assert.AreEqual(1.4f, rect.height, 1e-5f);
            Assert.AreEqual(10f - 0.06f, rect.center.x, 1e-5f, "the offset moves the box off the player's position");
            Assert.AreEqual(20f - 0.08f, rect.center.y, 1e-5f);
        }
    }
}
