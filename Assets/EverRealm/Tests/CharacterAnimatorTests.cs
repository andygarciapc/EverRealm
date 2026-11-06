using EverRealm.Characters;
using EverRealm.Combat;
using NUnit.Framework;
using UnityEngine;

namespace EverRealm.Tests
{
    public class CharacterAnimatorTests
    {
        [Test]
        public void SwingPhases_FillTheirStretchOfTheClip()
        {
            Assert.AreEqual(0f, CharacterAnimator.AttackPhase(CombatState.Windup, 0f, 0.3f, 0.45f, 0.8f), 1e-5f);
            Assert.AreEqual(0.3f, CharacterAnimator.AttackPhase(CombatState.Windup, 1f, 0.3f, 0.45f, 0.8f), 1e-5f);
            Assert.AreEqual(0.375f, CharacterAnimator.AttackPhase(CombatState.Active, 0.5f, 0.3f, 0.45f, 0.8f), 1e-5f);
            Assert.AreEqual(0.8f, CharacterAnimator.AttackPhase(CombatState.Recovery, 1f, 0.3f, 0.45f, 0.8f), 1e-5f);
        }

        [Test]
        public void DodgePhase_MovementThenRecovery_WhateverTheTimings()
        {
            // 0.4s of movement then 0.2s of recovery, mapped onto [0, 0.6] and [0.6, 1].
            Assert.AreEqual(0.3f, CharacterAnimator.DodgePhase(0.2f, 0.4f, 0.2f, 0.6f, 1f), 1e-5f);
            Assert.AreEqual(0.6f, CharacterAnimator.DodgePhase(0.4f, 0.4f, 0.2f, 0.6f, 1f), 1e-5f);
            Assert.AreEqual(0.8f, CharacterAnimator.DodgePhase(0.5f, 0.4f, 0.2f, 0.6f, 1f), 1e-5f);
            // Changing gameplay timings only stretches the mapping.
            Assert.AreEqual(0.3f, CharacterAnimator.DodgePhase(0.1f, 0.2f, 0.1f, 0.6f, 1f), 1e-5f);
        }

        [TestCase(0f, 1f, DodgeClip.HopForward, false)]
        [TestCase(0f, -1f, DodgeClip.HopBackward, false)]
        [TestCase(1f, 0f, DodgeClip.HopSide, false)]  // The side clip steps right.
        [TestCase(-1f, 0f, DodgeClip.HopSide, true)]
        public void Hopsteps_PickClipByDirection(float x, float z, DodgeClip clip, bool mirror)
        {
            Assert.AreEqual((clip, mirror), CharacterAnimator.ChooseDodge(DodgeStyle.Hopstep, new Vector3(x, 0f, z)));
        }

        [Test]
        public void PowerStep_ClipStepsLeft_MirroredForRight()
        {
            Assert.AreEqual((DodgeClip.PowerStep, false), CharacterAnimator.ChooseDodge(DodgeStyle.PowerStep, Vector3.left));
            Assert.AreEqual((DodgeClip.PowerStep, true), CharacterAnimator.ChooseDodge(DodgeStyle.PowerStep, Vector3.right));
            Assert.AreEqual((DodgeClip.Roll, false), CharacterAnimator.ChooseDodge(DodgeStyle.Roll, Vector3.back));
        }

        [Test]
        public void PowerStep_TurnsSideOn_ByTheSmallerTurn()
        {
            // Facing north, stepping north: turn east or west, never around.
            Vector3 facing = Combatant.FacingDuringDodge(DodgeStyle.PowerStep, Vector3.forward, new Vector3(0.2f, 0f, 1f).normalized);
            Assert.AreEqual(0f, Vector3.Dot(facing, Vector3.forward), 1e-5f);
            Assert.Greater(Vector3.Dot(facing, Vector3.right), 0.99f);
            Assert.AreEqual(Vector3.back, Combatant.FacingDuringDodge(DodgeStyle.Roll, Vector3.back, Vector3.forward));
            Assert.AreEqual(Vector3.forward, Combatant.FacingDuringDodge(DodgeStyle.Hopstep, Vector3.left, Vector3.forward));
        }
    }
}
