# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project docs

- `Documentation/vision.md` — what EverRealm is meant to become (intent, not a roadmap). Check design suggestions against it; only the creative director changes it.
- `Documentation/session.md` — handoff of where things stand. Read at the start of a session; rewrite (don't append) at the end of each session.

## How we work

- EverRealm (procedural fantasy sandbox RPG) is a large, long-term project, not a small one: the long-term systems in `vision.md` (towns, kingdoms, NPC lives, armies, politics, dungeons, progression…) will happen. It is built through small playable milestones. Build only what the current milestone needs — no speculative systems or upfront architecture — but don't make choices that would paint those known future systems into a corner, and flag it when a shortcut would.
- The user is creative director. Surface game-design and feel decisions (controls, camera, art direction, balance, content) instead of choosing silently; proceed on obvious technical ones.
- Use Plan Mode for substantial or uncertain features, scoped to the current milestone.
- **The player is not special.** The player is an ordinary world agent whose intentions come from input instead of AI: same underlying rules and capabilities as other human/humanoid characters where appropriate (movement, combat, stamina, permanent death). Only the brain differs (`PlayerController` vs AI). Other species (e.g. goblins) may differ in capabilities and behaviour — no forced symmetry. Presentation for the local viewer (camera, shake, overlay, death screen in `PlayerSession`) is the only player-specific layer. Death is permanent; a succession/heir mechanic is planned.
- Keep responsibilities separate: agent discovery (`AgentDirectory`) is not combat; locomotion (gaits, sprint) is not driven by the combat state machine — actions only restrict movement.
- Multiplayer is a long-term goal: don't build networking now, but avoid global state for gameplay effects (e.g. no `Time.timeScale` hit-stop; use per-character time).
- Verify in Unity (compile, console, tests, Play Mode via the `unity` CLI / `unity:unity-cli` skill) before calling work done.

## Current milestone: M3 "The Hamlet"

Question: **does the world feel like it lives without me?** A seeded voxel hamlet (huts around a square) near the island centre; a day/night `WorldClock` drives villager routines (work by day, gather in the evening, sleep at home at night); `RaidDirector` sends goblin war parties that outnumber the militia (1.5×, gathering from several camps; camps regrow) on a world-time schedule; villagers are militia (fight via the shared `MeleeTactics`) or civilians (flee home/away), each with a seeded resolve (some militia fight to the death, some civilians fight when cornered); whoever spots danger shouts an alarm through `Speech` (world event; floating text sized by volume, shown only within earshot and line of sight). The player starts in the hamlet as an ordinary resident. Out of scope: pathfinding, births, healing (waits for food/inventory), productive jobs, trade, more factions, succession, town bells.

## Completed milestones

- **M1 "Walk the Realm"** — seeded voxel island, 0.7m blocks chosen, third-person movement accepted.
- **M2 "First Blood"** — melee combat accepted as a rough but promising base; all three dodge styles fit EverRealm; goblin AI "good enough for now" (refine when a milestone needs it). Combat design: simple, with depth from timing, spacing, positioning and reach — telegraphed single swing aimed at camera forward (body turns, never snaps), wind-up feintable into a dodge (cheaper than a swing), hold-block with stamina and guard break, timed block = parry, weight-class dodges (light roll / medium hopstep / heavy power step; no weight system yet), sprint drains stamina, per-character hit-stop, permanent death with a debug "take control of a new human" key.

Temporary prototype behaviour still in the game: `OceanRescue` teleports agents out of the sea — a placeholder until real swimming/water exists, not a world rule. Settlers appearing out of nowhere and the one-off recolonization roll in `HamletPopulation` are provisional M3 placeholders — to be replaced by parties travelling a larger map (wanderers, colonization parties), not world rules. Goblin camps and the G/N/V/R/T/Y/F1 keys are debug tools (T skips 2 in-game hours, Y triggers a raid, F1 hides the debug overlay).

## Unity pitfalls

- PC-only, URP, Unity 6000.6.7f1. New Input System only — use `Assets/InputSystem_Actions.inputactions`, never `UnityEngine.Input`.
- When moving/renaming assets outside the Editor, carry the `.meta` file (GUIDs). Never hand-edit generated `.csproj`/`.slnx` or `packages-lock.json`.
- `com.unity.pipeline` is what lets the `unity` CLI drive the open Editor — keep it installed.
- CLI-simulated key presses (`runtime simulate_key`) are missed by `wasPressedThisFrame` polling; handle discrete presses with `InputAction.performed` callbacks so they stay verifiable. Each simulated key event replaces the keyboard state, so held-key combos (Shift+W) can't be simulated — drive the motor/combatant directly via `eval` instead.
- Before Play Mode verification through the CLI, run `set_auto_tick` and `set_run_in_background`, or an unfocused Editor freezes at frame 1 (and the test runner can hang).
