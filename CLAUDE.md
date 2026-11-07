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
- Don't edit a scene file on disk while it's open in the Editor: Unity blocks on a reload dialog and the CLI hangs. Change scenes through `eval` and save them there.

## Current milestone: combat touch-up

The end goal for combat is Hytale-type combat, which the creative director sees as a more polished version of what we have. For now, the creative director has settled on combat like Minecraft's Better Combat mod: weighty, readable swings you can make while running. We take its structure (an ordered attack chain per weapon, hit at the halfway point, wide slashes, hold to repeat, slowed movement) and none of its animation files. Animation is one of their biggest concerns. Swings play on an upper-body layer from clips made at gameplay length, so legs keep moving, and no timing is locked in. Swings bend to the aim and land where it points, up or down. Work goes in passes they playtest before the next starts. Every mechanic lives in the shared body so player and NPC brains both have it. It comes before food and inventory. Known issues: goblins pile into one clump, standing on each other's heads, so a big war party dies in seconds without landing many hits; goblins march in straight lines and can't climb, so terrain stalls them. Re-check raid tuning afterwards, since combat decides whether hamlets fall.

## How the world works (from M4, still binding)

Rules: a hamlet is abandoned only when **no fighters remain after an attack** (a raid on it), never just because its fighter count is zero. A fighter is any living, armed, unbroken ally present in the hamlet, resident or not, so the player holds a hamlet just by being there. Survivors leave at the next dawn unless a fighter is present by then. Travellers check their destination on arrival: whoever fits moves in, the rest go to the nearest other viable hamlet, and with none they become explicitly **Stranded** (logged, re-check each dawn, first in line as colonists). **Nobody appears from nowhere**: every person arrives by walking from somewhere you could have followed them from.

Every agent stays a full body everywhere, so a raid plays out the same watched or not. That won't scale to towns and armies: a records-versus-bodies layer is its own milestone before those. Keep hamlet and journey rules in plain data, schedules on `WorldClock`, and nothing dependent on where the player is.

The world should read as normal land with striking features scattered through it. This island's density of pillars, mesas and arches is only acceptable because it's a test chunk, so don't scale feature counts with world size. Haze is really render distance and will become a player setting.

## Completed milestones

- **M1 "Walk the Realm"** — seeded voxel island, 0.7m blocks chosen, third-person movement accepted.
- **M2 "First Blood"** — melee combat accepted as a rough but promising base; all three dodge styles fit EverRealm; goblin AI "good enough for now" (refine when a milestone needs it). Combat design: simple, with depth from timing, spacing, positioning and reach — telegraphed single swing aimed at camera forward (body turns, never snaps), wind-up feintable into a dodge (cheaper than a swing), hold-block with stamina and guard break, timed block = parry, weight-class dodges (light roll / medium hopstep / heavy power step; no weight system yet), sprint drains stamina, per-character hit-stop, permanent death with a debug "take control of a new human" key.
- **M3 "The Hamlet"** — accepted as a good starting point, with combat owed a touch-up. A day/night `WorldClock` drives villager routines; `RaidDirector` sends goblin war parties (1.5× the militia, camps regrow); villagers are militia (shared `MeleeTactics`) or civilians, each with a seeded resolve; alarms travel through `Speech` (world event; floating text sized by volume, shown only within earshot and line of sight). The player is an ordinary resident.
- **M4 "The Road"** — accepted. A ~1 km island of biome regions with plateaus, cliffs, arches, pillars and mesas; three seeded, named hamlets ~450 m apart, joined by roads and a coastal landing. Each hamlet has its own residents, state and raid schedule. Refugees, colonists and newcomers walk the roads as journeys, and arriving travellers tell the news through `Speech`. The creative director confirmed the road and the world in play; the news loop was verified by tests and unattended runs only.

Temporary prototype behaviour still in the game: `OceanRescue` teleports agents out of the sea — a placeholder until real swimming/water exists, not a world rule. Goblins are the human model shrunk and coloured green, until the creative director models them. Newcomers appearing at the coastal landing is provisional until births or a wider world exist, and so is an emptied hamlet being "known" to need colonists after 2–4 days when nobody survived to carry the news. Goblin camps and the G/N/V/R/T/Y/F1/F2 keys are debug tools (T skips 2 in-game hours, Y raids the hamlet nearest the camera, F1 hides the debug overlay, F2 shows the ledger of who is where).

## Unity pitfalls

- PC-only, URP, Unity 6000.6.7f1. New Input System only — use `Assets/InputSystem_Actions.inputactions`, never `UnityEngine.Input`.
- When moving/renaming assets outside the Editor, carry the `.meta` file (GUIDs). Never hand-edit generated `.csproj`/`.slnx` or `packages-lock.json`.
- `com.unity.pipeline` is what lets the `unity` CLI drive the open Editor — keep it installed.
- CLI-simulated key presses (`runtime simulate_key`) are missed by `wasPressedThisFrame` polling; handle discrete presses with `InputAction.performed` callbacks so they stay verifiable. Each simulated key event replaces the keyboard state, so held-key combos (Shift+W) can't be simulated — drive the motor/combatant directly via `eval` instead.
- A prefab keeps the value it was last saved with: changing a field's default in code does nothing once a prefab has serialized that field. Change prefab values through `eval`.
- Before Play Mode verification through the CLI, run `set_auto_tick` and `set_run_in_background`, or an unfocused Editor freezes at frame 1 (and the test runner can hang).
