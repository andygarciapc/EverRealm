using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EverRealm.EditorTools
{
    /// <summary>
    /// Writes the sword's attack clips from the poses in SwordMoves.txt, so a swing can be reshaped without
    /// touching code. Each clip is made at gameplay length and plays at normal speed. Poses are Humanoid muscle
    /// values laid over a base pose taken from another clip, which supplies the sword grip. Upper body only:
    /// anything on the legs or root is refused.
    /// </summary>
    public static class SwordMovesetBuilder
    {
        const string Folder = "Assets/EverRealm/Art/Characters/Human/Animations/";
        const string MovesPath = Folder + "SwordMoves.txt";
        const string BaseClipPath = Folder + "standing_melee.fbx";

        sealed class Key
        {
            public float Time;
            public readonly Dictionary<string, float> Muscles = new Dictionary<string, float>();
        }

        sealed class Move
        {
            public string State;
            public float Seconds, BaseTime;
            public readonly List<Key> Keys = new List<Key>();
        }

        /// <summary>Whether a muscle belongs to the upper body. The clips, and the layer playing them, leave everything else alone.</summary>
        public static bool IsUpperBody(string muscle) =>
            !(muscle.Contains("Leg") || muscle.Contains("Foot") || muscle.Contains("Toes") || muscle.StartsWith("Root") || muscle.StartsWith("Motion"));

        /// <summary>Writes every move's clip, replacing the contents of any that exist, and returns them by Animator state name.</summary>
        public static Dictionary<string, AnimationClip> Build()
        {
            AnimationClip baseClip = BaseClip();
            var clips = new Dictionary<string, AnimationClip>();
            foreach (Move move in ReadMoves(File.ReadAllText(MovesPath)))
                clips[move.State] = Write(move, baseClip);
            AssetDatabase.SaveAssets();
            return clips;
        }

        static AnimationClip BaseClip()
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(BaseClipPath))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__")) return clip;
            throw new InvalidOperationException($"{BaseClipPath} has no clip for the base pose.");
        }

        /// <summary>Format: "move State seconds baseTime", then "key time", then "Muscle Name = value" lines. # starts a comment.</summary>
        static List<Move> ReadMoves(string text)
        {
            var moves = new List<Move>();
            Move move = null;
            Key key = null;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Split('#')[0].Trim();
                if (line.Length == 0) continue;
                string[] words = line.Split(' ');
                if (words[0] == "move")
                {
                    move = new Move { State = words[1], Seconds = Number(words[2]), BaseTime = Number(words[3]) };
                    moves.Add(move);
                }
                else if (words[0] == "key")
                {
                    key = new Key { Time = Number(words[1]) };
                    move.Keys.Add(key);
                }
                else
                {
                    string[] parts = line.Split('=');
                    string muscle = parts[0].Trim();
                    if (!IsUpperBody(muscle)) throw new InvalidOperationException($"{muscle} isn't upper body.");
                    key.Muscles[muscle] = Number(parts[1].Trim());
                }
            }
            return moves;
        }

        static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);

        static AnimationClip Write(Move move, AnimationClip baseClip)
        {
            string path = $"{Folder}{move.State}.anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = move.State };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();

            foreach (EditorCurveBinding source in AnimationUtility.GetCurveBindings(baseClip))
            {
                if (source.type != typeof(Animator) || !IsUpperBody(source.propertyName)) continue;
                float baseValue = AnimationUtility.GetEditorCurve(baseClip, source).Evaluate(move.BaseTime);
                var curve = new AnimationCurve();
                float previous = baseValue;
                foreach (Key key in move.Keys)
                {
                    // A muscle a key doesn't name holds its last value, so a pose only lists what moves.
                    if (key.Muscles.TryGetValue(source.propertyName, out float value)) previous = value;
                    curve.AddKey(key.Time * move.Seconds, previous);
                }
                for (int i = 0; i < curve.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                }
                AnimationUtility.SetEditorCurve(clip, source, curve);
            }
            EditorUtility.SetDirty(clip);
            return clip;
        }
    }
}
