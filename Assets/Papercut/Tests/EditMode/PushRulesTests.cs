using NUnit.Framework;
using UnityEngine;

namespace Papercut.Tests
{
    public sealed class PushRulesTests
    {
        [Test]
        public void TryCardinal_AxisAligned_Diagonal_Threshold()
        {
            Assert.IsTrue(PushRules.TryCardinal(new Vector2(0f, -1f), 0.9f, out var c));
            Assert.AreEqual(Vector2.down, c);
            Assert.IsTrue(PushRules.TryCardinal(new Vector2(3f, 0.1f), 0.9f, out c));
            Assert.AreEqual(Vector2.right, c);
            Assert.IsFalse(PushRules.TryCardinal(new Vector2(1f, 1f), 0.9f, out _), "45° is not a face");
            Assert.IsTrue(PushRules.TryCardinal(new Vector2(1f, 1f), 0.5f, out c), "lenient threshold");
            Assert.AreEqual(Vector2.right, c);
            Assert.IsFalse(PushRules.TryCardinal(Vector2.zero, 0.9f, out _));
        }

        [Test]
        public void IntoBlock_OrientsTheNormalFromPusherToBlock()
        {
            var pusher = new Vector2(0f, 0f);
            var block = new Vector2(2f, 0f);
            Assert.AreEqual(Vector2.right, PushRules.IntoBlock(Vector2.right, pusher, block));
            Assert.AreEqual(Vector2.right, PushRules.IntoBlock(Vector2.left, pusher, block));
        }

        [Test]
        public void PushDistance_ComponentIntoTheBlock_NeverNegative()
        {
            Assert.AreEqual(0.04f, PushRules.PushDistance(new Vector2(2f, 1f), Vector2.right, 0.02f), 1e-6f);
            Assert.AreEqual(0f, PushRules.PushDistance(new Vector2(-2f, 1f), Vector2.right, 0.02f), 1e-6f);
        }

        [Test]
        public void ClampToHit()
        {
            Assert.AreEqual(0.1f, PushRules.ClampToHit(0.1f, float.PositiveInfinity, 0.01f), 1e-6f);
            Assert.AreEqual(0.04f, PushRules.ClampToHit(0.1f, 0.05f, 0.01f), 1e-6f);
            Assert.AreEqual(0f, PushRules.ClampToHit(0.1f, 0.005f, 0.01f), 1e-6f);
        }

        [Test]
        public void CanPush()
        {
            Assert.IsTrue(PushRules.CanPush(Ability.None, false, Ability.Push));
            Assert.IsFalse(PushRules.CanPush(Ability.None, true, Ability.Push));
            Assert.IsTrue(PushRules.CanPush(Ability.Push, true, Ability.Push));
            Assert.IsFalse(PushRules.CanPush(Ability.Push, true, Ability.Push | Ability.Swim));
            Assert.IsTrue(PushRules.CanPush(Ability.Push | Ability.Swim, true, Ability.Push | Ability.Swim));
        }
    }
}
