using System.Collections.Generic;
using EverRealm.Combat;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace EverRealm.Tests
{
    /// <summary>Swings play on the upper body from clips made at gameplay length, and chain in order.</summary>
    public class SwordMovesetTests
    {
        const string Folder = "Assets/EverRealm/Art/Characters/Human/Animations/";

        static WeaponData Weapon(string name) => AssetDatabase.LoadAssetAtPath<WeaponData>($"Assets/EverRealm/Data/{name}.asset");
        static AnimatorController Controller => AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "Human.controller");

        static AnimatorControllerLayer Layer(string name)
        {
            foreach (AnimatorControllerLayer layer in Controller.layers)
                if (layer.name == name) return layer;
            Assert.Fail($"no {name} layer");
            return null;
        }

        static HashSet<string> States(AnimatorControllerLayer layer)
        {
            var names = new HashSet<string>();
            foreach (ChildAnimatorState child in layer.stateMachine.states) names.Add(child.state.name);
            return names;
        }

        [Test]
        public void UpperBodyMask_HasArmsAndNoLegs()
        {
            AvatarMask mask = Layer("UpperBody").avatarMask;
            Assert.IsNotNull(mask);
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm));
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm));
            Assert.IsTrue(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body));
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg));
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg));
            Assert.IsFalse(mask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Root));
        }

        [Test]
        public void EverySwordAttack_PlaysOnTheUpperBodyOnly()
        {
            HashSet<string> upper = States(Layer("UpperBody")), legs = States(Controller.layers[0].name == "UpperBody" ? null : Controller.layers[0]);
            foreach (WeaponAttack attack in Weapon("Sword").attacks)
            {
                Assert.IsTrue(upper.Contains(attack.animation), $"{attack.name} has no state");
                Assert.IsFalse(legs.Contains(attack.animation), $"{attack.name} would stop the legs");
            }
            Assert.AreEqual(0f, Layer("UpperBody").defaultWeight);
        }

        [Test]
        public void SwordClips_TouchNoLegsAndPlayAtNormalSpeed()
        {
            foreach (WeaponAttack attack in Weapon("Sword").attacks)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Folder}{attack.animation}.anim");
                Assert.IsNotNull(clip, attack.name);
                foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                    Assert.IsFalse(binding.propertyName.Contains("Leg") || binding.propertyName.Contains("Foot") || binding.propertyName.Contains("Toes"), $"{attack.name} moves {binding.propertyName}");

                float seconds = attack.windup + attack.active + attack.recovery;
                Assert.AreEqual(seconds, clip.length, 0.02f, $"{attack.name} should be made at gameplay length");
            }
        }

        [Test]
        public void HitWindow_SitsOnEachClipsStrike()
        {
            foreach (WeaponAttack attack in Weapon("Sword").attacks)
            {
                float seconds = attack.windup + attack.active + attack.recovery;
                Assert.AreEqual(attack.windup / seconds, attack.clipWindupEnd, 0.01f, attack.name);
                Assert.AreEqual((attack.windup + attack.active) / seconds, attack.clipSwingEnd, 0.01f, attack.name);
            }
        }

        [Test]
        public void Sword_ChainsThreeAttacks_AndClubStaysOne()
        {
            Assert.AreEqual(3, Weapon("Sword").attacks.Length);
            Assert.AreEqual(1, Weapon("Club").attacks.Length);
        }

        [Test]
        public void Sword_IsGrippedAcrossTheFist_NotInLineWithTheArm()
        {
            GameObject human = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EverRealm/Prefabs/Human.prefab");
            Transform mount = null, handEnd = null;
            foreach (Transform bone in human.GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "WeaponMount") mount = bone;
                if (bone.name == "HandR_end") handEnd = bone;
            }
            Assert.IsNotNull(mount);
            Assert.IsNotNull(handEnd);

            // Both are children of the hand, so these directions share its space.
            float angle = Vector3.Angle(mount.localRotation * Vector3.forward, handEnd.localPosition);
            Assert.Greater(angle, 60f, "a blade running along the forearm looks like a prosthetic");
            Assert.Greater(mount.localPosition.magnitude, handEnd.localPosition.magnitude * 0.25f, "the grip belongs in the palm, not at the wrist");
        }

        [Test]
        public void SwordGuard_IsAnUpperBodyPose()
        {
            string guard = Weapon("Sword").guardAnimation;
            Assert.IsFalse(string.IsNullOrEmpty(guard));
            Assert.IsTrue(States(Layer("UpperBody")).Contains(guard));
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Folder}{guard}.anim");
            Assert.IsNotNull(clip);
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
                Assert.IsFalse(binding.propertyName.Contains("Leg") || binding.propertyName.Contains("Foot"), binding.propertyName);
        }

        [Test]
        public void Bodies_HaveNoProceduralArmsLeft()
        {
            Assert.IsNull(System.Type.GetType("EverRealm.Combat.ArmPoser, EverRealm"));
        }

        [Test]
        public void Goblin_IsASmallerBodyOnTheHumanRig()
        {
            GameObject human = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EverRealm/Prefabs/Human.prefab");
            GameObject goblin = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EverRealm/Prefabs/Goblin.prefab");
            Animator humanRig = human.GetComponentInChildren<Animator>(true), goblinRig = goblin.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(goblinRig);
            Assert.AreEqual(humanRig.avatar, goblinRig.avatar);
            Assert.AreEqual(humanRig.runtimeAnimatorController, goblinRig.runtimeAnimatorController);
            Assert.IsNotNull(goblin.GetComponent<EverRealm.Characters.CharacterAnimator>());
            Assert.Less(goblinRig.transform.localScale.x, humanRig.transform.localScale.x);
        }

        [TestCase(1f, 0f, 0f), TestCase(1f, 1f, 45f), TestCase(1f, -1f, -45f), TestCase(0f, 1f, 90f)]
        public void PitchOf_IsDegreesAboveLevel(float flat, float up, float expected)
        {
            Assert.AreEqual(expected, CombatRules.PitchOf(new Vector3(flat, up, 0f)), 0.01f);
            Assert.AreEqual(expected, CombatRules.PitchOf(new Vector3(0f, up, flat)), 0.01f);
        }

        [TestCase(40f, 0.5f, 3), TestCase(-30f, 0.25f, 2), TestCase(50f, 1f, 3)]
        public void SplitPitch_SpineAndArmAddUpToTheWholeAim(float pitch, float share, int bones)
        {
            (float perSpineBone, float perArm) = EverRealm.Characters.CharacterAnimator.SplitPitch(pitch, share, bones);
            Assert.AreEqual(pitch, perSpineBone * bones + perArm, 0.001f);
        }

        [TestCase("Human"), TestCase("Goblin")]
        public void Bodies_HaveTheBonesAimingBends(string prefab)
        {
            Animator rig = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/EverRealm/Prefabs/{prefab}.prefab").GetComponentInChildren<Animator>(true);
            Assert.IsTrue(rig.isHuman);
            Assert.IsTrue(rig.avatar.isValid);
            Assert.IsNotNull(rig.avatar.humanDescription.human);
            var bones = new HashSet<string>();
            foreach (HumanBone bone in rig.avatar.humanDescription.human) bones.Add(bone.humanName);
            foreach (string needed in new[] { "Spine", "LeftUpperArm", "RightUpperArm" }) Assert.IsTrue(bones.Contains(needed), needed);
        }

        [Test]
        public void Goblin_HitboxIsAsTallAsItsModel()
        {
            GameObject human = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EverRealm/Prefabs/Human.prefab");
            GameObject goblin = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/EverRealm/Prefabs/Goblin.prefab");
            float scale = goblin.GetComponentInChildren<Animator>(true).transform.localScale.x / human.GetComponentInChildren<Animator>(true).transform.localScale.x;
            Assert.AreEqual(human.GetComponent<CharacterController>().height * scale, goblin.GetComponent<CharacterController>().height, 0.01f);
        }

        [Test]
        public void ClubAttacks_HaveStatesOnTheUpperBody()
        {
            WeaponData club = Weapon("Club");
            HashSet<string> upper = States(Layer("UpperBody"));
            Assert.IsTrue(upper.Contains(club.attacks[0].animation));
            Assert.IsTrue(upper.Contains(club.guardAnimation));
        }

        [Test]
        public void Chain_AdvancesAndWraps()
        {
            var chain = new AttackChain();
            var seen = new List<int>();
            for (int swing = 0; swing < 4; swing++)
            {
                seen.Add(chain.Next(3, swing, 5f));
                chain.Commit(3, swing);
            }
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0 }, seen);
        }

        [Test]
        public void Chain_StartsOverAfterAPause()
        {
            var chain = new AttackChain();
            chain.Commit(3, 0f);
            Assert.AreEqual(1, chain.Next(3, 1f, 1.2f));
            Assert.AreEqual(0, chain.Next(3, 1.3f, 1.2f));
        }

        [Test]
        public void CancelledWindup_RepeatsTheSameAttack()
        {
            var chain = new AttackChain();
            chain.Commit(3, 0f);
            Assert.AreEqual(1, chain.Next(3, 0.5f, 1.2f));
            // The wind-up is cancelled into a dodge: nothing committed, so the same attack comes up again.
            Assert.AreEqual(1, chain.Next(3, 0.8f, 1.2f));
        }

        [Test]
        public void SingleAttackWeapon_RepeatsIt()
        {
            var chain = new AttackChain();
            chain.Commit(1, 0f);
            Assert.AreEqual(0, chain.Next(1, 0.5f, 1.2f));
        }
    }
}
