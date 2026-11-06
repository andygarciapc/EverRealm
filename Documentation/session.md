# Session handoff

> Rewritten (not appended) at the end of each session. Only what the next session needs to know about where things stand. History lives in git.

**Last updated:** 2026-11-06 (clip re-drop, locomotion at natural speed, run clip added)

## Where things stand

- **M3 "The Hamlet":** built and verified in Play Mode; waiting on the creative director's playtest of the latest round. Question: *does the world feel like it lives without me?*
- **Creative director's answer so far:**
  - one raid isn't a threat, but several wear the hamlet down;
  - before this round, villagers survived until the final raid (militia always fled at 30 % HP, civilians always ran);
  - hamlets should be able to fall and to heal, with healing waiting for food and inventory;
  - "really barebones but I can see the good structure".
- **This round added:** per-villager resolve, speech and alarms, settlers, and recolonization. Next: playtest whether deaths now spread across raids and whether the hamlet's rise and fall reads well.
- **Playtest suggestion:** watch a raid (Y) from the square. Check that a shout goes up, civilians hide, some militia die fighting and others fall back. Then skip time (T) over a few days to see settlers arrive, or the hamlet fall and either get recolonized or stay a ruin.

## Animation (rewritten 2026-11-05)

- **2026-11-06 re-drop:** the creative director's `New Animations` folder was swapped into `Animations/` and removed, untweaked. All 10 files were byte-identical to the existing clips, so nothing changed in game. Still open: whether different files were meant, and whether the arm twist (1, default 0.5) and jump offset (-0.18) should go.
- **Rig:** `Art/Characters/Human/HumanMale.fbx`, the creative director's rebuilt model, **Humanoid**. It has a `Neck`, a clean T-pose, foot bones pointing at the toes, and its mesh is `BodyUpper`. The old model and the old Generic and retargeted clips are deleted.
- **Clips:** `Art/Characters/Human/Animations/`, 11 Mixamo FBXs downloaded on this skeleton: idle, jog, run, sprint, running_jump, standing_melee, light_dodge_forward, medium_dodge_forward, medium_dodge_backward, heavy_dodge_left_right, death.
  - They use the model's avatar (`CopyFromOther`), not their own: the files hold no T-pose (their rest pose is the first animation frame), so an avatar built from a clip file is garbage. Clips with their own avatar import visibly broken, and `running_jump` fails to auto-map.
- **`EverRealm/Rebuild Human Animations`** (`Editor/HumanAnimationBuilder.cs`). **Run it after adding or replacing a clip.** It sets each clip's import (name, loop, in place), refills `Animations/Human.controller`, and gives the controller, avatar and death length to `Human.prefab`.
  - Adding a clip to the `Sources` table is all a new one needs.
- **Stand-ins, until these are downloaded** (drop them in `Animations/` with these names and rebuild; nothing else changes):
  - `standing_jump`: the running jump plays instead;
  - `medium_dodge_left_right`: the heavy side step plays for side hops. If the real clip steps right, the builder's mirroring is already correct; `CharacterAnimator.hopSide` keys will need tuning.
- **Locomotion:** every gait plays at natural speed, with no multipliers (the creative director removed them on 2026-11-06). Walk (2.5 m/s) is the jog, run (6) is `run.fbx`, sprint (10) is the sprint clip. Measured on the model, the clips travel about 2.2 (jog), 5.1 (run) and 5 (sprint; 4.7 to 5.3 by method) m/s, so run slides about 1.2× and sprint about 2×. The sprint clip is no faster than the run clip. There is no walk clip.
- **Jumps:** the clip's own 1 m rise is left out because the motor provides the height (`keepHeight: false`), with `JumpHeightOffset` lifting the pose back up. Note a negative offset raises it.
- **Twist settings on the model's avatar (in `HumanMale.fbx.meta`, set once):** arm upper 1 / lower 0.5, leg 0.5 / 0.5. These beat the default on these clips; re-check them if the rig changes.
- **Known limits of Humanoid here:** arms can roll around their length during the swing and roll (hands stay exact); the hips wander from the body's centre by up to about 20 cm in the roll and death.
- **`CharacterAnimator`** (on the Human prefab) drives it from gameplay state; presentation only.
  - **Phase-warped actions:** the swing's wind-up / active / recovery map onto clip key points 0.29 / 0.43 / 0.83. Dodge movement and recovery map onto per-clip keys. Combat timings can change without touching animation, and hit-stop freezes poses.
  - **Dodge clips:** roll plays forward and the body turns into it; hop is forward / back / side; power step turns the body side-on (`Combatant.FacingDuringDodge` / `DodgeFacing`) and plays the heavy side clip, which steps left. The other side is the same clip in a state with Mirror on.
  - **Death:** starts 20 % into the clip and holds the last frame.
- **Feet on the ground:** `CombatVisuals` lowers `Visual` by the capsule bottom minus the skin width, computed from the `CharacterController`.
- **Procedural leftovers:** `CombatVisuals` keeps only the stagger lean for animated bodies; `ArmPoser` only blends in the guard pose while blocking; goblins (primitive bodies, no rig) stay fully procedural.
- **Verified on the final build:** compile, 46/46 tests, game starts with a clean console. Idle, jog, jump, swing, block, roll and side hops were seen earlier on the same wiring. **Not yet seen in the live game:** the final jump height, power steps, death, a hit flash on this mesh, and a full raid. The creative director is judging these by playing.

### Open: combat timing and better clips
- The creative director wants a fast, snappy, arcade action-RPG feel (Cube World was the named reference) and hasn't settled timings; M2 timings are unchanged.
- The clips are slow Mixamo motions sped up 2–3× to fit gameplay timings.
- Next: discuss target timings (likely shorter wind-ups and quicker dodges), and look for snappier stylized clips. Suggestions given:
  - Mixamo sword/shield slashes;
  - paid stylized packs on the Asset Store / Fab;
  - simple hand-keyed blocky-character animation, which suits the art style.
- No clips yet for block, stagger, strafing or hit reactions. AI strafing plays forward locomotion, so feet slide.

## What M3 has

### World and hamlet
- **`WorldClock`:**
  - 12-minute day starting 08:00, phases Day 6–17 / Evening 17–21 / Night 21–6;
  - drives the sun and Trilight ambient;
  - T skips 2 h.
- **`SettlementBuilder`:**
  - flattest grass site within 60 m of the centre, levelled pad (14 m radius, 6 m blend), 3–5 huts on a ring with doors facing the square;
  - huts measured in whole blocks: 7 wide, walls 5 tall, doorways 2 × 4 (1.4 × 2.8 m at 0.7 m);
  - `WorldBuilder.Settlement` holds houses, work spots and the square.

### Combat
- **`MeleeTactics`:** shared by goblins and militia, up to 2 attackers per target (`AttackersPerTarget`).
- **`CombatVisuals`:**
  - new `weaponModel` (the hand `WeaponMount` on the Human) is what `SetWeaponVisible` toggles;
  - it was previously aiming at `WeaponPivot`, which holds no renderers on the Human, so civilians' swords were never actually hidden;
  - goblins fall back to `WeaponPivot`, where their club lives.

### Speech
- **`Speech` (Characters):** `Say(text, Volume, SpeechIntent)`.
  - It is a world event: static `Speech.Spoken`, carrying an `Utterance`.
  - Hearing radii: Whisper 3 m / Talk 12 m / Shout 40 m.
  - Listeners check `Utterance.CanBeHeardAt` themselves.
  - Meaning travels in the intent (Chatter / Alarm); nobody parses text.
- **`SpeechBubbles` (Player, on the Player Session object):** the viewer-side presenter.
  - Floating text above the speaker, font size by volume (12 / 18 / 30, shouts bold), about 2.5 s with a fade.
  - Shown only if the local listener (the controlled body, else the camera) was within the hearing radius.
  - Shown only while the camera-to-head line isn't blocked by non-character colliders.
  - F1 does not hide it.

### Villagers
- **`VillagerAI`:**
  - routine by phase;
  - doorway-aware navigation plus a corner detour round huts (no general pathfinding).
- **Resolve:**
  - each villager has `BreakHealthFraction` (0 = fights to the death) and, for civilians, `FightsWhenCornered`;
  - militia that break say "Fall back!" and stay broken;
  - a cornered civilian latches: it fights its foe until the foe dies, gets beyond `cornerReleaseRadius` (8 m, versus the 4 m trigger) or it breaks, so it can't oscillate between fighting and fleeing; it draws its sword while cornered.
- **Alarms:**
  - a villager who sees danger while not already alarmed shouts ("Goblins!" for militia, "Help!" for civilians; Shout + Alarm, 10 s cooldown);
  - allies, identified by explicit `Agent.IsAlliedWith` (same faction, not just "not hostile"), who hear it become alarmed;
  - hearing never triggers another shout;
  - civilians stay hidden at home for 30 s after the last sign of danger (`hideSeconds`; Activity shows "Hiding").

### Population
- **`HamletPopulation`:**
  - 1–2 villagers per hut (seeded), ~40 % militia;
  - resolve tunables: `fightToDeathChance` 0.33, break range 10–40 %, `cornerFightChance` 0.25;
  - 3 beds per house; dead residents free their beds;
  - bodies the player starts in are adopted as militia residents.
- **Provisional (M3 placeholder, also flagged in `CLAUDE.md`):** to be replaced by parties travelling a larger map.
  - **Settlers:** 1–2 every 36 ± 12 h while the hamlet lives and has free beds. They appear 70 m out, in a direction with no hostile within 30 m, and walk in.
  - **Fall:** when the last resident dies, one roll: 50 % a colonization party of 3–5 (at least 1 militia) arrives 2–4 days later, otherwise the hamlet stays a ruin.
  - **Revival:** a fallen or ruined hamlet becomes Living again if anyone moves in, such as a new character via N.

### Raids
- **`RaidDirector`:**
  - first raid 10 h in, then every 30 ± 6 h;
  - party = ceil(1.5 × living militia), min 2, from the camps nearest the hamlet;
  - gathers 36 m from the square, then charges together once all have arrived, after 90 s, or as soon as any member is fighting (party spotted); one goblin gives a war cry ("Raaah!");
  - raid leash is anchored on the target and reaches past the muster point, so a fight there doesn't abort the raid;
  - scheduled raids wait (retry every 4 h) until a full party is free;
  - no raids on an empty hamlet;
  - Y sends whatever is free.
- **`EncounterSpawner`:** 6 camps, ≥ 50 m from the hamlet, regrow one goblin per 12 h up to 4.

### Debug overlay
- Shows time, villagers alive, hamlet state (free beds and settlers due / colonists due / ruin), raiders (gathering), next and last raid.
- F1 hides it.

## Observed in verification (not yet judged by the creative director)

- **8 goblins vs 5 militia** (seed 1203978394):
  - before the leash fix, 3 raiders gave up mid-fight and one militia fought to the death;
  - after it, all 8 charged together, all 8 died, and no humans died (militia on 40–55 HP).
  - Goblins are still much weaker one-on-one (60 HP, slow club) than humans (100 HP, quick sword); raids win by attrition, now with more deaths along the way.
- **Civilians** ran home at the first shout, before goblins reached the hamlet, and stayed hidden.
- **Cornered civilian 1-on-1:** kept fighting at 7 m (inside the 8 m release), killed the goblin, sheathed and went home to hide.
- **Speech bubbles:**
  - a nearby talker shows;
  - a goblin shouting 94 m away doesn't;
  - a villager shouting inside a hut is heard but hidden by the walls.
- **Settlers** take freed beds with no shared beds; colonists walk in from about 65 m.
- Goblins still have no doorway/house awareness.

## Decisions taken without the creative director (flagged, easy to change)

- **Speech lines:** "Goblins!", "Help!", "Fall back!", "Get back!", "Raaah!".
- **Speech tuning:** hearing radii 3 / 12 / 40 m; bubble sizes 12 / 18 / 30.
- **Settler tuning:** every 36 ± 12 h, 1–2 at a time, from 70 m.
- **Colonists:** arrive 2–4 days after a fall, with at least 1 militia.
- **Hiding:** civilians hide for 30 s.
- **Raids and newcomers:** goblin war cry on charge; no raids on an empty hamlet; a ruin revives if someone moves in.
- **Earlier choices still standing:**
  - the player starts as a resident; "Villager N" names;
  - `hamletMaxRelief` 8 m;
  - regrowth 12 h / cap 4; muster 36 m / 90 s.

## Waiting on playtest / unexplored

- Parrying has barely been tested by hand (window is 0.15 s on the Human's Combatant).
- Goblin AI is "good enough for now"; the creative director has nitpicks to raise when a milestone needs better AI.
- **Human model notes** (unchanged since M2):
  - `HumanMale.fbx` scaled ×1.0746 to 1.8 m;
  - `ArmPoser` placeholder arm; the forearm clips the chest at the end of a swing;
  - Blender cm/axis conversion in the FBX;
  - default grey material.

## Known rough edges (left on purpose)

- Sprinting drains stamina while the key is held even if a wall blocks movement.
- The first character taken with N is named "Human 2".
- `OceanRescue` is a temporary placeholder for swimming/water.
- Goblin camps and the G/N/V/R/T/Y/F1 keys are debug tools, not game features.
- Villagers don't react to being hit by the player (no crime/reputation).
- The player can't speak yet (no input for it).

## Tuning the creative director has set by hand

- Light dodge (roll) distance: 5.5 m (on the Human prefab).
- Camera shake: Default Velocity (0.3, -0.6, 0), duration 0.2 s on the Player Session's impulse source.

## Tooling notes

- **Playmode and recompiles:**
  - start Play Mode with `unity command editor.playmode start` (the old `editor_play` name is gone);
  - it can silently fail right after a recompile, so retry until `isPlaying`;
  - don't edit scripts while Play Mode runs: the reload nulls runtime-only state (the world, settlement) and floods the console with NullReferenceExceptions that look like real bugs.
- **Run in background:** `set_run_in_background` writes `runInBackground: 1` into ProjectSettings; turn it back off after verifying.
- **Screenshots:** `capture game_view --save_path` only accepts paths inside the project and saves under `Assets/`; delete the PNGs afterwards.
- **Hung commands:** if CLI commands time out while the Editor is unfocused, check for a modal dialog (the test runner asks to save a dirty scene).
- **Quoting:** for eval code containing quotes, write it to a `.cs` file and use `unity command eval_file --file <path>`.
- **Capturing animation poses:** don't pause and `EditorApplication.Step()`, because Animators don't evaluate there and the capture shows a T-pose. Instead, trigger the action from eval, start a coroutine on the body that calls `LocalTime.Freeze(6)` after a delay, then capture with `capture game_view --source camera --camera <temp camera>`.
