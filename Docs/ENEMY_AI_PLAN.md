# Enemy AI rework: Stage 0 (inspection and plan)

Written 2026-10-09 from the code at `8308709`. Nothing has been changed yet. Stages 1-6 follow
the brief ("commando enemies"); each one ends playable and committed.

## 1. What the enemy is today

**The prefab** (`FPSKit_Generated/Enemy.prefab`, built by `Editor/FPSKitRiggedSoldier.cs`
over the Survivalist Humanoid mesh) carries: `Health`, `EnemyAI`, `EnemyBrain`, `EnemyWounds`,
`EnemyBody`, `RagdollController`, `EnemyBrutality`, `EnemyGore`, `EnemyVoice`, 13 `Hitbox`es.
`EnemyLimbAnimator` and `EnemyDeath` belong to the old built soldier and are not on it.

| Layer | File | What it does |
|---|---|---|
| Mind | `EnemyBrain` (1152 lines) | Calm / Suspicious / Searching / Alert / Combat. Vision cone with light, hearing via `NoiseSystem`, near misses, a weighed tactic table (`Think`: Bound/Assault/Flank/Hold/Retreat). Cover = 8 random navmesh samples hidden by one linecast (`FindCover`). |
| Squad | `EnemySquad` (static, 195 lines) | One global "squad": attack turns, shouts, bullet-passed and bomb-incoming broadcasts. No leader, roles or plans. |
| Body driver | `EnemyAI` (1622 lines) | NavMeshAgent moves, `FaceTowards` (Slerp), firing, stagger, shove/knock, leash. Writes one Animator float: `Speed` = agent velocity. |
| Pose | `EnemyBody` (1066 lines) | Everything else is **procedural bone turns in `LateUpdate`**: rifle aim, two-hand grip (`LimbIK`), hit kicks, pain poses (kneel, clutch, doubled over), limp, crawl, the kick throw and get-up, the rage. |
| Wounds | `EnemyWounds` | Damage per limb as a share of max health; `Legs` = Sound / Limping / Crawling (only ever rises); disarm; shaky aim. |
| Damage | `Hitbox` -> `Health.ApplyDamage` | Multiplier per part from `Soldier/BodyDamage.asset`; headshot raised to lethal. |
| Death | `RagdollController`, `EnemyBrutality` | Ragdoll on death with a classified push; cave head, break limbs, sever (mesh cut by bone weights) **on death only**. |

**The Animator** (`Soldier.controller`, rebuilt by `FPSKitRiggedSoldier.LocomotionController()`):
one 1D blend tree on `Speed` with three **unarmed** Starter Assets clips (Idle, Walk_N,
Run_N), root motion off, plus an Override "Action" layer with EEJANAI fighter clips (rage,
strikes). No strafe, backwards, turn, start/stop, crouch, injured, crawl or get-up clip
exists. No Animation Rigging package is installed.

## 2. Why the bugs happen

**Bug 1, limb shots kill.** A limb hit goes through `Hitbox.Receive` into the one health pool
at 0.6-0.8 of the weapon's damage. Nothing separates limb damage from core health. A Grunt has
100 HP; the Service Rifle does 24, so a thigh hit is 19.2 and **six leg shots kill it**.
`EnemyWounds` only reads the damage after it has already been taken from `Health`.

**Bug 2, the knockdown and get-up are one fixed routine.** `EnemyBody.Update` gets up after
the pelvis has been still for a fixed `lieSeconds` (1.5 s), so time on the ground barely
varies. `GetUp` teleports the root under the pelvis and **snaps it to face the player**, then
`BlendFromFallen` slerps every bone from the ragdoll snapshot into **one hand-built kneel
(always the right knee, a hand down)** and then to standing, over a fixed 1.6 s. It never asks
whether the body is face up or face down, so a body on its back is rotated through the floor
into the same kneel. Every get-up then ends in the same "Rage" clip.

**Bug 3, a crawler stands up.** Two causes, both visible in the code (I will reproduce them
on the test dummy in Stage 1):
- The **leg-pain pose** (`EnemyBody.Hurt`/`PainPose`, `Pain.Leg`) drops the body onto one
  knee for 1.1-2.1 s and then lets it back up. Below the crawl threshold that is the whole
  story: it goes down, then stands as if nothing happened. It is pain, not a wound state.
- **Crawling is not a state the body is in.** `Crawl()` tilts three bones over the standing
  walk clip each frame, with no blend in or out. The agent, the collider and the Animator
  stay standing. Anything else that owns the pose for a moment draws a standing body over it:
  a kick on a crawler (the kick key still calls `Knock` -> `Launch` -> `GetUp`, which ends in
  `StartRage`, a standing charge), the get-up blend, the Override action layer.

**Bug 4, robotic movement.** `Speed` is the only animator input, so strafing or backing off
plays the forward walk (feet slide sideways). The agent turns at its `angularSpeed` and
`FaceTowards` slerps the whole root at once; nothing leads with the head. The agent reaches
full speed in a few frames (no start or stop clip). Every enemy idles in phase.

## 3. Architecture

**Recommendation: a squad utility planner over a per-soldier hierarchical state machine with
utility-scored combat actions, and a separate "motor" layer for the body.** Not Unity's
Behavior package, not a pure behaviour tree.

```
SquadBrain (one per squad, 2-4 Hz)        blackboard: player estimate + confidence, wounded,
  scores plans -> roles + orders           reloading, plan, cover reservations, attack tokens
        |
EnemyBrain (per soldier, 2-10 Hz by LOD)  modes: Relaxed > Suspicious > Investigate > Combat
  interrupts first (pain, knockdown,       > Search > Fall back. Inside Combat a utility pick
  suppression, grenade), then mode         over actions: cover, peek-shoot, suppress, reload,
        |                                  advance, flank, throw, help ally, melee
EnemyMotor (every frame)                   intents in (go to, pace, stance, face, aim at),
  Animator params with damping, root       out: animator params, root motion synced to the
  motion, turn-in-place, IK rig            agent, look/aim/hand/foot IK
        |
EnemyBody (LateUpdate overlays)            pain flinch, partial-ragdoll hits, clutch, limp lean
```

Why:
- Commando behaviour is mostly **interruptions and re-evaluation** (a near miss, a man down,
  the player reloading). A utility layer re-scores cheaply every think; a behaviour tree
  either re-ticks from the root every frame or needs a decorator for every interrupt.
- The existing `EnemyBrain.Think` weight table is already a utility scorer, and you approved
  that style ("weighed plans, real-life critical thinking"). It moves up to the squad, where
  plans belong, instead of being thrown away.
- Unity's **Behavior package** (`com.unity.behavior`) stores logic in graph assets: a new
  dependency, harder to diff and review, a per-agent graph runtime cost, and it cuts against
  this project's rule that content is built from C# and data.
- The motor layer means the mind never touches bones, so Stages 2 and 4-5 can be built and
  tuned separately.

What happens to the current files: `EnemyBrain` is refactored, not restarted (its perception
and noise code is kept). `EnemySquad` becomes per-squad `SquadBrain` instances (the static
bits that remain get `SubsystemRegistration` reset hooks). `EnemyAI` keeps weapon, attack,
health, leash and the public API levels use. `EnemyBody` is split: locomotion and IK go to
`EnemyMotor`, the overlays stay.

## 4. Damage model (Stage 1)

- New `EnemyBodyProfile` ScriptableObject per enemy type (extends `BodyDamageProfile`, which
  stays for the multipliers): health per part (head, neck, upper/lower torso, L/R upper arm,
  forearm, thigh, lower leg), limb thresholds (Wounded / Crippled / Severed), core-chip share,
  core floor (default 15%), pain per part, bleed rates.
- `Hitbox.Receive` books a limb hit to the **part pool** first, then chips core health by
  `coreShare x damage`, **clamped so core health never drops below the floor**. Head, neck
  and torso hit core in full. Blasts and melee are not clamped. Headshot rule unchanged.
- Limb states are permanent (`Healthy -> Wounded -> Crippled -> Severed`, never back down).
  One crippled leg = limp for life; two crippled legs or one severed leg = crawl for life,
  and nothing (kick, get-up, rage) can stand it back up.
- A pain value per enemy (added per hit, fades) drives flinches, groans, stumbles and aim.
- Bosses keep the CLAUDE.md rule (never crawl, disarm or die to one headshot).

## 5. Dismemberment technique (Stage 3)

**Recommendation: keep and extend the method `EnemyBrutality.Sever` already uses on corpses**,
which is both of the techniques in the brief combined:
1. On the body: the severed bone chain is collapsed (scale 0.001 at the knee/hip/elbow/
   shoulder bone), its colliders and joints go, and a **stump cap** mesh with a wound
   material is parented to the parent bone. The Animator writes rotations, never scale, so a
   collapsed bone stays collapsed while it keeps animating.
2. The limb: the skinned meshes are cut by bone weights in the pose they have at that moment
   (every garment, so the boot goes with the shin) into a separate rigidbody with its own cap.
   This works on the Survivalist model with no Blender work because its meshes are Read/Write.
3. Blood: stump spurts (pooled particle bursts for a few seconds), pool, decals.

What changes: it works on a **living** enemy (then crawl or one-armed behaviour), pieces are
**pooled**, fade after a while and are capped in number, it is gated on the Blood setting
(off = heavy hits only cripple), and only `Heavy` damage can do it.

The alternative, pre-split limb meshes made in Blender (clean seams), needs every garment in
three skins re-authored. Not worth it while the cap hides the cut edge.

**Heavy damage class.** Nothing has one today. Proposed `WeaponData.damageClass`
(Standard / Heavy) plus `heavyRange` (Heavy only inside it):

| Weapon | Damage | Proposed |
|---|---|---|
| Breacher (shotgun, 9 x 13) | 117 per shot | Heavy within 8 m |
| Marksman DMR | 62 | Heavy |
| Suppressor LMG | 21 | Standard (per-round damage is rifle-class) |
| Service Rifle, Tactical Burst, Vector SMG | 15-26 | Standard |
| Bombs (Explosion) | blast | Heavy always |
| Melee | | never severs (kicks stay low-blood) |

## 6. Animations

**What exists.** Starter Assets (Humanoid, unarmed): Idle, Walk_N, Run_N, Run_S, jumps.
EEJANAI fighter clips: charge fist, elbow chop, knee strike, back fist, kicks (used by the
rage). LowPolySoldiers demo: combat idle/run/shoot, but imported as Generic, three clips, a
different skeleton; not worth converting. Everything else is procedural today (aim, grip,
reactions, pain, limp, crawl, get-up). The Survivalist body is Humanoid, so Mixamo clips
retarget onto it.

**Legend:** Have = usable now. Proc = done procedurally today (replaced or kept as overlay).
Need = download.

| Group | Clip(s) | Status | Mixamo search |
|---|---|---|---|
| Rifle locomotion, 8 directions | idle aiming, walk F/B/L/R/FL/FR/BL/BR, run 8-dir, sprint F | Need | Download the pack **"Pro Rifle Pack"**, then the pack **"Rifle 8-Way Locomotion Pack"** |
| Crouch, 8 directions | crouch idle, crouch walk 8-dir | Need | in Pro Rifle Pack; else "rifle crouch walk" |
| Starts / stops | walk start, run start, walk stop, run stop (rifle) | Need | "rifle walk start", "rifle run to stop", "stop walking with rifle" |
| Turn in place | turn 90 L/R, turn 180 | Need | "rifle turn" (Pro Rifle Pack has 90 L/R), "rifle turn 180" |
| Idle life | breathing idle, look around, weapon check, weight shift | Need | "rifle idle", "rifle look around", "weapon inspect", "idle shifting weight" |
| Reload | standing, crouching | Need | "reloading" (Pro Rifle Pack), "crouch reload" |
| Limp | injured walk, injured walk back, injured idle | Need (Proc today) | "injured walk", "injured walk backwards", "injured idle", "injured hurting idle". Download one side; the other is made by Mirror on import |
| Stumble | stumble while walking, stumble back | Need | "stumble", "injured stumble idle", "stumble backwards" |
| Crawl | belly crawl forward, crawl idle, crawl turn | Need (Proc today) | "crawl", "prone", "injured crawl" (pick a belly crawl, not hands-and-knees) |
| Firing from the ground | prone / lying aim | Need | "prone", "lying aim"; if none, I keep the procedural aim on the crawl pose |
| Get-ups | face up (from back), face down (from belly), slow injured version of each | Need (Proc today) | "getting up", "stand up", "standing up from back", "get up from stomach", "injured stand up" |
| Hit reactions (upper body) | chest, gut, left/right shoulder, head | Need (Proc today) | "hit reaction", "stomach hit", "shoulder hit", "head hit", "rifle hit reaction" |
| Deaths | 3-5 short falls into ragdoll | Optional | "rifle death", "death from front", "death from back", "falling back death" |
| Cover | crouch-to-stand, stand-to-crouch, peek | Need | "crouch to stand", "stand to crouch", "peek" (Mixamo's cover set is thin; lean and blind fire will be spine IK on crouch clips) |
| Hand signals | point, halt, move up, flank | Need | "pointing forward", "stop gesture", "hand raising", "come here gesture" / "waving" |
| One-handed pistol | idle, walk, run, aim, kneel | Need | "pistol idle", "pistol walk", "pistol run", "pistol kneeling idle", "pistol strafe". Model: `M1911` from Low Poly Weapons |
| Throwing | grenade throw standing / crouched | Need | "toss grenade", "throw grenade" (Pro Rifle Pack) |
| Melee | strikes | Have | EEJANAI clips |
| Arm held to body | crippled arm carry | Proc | done with IK (hand to the wound), no clip |

About 60-80 clips. Download size roughly 20-40 MB.

**Download steps (Mixamo)**
1. Sign in at mixamo.com (free Adobe account). Leave the default character (Y Bot); there is
   no need to upload ours, retargeting is done by Unity's Humanoid avatar.
2. For each clip, leave the sliders at default. **Leave "In Place" unticked** for everything
   (root motion is wanted for locomotion, starts, stops, turns and get-ups).
3. Download settings, every time:
   - Format: **FBX for Unity (.fbx)**
   - Skin: **Without Skin**
   - Frames per Second: **30**
   - Keyframe Reduction: **none**
   Packs ("Pro Rifle Pack", "Rifle 8-Way Locomotion Pack") download as a zip with the same dialog.
4. Keep Mixamo's file names. Put them under `Assets/Animations/Mixamo/`, one folder per group:
   `Locomotion/`, `Crouch/`, `Transitions/` (starts, stops, turns), `Idle/`, `Injured/`
   (limp, stumble), `Crawl/`, `GetUp/`, `Hits/`, `Deaths/`, `Cover/`, `Signals/`, `Pistol/`,
   `Actions/` (reload, throw).
5. Don't touch the import settings. Stage 2 adds an import step that sets Humanoid, loop flags,
   root-motion baking and mirrored copies by folder and name.

Mixamo's terms allow the clips in a shipped game. The repo would carry the raw files, so they
should go through Git LFS with the other packs (open decision below).

## 7. Animation and IK plan (Stage 2)

- **Animator** rebuilt by the soldier builder: base layer = 2D freeform directional blend
  trees (walk, jog, sprint, crouch) with rifle up, driven by `MoveX/MoveZ` in the body's
  space; start/stop/turn states; an upper-body layer (avatar mask) for hit reactions, reload,
  signals, throw; an injured layer (limp left/right, crawl) blended by weight, never switched.
  Every parameter is damped (`SetFloat(..., dampTime)`).
- **Root motion synced to the agent**: `agent.updatePosition = false`, `OnAnimatorMove`
  takes the root motion and pulls it towards `agent.nextPosition`. The agent's speed stays
  the cap, so the "nothing outruns a sprint" rule still holds.
- **Turning**: a turn budget per second; the head and eyes lead, then the chest (look/aim
  weights), then the body (turn-in-place clip above ~60°).
- **Animation Rigging package** (`com.unity.animation.rigging`, needs adding): Multi-Aim on
  head and chest, Two-Bone IK for the support hand on the rifle grip. **Foot IK** through
  the Humanoid IK pass (`OnAnimatorIK` with ground raycasts), which is cheaper than a rig per
  foot and only runs near the player.
- **Variation**: per-soldier seed for idle choice, playback speed (0.92-1.08), timing,
  reaction delay 0.2-0.6 s scaled by difficulty and alertness.
- **Hit reactions**: legs stay animated; the spine, arms and head get a physics-driven
  impulse (a spring per bone driven by the hit), so no two flinches match.
- **Knockdown**: full ragdoll with force along the blow; time on the ground 1.5-4 s, longer
  in pain; then pick face-up or face-down get-up, align the root to the ragdoll's pelvis
  heading, and blend from the ragdoll pose into the clip's first frame over 0.3-0.6 s.

## 8. Perception, cover, squads, debug, performance (Stages 4-6)

- Perception extends what `EnemyBrain` has: peripheral zone, light and muzzle flash
  visibility, sound table (shots, sprint, walk, crouch, melee, reload), near-miss tracking
  already exists (`Weapon.FireRay -> EnemySquad.BulletPassed`), memory with fading confidence.
- **Cover bake**: an editor tool bakes cover points for the AbandonedFairground into a data
  asset (not into the binary scene, which the builder rebuilds), and the scene builder reruns
  it after a rebuild. Low/high cover with the protected direction. Runtime scoring and
  reservations; peek, lean, duck, reload behind cover, blind fire.
- Squads of 3-5 with leader and roles, the six plans, attack tokens (2-3 shooters at once),
  bursts with accuracy that grows while tracking, morale. Callouts with cooldowns through the
  existing `EnemyVoice`/`VoiceBank` (placeholder hooks plus a recording list), hand signals.
- Debug overlay (toggle key), gizmos, and a generated AI test scene with the four scenarios.
- AI level of detail (think rate and animation culling by distance and awareness), work spread
  over frames, pools and caps for ragdolls, limbs, decals and particles. **Baseline fps with
  20 enemies is measured at the start of Stage 1, before any change.**

## 9. Risks

- **20 enemies at 60 fps on this laptop** (integrated Intel Arc, throttles at 100 C) is the
  hardest target here. Animation Rigging and Humanoid IK on 20 bodies cost real CPU. The LOD
  layer (Stage 6) is what makes it possible; I will report honest numbers, not the target.
- The play-mode checks have to stay short (the editor runs at ~2 fps unfocused).
- The 13 local commits still cannot be pushed until the packs go through LFS. Mixamo clips
  add to that.

## 10. Progress

**Stage 1 (bugs 1-3, sections 1-2), 2026-10-09.**

- Baseline, before any change (FPSKit > Enemy AI > Measure 20 Enemies, AbandonedFairground,
  Medium, editor play mode): player loop 15.8 ms mean / 13.5 median / 25.2 p95 per frame;
  physics 4.2 ms; scripts (Update + LateUpdate) 3.2 ms; animator < 0.3 ms. The editor ran at
  about 1 fps unfocused, so only CPU times mean anything. `Build/Perf/enemies_baseline.txt`.
- Built: limb-capped core damage (`DamageInfo.coreCapped`, `EnemyWounds.CoreCapFor`); limb
  states and pain (`EnemyWounds`); per-type `BodyDamageProfile` (21 in
  `FPSKit_Generated/Soldier/BodyProfiles`); the "Injured" animator layer (limp L/R,
  crawl face down/up, six get-ups, stumble); `EnemyMotor` + `EnemyRootMotion` (root motion with
  the agent following); clip get-ups matched to how the body landed; crawl-to-cover, cover on a
  new limp, flee when both arms go; groans in high pain; the AI test scene and the limb test.
- Not in Stage 1, moved on: one-handed **pistol** for a crippled gun arm (needs the Stage 2
  animator; for now the rifle is held low in the good hand); shooting from the ground after a
  knockdown and the face-up crawl idle (it reads as a corpse when still); partial-ragdoll hit
  reactions (Stage 2).
- Limb test (FPSKit > Enemy AI > Run Limb Test, final run 21:38): **all pass**. 20 thigh rounds
  leave a Grunt at exactly 15%, left leg crippled, limping on the left, firing; both legs
  crippled crawl and stay down after a kick (pelvis never above 0.12 m); one crippled arm keeps
  the rifle (spread x6.1, bursts x1.8 apart), two drop it; torso and head still kill. Three
  kicks: lay 3.4 / 2.2 / 2.6 s, rose with Stomach_Zombie / Stomach / Back to match how each
  landed; worst sudden change during a get-up 4.4 cm/frame (pelvis) and 19.4 deg/frame (a bone),
  under the clips' own sharpest (6.9 cm, 23.5 deg). The limp moved at 1.64 m/s (walk and
  hobbling run mixed). Report and pictures: `Build/LimbTest`.
- Fixed on the way, found by the test: a floating rifle during get-ups (it now rides in the
  hand and eases up over 0.6 s once standing); elbows flipping when the hands reached for the
  rifle while still bent over (no arm IK until standing; IK is now blended per bone, not by
  target); a pop when the rifle was slung for the rage (hands let go first); a velocity jump
  when the agent took back over from the clip (its velocity is zeroed).
- `VerifyCombat` now exits only in batch mode (`FPSKitBatch.Exit`). Run in the open editor it
  got 14 frames in its 14 s (the unfocused editor runs at ~1 fps) and reported a meaningless
  failure; it also still expects a jab as the first punch (the rework made it a rifle butt).
  It needs a batch run with Unity closed before the next push. `VerifyWounds` is stale (see
  DESIGN_NOTES).
- Still to download before Stage 5: Mixamo "Reload" x3 (run, crouch, walk variants) and
  "Injured Stumble Idle" (Firefox dropped them).

**Fixes before Stage 2, and Stage 2 (sections 4-5 and the Stage 1 leftovers), 2026-10-09/10.**

- Fix 2, his correction "everything after the fall is too slow": ground 1-2.5 s (+1 at full
  pain, +0.5 wounded), with small movements (groans, a hand to the wound, a little roll); each
  get-up trimmed to its real rise and played at whatever speed fits 1.5-2.5 s (0.85-1.8x, slow
  clips in pain); a brief recovery (0.2-0.4 s stagger, hand to the knee, a look), the rifle back
  (0.5 s), then 0.4-0.8 s before the first shot (scaled by aggression). All on `BodyDamageProfile`.
- Fix 1, the rifle's weight: `EnemyRifle`. A fall at under 4 m/s keeps it on a sling (a 3.5 kg
  body on a damped, limited strap from the chest); over 10 m/s knocks it out of the hands; in
  between the chance rises with speed (a full kick, about 7.7, drops it about 60% of the time).
  Standing again: the hand brings it up off the sling, or it walks over, crouches and picks it up
  (within 5 m, the player at least 8 m off), or goes to the pistol. Held, it hangs on a spring
  under the hand: it lags and settles when the body starts, stops or turns.
- Stage 2: rifle movement in eight directions (walk, run, sprint, crouch) by root motion with the
  agent following (`EnemyMotor`); a separate Idle state with calm variations per soldier, tempo
  and phase differing per soldier; quarter turns on the spot, run start and stop; additive aim
  down the spine with the head leading (`EnemyBody.RigAim`); left-hand IK on the handguard;
  foot IK on slopes and steps (`EnemyFootIK`); physical hit reactions (springs in the upper body,
  legs still animated; the head's snap kept); the pistol for a crippled arm or a lost rifle; the
  face-up crawler shifting and breathing; the rifle carried at the chest in a hobbling run.
- **Animation Rigging is installed but not used.** Its aim constraints set a bone's rotation
  outright and straightened the bladed stance the Mixamo rifle clips are made in (chest twisted
  37 degrees off the target); a rig's weight set from script was put back by the rig's own pass
  every frame, so the left hand could not be eased on and off. Both are done in our own code
  (additive spine turn; weighted two-bone IK). Remove the package unless a later stage needs it.
- Two traps found: an idle clip at the centre of a movement blend tree stretches every clip to
  its length (the walk ran at a third of its speed), so standing still is its own state; and
  reparenting anything into the Animator's hierarchy in play makes it rebind, so weapons stay
  children of the root and are laid on sockets each frame. Write Defaults off on every state.
- Tests: pose check (FPSKit > Enemy AI > Run Pose Check) and the limb test with the rifle in
  each fall: all pass. Performance with 20 enemies: player loop 15.8 -> 18.1 ms mean (13.5 ->
  17.1 median), animation 0.27 -> about 1.0 ms. `Build/Perf/enemies_stage2.txt`.
