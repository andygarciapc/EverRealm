using System;
using System.Collections.Generic;
using EverRealm.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace EverRealm.EditorTools
{
    /// <summary>
    /// Sets up the human animation set from the Mixamo FBX clips in Art/Characters/Human/Animations and gives
    /// it to the Human prefab. The clips are on the model's own skeleton but their files hold no T-pose (their
    /// rest pose is the first animation frame), so they use the model's Humanoid avatar rather than their own.
    /// Run it again after adding or replacing a clip.
    /// </summary>
    static class HumanAnimationBuilder
    {
        const string HumanFolder = "Assets/EverRealm/Art/Characters/Human/";
        const string ClipFolder = HumanFolder + "Animations";
        const string ModelPath = HumanFolder + "HumanMale.fbx";
        const string ControllerPath = ClipFolder + "/Human.controller";
        const string PrefabPath = "Assets/EverRealm/Prefabs/Human.prefab";

        // Without their own height, jumps sit too low. A negative offset raises them.
        const float JumpHeightOffset = -0.18f;

        readonly struct ClipSource
        {
            public readonly string file, name;
            public readonly bool loop, keepHeight;

            public ClipSource(string file, string name, bool loop = false, bool keepHeight = true)
            {
                this.file = file;
                this.name = name;
                this.loop = loop;
                this.keepHeight = keepHeight;
            }
        }

        // keepHeight: the clip's vertical motion stays in the pose. Not for jumps, whose height the motor provides.
        // standing_jump and medium_dodge_left_right are optional: until they exist, the running jump and the
        // heavy side step stand in for them.
        static readonly ClipSource[] Sources =
        {
            new ClipSource("idle", "Idle", loop: true),
            new ClipSource("jog", "Jog", loop: true),
            new ClipSource("run", "Run", loop: true),
            new ClipSource("sprint", "Sprint", loop: true),
            new ClipSource("running_jump", "RunningJump", keepHeight: false),
            new ClipSource("standing_jump", "StandingJump", keepHeight: false),
            new ClipSource("standing_melee", "Attack"),
            new ClipSource("light_dodge_forward", "RollForward"),
            new ClipSource("medium_dodge_forward", "HopForward"),
            new ClipSource("medium_dodge_backward", "HopBackward"),
            new ClipSource("medium_dodge_left_right", "HopSide"),
            new ClipSource("heavy_dodge_left_right", "PowerStepSide"),
            new ClipSource("death", "Death"),
        };

        [MenuItem("EverRealm/Rebuild Human Animations")]
        public static void Build()
        {
            Avatar avatar = ModelAvatar();
            var clips = new Dictionary<string, AnimationClip>();
            foreach (ClipSource source in Sources)
            {
                string path = $"{ClipFolder}/{source.file}.fbx";
                if (AssetImporter.GetAtPath(path) is ModelImporter importer) clips[source.name] = Import(importer, avatar, source);
            }

            AnimatorController controller = BuildController(clips);
            GiveToPrefab(controller, avatar, clips["Death"]);
            AssetDatabase.SaveAssets();
            Debug.Log($"Rebuilt {clips.Count} human animation clips and {ControllerPath}.");
        }

        static Avatar ModelAvatar()
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                if (asset is Avatar avatar && avatar.isHuman) return avatar;
            throw new InvalidOperationException($"{ModelPath} has no Humanoid avatar.");
        }

        /// <summary>
        /// Imports one clip in place, since the motor moves the body: the root's turn and height stay in the pose
        /// and its horizontal travel is dropped with root motion.
        /// </summary>
        static AnimationClip Import(ModelImporter importer, Avatar avatar, ClipSource source)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
            importer.SaveAndReimport();

            ModelImporterClipAnimation clip = importer.defaultClipAnimations[0];
            clip.name = source.name;
            clip.loopTime = source.loop;
            clip.lockRootRotation = true;
            clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = source.keepHeight;
            clip.keepOriginalPositionY = true;
            clip.heightOffset = source.keepHeight ? 0f : JumpHeightOffset;
            importer.clipAnimations = new[] { clip };
            importer.SaveAndReimport();

            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(importer.assetPath))
                if (asset is AnimationClip imported && imported.name == source.name) return imported;
            throw new InvalidOperationException($"{importer.assetPath} has no animation clip.");
        }

        /// <summary>
        /// One layer, driven from code (CharacterAnimator cross-fades between states): locomotion blends on
        /// Speed; actions are posed by the Phase parameter (Motion Time) so gameplay timing decides the pose.
        /// </summary>
        static AnimatorController BuildController(Dictionary<string, AnimationClip> clips)
        {
            AnimationClip Clip(string name) =>
                clips.TryGetValue(name, out AnimationClip clip) ? clip : throw new InvalidOperationException($"No clip named {name} in {ClipFolder}.");

            // Empty the existing controller rather than replacing it, so references to it stay valid.
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                                            ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (asset is BlendTree old) UnityEngine.Object.DestroyImmediate(old, true);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            foreach (ChildAnimatorState child in machine.states) machine.RemoveState(child.state);
            foreach (AnimatorControllerParameter parameter in controller.parameters) controller.RemoveParameter(parameter);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Phase", AnimatorControllerParameterType.Float);

            // Every gait plays its clip at natural speed: walking is the jog, running the run, sprinting the sprint clip.
            var motor = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<CharacterMotor>();
            var tree = new BlendTree { name = "Locomotion", blendParameter = "Speed", blendType = BlendTreeType.Simple1D, useAutomaticThresholds = false };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.hideFlags = HideFlags.HideInHierarchy;
            tree.children = new[]
            {
                new ChildMotion { motion = Clip("Idle"), threshold = 0f, timeScale = 1f },
                new ChildMotion { motion = Clip("Jog"), threshold = motor.walkSpeed, timeScale = 1f },
                new ChildMotion { motion = Clip("Run"), threshold = motor.runSpeed, timeScale = 1f },
                new ChildMotion { motion = Clip("Sprint"), threshold = motor.sprintSpeed, timeScale = 1f },
            };
            AnimatorState locomotion = machine.AddState("Locomotion");
            locomotion.motion = tree;
            machine.defaultState = locomotion;

            machine.AddState("StandingJump").motion = clips.TryGetValue("StandingJump", out AnimationClip standingJump) ? standingJump : Clip("RunningJump");
            machine.AddState("RunningJump").motion = Clip("RunningJump");

            void Phased(string state, AnimationClip clip, bool mirror = false)
            {
                AnimatorState added = machine.AddState(state);
                added.motion = clip;
                added.mirror = mirror;
                added.timeParameterActive = true;
                added.timeParameter = "Phase";
            }
            Phased("Attack", Clip("Attack"));
            Phased("Roll", Clip("RollForward"));
            Phased("HopForward", Clip("HopForward"));
            Phased("HopBackward", Clip("HopBackward"));
            // The side hop steps right. The heavy side step standing in for it steps left, so its sides swap.
            bool standIn = !clips.TryGetValue("HopSide", out AnimationClip hopSide);
            if (standIn) hopSide = Clip("PowerStepSide");
            Phased("HopSide", hopSide, mirror: standIn);
            Phased("HopSideMirror", hopSide, mirror: !standIn);
            Phased("PowerStep", Clip("PowerStepSide"));
            Phased("PowerStepMirror", Clip("PowerStepSide"), mirror: true);
            Phased("Death", Clip("Death"));
            EditorUtility.SetDirty(controller);
            return controller;
        }

        static void GiveToPrefab(AnimatorController controller, Avatar avatar, AnimationClip death)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Animator animator = root.GetComponentInChildren<Animator>(true);
                animator.avatar = avatar;
                animator.runtimeAnimatorController = controller;
                root.GetComponent<CharacterAnimator>().deathClipSeconds = death.length;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
