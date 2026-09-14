using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>
    /// <see cref="OccludedCollider"/> over an authored polygon (and box): which collider is live in each
    /// coverage state, and that the runtime clip polygon is never confused with the authored one on the same object.
    /// </summary>
    public sealed class OccludedColliderTests
    {
        readonly List<GameObject> objects = new();
        Transform space;

        [SetUp]
        public void SetUp()
        {
            space = Track(new GameObject("Sheet")).transform;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in objects)
                if (go != null)
                    Object.DestroyImmediate(go);
            objects.Clear();
        }

        GameObject Track(GameObject go)
        {
            objects.Add(go);
            return go;
        }

        PolygonCollider2D AuthoredPolygon()
        {
            var go = Track(new GameObject("Region"));
            go.transform.SetParent(space, false);
            go.transform.localPosition = new Vector3(1f, 1f, 0f);
            var polygon = go.AddComponent<PolygonCollider2D>();
            polygon.SetPath(0, new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) });
            return polygon;
        }

        static CoverageResult TwoParts()
        {
            return CoverageResult.Clipped(new[]
            {
                ConvexPolygon.FromRect(Rect.MinMaxRect(0f, 0f, 1f, 2f)),
                ConvexPolygon.FromRect(Rect.MinMaxRect(1.5f, 0f, 2f, 2f)),
            });
        }

        [Test]
        public void Whole_AuthoredOn_NoRuntimePolygon()
        {
            var authored = AuthoredPolygon();
            var occluded = new OccludedCollider(authored);
            occluded.Apply(CoverageResult.Whole(FoldCoverage.Uncovered, FaceFootprint.FromRect(Rect.MinMaxRect(0f, 0f, 2f, 2f))), space);

            Assert.IsTrue(authored.enabled);
            Assert.IsNull(occluded.RuntimePolygon);
            CollectionAssert.AreEqual(new Collider2D[] { authored }, new List<Collider2D>(occluded.LiveColliders));
            Assert.AreEqual(1, authored.GetComponents<PolygonCollider2D>().Length, "no second polygon was added");
        }

        [Test]
        public void Partial_AuthoredOff_RuntimePolygonCarriesOnePathPerPart_InTheObjectsLocalSpace()
        {
            var authored = AuthoredPolygon();
            var occluded = new OccludedCollider(authored);
            occluded.Apply(TwoParts(), space);

            Assert.IsFalse(authored.enabled);
            var runtime = occluded.RuntimePolygon;
            Assert.IsNotNull(runtime);
            Assert.AreNotSame(authored, runtime, "the clip polygon is a second component");
            Assert.IsTrue(runtime.enabled);
            Assert.AreEqual(HideFlags.DontSave, runtime.hideFlags);
            Assert.AreEqual(2, runtime.pathCount);
            // Sheet-local (0,0)-(1,2) is object-local (-1,-1)-(0,1): the region sits at (1,1).
            var path = runtime.GetPath(0);
            var min = path[0];
            var max = path[0];
            foreach (var p in path)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            Assert.AreEqual(-1f, min.x, 1e-4f);
            Assert.AreEqual(0f, max.x, 1e-4f);
            Assert.AreEqual(-1f, min.y, 1e-4f);
            Assert.AreEqual(1f, max.y, 1e-4f);
            CollectionAssert.AreEqual(new Collider2D[] { runtime }, new List<Collider2D>(occluded.LiveColliders));
            Assert.AreEqual(2, authored.GetComponents<PolygonCollider2D>().Length);
        }

        [Test]
        public void None_BothOff_AndWholeAgainRestoresTheAuthoredOne()
        {
            var authored = AuthoredPolygon();
            var occluded = new OccludedCollider(authored);
            occluded.Apply(TwoParts(), space);
            occluded.Apply(CoverageResult.None(FoldCoverage.Covered), space);

            Assert.IsFalse(authored.enabled);
            Assert.IsFalse(occluded.RuntimePolygon.enabled);
            CollectionAssert.IsEmpty(new List<Collider2D>(occluded.LiveColliders));

            occluded.Apply(CoverageResult.Whole(FoldCoverage.Uncovered, FaceFootprint.FromRect(Rect.MinMaxRect(0f, 0f, 2f, 2f))), space);
            Assert.IsTrue(authored.enabled);
            Assert.IsFalse(occluded.RuntimePolygon.enabled);
            Assert.AreSame(authored, new List<Collider2D>(occluded.LiveColliders)[0], "the authored polygon is the live one again, not the clip");
        }

        [Test]
        public void Box_BehavesTheSame()
        {
            var go = Track(new GameObject("Box"));
            go.transform.SetParent(space, false);
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            var occluded = new OccludedCollider(box);

            occluded.Apply(TwoParts(), space);
            Assert.IsFalse(box.enabled);
            Assert.IsTrue(occluded.RuntimePolygon.isTrigger, "trigger-ness follows the authored collider");
            occluded.Apply(CoverageResult.None(FoldCoverage.Covered), space);
            CollectionAssert.IsEmpty(new List<Collider2D>(occluded.LiveColliders));
            occluded.Apply(CoverageResult.Whole(FoldCoverage.Uncovered, FaceFootprint.FromRect(Rect.MinMaxRect(0f, 0f, 1f, 1f))), space);
            Assert.IsTrue(box.enabled);
        }
    }
}
