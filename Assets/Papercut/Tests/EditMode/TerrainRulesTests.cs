using NUnit.Framework;

namespace Papercut.Tests
{
    public sealed class TerrainRulesTests
    {
        const Ability Other = (Ability)(1 << 1); // A second flag, so multi-bit rules can be tested before a second unlockable exists.

        [Test]
        public void WallIsNeverPassable()
        {
            Assert.IsFalse(TerrainRules.IsPassable(Ability.None, Ability.None));
            Assert.IsFalse(TerrainRules.IsPassable(Ability.Swim, Ability.None));
            Assert.IsFalse(TerrainRules.IsPassable(Ability.Swim | Other, Ability.None));
        }

        [Test]
        public void GatedRegionBlocksWithoutTheAbility()
        {
            Assert.IsFalse(TerrainRules.IsPassable(Ability.None, Ability.Swim));
            Assert.IsFalse(TerrainRules.IsPassable(Other, Ability.Swim));
        }

        [Test]
        public void GatedRegionPassesWithTheAbility()
        {
            Assert.IsTrue(TerrainRules.IsPassable(Ability.Swim, Ability.Swim));
        }

        [Test]
        public void ExtraAbilitiesDoNotMatter()
        {
            Assert.IsTrue(TerrainRules.IsPassable(Ability.Swim | Other, Ability.Swim));
        }

        [Test]
        public void MultiFlagRequirementNeedsEveryFlag()
        {
            var required = Ability.Swim | Other;

            Assert.IsFalse(TerrainRules.IsPassable(Ability.Swim, required));
            Assert.IsFalse(TerrainRules.IsPassable(Other, required));
            Assert.IsTrue(TerrainRules.IsPassable(required, required));
        }
    }
}
