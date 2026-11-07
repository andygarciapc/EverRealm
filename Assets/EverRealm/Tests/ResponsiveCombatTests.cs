using EverRealm.Characters;
using EverRealm.Combat;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EverRealm.Tests
{
    /// <summary>
    /// The rules Pass A depends on. Timing values are left to playtesting, so these check how things behave
    /// rather than what the numbers are.
    /// </summary>
    public class ResponsiveCombatTests
    {
        static WeaponData Weapon(string name) => AssetDatabase.LoadAssetAtPath<WeaponData>($"Assets/EverRealm/Data/{name}.asset");

        static Combatant Body(string prefab) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/EverRealm/Prefabs/{prefab}.prefab").GetComponent<Combatant>();

        // ---- Input buffer ----

        [Test]
        public void BufferedCommand_FiresOnce()
        {
            var intent = new BufferedIntent();
            intent.Store(Vector3.forward, 1f);
            Assert.IsTrue(intent.TryTake(0.5f, out Vector3 stored));
            Assert.AreEqual(Vector3.forward, stored);
            Assert.IsFalse(intent.TryTake(0.5f, out _));
        }

        [Test]
        public void BufferedCommand_Expires()
        {
            var intent = new BufferedIntent();
            intent.Store(Vector3.forward, 1f);
            Assert.IsFalse(intent.TryTake(1.01f, out _));
        }

        [Test]
        public void BufferedCommands_NeverStack()
        {
            var intent = new BufferedIntent();
            for (int press = 0; press < 5; press++) intent.Store(Vector3.right * press, 1f);
            Assert.IsTrue(intent.TryTake(0.5f, out Vector3 stored));
            Assert.AreEqual(Vector3.right * 4, stored, "the latest press wins");
            Assert.IsFalse(intent.TryTake(0.5f, out _), "only one command may be waiting");
        }

        [Test]
        public void ClearedCommand_NeverFires()
        {
            var intent = new BufferedIntent();
            intent.Store(Vector3.forward, 1f);
            intent.Clear();
            Assert.IsFalse(intent.TryTake(0.5f, out _));
        }

        // ---- Dodging ----

        [TestCase(CombatState.Idle, true)]
        [TestCase(CombatState.Blocking, true)]
        [TestCase(CombatState.Windup, true)]
        [TestCase(CombatState.Recovery, true)]
        [TestCase(CombatState.Active, false)]
        [TestCase(CombatState.Dodging, false)]
        [TestCase(CombatState.Staggered, false)]
        [TestCase(CombatState.Dead, false)]
        public void Dodge_CancelsWindupAndRecoveryOnly(CombatState state, bool allowed)
        {
            Assert.AreEqual(allowed, CombatRules.CanDodgeFrom(state));
        }

        [TestCase("Human"), TestCase("Goblin")]
        public void EveryDodge_KeepsItsInvulnerability(string prefab)
        {
            foreach (DodgeProfile dodge in Body(prefab).dodges)
            {
                float window = dodge.invulnerableTo - dodge.invulnerableFrom;
                Assert.GreaterOrEqual(window, Weapon("Club").attacks[0].active, $"{dodge.style} should last as long as a club swing");
                Assert.LessOrEqual(dodge.invulnerableTo, dodge.TotalTime, $"{dodge.style} is protected past its own end");
                Assert.IsTrue(dodge.IsInvulnerableAt(dodge.duration * 0.5f), $"{dodge.style} should be protected mid-dodge");
            }
        }

        [Test]
        public void InvulnerableDefender_CannotBeHit()
        {
            var defender = new DefenderState { IsInvulnerable = true, Forward = Vector3.forward };
            Assert.AreEqual(HitResult.Dodged, CombatRules.Resolve(defender, Vector3.forward, 10f, 140f, 0.15f));
        }

        // ---- Weapons ----

        [Test]
        public void Sword_CostsNothingAndDoesNotInterrupt()
        {
            WeaponData sword = Weapon("Sword");
            Assert.AreEqual(0f, sword.staminaCost);
            Assert.AreEqual(0f, sword.feintStaminaCost);
            Assert.AreEqual(0f, sword.hitStagger);
        }

        [Test]
        public void Club_StillStaggers_AndStaysEasierToReadThanTheSword()
        {
            WeaponData club = Weapon("Club"), sword = Weapon("Sword");
            Assert.Greater(club.hitStagger, 0f);
            Assert.GreaterOrEqual(club.attacks[0].windup, sword.attacks[0].windup, "a goblin's swing must stay readable");
        }

        // ---- Animation ----

        [TestCase(0.1f, 0.09f, 0.16f), TestCase(0.35f, 0.12f, 0.4f), TestCase(0.7f, 0.15f, 0.6f)]
        public void HitWindow_LinesUpWithTheClip_WhateverTheTimings(float windup, float active, float recovery)
        {
            const float windupEnd = 0.29f, swingEnd = 0.43f, settled = 0.83f;
            Assert.AreEqual(windupEnd, CharacterAnimator.AttackPhase(CombatState.Windup, 1f, windupEnd, swingEnd, settled), 0.0001f);
            Assert.AreEqual(windupEnd, CharacterAnimator.AttackPhase(CombatState.Active, 0f, windupEnd, swingEnd, settled), 0.0001f);
            Assert.AreEqual(swingEnd, CharacterAnimator.AttackPhase(CombatState.Active, 1f, windupEnd, swingEnd, settled), 0.0001f);
            Assert.AreEqual(swingEnd, CharacterAnimator.AttackPhase(CombatState.Recovery, 0f, windupEnd, swingEnd, settled), 0.0001f);
        }

        [Test]
        public void BlendIntoASwing_DoesNotHideTheWindup()
        {
            var animator = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EverRealm/Prefabs/Human.prefab").GetComponent<CharacterAnimator>();
            Assert.Less(animator.attackCrossFadeSeconds, Weapon("Sword").attacks[0].windup * 0.5f);
        }
    }
}
