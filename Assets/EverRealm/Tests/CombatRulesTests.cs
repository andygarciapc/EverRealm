using EverRealm.Combat;
using NUnit.Framework;
using UnityEngine;

namespace EverRealm.Tests
{
    public class CombatRulesTests
    {
        const float BlockCost = 20f, BlockArc = 140f, ParryWindow = 0.15f;

        static DefenderState Blocking(float heldTime, float stamina = 100f) => new DefenderState
        {
            IsBlocking = true,
            BlockHeldTime = heldTime,
            Stamina = stamina,
            Forward = Vector3.forward,
        };

        static HitResult ResolveFromFront(DefenderState defender) =>
            CombatRules.Resolve(defender, Vector3.forward, BlockCost, BlockArc, ParryWindow);

        [Test]
        public void InArc_IncludesFrontAndExcludesBehind()
        {
            Assert.IsTrue(CombatRules.InArc(Vector3.forward, new Vector3(0.3f, 0f, 1f), 100f));
            Assert.IsFalse(CombatRules.InArc(Vector3.forward, Vector3.back, 100f));
            Assert.IsFalse(CombatRules.InArc(Vector3.forward, Vector3.right, 100f)); // 90° is outside a 100° arc.
        }

        [Test]
        public void SwingReaches_RespectsReachAndTargetRadius()
        {
            Vector3 target = new Vector3(0f, 0f, 2.4f);
            Assert.IsTrue(CombatRules.SwingReaches(Vector3.zero, Vector3.forward, target, 0.35f, 2.2f, 100f, 1.5f));
            Assert.IsFalse(CombatRules.SwingReaches(Vector3.zero, Vector3.forward, target, 0f, 2.2f, 100f, 1.5f));
            Assert.IsFalse(CombatRules.SwingReaches(Vector3.zero, Vector3.back, target, 0.35f, 2.2f, 100f, 1.5f));
        }

        [Test]
        public void SwingReaches_FailsAcrossLargeHeightDifference()
        {
            Vector3 below = new Vector3(0f, -2.2f, 1.5f);
            Assert.IsFalse(CombatRules.SwingReaches(Vector3.zero, Vector3.forward, below, 0.35f, 2.2f, 100f, 1.5f));
        }

        [Test]
        public void UnprotectedHit_Damages() =>
            Assert.AreEqual(HitResult.Damaged, ResolveFromFront(new DefenderState { Forward = Vector3.forward }));

        [Test]
        public void Invulnerable_Dodges() =>
            Assert.AreEqual(HitResult.Dodged, ResolveFromFront(new DefenderState { IsInvulnerable = true, Forward = Vector3.forward }));

        [Test]
        public void FreshBlock_Parries() =>
            Assert.AreEqual(HitResult.Parried, ResolveFromFront(Blocking(heldTime: 0.1f)));

        [Test]
        public void HeldBlock_BlocksWhileStaminaLasts()
        {
            Assert.AreEqual(HitResult.Blocked, ResolveFromFront(Blocking(heldTime: 1f, stamina: 50f)));
            Assert.AreEqual(HitResult.GuardBroken, ResolveFromFront(Blocking(heldTime: 1f, stamina: 5f)));
        }

        [Test]
        public void BlockDoesNotCoverBehind() =>
            Assert.AreEqual(HitResult.Damaged,
                CombatRules.Resolve(Blocking(heldTime: 1f), Vector3.back, BlockCost, BlockArc, ParryWindow));

        [Test]
        public void DeadTarget_IsIgnored() =>
            Assert.AreEqual(HitResult.Ignored, ResolveFromFront(new DefenderState { IsDead = true, IsInvulnerable = true }));

        [Test]
        public void DodgeProfiles_InvulnerabilityWindow()
        {
            DodgeProfile roll = DodgeProfile.Defaults()[(int)WeightClass.Light];
            Assert.IsFalse(roll.IsInvulnerableAt(0f));
            Assert.IsTrue(roll.IsInvulnerableAt(0.2f));
            Assert.IsFalse(roll.IsInvulnerableAt(roll.duration));
        }
    }
}
