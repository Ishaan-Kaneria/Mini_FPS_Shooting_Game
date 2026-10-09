# Handoff: player and melee

State at commit `989a2ff` (2026-10-09). Covers the first-person player (movement, view model,
health feedback) and melee (punch chain, kick, takedown). Enemy-side behaviour is summarised
only where melee depends on it. All of this work is in **12 local commits that are not
pushed** (see Open decisions).

## What the player does now

**Movement** (`PlayerMotor`, tuned by `MovementSettings`)
- Weighted acceleration and braking; sprint in any direction and from standstill; sprint
  turns are carved (rate-limited), not instant. Sprint is **infinite** (`infiniteSprint`).
- **Combat jog**: firing or aiming while sprinting drops to a faster-than-jog run with the
  gun up (GTA style) instead of cancelling the sprint.
- Light step-synced camera bob, roll and strafe lean (restored after "you removed them all").
- FOV: +6% while sprinting; -5% "focus" while combat-jogging or firing on the move.
- Hurt players move slower (`PlayerCondition` sets `ConditionMultiplier`).
- `Lunge` and `TurnTowards` are used by melee (step-in, and pad/touch-only magnetism).

**Health feedback** (`PlayerCondition`, tuned by `ConditionSettings`). No health bar
anywhere. Blood at the screen edge, desaturation/dim, breathing and heartbeat, low-pass
muffle, slower legs as health drops.

**Weapon** (`Weapon`, `WeaponSway`)
- Tactical vs empty reload (`WeaponData.tacticalReloadShare`); reload time x
  `MovementSettings.reloadTimeScale` (0.6); sprinting no longer cancels a reload.
- Sprint carry pose. **Pressing fire while sprinting snaps the gun up fast** (`_raiseFast`)
  and holds the shot until it is up, so the first rounds no longer leave from the hip pose.
- `Lower(seconds)` is how melee takes the gun out of the hands; `ReportHit` is how melee hits
  reach the hit marker and stats.

**View model** (`ViewModelRig`, `ViewModelSpec`, `LimbIK`; built by `FPSKitViewModels`)
- Survivalist FP arms posed by two-bone IK onto sockets on each `VM_<gun>` prefab; finger
  curls baked through Humanoid muscles; fists for punches.
- A legs-only first-person body (`FP_Body`) that walks under you, is seen when looking down,
  and throws the kick.
- **Arms drawn 1.14x and gun 1.1x** (`ArmsScale`, `ViewScale` consts in `ViewModelRig`): at
  this FOV true-size hands read small. `_headLocal` is measured in holder space so the scale
  is accounted for.

**Melee** (`MeleeStrike`, tuned by `MeleeSettings`)
- **Punch key**: the first press is a **rifle butt** with the gun in hand. Pressing again
  inside `chainWindow` (1.2 s) puts the gun on the back and switches to fists: jab, cross,
  alternating. The fists stay up for `fistIdleSeconds` (1.8 s), or until fire/aim/reload
  is pressed, then the gun is drawn again.
- **Kick key**: front kick. The gun slides aside, the knee chambers high, and the boot drives
  out to chest height. The view dips as the kick starts so the leg is seen. A surviving
  ordinary enemy is **thrown as a ragdoll**, lies, gets up in two stages (kneel, stand),
  then rages back with fists. Elites are shoved less; bosses are only nudged.
- **Takedown**: replaces the strike when the target is below `takedownHealthShare`, reeling
  from something heavier than your own last punch, or down/crawling. Free (no stamina).
- **Damage lands at impact**, not on the key press: `_pendingTarget` is resolved in `Animate`
  at `ImpactAt(strike)` of the swing, and missed if the target has got out of
  `range + 1.2 m`.
- **Step-in lunge** closes the gap to `lungeStopDistance` when a target is found within
  `lungeRange`. Target search reaches `min(lungeRange, range + lungeMaxStep)`.
- **Never ignores a press** (as of `989a2ff`):
  - A press made during recovery is queued for 0.4 s and thrown the moment it can be.
  - Low stamina makes the strike 1.2x slower and 0.7x weaker instead of refusing it.
- Hit-stop (real time) and screen shake per strike. Melee blood is deliberately capped low
  ("kicks must not spray blood"). A kicked body ignores the player's colliders while
  airborne. Overlapping the player was what launched kicked enemies into the sky.

## Key scripts

| File | Role |
|---|---|
| `Runtime/PlayerMotor.cs` | Movement, sprint/combat jog, bob/roll/lean, `Lunge`, `TurnTowards`; adds `PlayerCondition` and `PlayerNoise` in `Awake` |
| `Runtime/MovementSettings.cs` | Movement tunables (Resources asset) |
| `Runtime/PlayerCondition.cs`, `ConditionSettings.cs` | On-screen and audio health feedback |
| `Runtime/PlayerNoise.cs`, `NoiseSystem.cs` | Footsteps and shots heard by enemies |
| `Runtime/Weapon.cs`, `WeaponSway.cs` | Gun, reloads, sprint pose, fast raise, FOV, `Lower`, `HandsOffset`/`HandsRotation` |
| `Runtime/MeleeStrike.cs` | Melee chain, kick, takedown, queue, stamina, lunge, impact timing, butt animation |
| `Runtime/MeleeSettings.cs` | Melee tunables (Resources asset) |
| `Runtime/ViewModelRig.cs` | Poses gun, arms, fists and legs every frame (`Pose(State)`); also used by the preview |
| `Runtime/ViewModelSpec.cs` | Per-gun sockets, hip/aim pose, arms offset (on each `VM_<gun>` prefab) |
| `Runtime/LimbIK.cs` | Two-bone IK and hand frames |
| `Runtime/RagdollController.cs` | Ragdoll on/off; self-collision off, capped depenetration, corpse frozen once still |
| `Runtime/EnemyBody.cs` | Enemy side of melee: `Launch` (kick throw), get-up, rage, hand-to-hand, pain poses |
| `Editor/FPSKitViewModels.cs` | **FPSKit > View Models > Build** (FP_Arms, FP_Body, VM_<gun>, stamps `WeaponData.viewModel`) and **Preview** (edit-mode renders; bakes skinned meshes unscaled) |
| `Editor/FPSKitMeleeShots.cs` | Play-mode walkthroughs: `Run` (melee), `RunEnemy`, `RunGore`, `RunBrain`, `RunPain` |
| `Editor/FPSKitRuntimeSettings.cs` | Ensures the Resources settings assets exist |
| `Editor/FPSKitCombatTest.cs`, `FPSKitControlsTest.cs` | Batch checks, updated for the chain and impact-time damage |

## ScriptableObjects and assets

- `Assets/FPSKit_Generated/Resources/MeleeSettings.asset`, `MovementSettings.asset` and
  `ConditionSettings.asset` are loaded through `.Active`, which falls back to code defaults if
  an asset is missing.
- **The Melee and Movement assets currently equal the code defaults.** They were reset with
  `EditorUtility.CopySerialized(ScriptableObject.CreateInstance(type), asset)` after the
  defaults changed. A changed default does nothing until the asset is reset or edited.
- `Controls.asset` (`ControlSettings`) holds keyboard bindings, including the melee keys.
- `Assets/FPSKit_Generated/ViewModels/VM_<gun>.prefab`, `FP_Arms`, `FP_Body` are generated.
  Edit `FPSKitViewModels` and rebuild, never the prefabs.
- The rifle-butt pose fields (`buttCockTurn`, `buttCockOffset`, `buttStrikePoint`,
  `buttStrikeAim`, `buttLunge`) and `holsterOffset/Turn`, `kickAsideOffset/Turn` are
  **serialized on the `MeleeStrike` component**, not in `MeleeSettings`. A scene or prefab
  value overrides the code default.

## Current tuning

**MeleeSettings**

| Group | Values |
|---|---|
| Reach | punchRange 2.0, kickRange 2.5, coneDegrees 45 |
| Lunge | lungeRange 3.6, lungeStopDistance 0.75, lungeMaxStep 2.2, lungeSeconds 0.11; magnetism 0.5 (pad/touch only) |
| Butt | buttSeconds 0.32, buttRecovery 0.12 |
| Chain | chainWindow 1.2, holsterSeconds 0.16, drawSeconds 0.34, fistIdleSeconds 1.8 |
| Jab / cross | jab 0.22 s + 0.12 recovery; cross 0.26 s + 0.15 recovery; stagger 0.35 / 0.6 |
| Kick | kickSeconds 0.42, kickRecovery 0.3, kickLaunchSpeed 7.5 (launch direction = forward + 0.25 up), kickKnockback 3.5, kickStagger 1.5, eliteKnockbackShare 0.5 |
| Takedown | takedownHealthShare 0.35, when staggered/crawling on, 0.4 s + 0.35 recovery |
| Stamina | max 100; costs jab 16, cross 20, kick 34, takedown 0; regen 32/s after 0.7 s |
| Hit feel | hit-stop punch 0.045 / kick 0.08 / takedown 0.12 s; shake 0.3 / 0.5 / 0.6 |

Damage is a share of the target's pool by role (standard / elite / boss):

| Strike | Standard | Elite | Boss |
|---|---|---|---|
| butt | 0.45 | 0.18 | 0.03 |
| jab | 0.6 | 0.2 | 0.03 |
| cross | 0.75 | 0.3 | 0.05 |
| kick | 0.35 | 0.15 | 0.03 |
| takedown | 1.2 | 1.2 | 0.12 |

Impact beats (`MeleeStrike.ImpactAt`, share of the swing): jab 0.30, cross 0.32, kick 0.36,
takedown 0.34, butt 0.32.

**MovementSettings**

| Group | Values |
|---|---|
| Speeds | jog 6.0, sprint 9.5, combat jog 6.4, crouch 3.0 |
| Speed multipliers | backpedal 0.8, strafe 0.92, aiming 0.65 |
| Acceleration | acceleration 34, sprint acceleration 18, stopping 26, sprint turn rate 540°/s |
| Sprint | infiniteSprint true (the 7 s / 6 s / 0.3 stamina values only apply if it is turned off) |
| Camera | bobDip 0.017, bobSway 0.011, sprintBob 1.5, stepRoll 0.3, strafeLean 1.0, step length 0.78 / sprint 1.15 |
| Gun | reloadTimeScale 0.6, gunBob 0.55 |

Enemies must never outrun a sprint (CLAUDE.md). The sprint is now 9.5; the enemy speed cap
of 1.35x has not been re-checked against it.

**View model**

| Item | Values |
|---|---|
| Hip pose | Default `Gun.hip` (0.16, -0.095, 0.30); Uzi (0.15, -0.085, 0.26). `ViewModelSpec.hipTurn` (3, -3, -7) |
| Fists | guard L (-0.14, -0.15, 0.27), R (0.13, -0.17, 0.24); jab reach (-0.05, -0.06, 0.55); cross reach (0.02, -0.05, 0.57) |
| Kick foot | chambered at 1.05 m high / 0.28 m ahead; struck at (0.04, 1.50, 0.56); body lean 16°; toe-up 85° |
| View on strikes | kick dips the view -9° from the start; punches -1.2° at the blow |

The hip pose's z must stay about 0.28 or more. Closer than that puts the right-hand grip
socket behind the eye and the gun hand disappears.

## Input bindings

| Action | Keyboard / mouse | Gamepad | Touch |
|---|---|---|---|
| Punch (butt, then fists) | **V**, alt **Mouse3** (back side button) | **R3** (right stick press) | PUNCH button |
| Kick | **B**, alt **Mouse4** (forward side button) | **Y / Triangle** (button north) | KICK button (`FPSKitMobileControls`, cluster col 0 row 2) |
| Back to the gun | fire / aim / reload, or wait 1.8 s | same | same |

- Keyboard keys come from `ControlSettings` (`Controls.asset`); pad bindings are in
  `GameInput.AddPadBindings`, matched by `InputPrompts.PadGlyph` (`ps_r3`/`xbox_rs`, north
  face).
- Touch buttons queue through `MobileInput.QueueMelee/QueueKick`. They glow amber when
  `MobileInput.MeleeReady` (someone in reach). Taps only, nothing to hold, so they are fine
  for touchpad play.
- Magnetism (turning onto the target) is pad and touch only, never mouse, per the aim-assist
  rule.

## Tested vs untested

**Seen working in play mode** (walkthroughs or direct MCP checks, earlier commits):
- the butt → jab/cross chain
- kick throw → lie → get up → rage charge (before the kneel stage was added)
- gore (caved head, decapitation, severed leg)
- enemy bounding and firing, and the near-miss → break-for-cover reaction
- leg/arm/gut pain poses
- corpses settling and freezing (chest kill and severed-leg kill, `c552621`)
- movement speeds (driven through `MobileInput`)

**Edit-mode preview only** (`FPSKit > View Models > Preview`): the new hip pose, bigger
arms/gun, closer fists and the higher kick (`989a2ff`).

**Compiled but never run in play**:
- the press queue and the no-refusal stamina rule
- the kicked body ignoring the player's colliders, the flatter launch and the kick view-dip
- the two-stage kneel get-up
- the FOV focus and fast raise when firing from a sprint
- enemy-side changes: hand-to-hand inside 3.5 m, dodging, running from a bomb, suppressive
  fire, plan weighing

**Batch checks not run since the melee rework**: VerifyCombat, VerifyControls, VerifyTouch,
VerifyHudLayout, VerifyStatics. They exit the editor, so they were skipped while working over
MCP. Run them through the `verify` skill before pushing. HudLayout may still look for the
removed `hud.player` element.

## Known issues

1. **Rifle-butt strike looks fake** (his screenshot): the rifle is pitched over so the
   flat stock fills the view. Proposed rework: a short forward thrust with the stock low and
   right of centre, the rifle rolled about 45° and the muzzle swung left. Change it through
   the `butt*` fields and `AnimateButt`/`BlowPose`, not the gun pose.
2. **Butt damage and the drawn blow are out of step**: `ImpactAt(Butt)` = 0.32 but
   `AnimateButt` draws the blow at `blowAt` = 0.38, so damage lands about 20 ms before the
   stock arrives. Fix alongside item 1 by making one read the other.
3. **The gun hold is still not convincing** to him after several preview passes. A reference
   screenshot was asked for and has not come yet. In the preview the Uzi's support hand is
   splayed.
4. **Arms 1.14x and gun 1.1x are unchecked in play**: the hands may clip the grips at the new
   scale, and the gun may poke through walls (near plane 0.02 in the preview).
5. **The editor runs at about 2 fps when unfocused**, so a play-mode walkthrough costs 10+
   minutes and some timed out. He asked for minimal play-mode watching: prefer the edit-mode
   preview and one short targeted check.
6. **Frozen corpses don't react** to a later bomb (bones go kinematic once still).
7. **A hunting Marksman can wander far** when the position it heard is unreachable.
8. **Uncommitted files that are not from this work**: `Assets/FPSKit_Generated/Soldier/Rig/*.mat`
   (rewritten by the editor) and `Assets/Fairground/Settings/FG_Night.asset`. Left alone on
   purpose.

## Open decisions (his to make)

1. **Push via Git LFS?** The Survivalist and Low Poly Weapons packs (and the EEJANAI clips the
   enemy Action layer uses) are untracked. The enemy and view-model prefabs need them, so the
   12 local commits cannot be pushed without adding them, most likely via LFS.
2. **Rework the rifle butt** as in Known issue 1, or drop the butt and go straight to fists?
3. **Gun hold reference**: a screenshot of a game whose hold he likes.
4. **Tired strikes**: currently slower and weaker, never refused. Confirm he prefers that to a
   visible "winded" refusal.

## Rules learned this session

- Kicks: little blood ("it will feel unreal").
- No health bars anywhere; player health shown through the screen and sound.
- Separate keys for each melee action; any hold-to-X must also accept a tap (touchpad).
- After changing a settings default, reset the Resources asset or nothing changes.
- When tuning the view model, judge with Preview. Its skinned-mesh bake uses
  `BakeMesh(mesh, false)` with an unscaled copy. Baking with scale (or applying `lossyScale`)
  draws the scaled arms in the wrong place.
