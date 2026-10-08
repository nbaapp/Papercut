using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary>
    /// The terrain prefabs' data (Aaron, 2026-09-28: universal and filter walls): the older kinds deserialize
    /// to today's behaviour, the six new prefabs carry their kind, and the region's passability answers for a
    /// missing player the way arrival and the Studio's spawn check need.
    /// </summary>
    public sealed class TerrainRegionTests
    {
        const string Folder = "Assets/Papercut/Prefabs/Terrain/";

        static TerrainRegion Region(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + name + ".prefab");
            Assert.IsNotNull(prefab, $"prefab missing: {name}");
            var region = prefab.GetComponent<TerrainRegion>();
            Assert.IsNotNull(region, $"{name} has no TerrainRegion");
            return region;
        }

        static (TerrainBlocks blocks, bool universal, Ability required) DataOf(TerrainRegion region)
        {
            var so = new SerializedObject(region);
            return ((TerrainBlocks)so.FindProperty("blocks").intValue, so.FindProperty("universal").boolValue, (Ability)so.FindProperty("requiredAbility").intValue);
        }

        [TestCase("Wall")]
        [TestCase("Wall (Polygon)")]
        [TestCase("Water")]
        [TestCase("Water (Polygon)")]
        [TestCase("Gate")]
        [TestCase("Gate (Off)")]
        public void OlderKinds_BlockPlayerAndBlocks_OnAFace(string name)
        {
            var (blocks, universal, _) = DataOf(Region(name));
            Assert.AreEqual(TerrainBlocks.Player | TerrainBlocks.Blocks, blocks, name);
            Assert.IsFalse(universal, name);
        }

        [TestCase("Universal Wall", TerrainBlocks.Player | TerrainBlocks.Blocks, true)]
        [TestCase("Universal Wall (Polygon)", TerrainBlocks.Player | TerrainBlocks.Blocks, true)]
        [TestCase("Filter Wall", TerrainBlocks.Blocks, false)]
        [TestCase("Filter Wall (Polygon)", TerrainBlocks.Blocks, false)]
        [TestCase("Universal Filter Wall", TerrainBlocks.Blocks, true)]
        [TestCase("Universal Filter Wall (Polygon)", TerrainBlocks.Blocks, true)]
        public void NewKinds_CarryTheirKind(string name, TerrainBlocks blocks, bool universal)
        {
            var region = Region(name);
            var (actualBlocks, actualUniversal, required) = DataOf(region);
            Assert.AreEqual(blocks, actualBlocks, name);
            Assert.AreEqual(universal, actualUniversal, name);
            Assert.AreEqual(Ability.None, required, name + " is not gated");
            Assert.AreEqual(universal, region.IsUniversal);
            Assert.AreEqual(TerrainRules.StopsBlocks(blocks), region.StopsBlocks);
            var polygon = name.EndsWith("(Polygon)");
            Assert.AreEqual(polygon, region.GetComponent<PolygonCollider2D>() != null, name + " shape");
            Assert.AreEqual(!polygon, region.GetComponent<BoxCollider2D>() != null, name + " shape");
            Assert.AreEqual(-0.05f, region.transform.localPosition.z, 1e-5f, "the element z convention");
        }

        [Test]
        public void NewKinds_HaveFillsThatReadApart()
        {
            var wall = Region("Wall").GetComponent<TerrainFill>().Color;
            var water = Region("Water").GetComponent<TerrainFill>().Color;
            foreach (var name in new[] { "Universal Wall", "Filter Wall", "Universal Filter Wall" })
            {
                var fill = Region(name).GetComponent<TerrainFill>();
                Assert.IsNotNull(fill, name);
                Assert.IsNotNull(new SerializedObject(fill).FindProperty("material").objectReferenceValue, name + " fill has no material");
                Assert.AreNotEqual(wall, fill.Color, name + " vs Wall");
                Assert.AreNotEqual(water, fill.Color, name + " vs Water");
                Assert.AreEqual(fill.Color, Region(name + " (Polygon)").GetComponent<TerrainFill>().Color, name + " and its polygon twin match");
            }
        }

        [Test]
        public void Passability_WithoutAPlayer_FollowsTheKind()
        {
            Assert.IsFalse(Region("Wall").IsPassableBy(null), "a wall is solid whoever asks");
            Assert.IsFalse(Region("Water").IsPassableBy(null), "the ability gate fails solid with no player");
            Assert.IsTrue(Region("Filter Wall").IsPassableBy(null), "a filter wall is never solid to the player");
            Assert.IsTrue(Region("Universal Filter Wall").IsPassableBy(null));
            Assert.IsFalse(Region("Universal Wall").IsPassableBy(null));
        }
    }
}
