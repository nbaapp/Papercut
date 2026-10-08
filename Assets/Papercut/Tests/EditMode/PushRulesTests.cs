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
        public void HoldInput_PushPullAndDeadBand()
        {
            // Block on the right (axis = right).
            var move = PushRules.HoldInput(Vector2.right, Vector2.right, 0.3f, out var sense);
            Assert.AreEqual(Vector2.right, move);
            Assert.AreEqual(1, sense, "into the block is a push");

            move = PushRules.HoldInput(Vector2.left, Vector2.right, 0.3f, out sense);
            Assert.AreEqual(Vector2.left, move);
            Assert.AreEqual(-1, sense, "away from the block is a pull");

            move = PushRules.HoldInput(new Vector2(0.2f, 0f), Vector2.right, 0.3f, out sense);
            Assert.AreEqual(Vector2.zero, move);
            Assert.AreEqual(0, sense, "below the threshold nothing moves");

            move = PushRules.HoldInput(new Vector2(-0.3f, 0f), Vector2.right, 0.3f, out sense);
            Assert.AreEqual(-1, sense, "the threshold itself counts");
            Assert.AreEqual(-0.3f, move.x, 1e-6f);

            move = PushRules.HoldInput(Vector2.zero, Vector2.right, 0f, out sense);
            Assert.AreEqual(0, sense, "zero input is nothing even at threshold 0");
            Assert.AreEqual(Vector2.zero, move);
        }

        [Test]
        public void HoldInput_DropsInputAcrossTheAxis()
        {
            var move = PushRules.HoldInput(Vector2.up, Vector2.right, 0.3f, out var sense);
            Assert.AreEqual(Vector2.zero, move, "sideways does nothing");
            Assert.AreEqual(0, sense);

            var diagonal = new Vector2(0.7f, 0.7f);
            move = PushRules.HoldInput(diagonal, Vector2.right, 0.3f, out sense);
            Assert.AreEqual(new Vector2(0.7f, 0f), move, "only the axis component survives");
            Assert.AreEqual(1, sense);
        }

        [Test]
        public void HoldInput_BlockOnTheLeft()
        {
            // Axis = left: pressing left pushes, pressing right pulls.
            var move = PushRules.HoldInput(Vector2.left, Vector2.left, 0.3f, out var sense);
            Assert.AreEqual(Vector2.left, move);
            Assert.AreEqual(1, sense);
            move = PushRules.HoldInput(Vector2.right, Vector2.left, 0.3f, out sense);
            Assert.AreEqual(Vector2.right, move);
            Assert.AreEqual(-1, sense);

            // Axis = down.
            move = PushRules.HoldInput(Vector2.up, Vector2.down, 0.3f, out sense);
            Assert.AreEqual(Vector2.up, move);
            Assert.AreEqual(-1, sense);
        }

        [Test]
        public void PushDistance_ComponentIntoTheBlock_NeverNegative()
        {
            Assert.AreEqual(0.04f, PushRules.PushDistance(new Vector2(2f, 1f), Vector2.right, 0.02f), 1e-6f);
            Assert.AreEqual(0f, PushRules.PushDistance(new Vector2(-2f, 1f), Vector2.right, 0.02f), 1e-6f);
        }

        [Test]
        public void FollowInput_CarriesThePlayerAsFarAsTheBlockWent()
        {
            // Speed 2 over 0.02 s covers 0.04: a full move is a unit input.
            var input = PushRules.FollowInput(Vector2.right, 0.04f, 2f, 0.02f);
            Assert.AreEqual(1f, input.x, 1e-5f);
            Assert.AreEqual(0f, input.y, 1e-6f);

            // Half the move is half the input, and pulling (direction away) points away.
            input = PushRules.FollowInput(Vector2.left, 0.02f, 2f, 0.02f);
            Assert.AreEqual(-0.5f, input.x, 1e-5f);

            // Round trip: input * speed * dt is the move, for a few values.
            foreach (var (moved, speed, dt) in new[] { (0.01f, 4f, 0.02f), (0.03f, 2f, 0.02f), (0.005f, 0.5f, 0.0166f) })
            {
                var i = PushRules.FollowInput(Vector2.up, moved, speed, dt);
                Assert.AreEqual(moved, i.y * speed * dt, 1e-6f);
            }
        }

        [Test]
        public void FollowInput_NeverAboveUnit_ZeroWhenNothingCanMove()
        {
            Assert.AreEqual(1f, PushRules.FollowInput(Vector2.right, 1f, 2f, 0.02f).magnitude, 1e-5f, "clamped to a unit input");
            Assert.AreEqual(Vector2.zero, PushRules.FollowInput(Vector2.right, 0f, 2f, 0.02f), "block did not move");
            Assert.AreEqual(Vector2.zero, PushRules.FollowInput(Vector2.right, 0.04f, 0f, 0.02f), "speed 0 (push speed factor 0)");
            Assert.AreEqual(Vector2.zero, PushRules.FollowInput(Vector2.right, 0.04f, 2f, 0f), "dt 0");
            Assert.AreEqual(Vector2.zero, PushRules.FollowInput(Vector2.right, -0.04f, 2f, 0.02f), "a negative move is no move");
        }

        [Test]
        public void HoldBroken_OutsideTheToleranceEitherWay()
        {
            Assert.IsFalse(PushRules.HoldBroken(0.5f, 0.5f, 0.1f));
            Assert.IsFalse(PushRules.HoldBroken(0.59f, 0.5f, 0.1f));
            Assert.IsFalse(PushRules.HoldBroken(0.41f, 0.5f, 0.1f));
            Assert.IsTrue(PushRules.HoldBroken(0.61f, 0.5f, 0.1f), "block drifted away");
            Assert.IsTrue(PushRules.HoldBroken(0.39f, 0.5f, 0.1f), "block came nearer");
            Assert.IsTrue(PushRules.HoldBroken(0.5f, -0.5f, 0.1f), "block went to the other side");
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
            Assert.IsFalse(PushRules.CanPush(false, Ability.Push, requiresAbility: false, Ability.Push), "a fixed paperweight is a wall whatever the pusher holds");
            Assert.IsFalse(PushRules.CanPush(false, Ability.Push, requiresAbility: true, Ability.Push));
            Assert.IsTrue(PushRules.CanPush(true, Ability.None, false, Ability.Push), "an ordinary block needs nothing");
            Assert.IsFalse(PushRules.CanPush(true, Ability.None, true, Ability.Push));
            Assert.IsTrue(PushRules.CanPush(true, Ability.Push, true, Ability.Push));
            Assert.IsFalse(PushRules.CanPush(true, Ability.Push, true, Ability.Push | Ability.Swim));
            Assert.IsTrue(PushRules.CanPush(true, Ability.Push | Ability.Swim, true, Ability.Push | Ability.Swim));
        }
    }
}
