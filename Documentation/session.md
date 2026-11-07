# Session handoff

> Rewritten (not appended) at the end of each session. Only what the next session needs to know about where things stand. History lives in git.

**Last updated:** 2026-11-07 (end of session. Goblin size and movement accepted; swings now aim up and down and land where aimed, which the creative director hasn't played yet)

## Where things stand

- **M4 "The Road":** closed and accepted by the creative director. **Combat is the current work; see below.** The old plan is `C:\Users\Andy\.claude\plans\let-s-work-on-m4-enumerated-dijkstra.md`; its rules are summarised in `CLAUDE.md`.
- **Carried over from M4:** goblins can't path around terrain, and some attacks time out; goblin clumping; raid balance needs a re-check after combat; thin light leaks in shadows; startup about 7 s. The creative director hasn't had a clean look at the cliff arches. They saw the road and the world in play, but the news loop only in tests and unattended runs.
- **How we work now:** build faster, ask for short feel checks, keep verification on our side and sized to catch breakage. Balance tuning waits until the mechanics that affect it exist.
- **Road feel check:** passed. The creative director said the road "definitely feels like its a good length".
- **M3:** accepted as a good starting point.

### Combat: Better Combat style (built, waiting on the creative director)
**End goal:** the creative director wants Hytale-type combat in the end and says we are heading there. They see it as a more polished version of what we already have, so the path is polish rather than a change of direction. Better Combat remains the structure.
The reference changed from Cube World to Minecraft's Better Combat mod after a 0.35 s swing felt "wayyy too fast". We take its structure only. Plan: `C:\Users\Andy\.claude\plans\let-s-work-on-m4-enumerated-dijkstra.md`. **They've said the sword looks held and the swings look good.** No timing is locked in.
- **Moveset:** right slash, left slash, stab, authored by us. `Art/Characters/Human/Animations/SwordMoves.txt` holds the poses as Humanoid muscle values; `Editor/SwordMovesetBuilder.cs` writes `Sword*.anim` from it over a base pose from `standing_melee` (which supplies the grip). Edit the text file and run `EverRealm/Rebuild Human Animations`, or call `SwordMovesetBuilder.Build()` in Play Mode to see changes at once.
- **Any clip can replace these:** `WeaponAttack.animation` names a state on the `UpperBody` layer, and `clipWindupEnd`, `clipSwingEnd` and `clipSettled` say where the hit sits in the clip. Add the state in `HumanAnimationBuilder`.
- **Grip:** the sword used to run in line with the forearm from the wrist, which the creative director said looked like a prosthetic. `WeaponMount` on `HandR` now sits in the palm with the blade out of the thumb side, about 80° from the forearm, edge along it. The rig has no finger bones, so the mount is all there is. In `SwordMoves.txt`, Forearm Twist turns the blade around the arm and Hand In-Out tilts it towards the arm's line.
- **Guard:** `WeaponData.guardAnimation` (`SwordGuard` in `SwordMoves.txt`) holds on the upper-body layer while blocking, so legs keep moving. `ArmPoser` is deleted: nothing needs it now that every body has clips.
- **Goblins are a stand-in:** the creative director will model them later. Until then the Goblin prefab uses the human model at rig scale 0.8 in `GoblinSkin` green (the Human rig is 1.075, so that is 1.36 m against 1.83 m, 74 %; the 0.7 they found too small was really 65 %), with the same avatar, controller and `CharacterAnimator`, so its swings are real animation. The capsule keeps the human's proportions (1.49 m). The club hangs from `WeaponMount` and plays `SwordSlashRight` stretched over its 1.45 s swing; `CombatFeedback.telegraphBody` still glows the body through the wind-up. Rebuilding human animations updates goblins too, since they share the controller; they keep their own copy of `deathClipSeconds`.
- **Aiming up and down:** `Combatant.AimPitch` is body state any brain sets with `SetAimPitch` (capped by `maxAimPitch`, 50°). The player's is camera pitch minus `PlayerController.restingCameraPitch` (-15°, since the camera rests looking down), and `MeleeTactics` aims at the target's height. `CharacterAnimator.LateUpdate` bends the finished pose through the spine and arms (`spinePitchShare`), faded with the upper-body layer, so every swing and guard gets it with no extra clips. It skips culled bodies, because a culled animator doesn't rewrite bones and the bend would accumulate. 
- **Swings land where you aim:** the creative director's call. `CombatRules.SwingReaches` starts at the shoulder (`Combatant.swingOriginHeight`, 0.65 of body height), needs the target's body within the yaw arc and within reach in 3D, and needs the aim to pass through that body to within half of `WeaponAttack.heightArc` (slashes and club 60°, stab 30°). Targets closer than half the reach count as being there, so anything on top of you can be hit. This replaced the old flat height limit. AI uses `Combatant.CouldHit`, and commits its height aim with its turn (`MeleeTactics.AimAtTarget`). Real swings at a held goblin 1.6 m away: level, 25° up and 40° down hit, 45° up missed; with it raised 2.6 m, level missed and 45° up hit. Blocking still ignores pitch.
- **Accepted by the creative director:** goblins at 74 % of human height, and how they move while swinging ("this is good as it is").
- **Goblins move through swings:** `attackMoveMultiplier` went from 0.2 to the human's 0.6 after they felt stuck in place. In `MeleeTactics`, every brain now keeps circling during a swing, steps in to `swingSpacingFactor` (0.55 of reach) through wind-up and strike, and follows its target for the first `windupTrackFraction` (0.6) of the wind-up, then the aim is committed. Facing during a swing now comes only from the body; before, brain and body both asked and script order picked the winner. Against one goblin with reactions off, over three runs: walking round it was hit nearly every swing, running round it 4 in 8 once and nearly every swing twice, and rolling 0.45 s into the wind-up never (0 in 23). Footwork alone rarely beats a swing; the roll does. Militia share this brain, so raids need re-checking.
- **Upper-body layer:** `UpperBody` with `UpperBody.mask` (spine, head, arms, fingers). `CharacterAnimator` keeps locomotion running on the base layer during swings, posing the swing with a separate `SwingPhase` parameter so a dodge can't jerk the arms. `swingBlendIn` 0.05, `swingBlendOut` 0.12.
- **Attacks as data:** `WeaponData.attacks` is an ordered list (timing, arc, `reachBonus`, damage multiplier, clip). `AttackChain` advances only when a swing strikes, so a cancelled wind-up repeats the same attack, and it resets `comboResetSeconds` (1.2) after the last strike. Club has one attack.
- **Sword:** 0.30 / 0.10 / 0.20 s, 20 damage, stab ×1.2 with +0.6 m reach and a 40° arc, slashes 120°, knockback 4, 720 deg/s turn, no stamina cost or stagger. Humans move at 60 % while swinging.
- **Kept from Pass A:** `SetAttackHeld` (a state on the body, so release stops after the current swing), `QueueAttack` and `QueueDodge` (one slot, `inputBuffer` 0.2 s on the prefabs), `SetAim`, dodge cancels of wind-up and recovery, jumping during swings, goblin body glow. Light roll is 0.50 s plus 0.15 s, 7 m (the creative director asked for a little less than 7.5), invulnerable 0.03–0.40 s, which plays its 1.63 s clip at about 2.5×, the same as the other dodges they liked. Medium and heavy dodges are unchanged.
- **Known limits:** legs play the forward run when you move sideways or backwards while swinging (no strafe clips); the sword's red wind-up tint from M2 flashes on every swing, including the player's; militia use the same chain, so raid balance needs the later check.
- **Next:** their verdict on aimed swings first; if they miss too much, widen `heightArc` before anything else. Then goblin group fighting and stacking (the suggested next step), goblin pathing, raid check, ready stance, strafe legs. The lunge special and charge wait for their go-ahead.
- **Unanswered:** whether the red wind-up tint should be limited to goblins rather than flashing on every swing. Their judgement of the rebuilt guard pose too: they never said.
- **Render tool for poses:** set a pose, wait a few frames, capture: the skinned mesh only updates through the normal frame, so a same-frame `Camera.Render` shows a stale body and `BakeMesh` came out empty.

### Terrain and camera pass (after M4, waiting on the creative director's eye)
The creative director asked for world generation that feels more like Cube World's, naming its plateaus and natural arches. They like the biomes, though they hadn't asked for them. We take landforms and structure from it, not its actual look: palette and art stay EverRealm's own.
- **Regions:** `TerrainGenerator` lays a jittered grid of regions about 260 m wide, dealing biomes from the centre out so every island has all five. Borders are warped and blend over 70 m.
- **Biomes** are data in `WorldSettings.biomes`, editable in the Inspector: Meadow, Deepwood, Highlands, Badlands, Amberwood. Each sets its landform (base height, relief, ridged, terrace), colours and growth.
- **Mountains:** the creative director found them too easy to climb once steps were smoothed. They now reach about 140 m (`maxHeight` 146, snow above 104 m), with narrower ridges and 13 m cliff bands broken by gullies. Running straight at one, a wall stops you about 30 m up; the best straight lines get past 90 m, and the summit means finding a way. `gullyShare` and `ridgeScale` tune this.
- **Plateaus:** `TerrainGenerator.Plateau`, two sheer tiers on noise, height set per biome (`plateauHeight`). Highlands have none.
- **3D rock:** the ground is a heightmap, but everything below is stamped on after roads through `RockStamp` (fills air only, grasses flat tops, keeps headroom over roads and buildings). `WorldBuilder.Build` runs formations, then `Cliffs.FindRims`, arches, overhangs, then trees.
- **Overhangs:** rare by the creative director's order: they preferred the world without them everywhere. `OverhangBuilder` pushes the odd stretch of a wall dropping 14 m or more out up to 8 m as a wedge, level with the land behind. `overhangLine`, `overhangMinDrop` and `Biome.overhangs` set how rare; Highlands have none. A column takes only its highest lip: merging lips from two tiers made solid walls of rock.
- **Direction:** the finished world should read as normal land with these features scattered about. Today's density is only acceptable because this island is a test chunk, so don't scale feature counts with world size.
- **Pillars and mesas:** `FormationBuilder`, up to 26 per island (`formationCount`), most in the Badlands (`Biome.formationChance`). Pillars come in clusters of 1–5 as hoodoo, spire, tower or anvil, some with off-centre caps. They stop below the snow line and sit on scree (`RockStamp.Scree`) so they rise from the land.
- **Arches:** `ArchBuilder` grows about 4 per island from cliffs: a fin of rock runs out from a rim and a window is eroded through it, so the land carries on over the bridge. The creative director found standalone arches came out of nowhere and were too frequent.
- **Rock and grass:** a cliff face is bare banded rock; its rim keeps its grass, with rock below. Steep ground shows rock, so what you can't walk up looks it.
- **Hamlets** are built below `hamletMaxHeight` 36 m, or roads can't reach them.
- **Goblins and cliffs:** they march in straight lines and can't climb, so `WorldBuilder.CanMarch` now decides which camps can raid a hamlet, and hamlet camps are only placed with a clear march.
- **Memory:** `WorldData` chunks hold only the layers in use, so the extra height doesn't cost empty sky.
- **Flowers** were vetoed: coloured blocks made no sense. Real flowers can come later.
- **Colour:** `ChunkMesher` now samples a 512 x 512 colour cube by UV, so any block can be any colour with the stock Lit shader. `WorldData.Paint(x, z)` holds each column's ground, foliage and rock colours.
- **Trees:** `TreeBuilder` plants after roads, clear of roads and hamlets. Canopies start above head height. `WorldBuilder.TryGetLandSurface` ignores trees.
- **Haze** widened to 140–420 m (on `WorldClock`, which overrides the scene's fog) so mountains show from further off. The creative director says this is really render distance and will become a player setting, so it's not a design question.
- **Camera:** two causes. `StepSmoother` lets the model and camera target catch up after the controller pops up a block, and the `CinemachineDeoccluder` had zero damping, so block edges snapped the camera in. At 60 fps the biggest single-frame jump went from 0.50 to 0.13 m for the model and 0.27 to 0.07 m for the camera.
- **Road fix:** huts could box in a square so no road could leave it, on the default seed and others. Roads can now squeeze between huts at a cost. The old test passed anyway, because a route "existed" that stopped short, so it now checks routes reach the hamlet.
- **Road junctions:** a later road takes the height of any road within a road's width, and leaves laid road alone. Steeper land had exposed steps of up to six blocks where roads met.
- **Road corners:** a road could cut a hut's corner diagonally, walking travellers into the wall. Diagonals can no longer pass a blocked corner.
- **Left out on purpose:** rivers and lakes (nobody can swim yet, so water would cut the island in two), grass tufts and rocks.

### What M4 built
- **World:** 1 km island, up to 3 seeded named hamlets 380–560 m apart, `RoadBuilder` roads (5 blocks wide, 1-in-3 grades), `RoadNetwork.TryFindRoute`, a plank landing on the coast, far clip 1500 (400 leaves a dark band on the horizon).
- **`Hamlet` (plain data):** beds, state (Living, UnderAttack, Abandoning, Empty) and the abandonment rules. `IPerson` is how rules see a body.
- **`HamletPopulation`:** all hamlets; fills them (at least `minStartingMilitia` 2 each), reviews state, runs the dawn check, sends survivors away. A body the player controls isn't sent and keeps its home.
- **`Journey` (plain data):** a party's course, what it has seen, arrival, fallback and colonist rules.
- **`JourneyDirector`** (own object in the scene): refugees, colonists, newcomers, Stranded, news, the event record.
  - Refugees head for the nearest other hamlet, knowing nothing of its state. Reroutes use the true state.
  - Stranded parties re-check at dawn: a viable hamlet, else the nearest empty one, as colonists.
  - An empty hamlet is due colonists 2–4 days on. At dawn the nearest Living hamlet that can spare them sends 2–5, keeping 3 residents and its last fighter.
  - Newcomers: a boat every 18 ± 6 h, 1–2 people, to a random hamlet with a free bed. With no hamlet alive, 3–5 land as colonists.
- **`RaidDirector`:** a `RaidFront` per hamlet; only camps within `raidReach` 250 m are drawn on. An attack runs from the charge until no raider is alive and within 40 m, with stragglers still marching in counted for the first 2 h, and a 4 h timeout. Survivors are called home when it ends.
- **`VillagerAI`:** travels by road points. Militia still fight; those who can't fight run on down the road; anyone driven off rejoins at the nearest point ahead. Knows nearby hamlets' houses while passing.
- **`Steering`:** after three failed hops, a body sidesteps along the obstacle, further each time.
- **`EncounterSpawner`:** 4 of the 18 camps per hamlet are placed within 180 m of it first.
- **F2 ledger:** who is where (counted from bodies, checked against the record), parties, recent events.

### Decisions taken without the creative director (flagged, easy to change)
- **Raid tuning:** 2 goblins per fighter (was 1.5), every 20 ± 4 h per hamlet (was 30 ± 6). Goblin camp regrowth is unchanged.
- **First raids:** 10, 20 and 30 h in, the starting hamlet last, so trouble starts elsewhere.
- **At least 2 militia per hamlet at the start:** seed 12345 otherwise rolled a hamlet of 8 with none.
- **Civilians on the road run on instead of back:** a lone civilian otherwise bounced for ever off any camp in sight of the road.
- **Lines:** "X has fallen!", "X and Y have fallen!", "We've come to settle X.", "We've walked from X." All shouted, once per hamlet.
- **Newcomers pick a hamlet at random:** they know nothing of the island.
- **Y now raids the hamlet nearest the camera.**

### Verified
- **Tests:** 178/178 at the end of this session, all EditMode (there are no PlayMode tests, so the runner's "PlayMode started" message means nothing). Console clean. The runs below are from M4 and predate the combat rework.
- **Defending table, with the controlled body:** all passed. Last militia dying outside a raid changes nothing; killing the last raiders while present holds; running leaves it Abandoning; returning before dawn cancels; an absent resident doesn't hold it; others leave at dawn while the controlled body keeps its home; coming home revives it; dying leaves it Abandoning.
- **On the road:** a militia fought at road point 20 and walked on; a civilian ran past two goblins unhurt; Hollowmere sent 5 colonists to an empty Marrowdale, who settled and announced it.
- **Final unattended runs (no debug keys, about 9 game days, 10× time, idle player body):**

  | | Seed 12345 | Seed 777 |
  |---|---|---|
  | Attacks | 19 (4 timed out) | 18 (6 timed out) |
  | First hamlet abandoned | Day 4 16:00, about 40 real min | Day 4 08:00, about 36 real min |
  | First news of a fall | Day 5 12:00, about 50 real min | Day 5 13:00, about 50 real min |
  | Journeys arrived / died | 11 / 0 | 11 / 0 |
  | Stranded / stuck | 5 / 1 | 3 / 1 |

  Every journey ended arrived, stranded or dead. The census balanced in all 32 samples, with no errors.
- **Peak cost:** about 4.4 ms main thread with roughly 90 agents alive, in the Editor. GPU numbers from an unfocused Editor aren't trustworthy; memory reserved about 3.5 GB.

### Known issues, left for now
- **The world trends harsh:** by Day 10, seed 777 has two of three hamlets empty and seed 12345 has one empty and one down to a single resident. Goblins sit near the 72 cap (18 camps × 4) from about Day 7.
- **Goblins stack:** a war party clumps and stands on each other's heads, so 11 goblins can die in about 36 s for no human deaths. Not caused by the step-height change (same at 0.45 m). One for the combat touch-up.
- **Attacks that time out** are usually goblins pressing on hut walls after civilians hiding inside: goblins have no door awareness.
- **Rare stuck travellers:** 2 in 18 game days, both just behind huts. Logged with position, retried each dawn.
- **Civilians can flee far in a raid** (186 m seen), since `Flee` just runs from the threat. They walk back afterwards.
- **Empty-hamlet colonists are provisional:** hamlets simply know, even if nobody survived to say so.
- **Hamlet names** are from a hand-written list in `SettlementBuilder`.

### Step 0 measurements (for later milestones)
- **1 km world, no streaming needed:** 3.3–3.6 s to build, about 570 MB more memory (12.6M vertices).
- **Road times at a run, seed 12345:** 93–119 s (579–735 m); walking is about 2.4× longer.

### Creative director's M3 notes, still current
- One raid isn't a threat, but several wear the hamlet down.
- Hamlets should be able to fall and to heal, with healing waiting for food and inventory.
- "Really barebones but I can see the good structure."

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
- **Procedural leftovers:** `CombatVisuals` keeps only the stagger lean for animated bodies; every body now plays clips, and `CharacterAnimator.LateUpdate` bends them towards the aim.
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
  - levelled pad (14 m radius, 6 m blend), 3–5 huts on a ring with doors facing the square;
  - huts measured in whole blocks: 7 wide, walls 5 tall, doorways 2 × 4 (1.4 × 2.8 m at 0.7 m).

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

### Population and raids- 1–2 villagers per hut (seeded), about 40 % militia; resolve: `fightToDeathChance` 0.33, break range 10–40 %, `cornerFightChance` 0.25; 3 beds per house; dead residents free their beds; bodies the player starts in are adopted as militia residents of the first hamlet.- War parties gather 36 m from the square, then charge together once all have arrived, after 90 s, or as soon as any member is fighting (party spotted); one goblin gives a war cry ("Raaah!"). Scheduled raids retry every 4 h until a full party is free. Camps regrow one goblin per 12 h, up to 4.- M4's changes to all of this are under "What M4 built" at the top.
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
- Goblins still have no doorway/house awareness.

## Decisions taken without the creative director (flagged, easy to change)

- **Speech lines:** "Goblins!", "Help!", "Fall back!", "Get back!", "Raaah!".
- **Speech tuning:** hearing radii 3 / 12 / 40 m; bubble sizes 12 / 18 / 30.
- **Hiding:** civilians hide for 30 s.
- **Raids:** goblin war cry on charge; only Living hamlets are raided; an empty hamlet revives if someone moves in.
- **Earlier choices still standing:**
  - the player starts as a resident; "Villager N" names;
  - `hamletMaxRelief` 8 m;
  - regrowth 12 h / cap 4; muster 36 m / 90 s.

## Waiting on playtest / unexplored

- Parrying has barely been tested by hand (window is 0.15 s on the Human's Combatant).
- Goblin AI is "good enough for now"; the creative director has nitpicks to raise when a milestone needs better AI.
- **Human model notes** (unchanged since M2):
  - `HumanMale.fbx` scaled ×1.0746 to 1.8 m;
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

- Light dodge (roll): 7 m over 0.50 s plus 0.15 s recovery (on the Human prefab).
- Camera shake: Default Velocity (0.3, -0.6, 0), duration 0.2 s on the Player Session's impulse source.

## Tooling notes

- **Playmode and recompiles:**
  - start Play Mode with `unity command editor.playmode start` (the old `editor_play` name is gone);
  - it can silently fail right after a recompile, so retry until `isPlaying`;
  - don't edit scripts while Play Mode runs: the reload nulls runtime-only state (the world, settlement) and floods the console with NullReferenceExceptions that look like real bugs.
- **Run in background:** `unity command editor.playmode set_run_in_background --caller plugin --skill unity-cli -- --enable true` after `editor set_auto_tick --enable true`, or an unfocused Editor sits at frame 2. It overrides the project's value without writing `ProjectSettings`; turn it off again afterwards.
- **Screenshots:** `capture game_view --save_path` only accepts paths inside the project and saves under `Assets/`; delete the PNGs afterwards.
- **Hung commands:** if CLI commands time out while the Editor is unfocused, check for a modal dialog (the test runner asks to save a dirty scene).
- **Command names** have changed: `unity command test run -- --mode EditMode`, `unity command script eval_file -- --file <path>`, `unity command console get`, `unity command editor set_auto_tick`, `unity command get_performance`. `unity recompile --project-path <full path>`.
- **Eval code** runs inside a method body, so no `using` lines: write full type names. A local iterator function started on a body (`StartCoroutine`) works for driving a character for a while.
- **Quoting:** for eval code containing quotes, write it to a `.cs` file and use `unity command script eval_file --file <path>`.
- **Capturing animation poses:** don't pause and `EditorApplication.Step()`, because Animators don't evaluate there and the capture shows a T-pose. Instead, trigger the action from eval, start a coroutine on the body that calls `LocalTime.Freeze(6)` after a delay, then capture with `capture game_view --source camera --camera <temp camera>`.
- **After editing scripts from outside:** run `AssetDatabase.Refresh()` from eval and wait for the Editor to be `ready`. `unity recompile` alone answers `up_to_date` and the first test run can still use the old assembly.
- **Eval listeners outlive Play Mode:** a `logMessageReceived` handler added from eval keeps firing in later sessions until the next domain reload, so per-run logs collect later runs.
- **Reading long results:** have eval write to a scratchpad file and `cat` it, rather than unpicking escaped newlines from the CLI output.
