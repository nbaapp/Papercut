using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class StudioPolygonDraftTests
    {
        [Test]
        public void Add_RemoveLast_Clear()
        {
            var draft = new Papercut.EditorTools.StudioPolygonDraft();
            Assert.IsFalse(draft.IsActive);
            draft.Start(null, SheetFace.Back);
            Assert.IsTrue(draft.TryAdd(new Vector2(0f, 0f), out _));
            Assert.IsTrue(draft.TryAdd(new Vector2(1f, 0f), out _));
            Assert.IsFalse(draft.TryAdd(new Vector2(1f, 0f), out var reason), "a point on the last vertex is refused");
            Assert.IsNotEmpty(reason);
            Assert.IsFalse(draft.TryAdd(new Vector2(0f, 0f), out reason), "a point on the first vertex is refused too (no phantom point below three)");
            Assert.AreEqual(2, draft.Points.Count);
            draft.Retarget(null);
            Assert.AreEqual(2, draft.Points.Count, "retargeting keeps the outline");
            Assert.AreEqual(SheetFace.Back, draft.Face);
            Assert.IsTrue(draft.IsActive);

            Assert.IsTrue(draft.RemoveLast());
            Assert.AreEqual(1, draft.Points.Count);
            Assert.IsTrue(draft.RemoveLast());
            Assert.IsFalse(draft.IsActive, "removing the last point ends the draft");
            Assert.IsFalse(draft.RemoveLast());

            draft.Start(null, SheetFace.Front);
            draft.TryAdd(Vector2.zero, out _);
            draft.Clear();
            Assert.IsFalse(draft.IsActive);
            Assert.IsNull(draft.Prefab);
        }

        [Test]
        public void CanFinish_NeedsThreePointsAndASimplePolygon()
        {
            var draft = new Papercut.EditorTools.StudioPolygonDraft();
            draft.Start(null, SheetFace.Front);
            draft.TryAdd(new Vector2(0f, 0f), out _);
            draft.TryAdd(new Vector2(2f, 2f), out _);
            Assert.IsFalse(draft.CanFinish(out var few));
            StringAssert.Contains("three", few);

            draft.TryAdd(new Vector2(2f, 0f), out _);
            Assert.IsTrue(draft.CanFinish(out _), "a triangle");

            draft.TryAdd(new Vector2(0f, 2f), out _);
            Assert.IsFalse(draft.CanFinish(out var crossing), "a bow-tie");
            StringAssert.Contains("crosses", crossing);
        }

        [Test]
        public void TryFinish_WithoutASheet_IsRefusedAndKeepsTheDraft()
        {
            var draft = new Papercut.EditorTools.StudioPolygonDraft();
            draft.Start(null, SheetFace.Front);
            draft.TryAdd(new Vector2(0f, 0f), out _);
            draft.TryAdd(new Vector2(2f, 0f), out _);
            draft.TryAdd(new Vector2(2f, 2f), out _);
            Assert.IsFalse(draft.TryFinish(null, out var placed, out var reason));
            Assert.IsNull(placed);
            Assert.IsNotEmpty(reason);
            Assert.IsTrue(draft.IsActive, "a refused finish keeps the outline so it can be corrected");
        }
    }
}
