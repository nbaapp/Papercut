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

        // ----- Who a region blocks (Aaron, 2026-09-28: filter walls) -----

        [Test]
        public void ARegionNotSolidToThePlayer_IsPassableWhateverTheyHold()
        {
            Assert.IsTrue(TerrainRules.IsPassable(TerrainBlocks.Blocks, Ability.None, Ability.None), "a filter wall, no ability");
            Assert.IsTrue(TerrainRules.IsPassable(TerrainBlocks.Blocks, Ability.None, Ability.Swim), "even when gated: the gate is for the player it blocks");
            Assert.IsTrue(TerrainRules.IsPassable(TerrainBlocks.None, Ability.None, Ability.None), "solid to nobody");
        }

        [Test]
        public void ARegionSolidToThePlayer_UsesTheAbilityGate()
        {
            var wall = TerrainBlocks.Player | TerrainBlocks.Blocks;
            Assert.IsFalse(TerrainRules.IsPassable(wall, Ability.Swim, Ability.None), "a wall");
            Assert.IsFalse(TerrainRules.IsPassable(wall, Ability.None, Ability.Swim), "water without Swim");
            Assert.IsTrue(TerrainRules.IsPassable(wall, Ability.Swim, Ability.Swim), "water with Swim");
            Assert.IsFalse(TerrainRules.IsPassable(TerrainBlocks.Player, Ability.None, Ability.None), "a player-only wall still blocks the player");
        }

        [Test]
        public void StopsBlocks_IsTheBlocksBit()
        {
            Assert.IsTrue(TerrainRules.StopsBlocks(TerrainBlocks.Blocks));
            Assert.IsTrue(TerrainRules.StopsBlocks(TerrainBlocks.Player | TerrainBlocks.Blocks));
            Assert.IsFalse(TerrainRules.StopsBlocks(TerrainBlocks.Player));
            Assert.IsFalse(TerrainRules.StopsBlocks(TerrainBlocks.None));
        }
    }
}
