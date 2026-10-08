using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Papercut.Tests
{
    /// <summary>Creases persist when the player leaves a sheet (Aaron, 2026-10-07).</summary>
    public sealed class SheetFoldsCreaseTests
    {
        static readonly Rect PlayerFarNorth = new(-0.25f, 3f, 0.5f, 0.5f);

        GameObject go;

        [TearDown]
        public void TearDown()
        {
            if (go != null)
                Object.DestroyImmediate(go);
        }

        SheetFolds CreateSheetFolds(bool rememberCreases = true)
        {
            go = new GameObject("Sheet");
            var front = new GameObject("Front").transform;
            front.SetParent(go.transform, false);
            var back = new GameObject("Back").transform;
            back.SetParent(go.transform, false);
            // Sheet validates its roots as it is added, before they can be assigned (as in TerrainFillTests).
            LogAssert.Expect(LogType.Error, new Regex("no Front root"));
            LogAssert.Expect(LogType.Error, new Regex("no Back root"));
            var sheet = go.AddComponent<Sheet>();
            var sheetSerialized = new SerializedObject(sheet);
            sheetSerialized.FindProperty("front").objectReferenceValue = front;
            sheetSerialized.FindProperty("back").objectReferenceValue = back;
            sheetSerialized.ApplyModifiedPropertiesWithoutUndo();
            var folds = go.AddComponent<SheetFolds>(); // No Awake in edit mode: no renderer, nothing to draw.
            var serialized = new SerializedObject(folds);
            serialized.FindProperty("rememberCreases").boolValue = rememberCreases;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return folds;
        }

        [Test]
        public void Reset_FoldsStillMade_LeaveTheirCreases()
        {
            var folds = CreateSheetFolds();
            Assert.IsTrue(folds.TryCommit(new Fold(FoldAnchor.EdgeSouth, 2f), PlayerFarNorth, out var rejection), rejection.ToString());
            var marks = folds.Effects[0].CreaseMarks.Count;

            folds.Reset();

            Assert.AreEqual(0, folds.Folds.Count, "the sheet unfolds");
            Assert.AreEqual(marks, folds.RememberedCreases.Count, "its crease stays, on both faces");
        }

        [Test]
        public void Reset_KeepsEarlierCreases()
        {
            var folds = CreateSheetFolds();
            Assert.IsTrue(folds.TryCommit(new Fold(FoldAnchor.EdgeSouth, 2f), PlayerFarNorth, out _));
            folds.Reset();
            var afterFirstVisit = folds.RememberedCreases.Count;

            Assert.IsTrue(folds.TryCommit(new Fold(FoldAnchor.EdgeWest, 2f), PlayerFarNorth, out _));
            folds.Reset();

            Assert.Greater(afterFirstVisit, 0);
            Assert.Greater(folds.RememberedCreases.Count, afterFirstVisit, "the second visit's crease joins the first");
        }

        [Test]
        public void Reset_RememberCreasesOff_LeavesNone()
        {
            var folds = CreateSheetFolds(rememberCreases: false);
            Assert.IsTrue(folds.TryCommit(new Fold(FoldAnchor.EdgeSouth, 2f), PlayerFarNorth, out _));

            folds.Reset();

            Assert.AreEqual(0, folds.RememberedCreases.Count);
        }
    }
}
