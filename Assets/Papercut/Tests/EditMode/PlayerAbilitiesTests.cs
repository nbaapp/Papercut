using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    /// <summary><see cref="PlayerAbilities.Grant"/>: the one mutator, added with the first pickup (2026-09-17).</summary>
    public sealed class PlayerAbilitiesTests
    {
        GameObject player;
        PlayerAbilities abilities;
        int changedCount;

        [SetUp]
        public void SetUp()
        {
            player = new GameObject("Player");
            abilities = player.AddComponent<PlayerAbilities>();
            changedCount = 0;
            abilities.Changed += () => changedCount++;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(player);
        }

        [Test]
        public void Grant_AddsTheFlag_AndRaisesChangedOnce()
        {
            Assert.AreEqual(Ability.None, abilities.Abilities, "starts empty");
            abilities.Grant(Ability.Push);
            Assert.AreEqual(Ability.Push, abilities.Abilities);
            Assert.AreEqual(1, changedCount);
        }

        [Test]
        public void Grant_AlreadyHeld_OrNone_IsANoOp()
        {
            abilities.Grant(Ability.Push);
            changedCount = 0;
            abilities.Grant(Ability.Push);
            Assert.AreEqual(Ability.Push, abilities.Abilities);
            Assert.AreEqual(0, changedCount, "nothing new: no Changed");
            abilities.Grant(Ability.None);
            Assert.AreEqual(Ability.Push, abilities.Abilities);
            Assert.AreEqual(0, changedCount);
        }

        [Test]
        public void Grant_Accumulates()
        {
            abilities.Grant(Ability.Push);
            abilities.Grant(Ability.Swim);
            Assert.AreEqual(Ability.Push | Ability.Swim, abilities.Abilities);
            Assert.AreEqual(2, changedCount);
            changedCount = 0;
            abilities.Grant(Ability.Push | Ability.Swim);
            Assert.AreEqual(0, changedCount, "both already held");
        }
    }
}
