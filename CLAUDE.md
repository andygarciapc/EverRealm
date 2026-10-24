# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## How we work

- EverRealm (procedural fantasy sandbox RPG) is built through small playable milestones. Build only what the current milestone needs — no speculative systems or upfront architecture for future features.
- The user is creative director. Surface game-design and feel decisions (controls, camera, art direction, balance, content) instead of choosing silently; proceed on obvious technical ones.
- Use Plan Mode for substantial or uncertain features, scoped to the current milestone.
- Multiplayer is a long-term goal: don't build networking now, but avoid global state for gameplay effects (e.g. no `Time.timeScale` hit-stop; use per-character time).
- Verify in Unity (compile, console, tests, Play Mode via the `unity` CLI / `unity:unity-cli` skill) before calling work done.

## Current milestone: M2 "First Blood" (melee combat)

Question: is fighting a humanoid enemy on this terrain readable and fun, and which dodge style fits?
Design pillars: simple combat with depth from timing, spacing, positioning and weapon reach. Single telegraphed swing aimed at camera forward (body turns toward it, never snaps); hold-block in front arc draining stamina (guard break when empty), timed block = parry (staggers attacker); dodge varies by weight class (light roll / medium hopstep / heavy power step — debug-switchable, no weight system yet). Goblin fighters in seeded camps (1–3) plus debug spawn; one attacker at a time. Feedback: per-character hit-stop, camera shake, knockback, white flash. No health-bar HUD (stats in debug overlay).
Out of scope: weapon variety, inventory/weight, enemy block/dodge, ranged, XP/loot, audio, real animations.

Previous: M1 "Walk the Realm" done — seeded voxel island, 0.7m blocks chosen, third-person movement accepted.

## Unity pitfalls

- PC-only, URP, Unity 6000.6.7f1. New Input System only — use `Assets/InputSystem_Actions.inputactions`, never `UnityEngine.Input`.
- When moving/renaming assets outside the Editor, carry the `.meta` file (GUIDs). Never hand-edit generated `.csproj`/`.slnx` or `packages-lock.json`.
- `com.unity.pipeline` is what lets the `unity` CLI drive the open Editor — keep it installed.
- CLI-simulated key presses (`runtime simulate_key`) are missed by `wasPressedThisFrame` polling; handle discrete presses with `InputAction.performed` callbacks so they stay verifiable.
